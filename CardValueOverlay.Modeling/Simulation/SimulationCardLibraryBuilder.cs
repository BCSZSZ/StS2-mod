using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class SimulationCardLibraryBuilder
{
    private const decimal DefaultAoeDamageMultiplier = 1.3m;

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
        "debuffVulnerable"
    };

    public IReadOnlyList<SimulationCard> Build(
        IReadOnlyList<CardFactCatalogEntry> entries,
        ValueCalibration calibration,
        int layer,
        bool includeUpgrades = false)
    {
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
                calibration,
                layer))
            .OrderBy(card => card.TypeName, StringComparer.Ordinal)
            .ThenBy(card => card.UpgradeLevel)
            .ToArray();
    }

    private static SimulationCard BuildCard(
        CardForm form,
        CardValueEstimate estimate,
        CardValueEstimate weakLayerEstimate,
        ValueCalibration calibration,
        int layer)
    {
        bool hasSimulatedPersistentPower = form.Actions.Any(IsSupportedPersistentPowerTrigger);
        bool hasSimulatedCardObjectAction = form.Actions.Any(action =>
            IsSupportedCardObjectAction(action) || IsSupportedTransformAction(action));
        List<string> warnings =
        [
            .. estimate.Warnings.Where(warning => !IsSimulatorManagedEstimateWarning(warning, hasSimulatedPersistentPower, hasSimulatedCardObjectAction)),
            .. form.Unresolved
        ];
        warnings.AddRange(form.Actions
            .Where(action => !IsSimulatedAction(action))
            .Select(action => $"Unsupported simulation action '{action.Kind}' from {action.Source}."));
        int energyCost = form.Cost.GetValueOrDefault(-1);
        bool unplayable = !form.Cost.HasValue || form.Cost.Value < 0 || HasKeyword(form, "Unplayable");
        decimal damageUnitValue = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnitValue");
        decimal blockValuePerBlock = calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage") * damageUnitValue;
        decimal aoeDamageMultiplier = calibration.GetNamedValue(calibration.TargetingPenalties, "aoeDamageMultiplier", DefaultAoeDamageMultiplier);

        decimal intrinsicValue = estimate.Contributions
            .Where(contribution => !SimulatedResourceKinds.Contains(contribution.TermKind))
            .Where(contribution => !string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Where(contribution => !IsRuntimeSimulatedPersistentPowerContribution(contribution, hasSimulatedPersistentPower))
            .Where(contribution => !IsRuntimeSimulatedRetainContribution(contribution))
            .Sum(contribution => ContributionValue(contribution, form.UpgradeLevel));
        intrinsicValue += weakLayerEstimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Sum(contribution => ContributionValue(contribution, form.UpgradeLevel));

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
            Layer = layer,
            StaticEstimatedValue = form.UpgradeLevel > 0 ? estimate.UpgradedEstimatedValue : estimate.EstimatedValue,
            IntrinsicValue = intrinsicValue,
            DamageValue = DamageValue(estimate, form.UpgradeLevel),
            DamageUnitValue = damageUnitValue,
            BlockValuePerBlock = blockValuePerBlock,
            AoeDamageMultiplier = aoeDamageMultiplier,
            SetupPriorityValue = SimulationCard.SetupPriorityForCardType(form.CardType),
            EnergyCost = energyCost,
            StarCost = SumTermAmount(form, "starCost"),
            Draw = SumTermAmount(form, "draw"),
            DrawNextTurn = SumTermAmount(form, "drawNextTurn"),
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

    private static decimal DamageValue(CardValueEstimate estimate, int upgradeLevel)
    {
        return estimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "damage", StringComparison.Ordinal))
            .Sum(contribution => ContributionValue(contribution, upgradeLevel));
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

    private static bool HasKeyword(CardForm form, string keyword)
    {
        return form.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeSimulatedRetainContribution(CardValueContribution contribution)
    {
        return contribution.TermKind == "keyword"
            && string.Equals(contribution.Parameter, "Retain", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeSimulatedPersistentPowerContribution(
        CardValueContribution contribution,
        bool hasSimulatedPersistentPower)
    {
        return hasSimulatedPersistentPower && contribution.TermKind == "power";
    }

    private static bool IsSimulatorManagedEstimateWarning(
        string warning,
        bool hasSimulatedPersistentPower,
        bool hasSimulatedCardObjectAction)
    {
        bool persistentPowerWarning = hasSimulatedPersistentPower
            && (warning.Contains("Unsupported card action 'persistentPowerTrigger'", StringComparison.Ordinal)
                || warning.Contains("Contribution 'power' used a generic calibration fallback.", StringComparison.Ordinal));
        bool cardObjectWarning = hasSimulatedCardObjectAction
            && (warning.Contains("Unsupported card action 'selectCards'", StringComparison.Ordinal)
                || warning.Contains("Unsupported card action 'moveCardBetweenPiles'", StringComparison.Ordinal)
                || warning.Contains("Unsupported card action 'transformCard'", StringComparison.Ordinal));
        return persistentPowerWarning || cardObjectWarning;
    }

    private static bool IsSimulatedAction(CardActionFact action)
    {
        bool simulatedRuntimeAction = action.Kind is
            "damage" or
            "block" or
            "blockNextTurn" or
            "draw" or
            "drawNextTurn" or
            "energyGain" or
            "energyNextTurn" or
            "starGain" or
            "starNextTurn" or
            "starCost" or
            "forge" or
            "debuffVulnerable";
        return simulatedRuntimeAction
            || IsSupportedSelectionAction(action)
            || IsSupportedCardObjectAction(action)
            || IsSupportedTransformAction(action)
            || IsSupportedPersistentPowerInstall(action)
            || IsSupportedPersistentPowerTrigger(action);
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

    private static bool IsSupportedCardObjectAction(CardActionFact action)
    {
        return action.Kind == "moveCardBetweenPiles"
            && action.Parameter is not null
            && action.Parameter.Contains("from:", StringComparison.Ordinal)
            && (action.Parameter.Contains("to:Hand", StringComparison.Ordinal)
                || action.Parameter.Contains("to:Draw", StringComparison.Ordinal)
                || action.Parameter.Contains("to:Discard", StringComparison.Ordinal)
                || action.Parameter.Contains("to:Exhaust", StringComparison.Ordinal));
    }

    private static bool IsSupportedTransformAction(CardActionFact action)
    {
        return action.Kind == "transformCard";
    }

    private static bool IsSupportedPersistentPowerInstall(CardActionFact action)
    {
        return action.Kind == "power"
            && action.Parameter is not null
            && (action.Parameter.StartsWith("power:ChildOfTheStars", StringComparison.Ordinal)
                || action.Parameter.StartsWith("power:BlackHole", StringComparison.Ordinal));
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
}
