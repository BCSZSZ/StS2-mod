using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Estimation;

public sealed class CardValueEstimator
{
    public IReadOnlyList<CardValueEstimate> Estimate(
        IReadOnlyList<CardFactCatalogEntry> entries,
        ValueCalibration calibration,
        int layer)
    {
        CardFormBuilder formBuilder = new();
        return entries
            .Select(entry => Estimate(
                formBuilder.Build(entry, upgradeLevel: 0),
                formBuilder.Build(entry, upgradeLevel: 1),
                calibration,
                layer))
            .OrderBy(entry => entry.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    public CardValueEstimate Estimate(CardFactCatalogEntry entry, ValueCalibration calibration, int layer)
    {
        CardFormBuilder formBuilder = new();
        return Estimate(
            formBuilder.Build(entry, upgradeLevel: 0),
            formBuilder.Build(entry, upgradeLevel: 1),
            calibration,
            layer);
    }

    public CardValueEstimate Estimate(CardForm baseForm, CardForm upgradedForm, ValueCalibration calibration, int layer)
    {
        FormEstimate baseEstimate = EstimateForm(baseForm, calibration, layer);
        FormEstimate upgradedEstimate = EstimateForm(upgradedForm, calibration, layer);
        decimal estimatedValue = baseEstimate.Contributions.Sum(contribution => contribution.BaseValue);
        decimal upgradedEstimatedValue = upgradedEstimate.Contributions.Sum(contribution => contribution.BaseValue);
        decimal smithValue = upgradedEstimatedValue - estimatedValue;
        decimal averageConfidence = baseEstimate.Contributions.Count == 0
            ? 0m
            : baseEstimate.Contributions.Average(contribution => contribution.Confidence);
        double confidence = Math.Min(baseForm.Confidence, (double)averageConfidence);
        decimal? baseline = GetCostBaseline(baseForm.Cost, calibration);
        List<string> warnings = [.. baseEstimate.Warnings, .. upgradedEstimate.Warnings.Select(warning => $"Upgraded: {warning}")];

        if (baseEstimate.Contributions.Count == 0)
        {
            warnings.Add("No supported contribution was estimated for this card.");
        }

        if (baseline.HasValue && estimatedValue > baseline.Value * 3m)
        {
            warnings.Add("Estimated value exceeds 3x cost baseline; review parser and calibration assumptions.");
        }

        if (baseForm.Cost.HasValue && baseForm.Cost.Value >= 0 && !baseline.HasValue)
        {
            warnings.Add($"No cost baseline is calibrated for cost {baseForm.Cost.Value}.");
        }

        return new CardValueEstimate(
            baseForm.ModelId,
            baseForm.TypeName,
            baseForm.FullTypeName,
            baseForm.Cost,
            baseForm.CardType,
            baseForm.Rarity,
            baseForm.TargetType,
            layer,
            baseline,
            Round(estimatedValue),
            Round(upgradedEstimatedValue),
            Round(smithValue),
            Math.Round(confidence, 3),
            MergeContributionDeltas(baseEstimate.Contributions, upgradedEstimate.Contributions).Select(RoundContribution).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "card facts + model_calibration.json estimator v1");
    }

    private static FormEstimate EstimateForm(CardForm form, ValueCalibration calibration, int layer)
    {
        List<CardValueContribution> contributions = [];
        List<string> warnings = [.. form.Unresolved];

        foreach (CardActionFact action in form.Actions.Concat(KeywordActions(form)))
        {
            CardValueContribution? contribution = EstimateAction(action, calibration, layer);
            if (contribution is null)
            {
                warnings.Add($"Unsupported card action '{action.Kind}' from {action.Source}.");
                continue;
            }

            contributions.Add(contribution);
            if (action.Confidence < 0.7)
            {
                warnings.Add($"Low confidence card action '{action.Kind}' from {action.Source}.");
            }

            if (contribution.Description.Contains("generic fallback", StringComparison.Ordinal)
                && !IsSimulatorManagedNeutralKeyword(action))
            {
                warnings.Add($"Contribution '{action.Kind}' used a generic calibration fallback.");
            }

            if (action.Kind == "scalingDamage")
            {
                warnings.Add("Generic calculated damage scaling requires manual review.");
            }
        }

        return new FormEstimate(contributions, warnings);
    }

    private static IReadOnlyList<CardActionFact> KeywordActions(CardForm form)
    {
        List<CardActionFact> actions = [];
        actions.AddRange(form.Keywords.Select(keyword => KeywordAction("keyword", keyword)));
        return actions;
    }

    private static CardActionFact KeywordAction(string kind, string keyword)
    {
        return new CardActionFact(
            kind,
            null,
            null,
            null,
            null,
            keyword,
            "CanonicalKeywords",
            new SourceEvidence("canonical keyword", null, null, keyword, 0.7),
            0.7);
    }

    private static CardValueContribution? EstimateAction(
        CardActionFact action,
        ValueCalibration calibration,
        int layer)
    {
        decimal amount = action.Amount ?? 0m;
        decimal targetMultiplier = GetTargetMultiplier(action.TargetType, calibration);
        decimal confidence = (decimal)action.Confidence;
        decimal damageUnit = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnitValue");

        return action.Kind switch
        {
            "damage" => Direct(
                action,
                amount * GetHitCount(action) * targetMultiplier * damageUnit,
                0m,
                targetMultiplier,
                confidence,
                "Damage converted through damageUnitValue."),
            "block" => Direct(
                action,
                amount * calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage") * damageUnit,
                0m,
                targetMultiplier,
                confidence,
                "Block converted to damage-equivalent value by layer."),
            "blockNextTurn" => Direct(
                action,
                amount
                    * calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage")
                    * damageUnit
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnBlockMultiplier", 1m),
                0m,
                targetMultiplier,
                confidence,
                "Next-turn block converted by layer and resourceValues.nextTurnBlockMultiplier."),
            "draw" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "draw", 0m),
                targetMultiplier,
                confidence,
                "Draw valued from resourceValues.draw."),
            "drawNextTurn" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "draw", 0m)
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnDrawMultiplier", 0.75m),
                targetMultiplier,
                confidence,
                "Next-turn draw discounted by resourceValues.nextTurnDrawMultiplier."),
            "energyGain" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "energy", 0m),
                targetMultiplier,
                confidence,
                "Immediate energy valued from resourceValues.energy."),
            "energyNextTurn" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "energy", 0m)
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnEnergyMultiplier", 1m),
                targetMultiplier,
                confidence,
                "Next-turn energy discounted by resourceValues.nextTurnEnergyMultiplier."),
            "starGain" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "star", 0m),
                targetMultiplier,
                confidence,
                "Stars valued from resourceValues.star."),
            "starNextTurn" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "star", 0m)
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnStarMultiplier", 0.75m),
                targetMultiplier,
                confidence,
                "Next-turn stars discounted by resourceValues.nextTurnStarMultiplier."),
            "starCost" => Direct(
                action,
                0m,
                0m,
                targetMultiplier,
                confidence,
                "Star cost is modeled as a play constraint, not direct value."),
            "forge" => Resource(
                action,
                amount,
                calibration.GetNamedValue(calibration.ResourceValues, "forge", 0m),
                targetMultiplier,
                confidence,
                "Forge valued from resourceValues.forge."),
            "hpLoss" => Resource(
                action,
                amount,
                -calibration.GetNamedValue(calibration.ResourceValues, "selfHpLossPenalty", 0m),
                1m,
                confidence,
                "Self HP loss penalty from resourceValues.selfHpLossPenalty."),
            "debuffVulnerable" => Vulnerable(action, amount, targetMultiplier, confidence, calibration, layer),
            "debuffWeak" => Weak(action, amount, targetMultiplier, confidence, calibration, layer),
            "debuffPoison" => Power(action, "Poison", amount, targetMultiplier, confidence, calibration),
            "power" => Power(action, PowerKey(action), amount, targetMultiplier, confidence, calibration),
            "keyword" => Keyword(action, amount: 1m, targetMultiplier, confidence, calibration),
            "scalingDamagePerCardTag" => ScalingDamage(action, amount, targetMultiplier, confidence, calibration, damageUnit),
            "scalingDamage" => ScalingDamage(action, amount, targetMultiplier, confidence * 0.75m, calibration, damageUnit),
            _ => null
        };
    }

    private static CardValueContribution Direct(
        CardActionFact action,
        decimal baseValue,
        decimal upgradeValue,
        decimal targetMultiplier,
        decimal confidence,
        string description)
    {
        return new CardValueContribution(
            action.Kind,
            action.Source,
            action.Amount,
            baseValue,
            upgradeValue,
            targetMultiplier,
            confidence,
            action.Parameter,
            description);
    }

    private static CardValueContribution Resource(
        CardActionFact action,
        decimal amount,
        decimal unitValue,
        decimal targetMultiplier,
        decimal confidence,
        string description)
    {
        return Direct(action, amount * unitValue, 0m, targetMultiplier, confidence, description);
    }

    private static CardValueContribution Power(
        CardActionFact action,
        string key,
        decimal amount,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration)
    {
        bool hasSpecificValue = calibration.PowerValues.ContainsKey(key);
        decimal unitValue = hasSpecificValue
            ? calibration.PowerValues[key]
            : calibration.GetNamedValue(calibration.PowerValues, "generic", 0m);
        return Direct(
            action,
            amount * unitValue * targetMultiplier,
            0m,
            targetMultiplier,
            confidence,
            hasSpecificValue
                ? $"Power/debuff valued from powerValues.{key}."
                : $"Power/debuff valued from powerValues.generic fallback for {key}.");
    }

    private static CardValueContribution Weak(
        CardActionFact action,
        decimal amount,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration,
        int layer)
    {
        decimal pressure = calibration.GetLayeredValue(calibration.DefensePressure, layer, "defensePressure");
        decimal blockToDamage = calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage");
        decimal damageReduction = calibration.GetNamedValue(calibration.WeakValueParameters, "damageReduction", 0.25m);
        decimal unitValue = pressure * damageReduction * blockToDamage;
        decimal baseMultiplier = GetDebuffStackMultiplier(amount, calibration);
        return Direct(
            action,
            unitValue * baseMultiplier * targetMultiplier,
            0m,
            targetMultiplier,
            confidence,
            "Weak valued as equivalent prevented damage from defensePressure, damageReduction, and blockToDamage.");
    }

    private static CardValueContribution Vulnerable(
        CardActionFact action,
        decimal amount,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration,
        int layer)
    {
        decimal pressure = calibration.GetLayeredValue(calibration.DefensePressure, layer, "defensePressure");
        decimal basePressure = calibration.GetNamedValue(calibration.VulnerableValueParameters, "basePressure", 8m);
        decimal baseValue = calibration.GetNamedValue(calibration.VulnerableValueParameters, "baseValue", 5m);
        decimal pressureGrowthMultiplier = calibration.GetNamedValue(calibration.VulnerableValueParameters, "pressureGrowthMultiplier", 0.9m);
        decimal pressureRatio = basePressure == 0m ? 1m : pressure / basePressure;
        decimal unitValue = baseValue * (1m + (pressureGrowthMultiplier * (pressureRatio - 1m)));
        decimal baseMultiplier = GetDebuffStackMultiplier(amount, calibration);
        return Direct(
            action,
            unitValue * baseMultiplier * targetMultiplier,
            0m,
            targetMultiplier,
            confidence,
            "Vulnerable valued from baseValue at basePressure, scaled by defensePressure with compressed growth.");
    }

    private static decimal GetDebuffStackMultiplier(decimal stacks, ValueCalibration calibration)
    {
        if (stacks <= 0m)
        {
            return 0m;
        }

        int wholeStacks = (int)Math.Floor(stacks);
        decimal fractionalStacks = stacks - wholeStacks;
        decimal wholeMultiplier = GetWholeDebuffStackMultiplier(wholeStacks, calibration);
        if (fractionalStacks == 0m)
        {
            return wholeMultiplier;
        }

        decimal nextMultiplier = GetWholeDebuffStackMultiplier(wholeStacks + 1, calibration);
        return wholeMultiplier + ((nextMultiplier - wholeMultiplier) * fractionalStacks);
    }

    private static decimal GetWholeDebuffStackMultiplier(int stacks, ValueCalibration calibration)
    {
        if (stacks <= 0)
        {
            return 0m;
        }

        string key = stacks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (calibration.DebuffStackMultipliers.TryGetValue(key, out decimal configured))
        {
            return configured;
        }

        decimal oneStack = calibration.GetNamedValue(calibration.DebuffStackMultipliers, "1", 1m);
        decimal twoStacks = calibration.GetNamedValue(calibration.DebuffStackMultipliers, "2", 1.5m);
        decimal threeStacks = calibration.GetNamedValue(calibration.DebuffStackMultipliers, "3", 1.9m);
        if (stacks == 1)
        {
            return oneStack;
        }

        if (stacks == 2)
        {
            return twoStacks;
        }

        if (stacks == 3)
        {
            return threeStacks;
        }

        decimal previousIncrement = twoStacks - oneStack;
        decimal lastIncrement = threeStacks - twoStacks;
        decimal decayRatio = previousIncrement <= 0m
            ? 0.8m
            : Math.Clamp(lastIncrement / previousIncrement, 0m, 1m);
        decimal multiplier = threeStacks;
        decimal increment = lastIncrement;
        for (int i = 4; i <= stacks; i++)
        {
            increment *= decayRatio;
            multiplier += increment;
        }

        return multiplier;
    }

    private static CardValueContribution Keyword(
        CardActionFact action,
        decimal amount,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration)
    {
        string key = action.Parameter ?? "generic";
        bool hasSpecificValue = calibration.KeywordValues.ContainsKey(key);
        decimal unitValue = hasSpecificValue
            ? calibration.KeywordValues[key]
            : calibration.GetNamedValue(calibration.KeywordValues, "generic", 0m);
        return Direct(
            action,
            amount * unitValue,
            0m,
            targetMultiplier,
            confidence,
            hasSpecificValue
                ? $"Keyword valued from keywordValues.{key}."
                : $"Keyword valued from keywordValues.generic fallback for {key}.");
    }

    private static bool IsSimulatorManagedNeutralKeyword(CardActionFact action)
    {
        if (action.Kind != "keyword")
        {
            return false;
        }

        return action.Parameter is "Eternal" or "Ethereal" or "Unplayable";
    }

    private static CardValueContribution ScalingDamage(
        CardActionFact action,
        decimal amount,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration,
        decimal damageUnit)
    {
        string key = action.Parameter ?? "generic";
        bool hasSpecificValue = calibration.ScalingAssumptions.ContainsKey(key);
        decimal multiplier = hasSpecificValue
            ? calibration.ScalingAssumptions[key]
            : calibration.GetNamedValue(calibration.ScalingAssumptions, "generic", 1m);

        return Direct(
            action,
            amount * multiplier * targetMultiplier * damageUnit,
            0m,
            targetMultiplier,
            confidence,
            hasSpecificValue
                ? $"Scaling damage uses scalingAssumptions.{key}."
                : $"Scaling damage uses scalingAssumptions.generic fallback for {key}.");
    }

    private static IReadOnlyList<CardValueContribution> MergeContributionDeltas(
        IReadOnlyList<CardValueContribution> baseContributions,
        IReadOnlyList<CardValueContribution> upgradedContributions)
    {
        Dictionary<string, CardValueContribution> upgradedByKey = upgradedContributions
            .GroupBy(ContributionKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate((left, right) => left with { BaseValue = left.BaseValue + right.BaseValue }),
                StringComparer.Ordinal);
        List<CardValueContribution> merged = [];
        HashSet<string> consumed = [];

        foreach (CardValueContribution contribution in baseContributions)
        {
            string key = ContributionKey(contribution);
            consumed.Add(key);
            decimal upgradedValue = upgradedByKey.TryGetValue(key, out CardValueContribution? upgraded)
                ? upgraded.BaseValue
                : 0m;
            merged.Add(contribution with { UpgradeValue = upgradedValue - contribution.BaseValue });
        }

        foreach (CardValueContribution contribution in upgradedByKey.Values.Where(contribution => !consumed.Contains(ContributionKey(contribution))))
        {
            merged.Add(contribution with { BaseValue = 0m, UpgradeValue = contribution.BaseValue });
        }

        return merged;
    }

    private static string ContributionKey(CardValueContribution contribution)
    {
        return string.Join(
            "|",
            contribution.TermKind,
            contribution.Source,
            contribution.Parameter ?? "");
    }

    private static decimal GetTargetMultiplier(string? targetType, ValueCalibration calibration)
    {
        return targetType switch
        {
            "AllEnemies" => calibration.GetNamedValue(calibration.TargetingPenalties, "aoeDamageMultiplier", 1.3m),
            "RandomEnemy" => calibration.GetNamedValue(calibration.TargetingPenalties, "randomTargetMultiplier", 1m),
            _ => 1m
        };
    }

    private static decimal GetHitCount(CardActionFact action)
    {
        return action.HitCount ?? 1;
    }

    private static string PowerKey(CardActionFact action)
    {
        const string prefix = "power:";
        if (action.Parameter is null || !action.Parameter.StartsWith(prefix, StringComparison.Ordinal))
        {
            return "generic";
        }

        string key = action.Parameter[prefix.Length..];
        int separator = key.IndexOf(';', StringComparison.Ordinal);
        return separator >= 0 ? key[..separator] : key;
    }

    private static decimal? GetCostBaseline(int? cost, ValueCalibration calibration)
    {
        if (!cost.HasValue)
        {
            return null;
        }

        return calibration.BaselineCardValues.TryGetValue($"cost{cost.Value}", out decimal value)
            ? value
            : null;
    }

    private static decimal Sqrt(decimal value)
    {
        return (decimal)Math.Sqrt((double)value);
    }

    private static CardValueContribution RoundContribution(CardValueContribution contribution)
    {
        return contribution with
        {
            BaseValue = Round(contribution.BaseValue),
            UpgradeValue = Round(contribution.UpgradeValue),
            TargetMultiplier = Round(contribution.TargetMultiplier),
            Confidence = Round(contribution.Confidence)
        };
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    private sealed record FormEstimate(
        IReadOnlyList<CardValueContribution> Contributions,
        IReadOnlyList<string> Warnings);
}
