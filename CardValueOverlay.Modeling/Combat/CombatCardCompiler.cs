using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Combat;

public sealed class CombatCardCompiler
{
    private static readonly HashSet<string> RecognizedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Eternal",
        "Ethereal",
        "Exhaust",
        "Innate",
        "Retain",
        "Sly",
        "Unplayable"
    };

    private readonly CardFormBuilder _formBuilder = new();

    public CombatCardCatalog CompileFile(string path)
    {
        IReadOnlyList<CardFactCatalogEntry> facts = JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"Card facts file '{path}' is empty.");
        return Compile(facts);
    }

    public CombatCardCatalog Compile(IReadOnlyList<CardFactCatalogEntry> facts)
    {
        List<CombatCardDefinition> cards = [];
        int definitionId = 0;
        foreach (CardFactCatalogEntry fact in facts.OrderBy(fact => fact.ModelId, StringComparer.Ordinal))
        {
            cards.Add(CompileForm(_formBuilder.Build(fact, 0), definitionId++));
            cards.Add(CompileForm(_formBuilder.Build(fact, 1), definitionId++));
        }

        return new CombatCardCatalog(cards);
    }

    public CombatCardDefinition CompileForm(CardForm form, int definitionId)
    {
        List<string> unsupported = [.. form.Unresolved];
        foreach (string keyword in form.Keywords.Where(keyword => !RecognizedKeywords.Contains(keyword)))
        {
            unsupported.Add($"Keyword '{keyword}' is outside the combat-aware physical slice.");
        }
        List<CombatCardEffect> effects = [];
        int starCost = 0;
        bool hasStarCost = false;
        for (int index = 0; index < form.Actions.Count; index++)
        {
            CardActionFact action = form.Actions[index];
            if (string.Equals(action.Kind, "selectCards", StringComparison.Ordinal))
            {
                if (TryCompileSelectAndMove(
                    form.Actions,
                    index,
                    out CombatCardEffect? selection,
                    out int consumedActions,
                    out string? selectionReason))
                {
                    effects.Add(selection);
                    index += consumedActions - 1;
                }
                else
                {
                    unsupported.Add(selectionReason ?? "Unsupported select/move action pair.");
                }
                continue;
            }

            if (string.Equals(action.Kind, "persistentPowerTrigger", StringComparison.Ordinal))
            {
                if (IsChildOfTheStarsTrigger(action) &&
                    form.Actions.Any(candidate =>
                        string.Equals(candidate.Kind, "power", StringComparison.Ordinal) &&
                        ContainsPower(candidate.Parameter, "ChildOfTheStars")))
                {
                    continue;
                }

                unsupported.Add(
                    $"Action 'persistentPowerTrigger' ({action.Parameter ?? "no parameter"}) is outside the combat-aware physical slice.");
                continue;
            }

            if (string.Equals(action.Kind, "starCost", StringComparison.Ordinal))
            {
                if (!TryInteger(action.Amount, out int parsedStarCost) || parsedStarCost < 0)
                {
                    unsupported.Add($"Action 'starCost' has invalid amount '{action.Amount}'.");
                }
                else if (hasStarCost)
                {
                    unsupported.Add("Card declares more than one star cost.");
                }
                else
                {
                    starCost = parsedStarCost;
                    hasStarCost = true;
                }
                continue;
            }

            if (!TryCompileEffect(action, index, out CombatCardEffect? effect, out string? reason))
            {
                unsupported.Add(reason ?? $"Unsupported action '{action.Kind}'.");
                continue;
            }

            effects.Add(effect);
        }

        bool unplayable = form.Keywords.Contains("Unplayable", StringComparer.OrdinalIgnoreCase) ||
            string.Equals(form.CardType, "Curse", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(form.CardType, "Status", StringComparison.OrdinalIgnoreCase);
        int cost = form.Cost ?? (unplayable ? -1 : 0);
        if (cost < 0 && !unplayable)
        {
            unsupported.Add("Variable/X energy cost is outside the Phase 1 exact slice.");
        }

        if (form.Actions.Count == 0 && !unplayable)
        {
            unsupported.Add("Playable card has no parsed physical action.");
        }

        return new CombatCardDefinition(
            definitionId,
            form.ModelId,
            form.TypeName,
            form.UpgradeLevel,
            form.CardType ?? "Unknown",
            Math.Max(0, cost),
            ParseTarget(form.TargetType),
            effects.OrderBy(effect => effect.SourceOrder).ToArray(),
            form.Keywords.Contains("Exhaust", StringComparer.OrdinalIgnoreCase) ||
                string.Equals(form.CardType, "Power", StringComparison.OrdinalIgnoreCase),
            !unplayable && cost >= 0,
            unsupported.Count == 0,
            unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            starCost,
            form.Keywords.Contains("Retain", StringComparer.OrdinalIgnoreCase),
            form.Keywords.Contains("Ethereal", StringComparer.OrdinalIgnoreCase),
            form.Keywords.Contains("Innate", StringComparer.OrdinalIgnoreCase),
            form.Tags.Contains("Defend", StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryCompileEffect(
        CardActionFact action,
        int sourceOrder,
        out CombatCardEffect effect,
        out string? reason)
    {
        effect = null!;
        reason = null;
        if (string.Equals(action.Kind, "forge", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(action.Parameter))
        {
            reason = $"Action 'forge' uses unresolved dynamic semantics '{action.Parameter}'.";
            return false;
        }
        if (!TryInteger(action.Amount, out int amount))
        {
            reason = $"Action '{action.Kind}' has unresolved or non-integral amount '{action.Amount}'.";
            return false;
        }

        CombatCardEffectKind? kind = action.Kind switch
        {
            "damage" => CombatCardEffectKind.Damage,
            "block" => CombatCardEffectKind.Block,
            "draw" => CombatCardEffectKind.Draw,
            "drawNextTurn" => CombatCardEffectKind.DrawNextTurn,
            "energyGain" => CombatCardEffectKind.GainEnergy,
            "energyNextTurn" => CombatCardEffectKind.GainEnergyNextTurn,
            "starGain" => CombatCardEffectKind.GainStars,
            "starNextTurn" => CombatCardEffectKind.GainStarsNextTurn,
            "blockNextTurn" => CombatCardEffectKind.GainBlockNextTurn,
            "forge" => CombatCardEffectKind.Forge,
            "hpLoss" => CombatCardEffectKind.LosePlayerHp,
            "debuffWeak" => CombatCardEffectKind.ApplyWeak,
            "debuffVulnerable" => CombatCardEffectKind.ApplyVulnerable,
            "debuffFrail" => CombatCardEffectKind.ApplyFrail,
            "power" when ContainsPower(action.Parameter, "Strength") => CombatCardEffectKind.GainStrength,
            "power" when ContainsPower(action.Parameter, "Dexterity") => CombatCardEffectKind.GainDexterity,
            "power" when ContainsPower(action.Parameter, "RetainHand") => CombatCardEffectKind.GainRetainHand,
            "power" when ContainsPower(action.Parameter, "ChildOfTheStars") => CombatCardEffectKind.InstallChildOfTheStars,
            "power" when ContainsPower(action.Parameter, "Orbit") => CombatCardEffectKind.InstallOrbit,
            "power" when ContainsPower(action.Parameter, "PaleBlueDot") => CombatCardEffectKind.InstallPaleBlueDot,
            "power" when ContainsPower(action.Parameter, "Fasten") => CombatCardEffectKind.InstallFasten,
            _ => null
        };
        if (!kind.HasValue)
        {
            reason = $"Action '{action.Kind}' ({action.Parameter ?? "no parameter"}) is outside the Phase 1 physical slice.";
            return false;
        }

        int hitCount = action.HitCount ?? 1;
        if (amount < 0 || hitCount <= 0)
        {
            reason = $"Action '{action.Kind}' has invalid amount/hit count.";
            return false;
        }

        effect = new CombatCardEffect(
            kind.Value,
            amount,
            hitCount,
            ParseTarget(action.TargetType),
            action.Source,
            sourceOrder,
            action.DynamicVarName);
        return true;
    }

    private static bool TryCompileSelectAndMove(
        IReadOnlyList<CardActionFact> actions,
        int sourceIndex,
        out CombatCardEffect effect,
        out int consumedActions,
        out string? reason)
    {
        effect = null!;
        consumedActions = 1;
        reason = null;
        CardActionFact selection = actions[sourceIndex];
        if (sourceIndex + 1 >= actions.Count ||
            !string.Equals(actions[sourceIndex + 1].Kind, "moveCardBetweenPiles", StringComparison.Ordinal))
        {
            reason = "Action 'selectCards' is not followed by its concrete move operation.";
            return false;
        }
        if (sourceIndex + 2 != actions.Count)
        {
            reason = "Select/move continuation is supported only when it is the final card operation.";
            return false;
        }

        CardActionFact move = actions[sourceIndex + 1];
        if (!TryInteger(selection.Amount, out int selectionCount) || selectionCount != 1 ||
            !TryInteger(move.Amount, out int moveCount) || moveCount != selectionCount)
        {
            reason = "Phase 1 select/move continuation requires exactly one selected and moved card.";
            return false;
        }

        CombatPile sourcePile;
        string expectedMove;
        if (string.Equals(selection.Parameter, "from:Hand", StringComparison.OrdinalIgnoreCase))
        {
            sourcePile = CombatPile.Hand;
            expectedMove = "from:Hand;to:Draw;position:Top";
        }
        else if (string.Equals(selection.Parameter, "from:Discard", StringComparison.OrdinalIgnoreCase))
        {
            sourcePile = CombatPile.Discard;
            expectedMove = "from:Discard;to:Draw;position:Top";
        }
        else
        {
            reason = $"Selection source '{selection.Parameter ?? "none"}' is outside the visible select/move slice.";
            return false;
        }

        if (!string.Equals(move.Parameter, expectedMove, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Move operation '{move.Parameter ?? "none"}' does not match supported top-deck movement '{expectedMove}'.";
            return false;
        }

        CombatCardSelectionSpec spec = new(
            sourcePile,
            CombatPile.KnownTop,
            CombatCardInsertionPosition.Top,
            selectionCount);
        effect = new CombatCardEffect(
            CombatCardEffectKind.SelectAndMoveCard,
            selectionCount,
            1,
            CombatCardTarget.Self,
            $"{selection.Source} + {move.Source}",
            sourceIndex,
            selection.DynamicVarName,
            spec);
        consumedActions = 2;
        return true;
    }

    private static bool TryInteger(decimal? value, out int result)
    {
        result = 0;
        if (!value.HasValue || decimal.Truncate(value.Value) != value.Value || value.Value > int.MaxValue)
        {
            return false;
        }

        result = (int)value.Value;
        return true;
    }

    private static bool ContainsPower(string? parameter, string power) =>
        parameter?.Contains($"power:{power}", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsChildOfTheStarsTrigger(CardActionFact action) =>
        string.Equals(
            action.Parameter,
            "AfterStarsSpent:gainBlockPerStarSpent",
            StringComparison.OrdinalIgnoreCase);

    private static CombatCardTarget ParseTarget(string? target) => target?.ToLowerInvariant() switch
    {
        "anyenemy" or "enemy" => CombatCardTarget.Enemy,
        "allenemies" => CombatCardTarget.AllEnemies,
        _ => CombatCardTarget.Self
    };
}
