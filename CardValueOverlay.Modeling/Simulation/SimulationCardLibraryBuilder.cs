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
        "debuffVulnerable"
    };

    private static readonly HashSet<string> IncompleteAttributionActionKinds = new(StringComparer.Ordinal)
    {
        "draw",
        "drawNextTurn",
        "selectCards",
        "moveCardBetweenPiles",
        "transformCard",
        "createCard",
        "createCardChoices"
    };

    public IReadOnlyList<SimulationCard> Build(
        IReadOnlyList<CardFactCatalogEntry> entries,
        ValueCalibration calibration,
        int layer,
        bool includeUpgrades = false,
        IReadOnlyList<CardPoolMembershipEntry>? memberships = null,
        SimulationSetupPriorityCatalog? setupPriorities = null)
    {
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
                setupPriorities ?? SimulationSetupPriorityCatalog.Empty))
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
        SimulationSetupPriorityCatalog setupPriorities)
    {
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
            .Where(contribution => !IsRuntimeSimulatedPowerContribution(contribution, form.Actions, hasSimulatedPersistentPower || hasSimulatedPower))
            .Where(contribution => !IsRuntimeSimulatedKeywordContribution(form, contribution))
            .Sum(contribution => ContributionValue(contribution, form.UpgradeLevel));
        intrinsicValue += weakLayerEstimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Sum(contribution => ContributionValue(contribution, form.UpgradeLevel));

        decimal damageValue = DamageValue(estimate, form.UpgradeLevel, form);
        decimal staticEstimatedValue = form.UpgradeLevel > 0 ? estimate.UpgradedEstimatedValue : estimate.EstimatedValue;
        if (IsBeatIntoShape(form) || hasSimulatedScalingDamage)
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
            StaticEstimatedValue = staticEstimatedValue,
            IntrinsicValue = intrinsicValue,
            DamageValue = damageValue,
            BaseDamage = BaseDamage(form),
            DamageModifierMultiplier = DamageModifierMultiplier(form, calibration),
            DamageUnitValue = damageUnitValue,
            ScalingDamageKind = ScalingDamageKind(form),
            ScalingDamageBase = ScalingDamageBase(form),
            ScalingDamagePerUnit = ScalingDamagePerUnit(form),
            ScalingDamageTargetMultiplier = ScalingDamageTargetMultiplier(form, calibration),
            BaseBlock = BaseBlock(form),
            BlockEffectCount = BlockEffectCount(form),
            BlockValuePerBlock = blockValuePerBlock,
            AoeDamageMultiplier = aoeDamageMultiplier,
            SetupPriorityValue = setupPriorities.Resolve(FormModelId(form), form.UpgradeLevel) ?? SetupPriorityValue(form),
            EnergyCost = energyCost,
            StarCost = SumTermAmount(form, "starCost"),
            HasExplicitStarCost = HasExplicitStarCost(form),
            HasStarCostX = HasStarCostX(form),
            Draw = SumTermAmount(form, "draw"),
            DrawNextTurn = SumTermAmount(form, "drawNextTurn") + PowerAmount(form, "ForegoneConclusion"),
            BlockNextTurn = SumTermAmount(form, "blockNextTurn"),
            EnergyGain = SumTermAmount(form, "energyGain"),
            EnergyNextTurn = SumTermAmount(form, "energyNextTurn"),
            StarGain = SumTermAmount(form, "starGain"),
            StarNextTurn = SumTermAmount(form, "starNextTurn"),
            Forge = SumTermAmount(form, "forge"),
            Vulnerable = SumTermAmount(form, "debuffVulnerable"),
            Exhausts = HasKeyword(form, "Exhaust"),
            Unplayable = unplayable,
            Ethereal = HasKeyword(form, "Ethereal"),
            Retain = HasKeyword(form, "Retain"),
            Innate = HasKeyword(form, "Innate"),
            Confidence = estimate.Confidence,
            Warnings = warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Actions = form.Actions
        };
        return card;
    }

    private static decimal DamageValue(CardValueEstimate estimate, int upgradeLevel, CardForm form)
    {
        return estimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "damage", StringComparison.Ordinal))
            .Where(contribution => !IsBeatIntoShapeCalculationBaseDamage(form, contribution))
            .Sum(contribution => ContributionValue(contribution, upgradeLevel));
    }

    private static decimal BaseDamage(CardForm form)
    {
        return form.Actions
            .Where(action => string.Equals(action.Kind, "damage", StringComparison.Ordinal))
            .Where(action => !IsBeatIntoShapeCalculationBaseDamage(form, action))
            .Sum(action => (action.Amount ?? 0m) * (action.HitCount ?? 1));
    }

    private static string? ScalingDamageKind(CardForm form)
    {
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

    private static bool IsSimulatedAction(CardForm form, CardActionFact action)
    {
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
            "createCardChoices";
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

    private static decimal SetupPriorityValue(CardForm form)
    {
        if (SimulationCard.SetupPriorityForCardType(form.CardType) > 0m)
        {
            return SimulationCard.SetupPriorityForCardType(form.CardType);
        }

        return BaseTypeName(form.TypeName) is "TheBomb" or "Monologue"
            ? SimulationCard.PowerSetupPriorityValue
            : 0m;
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
