using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class SimulationCardLibraryBuilder
{
    private const decimal DefaultAoeDamageMultiplier = 1.3m;

    private static readonly HashSet<string> SupportedRuntimePowerKeys = new(StringComparer.Ordinal)
    {
        "Automation",
        "BlackHole",
        "ChildOfTheStars",
        "Conqueror",
        "CrushUnder",
        "DarkShackles",
        "Dexterity",
        "DyingStar",
        "Entropy",
        "Fasten",
        "ForegoneConclusion",
        "Frail",
        "Furnace",
        "Genesis",
        "Orbit",
        "PaleBlueDot",
        "Panache",
        "Arsenal",
        "Calamity",
        "Parry",
        "PillarOfCreation",
        "Plating",
        "PrepTime",
        "Reflect",
        "RetainHand",
        "RollingBoulder",
        "SeekingEdge",
        "SpectrumShift",
        "Stratagem",
        "Strength",
        "SwordSage",
        "TheSealedThrone",
        "TheBomb",
        "Thorns",
        "Tyranny",
        "Vigor",
        "VoidForm",
        "Monologue"
    };

    // Powers that ARE simulated (their per-turn effect ripples into deck EV) but whose payoff is
    // draw / create-card / transform / tutor / cost-reduction / turn-ending - none of which is a
    // source-creditable channel. Installing such a power must be valued via play-delta (EV delta),
    // not source-credit (which would credit the installing card ~0). Flagging the power install as
    // incomplete-attribution makes the strategy resolver pick play-delta.
    private static readonly HashSet<string> PlayDeltaOnlyPowerKeys = new(StringComparer.Ordinal)
    {
        "Calamity",       // after each attack, create random Attacks into hand
        "Entropy",        // each turn, transform hand cards
        "PaleBlueDot",    // conditional bonus draw next turn
        "SpectrumShift",  // each turn, create Colorless cards into hand
        "Stratagem",      // on shuffle, tutor cards Draw -> Hand
        "Tyranny",        // +draw and forced exhaust each turn
        "VoidForm"        // next N cards cost 0, then ends the turn
    };

    private static readonly HashSet<string> SimulatedResourceKinds = new(StringComparer.Ordinal)
    {
        "draw",
        "drawNextTurn",
        "blockNextTurn",
        "energyGain",
        "energyNextTurn",
        "starGain",
        "starNextTurn",
        "starCost",
        "forge",
        "hpLoss",
        "debuffVulnerable",
        "damageIncreasePerDraw",
        "costReductionPerDraw",
        "energyLossPerDraw"
    };

    private static readonly HashSet<string> IncompleteAttributionActionKinds = new(StringComparer.Ordinal)
    {
        "draw",
        "drawNextTurn",
        "selectCards",
        "moveCardBetweenPiles",
        "transformCard",
        "createCard",
        "createCardChoices",
        "grantReplay",
        // A card returning ITSELF to a pile (ShiningStrike/Bolas) reshapes future draws, which
        // source-credit cannot attribute even though the card's own damage/stars are attributable.
        // Value it via play-delta so the future-draw effect is captured.
        "selfReturn"
    };

    public IReadOnlyList<SimulationCard> Build(
        IReadOnlyList<CardFactCatalogEntry> entries,
        ValueCalibration calibration,
        int layer,
        bool includeUpgrades = false,
        IReadOnlyList<CardPoolMembershipEntry>? memberships = null,
        IReadOnlyList<AutoPlayEffectEntry>? autoPlayEffects = null,
        CardSetupValueCatalog? setupValues = null)
    {
        Dictionary<string, AutoPlayEffectEntry> autoPlayByModelId = (autoPlayEffects ?? [])
            .GroupBy(effect => effect.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<string>> poolsByModelId = memberships is null
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            : memberships.ToDictionary(
                membership => membership.ModelId,
                membership => membership.Pools,
                StringComparer.OrdinalIgnoreCase);
        CardFormBuilder formBuilder = new();
        IReadOnlyList<CardForm> forms = entries
            .SelectMany(entry => includeUpgrades
                ? [formBuilder.Build(entry, upgradeLevel: 0), formBuilder.Build(entry, upgradeLevel: 1)]
                : new[] { formBuilder.Build(entry, upgradeLevel: 0) })
            .ToArray();
        CardValueEstimator estimator = new();
        IReadOnlyList<CardValueEstimate> estimates = estimator.Estimate(entries, calibration, layer);
        IReadOnlyList<CardValueEstimate> weakLayerEstimates = estimator.Estimate(entries, calibration, WeakEstimateLayer(calibration, layer));
        Dictionary<string, CardValueEstimate> estimatesByModelId = estimates.ToDictionary(
            estimate => estimate.ModelId,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CardValueEstimate> weakLayerEstimatesByModelId = weakLayerEstimates.ToDictionary(
            estimate => estimate.ModelId,
            StringComparer.OrdinalIgnoreCase);

        return forms
            .Select(form => BuildCard(
                form,
                estimatesByModelId[form.ModelId],
                weakLayerEstimatesByModelId[form.ModelId],
                poolsByModelId.TryGetValue(form.ModelId, out IReadOnlyList<string>? pools) ? pools : [],
                calibration,
                layer,
                autoPlayByModelId.GetValueOrDefault(form.ModelId),
                setupValues ?? CardSetupValueCatalog.Empty))
            .OrderBy(card => card.TypeName, StringComparer.Ordinal)
            .ThenBy(card => card.UpgradeLevel)
            .ToArray();
    }

    private static SimulationCard BuildCard(
        CardForm form,
        CardValueEstimate estimate,
        CardValueEstimate weakLayerEstimate,
        IReadOnlyList<string> pools,
        ValueCalibration calibration,
        int layer,
        AutoPlayEffectEntry? autoPlayEntry,
        CardSetupValueCatalog setupValues)
    {
        AutoPlayEffect? autoPlayEffect = ResolveAutoPlayEffect(form, autoPlayEntry);
        ResolvedSetupValue unifiedSetup = ResolveUnifiedSetupValue(setupValues, form);
        bool hasSimulatedPersistentPower = form.Actions.Any(IsSupportedPersistentPowerTrigger);
        bool hasSimulatedPower = form.Actions.Any(IsSupportedPowerInstall);
        bool hasSimulatedXCostDamage = form.Actions.Any(IsSupportedXCostDamageAction);
        bool hasSimulatedScalingDamage = form.Actions.Any(action => IsSupportedScalingDamageAction(form, action));
        bool hasSimulatedHpLoss = form.Actions.Any(action => action.Kind == "hpLoss");
        bool hasSimplifiedWeak = form.Actions.Any(action => action.Kind == "debuffWeak");
        bool hasSimulatedRuntimeKeyword = HasRuntimeSimulatedKeyword(form);
        bool hasSimulatedCardObjectAction = form.Actions.Any(action =>
            IsSupportedCardObjectAction(form, action)
            || IsSupportedTransformAction(action)
            || IsSupportedGeneratedCardAction(action));
        List<string> warnings =
        [
            .. estimate.Warnings.Where(warning => !IsSimulatorManagedEstimateWarning(
                warning,
                hasSimulatedPersistentPower,
                hasSimulatedPower,
                hasSimulatedXCostDamage,
                hasSimulatedScalingDamage,
                hasSimulatedHpLoss,
                hasSimplifiedWeak,
                hasSimulatedRuntimeKeyword,
                hasSimulatedCardObjectAction)),
            .. form.Unresolved
        ];
        warnings.AddRange(form.Actions
            .Where(action => !IsSimulatedAction(form, action))
            .Select(action => $"Unsupported simulation action '{action.Kind}' from {action.Source}."));
        warnings.AddRange(IncompleteAttributionWarnings(form));
        if (autoPlayEffect is not null)
        {
            // Auto-play value accrues to the auto-played cards / deck EV, not this card, so it is
            // not source-creditable. Flag it incomplete so the strategy resolver picks play-delta.
            warnings.Add("Attribution incomplete for action 'autoPlay'.");
        }

        if (form.Actions.Any(action => IsSupportedPowerInstall(action)
            && PlayDeltaOnlyPowerKeys.Contains(PowerKey(action.Parameter) ?? string.Empty)))
        {
            // The installed power is simulated (its effect is in deck EV) but is draw/create/
            // transform/tutor/cost-reduction/turn-ending - not source-creditable. Value via play-delta.
            warnings.Add("Attribution incomplete for action 'power'.");
        }
        int energyCost = form.Cost.GetValueOrDefault(-1);
        bool unplayable = !form.Cost.HasValue || form.Cost.Value < 0 || HasKeyword(form, "Unplayable");
        decimal damageUnitValue = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnitValue");
        decimal blockValuePerBlock = calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage") * damageUnitValue;
        decimal aoeDamageMultiplier = calibration.GetNamedValue(calibration.TargetingPenalties, "aoeDamageMultiplier", DefaultAoeDamageMultiplier);

        decimal intrinsicValue = estimate.Contributions
            .Where(contribution => !SimulatedResourceKinds.Contains(contribution.TermKind))
            .Where(contribution => !IsRuntimeSimulatedXCostDamageContribution(contribution, hasSimulatedXCostDamage))
            .Where(contribution => !IsRuntimeSimulatedScalingDamageContribution(contribution, hasSimulatedScalingDamage))
            .Where(contribution => !string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Where(contribution => !IsBeatIntoShapeCalculationBaseDamage(form, contribution))
            .Where(contribution => !IsConditionalHitScalingDamage(form, contribution))
            .Where(contribution => !IsRuntimeSimulatedPowerContribution(contribution, form.Actions, hasSimulatedPersistentPower || hasSimulatedPower))
            .Where(contribution => !IsRuntimeSimulatedKeywordContribution(form, contribution))
            .Sum(contribution => ContributionValue(contribution, form.UpgradeLevel));
        intrinsicValue += weakLayerEstimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Sum(contribution => ContributionValue(contribution, form.UpgradeLevel));

        decimal damageValue = DamageValue(estimate, form.UpgradeLevel, form);
        decimal staticEstimatedValue = form.UpgradeLevel > 0 ? estimate.UpgradedEstimatedValue : estimate.EstimatedValue;
        if (IsBeatIntoShape(form) || hasSimulatedScalingDamage || IsConditionalHitScalingCard(form))
        {
            staticEstimatedValue = intrinsicValue;
        }

        SimulationCard card = new()
        {
            ModelId = FormModelId(form),
            TypeName = FormTypeName(form),
            FullTypeName = form.FullTypeName,
            UpgradeLevel = form.UpgradeLevel,
            Cost = form.Cost,
            CardType = form.CardType,
            Rarity = form.Rarity,
            TargetType = form.TargetType,
            Tags = form.Tags,
            Pools = pools,
            Layer = layer,
            StaticEstimatedValue = (double)staticEstimatedValue,
            IntrinsicValue = (double)intrinsicValue,
            DamageValue = (double)damageValue,
            BaseDamage = (double)BaseDamage(form),
            DamageModifierMultiplier = (double)DamageModifierMultiplier(form, calibration),
            DamageUnitValue = (double)damageUnitValue,
            ScalingDamageKind = ScalingDamageKind(form),
            ScalingDamageBase = (double)ScalingDamageBase(form),
            ScalingDamagePerUnit = (double)ScalingDamagePerUnit(form),
            ScalingDamageTargetMultiplier = (double)ScalingDamageTargetMultiplier(form, calibration),
            DamageIncreasePerDraw = (double)DamageIncreasePerDraw(form),
            CostReductionPerDraw = CostReductionPerDraw(form),
            EnergyLossPerDraw = EnergyLossPerDraw(form),
            BaseBlock = (double)BaseBlock(form),
            BlockEffectCount = BlockEffectCount(form),
            BlockValuePerBlock = (double)blockValuePerBlock,
            AoeDamageMultiplier = (double)aoeDamageMultiplier,
            BeamSetupValue = unifiedSetup.Beam,
            PlaySetupValue = unifiedSetup.Play,
            EnergyCost = energyCost,
            StarCost = SumTermAmount(form, "starCost"),
            HasExplicitStarCost = HasExplicitStarCost(form),
            HasStarCostX = HasStarCostX(form),
            Draw = SumTermAmount(form, "draw"),
            DrawsToHandFull = form.Actions.Any(action =>
                action.Kind == "draw"
                && string.Equals(action.Parameter, "toHandFull", StringComparison.Ordinal)),
            DrawNextTurn = SumTermAmount(form, "drawNextTurn") + PowerAmount(form, "ForegoneConclusion"),
            BlockNextTurn = SumTermAmount(form, "blockNextTurn"),
            EnergyGain = SumTermAmount(form, "energyGain"),
            EnergyNextTurn = SumTermAmount(form, "energyNextTurn"),
            StarGain = SumTermAmount(form, "starGain"),
            StarNextTurn = SumTermAmount(form, "starNextTurn"),
            Forge = SumTermAmount(form, "forge"),
            ReplayGrant = SumTermAmount(form, "grantReplay"),
            Vulnerable = SumTermAmount(form, "debuffVulnerable"),
            Exhausts = HasKeyword(form, "Exhaust"),
            EndsTurn = form.Actions.Any(action => action.Kind == "endTurn"),
            Unplayable = unplayable,
            Ethereal = HasKeyword(form, "Ethereal"),
            Retain = HasKeyword(form, "Retain"),
            Innate = HasKeyword(form, "Innate"),
            Confidence = estimate.Confidence,
            Warnings = warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Actions = form.Actions,
            AutoPlay = autoPlayEffect
        };
        return card;
    }

    private static AutoPlayEffect? ResolveAutoPlayEffect(CardForm form, AutoPlayEffectEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        if (!form.Actions.Any(action => action.Kind == "autoPlay"))
        {
            return null;
        }

        int count = form.UpgradeLevel > 0
            ? entry.CountUpgraded ?? entry.Count
            : entry.Count;
        if (count <= 0)
        {
            return null;
        }

        return new AutoPlayEffect(
            entry.SourcePile,
            entry.CardTypeFilter,
            entry.ExcludeUnplayable,
            entry.Selection,
            entry.RepeatSameCard,
            count);
    }

    private static decimal DamageValue(CardValueEstimate estimate, int upgradeLevel, CardForm form)
    {
        if (IsConditionalHitScalingCard(form))
        {
            // All damage is routed through the scaling channel (base x per-turn count); no flat term.
            return 0m;
        }

        return ConstantRepeatHitCount(form) * estimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "damage", StringComparison.Ordinal))
            .Where(contribution => !IsBeatIntoShapeCalculationBaseDamage(form, contribution))
            .Sum(contribution => ContributionValue(contribution, upgradeLevel));
    }

    private static decimal BaseDamage(CardForm form)
    {
        return ConstantRepeatHitCount(form) * form.Actions
            .Where(action => string.Equals(action.Kind, "damage", StringComparison.Ordinal))
            .Where(action => !IsBeatIntoShapeCalculationBaseDamage(form, action))
            .Sum(action => (action.Amount ?? 0m) * (action.HitCount ?? 1));
    }

    // SevenStars: DamageCmd.Attack(7).WithHitCount(Repeat.IntValue) with RepeatVar(7). The parser only
    // records a LITERAL WithHitCount(n); a variable hit count is dropped, leaving 1 hit, so the card
    // was valued as 7 damage instead of 7x7=49 (AoE). RepeatVar is context-dependent elsewhere (clone
    // count for HeirloomHammer, replay count for DecisionsDecisions), so the constant hit count is
    // curated per card here. SevenStars' upgrade only lowers energy cost, so the count stays 7.
    private static decimal ConstantRepeatHitCount(CardForm form)
    {
        return BaseTypeName(form.TypeName) == "SevenStars" ? 7m : 1m;
    }

    // Attacks whose hit count is a computed per-turn game-state count (WithHitCount(CalculatedVar)).
    // The parser cannot infer the count basis, so it is curated here (mirrors the CrescentSpear etc.
    // hard-coded scaling set). All of the card's damage is routed through scaling; there is no flat
    // component (hits = count, which can be 0).
    private static string? ConditionalHitScalingKind(CardForm form)
    {
        return BaseTypeName(form.TypeName) switch
        {
            "LunarBlast" => "skillsPlayedThisTurn",
            "Radiate" => "starsGainedThisTurn",
            _ => null
        };
    }

    private static bool IsConditionalHitScalingCard(CardForm form)
    {
        return ConditionalHitScalingKind(form) is not null;
    }

    private static bool IsConditionalHitScalingDamage(CardForm form, CardValueContribution contribution)
    {
        return IsConditionalHitScalingCard(form)
            && string.Equals(contribution.TermKind, "damage", StringComparison.Ordinal);
    }

    private static string? ScalingDamageKind(CardForm form)
    {
        string? conditionalHit = ConditionalHitScalingKind(form);
        if (conditionalHit is not null)
        {
            return conditionalHit;
        }

        if (!form.Actions.Any(action => IsSupportedScalingDamageAction(form, action)))
        {
            return null;
        }

        return BaseTypeName(form.TypeName) switch
        {
            "CrescentSpear" => "starCostCardCount",
            "GoldAxe" => "cardsPlayedThisCombat",
            "MindBlast" => "drawPileCount",
            "Supermassive" => "generatedCardsCreated",
            _ => null
        };
    }

    // Raw damage gained every time the card is DRAWN (KinglyPunch AfterCardDrawn). Parsed as a
    // "damageIncreasePerDraw" action; upgrade scaling (Increase +2/level) flows through CardForm's
    // ApplyUpgradeOperations, so the upgraded form already carries 6. Realized per instance in the
    // simulator's draw loop.
    private static decimal DamageIncreasePerDraw(CardForm form)
    {
        return form.Actions
            .Where(action => string.Equals(action.Kind, "damageIncreasePerDraw", StringComparison.Ordinal))
            .Sum(action => action.Amount ?? 0m);
    }

    // Energy cost lost per draw this combat (KinglyKick AfterCardDrawn: -1 each draw, so first draw
    // makes the base-4 card cost 3). Parsed as a "costReductionPerDraw" action.
    private static int CostReductionPerDraw(CardForm form)
    {
        return SumTermAmount(form, "costReductionPerDraw");
    }

    // Player energy lost each time the card is DRAWN (Void AfterCardDrawn: LoseEnergy(1)). Parsed as
    // an "energyLossPerDraw" action; realized immediately against player energy in the draw loop.
    private static int EnergyLossPerDraw(CardForm form)
    {
        return SumTermAmount(form, "energyLossPerDraw");
    }

    private static decimal ScalingDamageBase(CardForm form)
    {
        if (ScalingDamageKind(form) is null)
        {
            return 0m;
        }

        return form.Actions
            .Where(action => string.Equals(action.Kind, "damage", StringComparison.Ordinal))
            .Where(action => string.Equals(action.Parameter, "calculationBase", StringComparison.OrdinalIgnoreCase))
            .Sum(action => action.Amount ?? 0m);
    }

    private static decimal ScalingDamagePerUnit(CardForm form)
    {
        if (ScalingDamageKind(form) is null)
        {
            return 0m;
        }

        if (IsConditionalHitScalingCard(form))
        {
            // Per-hit damage = the base DamageVar (excluding the 0 CalculationBase term).
            return form.Actions
                .Where(action => string.Equals(action.Kind, "damage", StringComparison.Ordinal))
                .Where(action => !string.Equals(action.Parameter, "calculationBase", StringComparison.OrdinalIgnoreCase))
                .Sum(action => action.Amount ?? 0m);
        }

        return form.Actions
            .Where(action => IsSupportedScalingDamageAction(form, action))
            .Sum(action => action.Amount ?? 0m);
    }

    private static decimal ScalingDamageTargetMultiplier(CardForm form, ValueCalibration calibration)
    {
        if (ScalingDamageKind(form) is null)
        {
            return 1m;
        }

        if (IsConditionalHitScalingCard(form))
        {
            return form.Actions
                .Where(action => string.Equals(action.Kind, "damage", StringComparison.Ordinal))
                .Select(action => GetTargetMultiplier(action.TargetType, calibration))
                .DefaultIfEmpty(1m)
                .Max();
        }

        return form.Actions
            .Where(action => IsSupportedScalingDamageAction(form, action))
            .Select(action => GetTargetMultiplier(action.TargetType, calibration))
            .DefaultIfEmpty(1m)
            .Max();
    }

    private static decimal DamageModifierMultiplier(CardForm form, ValueCalibration calibration)
    {
        return form.Actions
            .Where(action => string.Equals(action.Kind, "damage", StringComparison.Ordinal))
            .Where(action => !IsBeatIntoShapeCalculationBaseDamage(form, action))
            .Sum(action => (action.HitCount ?? 1) * GetTargetMultiplier(action.TargetType, calibration));
    }

    private static decimal BaseBlock(CardForm form)
    {
        return form.Actions
            .Where(action => string.Equals(action.Kind, "block", StringComparison.Ordinal))
            .Sum(action => action.Amount ?? 0m);
    }

    private static int BlockEffectCount(CardForm form)
    {
        return form.Actions.Count(action => string.Equals(action.Kind, "block", StringComparison.Ordinal));
    }

    private static decimal ContributionValue(CardValueContribution contribution, int upgradeLevel)
    {
        return upgradeLevel <= 0 ? contribution.BaseValue : contribution.BaseValue + contribution.UpgradeValue;
    }

    private static int WeakEstimateLayer(ValueCalibration calibration, int fallbackLayer)
    {
        return calibration.LayerBreakpoints
            .Order()
            .Skip(1)
            .FirstOrDefault(fallbackLayer);
    }

    private static int SumTermAmount(CardForm form, string kind)
    {
        decimal amount = form.Actions
            .Where(action => string.Equals(action.Kind, kind, StringComparison.Ordinal))
            .Sum(action => action.Amount ?? 0m);

        return amount <= 0m ? 0 : (int)Math.Round(amount, MidpointRounding.AwayFromZero);
    }

    private static int PowerAmount(CardForm form, string powerKey)
    {
        decimal amount = form.Actions
            .Where(action => action.Kind == "power")
            .Where(action => string.Equals(PowerKey(action.Parameter), powerKey, StringComparison.OrdinalIgnoreCase))
            .Sum(action => action.Amount ?? 0m);

        return amount <= 0m ? 0 : (int)Math.Round(amount, MidpointRounding.AwayFromZero);
    }

    private static bool HasKeyword(CardForm form, string keyword)
    {
        return form.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeSimulatedKeywordContribution(CardForm form, CardValueContribution contribution)
    {
        return contribution.TermKind == "keyword"
            && contribution.Parameter is not null
            && IsRuntimeSimulatedKeyword(contribution.Parameter)
            && HasKeyword(form, contribution.Parameter);
    }

    private static bool HasRuntimeSimulatedKeyword(CardForm form)
    {
        return form.Keywords.Any(IsRuntimeSimulatedKeyword);
    }

    private static bool IsRuntimeSimulatedKeyword(string keyword)
    {
        return string.Equals(keyword, "Retain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyword, "Innate", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal GetTargetMultiplier(string? targetType, ValueCalibration calibration)
    {
        return targetType switch
        {
            "AllEnemies" => calibration.GetNamedValue(calibration.TargetingPenalties, "aoeDamageMultiplier", DefaultAoeDamageMultiplier),
            "RandomEnemy" => calibration.GetNamedValue(calibration.TargetingPenalties, "randomTargetMultiplier", 1m),
            _ => 1m
        };
    }

    private static bool IsRuntimeSimulatedPowerContribution(
        CardValueContribution contribution,
        IReadOnlyList<CardActionFact> actions,
        bool hasSimulatedPower)
    {
        if (!hasSimulatedPower || contribution.TermKind != "power")
        {
            return false;
        }

        string? contributionPower = PowerKey(contribution.Parameter);
        return actions.Any(action => IsSupportedPowerInstall(action)
            && string.Equals(PowerKey(action.Parameter), contributionPower, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSimulatorManagedEstimateWarning(
        string warning,
        bool hasSimulatedPersistentPower,
        bool hasSimulatedPower,
        bool hasSimulatedXCostDamage,
        bool hasSimulatedScalingDamage,
        bool hasSimulatedHpLoss,
        bool hasSimplifiedWeak,
        bool hasSimulatedRuntimeKeyword,
        bool hasSimulatedCardObjectAction)
    {
        bool persistentPowerWarning = hasSimulatedPersistentPower
            && (warning.Contains("Unsupported card action 'persistentPowerTrigger'", StringComparison.Ordinal)
                || warning.Contains("Contribution 'power' used a generic calibration fallback.", StringComparison.Ordinal));
        bool powerWarning = hasSimulatedPower
            && warning.Contains("Contribution 'power' used a generic calibration fallback.", StringComparison.Ordinal);
        bool xCostDamageWarning = hasSimulatedXCostDamage
            && (warning.Contains("Unsupported card action 'xCostDamage'", StringComparison.Ordinal)
                || warning.Contains("No supported contribution was estimated for this card.", StringComparison.Ordinal));
        bool scalingDamageWarning = hasSimulatedScalingDamage
            && (warning.Contains("Unsupported card action 'scalingDamage'", StringComparison.Ordinal)
                || warning.Contains("Contribution 'scalingDamage' used a generic calibration fallback.", StringComparison.Ordinal)
                || warning.Contains("Generic calculated damage scaling requires manual review.", StringComparison.Ordinal)
                || warning.Contains("Low confidence card action 'scalingDamage'", StringComparison.Ordinal)
                || warning.Contains("No supported contribution was estimated for this card.", StringComparison.Ordinal));
        bool hpLossWarning = hasSimulatedHpLoss
            && warning.Contains("Unsupported card action 'hpLoss'", StringComparison.Ordinal);
        bool weakWarning = hasSimplifiedWeak
            && warning.Contains("Unsupported card action 'debuffWeak'", StringComparison.Ordinal);
        bool keywordWarning = hasSimulatedRuntimeKeyword
            && warning.Contains("Contribution 'keyword' used a generic calibration fallback.", StringComparison.Ordinal);
        bool cardObjectWarning = hasSimulatedCardObjectAction
            && (warning.Contains("Unsupported card action 'selectCards'", StringComparison.Ordinal)
                || warning.Contains("Unsupported card action 'moveCardBetweenPiles'", StringComparison.Ordinal)
                || warning.Contains("Unsupported card action 'transformCard'", StringComparison.Ordinal)
                || warning.Contains("Unsupported card action 'createCard'", StringComparison.Ordinal)
                || warning.Contains("Unsupported card action 'createCardChoices'", StringComparison.Ordinal));
        return persistentPowerWarning
            || powerWarning
            || xCostDamageWarning
            || scalingDamageWarning
            || hpLossWarning
            || weakWarning
            || keywordWarning
            || cardObjectWarning;
    }

    // Cards whose card GENERATION cannot be faithfully simulated. Their generate/select actions are
    // treated as unsupported so the strategy resolver marks them excluded, and so decks containing
    // them are flagged (they cannot be simulated as deck members either).
    //
    // Discovery (current-hero pool) and Jackpot (current-hero 0-cost pool) are now simulated against
    // the curated discovery.regent / jackpot.regent.zeroCost pools (see DeckMonteCarloSimulator's
    // generation switch). Splash Discovers from the OTHER heroes' Attack pools, which are outside the
    // Regent/Colorless/current-hero modeling scope, so it stays unsupported.
    private static readonly HashSet<string> UnsupportedGenerationCards = new(StringComparer.Ordinal)
    {
        "Splash"
    };

    private static bool IsSimulatedAction(CardForm form, CardActionFact action)
    {
        if (UnsupportedGenerationCards.Contains(BaseTypeName(form.TypeName))
            && action.Kind is "createCard" or "createCardChoices" or "selectCards")
        {
            return false;
        }

        bool simulatedRuntimeAction = action.Kind is
            "damage" or
            "block" or
            "blockNextTurn" or
            "debuffWeak" or
            "draw" or
            "drawNextTurn" or
            "energyGain" or
            "energyNextTurn" or
            "starGain" or
            "starNextTurn" or
            "starCost" or
            "forge" or
            "xCostDamage" or
            "hpLoss" or
            "debuffVulnerable" or
            "createCard" or
            "createCardChoices" or
            "autoPlay" or
            "selfReturn" or
            "grantReplay" or
            "endTurn" or
            "damageIncreasePerDraw" or
            "costReductionPerDraw" or
            "energyLossPerDraw";
        return simulatedRuntimeAction
            || IsSupportedSelectionAction(action)
            || IsSupportedCardObjectAction(form, action)
            || IsSupportedTransformAction(action)
            || IsSupportedGeneratedCardAction(action)
            || IsSupportedScalingDamageAction(form, action)
            || IsSupportedPowerInstall(action)
            || IsSupportedPersistentPowerTrigger(action);
    }

    private static IEnumerable<string> IncompleteAttributionWarnings(CardForm form)
    {
        foreach (string kind in form.Actions
            .Select(action => action.Kind)
            .Where(IncompleteAttributionActionKinds.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            yield return $"Attribution incomplete for action '{kind}'.";
        }

        if (PowerAmount(form, "ForegoneConclusion") > 0m)
        {
            yield return "Attribution incomplete for action 'drawNextTurn'.";
        }
    }

    private static bool IsSupportedSelectionAction(CardActionFact action)
    {
        return action.Kind == "selectCards"
            && action.Parameter is not null
            && (action.Parameter.Contains("from:Hand", StringComparison.Ordinal)
                || action.Parameter.Contains("from:Draw", StringComparison.Ordinal)
                || action.Parameter.Contains("from:Discard", StringComparison.Ordinal)
                || action.Parameter.Contains("from:Exhaust", StringComparison.Ordinal));
    }

    private static bool IsSupportedCardObjectAction(CardForm form, CardActionFact action)
    {
        if (action.Kind != "moveCardBetweenPiles" || action.Parameter is null)
        {
            return false;
        }

        bool pileToPile = action.Parameter.Contains("from:", StringComparison.Ordinal)
            && (action.Parameter.Contains("to:Hand", StringComparison.Ordinal)
                || action.Parameter.Contains("to:Draw", StringComparison.Ordinal)
                || action.Parameter.Contains("to:Discard", StringComparison.Ordinal)
                || action.Parameter.Contains("to:Exhaust", StringComparison.Ordinal));
        return pileToPile || IsSupportedSourceLessCardAdd(form, action);
    }

    private static bool IsSupportedSourceLessCardAdd(CardForm form, CardActionFact action)
    {
        string baseTypeName = BaseTypeName(form.TypeName);
        return action.Source == "CardPileCmd.Add"
            && !action.Parameter!.Contains("from:", StringComparison.Ordinal)
            && ((baseTypeName == "SummonForth" && action.Parameter.Contains("to:Hand", StringComparison.Ordinal))
                || (baseTypeName == "ShiningStrike" && action.Parameter.Contains("to:Draw", StringComparison.Ordinal)));
    }

    private static bool IsSupportedTransformAction(CardActionFact action)
    {
        return action.Kind == "transformCard";
    }

    private static bool IsSupportedGeneratedCardAction(CardActionFact action)
    {
        return action.Kind is "createCard" or "createCardChoices"
            || (action.Kind == "selectCards" && string.Equals(action.Parameter, "screen:chooseACard", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedXCostDamageAction(CardActionFact action)
    {
        return action.Kind == "xCostDamage"
            && string.Equals(action.Parameter, "energyX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeSimulatedXCostDamageContribution(
        CardValueContribution contribution,
        bool hasSimulatedXCostDamage)
    {
        return hasSimulatedXCostDamage
            && string.Equals(contribution.TermKind, "xCostDamage", StringComparison.Ordinal);
    }

    private static bool IsRuntimeSimulatedScalingDamageContribution(
        CardValueContribution contribution,
        bool hasSimulatedScalingDamage)
    {
        return hasSimulatedScalingDamage
            && string.Equals(contribution.TermKind, "scalingDamage", StringComparison.Ordinal);
    }

    private static ResolvedSetupValue ResolveUnifiedSetupValue(CardSetupValueCatalog setupValues, CardForm form)
    {
        // A missing catalog entry resolves to the source default (0), then the resolver applies the
        // power floor by CardType - so Powers stay explorable/played even without an explicit entry.
        // Constant/Source providers ignore the resource fields; Midline is a placeholder until
        // function/source providers read real fields + horizon in a later batch.
        CardSetupValueForm? setupForm = setupValues.Resolve(FormModelId(form), form.UpgradeLevel);
        SetupValueContext context = new(form.CardType, 0d, 0d, 0, 0, 0, 0, SetupHorizon.Midline);
        ResolvedSetupValue resolved = SetupValueResolver.Resolve(setupForm, context);

        // TheBomb / Monologue are non-Power skills that install a delayed payoff; keep them on the
        // always-play-early floor (formerly the builder's SetupPriorityValue TypeName fallback).
        if (BaseTypeName(form.TypeName) is "TheBomb" or "Monologue")
        {
            resolved = resolved with
            {
                Beam = Math.Max(resolved.Beam, SetupValueFunctions.PowerFloor),
                Play = Math.Max(resolved.Play, SetupValueFunctions.PowerFloor)
            };
        }

        return resolved;
    }

    private static bool HasExplicitStarCost(CardForm form)
    {
        return form.Actions.Any(action => action.Kind == "starCost" && (action.Amount ?? 0m) >= 0m);
    }

    private static bool HasStarCostX(CardForm form)
    {
        return form.Actions.Any(action =>
            action.Kind == "starCost"
            && action.Parameter is not null
            && action.Parameter.Contains("starX", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedPowerInstall(CardActionFact action)
    {
        string? key = PowerKey(action.Parameter);
        return action.Kind == "power"
            && key is not null
            && SupportedRuntimePowerKeys.Contains(key);
    }

    private static bool IsSupportedScalingDamageAction(CardForm form, CardActionFact action)
    {
        if (action.Kind != "scalingDamage")
        {
            return false;
        }

        return BaseTypeName(form.TypeName) is
            "CrescentSpear"
            or "GoldAxe"
            or "MindBlast"
            or "Supermassive";
    }

    private static string? PowerKey(string? parameter)
    {
        const string prefix = "power:";
        if (parameter is null || !parameter.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string key = parameter[prefix.Length..];
        int separator = key.IndexOf(';', StringComparison.Ordinal);
        return separator >= 0 ? key[..separator] : key;
    }

    private static bool IsSupportedPersistentPowerTrigger(CardActionFact action)
    {
        return action.Kind == "persistentPowerTrigger"
            && action.Parameter is
                "AfterStarsSpent:gainBlockPerStarSpent"
                or "AfterCardPlayed:damageAllEnemiesOnStarSpent"
                or "AfterStarsGained:damageAllEnemiesOnStarGained";
    }

    private static string FormModelId(CardForm form)
    {
        return form.UpgradeLevel <= 0 ? form.ModelId : $"{form.ModelId}+{form.UpgradeLevel}";
    }

    private static string FormTypeName(CardForm form)
    {
        return form.UpgradeLevel <= 0 ? form.TypeName : $"{form.TypeName}+{form.UpgradeLevel}";
    }

    private static string BaseTypeName(string typeName)
    {
        int upgradeSeparator = typeName.IndexOf('+', StringComparison.Ordinal);
        return upgradeSeparator < 0 ? typeName : typeName[..upgradeSeparator];
    }

    private static bool IsBeatIntoShape(CardForm form)
    {
        return string.Equals(form.TypeName, "BeatIntoShape", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBeatIntoShapeCalculationBaseDamage(CardForm form, CardValueContribution contribution)
    {
        return IsBeatIntoShape(form)
            && string.Equals(contribution.TermKind, "damage", StringComparison.Ordinal)
            && string.Equals(contribution.Parameter, "calculationBase", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBeatIntoShapeCalculationBaseDamage(CardForm form, CardActionFact action)
    {
        return IsBeatIntoShape(form)
            && string.Equals(action.Kind, "damage", StringComparison.Ordinal)
            && string.Equals(action.Parameter, "calculationBase", StringComparison.OrdinalIgnoreCase);
    }
}
