using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Estimation;

public sealed class CardValueEstimator
{
    public IReadOnlyList<CardValueEstimate> Estimate(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        ValueCalibration calibration,
        int layer)
    {
        return entries
            .Select(entry => Estimate(entry, calibration, layer))
            .OrderBy(entry => entry.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    public CardValueEstimate Estimate(CardEffectTermCatalogEntry entry, ValueCalibration calibration, int layer)
    {
        List<CardValueContribution> contributions = [];
        List<string> warnings = [.. entry.Unresolved];

        foreach (CardEffectTerm term in entry.Terms)
        {
            CardValueContribution? contribution = EstimateTerm(term, calibration, layer);
            if (contribution is null)
            {
                warnings.Add($"Unsupported effect term '{term.Kind}' from {term.Source}.");
                continue;
            }

            contributions.Add(contribution);
            if (term.Confidence < 0.7)
            {
                warnings.Add($"Low confidence term '{term.Kind}' from {term.Source}.");
            }

            if (contribution.Description.Contains("generic fallback", StringComparison.Ordinal))
            {
                warnings.Add($"Contribution '{term.Kind}' used a generic calibration fallback.");
            }

            if (term.Kind == "scalingDamage")
            {
                warnings.Add("Generic calculated damage scaling requires manual review.");
            }
        }

        decimal estimatedValue = contributions.Sum(contribution => contribution.BaseValue);
        decimal upgradedEstimatedValue = estimatedValue + contributions.Sum(contribution => contribution.UpgradeValue);
        decimal smithValue = upgradedEstimatedValue - estimatedValue;
        decimal averageConfidence = contributions.Count == 0
            ? 0m
            : contributions.Average(contribution => contribution.Confidence);
        double confidence = Math.Min(entry.Confidence, (double)averageConfidence);
        decimal? baseline = GetCostBaseline(entry.Cost, calibration);

        if (contributions.Count == 0)
        {
            warnings.Add("No supported contribution was estimated for this card.");
        }

        if (baseline.HasValue && estimatedValue > baseline.Value * 3m)
        {
            warnings.Add("Estimated value exceeds 3x cost baseline; review parser and calibration assumptions.");
        }

        if (entry.Cost.HasValue && !baseline.HasValue)
        {
            warnings.Add($"No cost baseline is calibrated for cost {entry.Cost.Value}.");
        }

        return new CardValueEstimate(
            entry.ModelId,
            entry.TypeName,
            entry.FullTypeName,
            entry.Cost,
            entry.CardType,
            entry.Rarity,
            entry.TargetType,
            layer,
            baseline,
            Round(estimatedValue),
            Round(upgradedEstimatedValue),
            Round(smithValue),
            Math.Round(confidence, 3),
            contributions.Select(RoundContribution).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "card effect terms + model_calibration.json estimator v1");
    }

    private static CardValueContribution? EstimateTerm(
        CardEffectTerm term,
        ValueCalibration calibration,
        int layer)
    {
        decimal amount = term.Amount ?? 0m;
        decimal upgradeDelta = term.UpgradeDelta ?? 0m;
        decimal targetMultiplier = GetTargetMultiplier(term.TargetType, calibration);
        decimal confidence = (decimal)term.Confidence;
        decimal damageUnit = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnitValue");

        return term.Kind switch
        {
            "damage" => Direct(
                term,
                amount * GetHitCount(term) * targetMultiplier * damageUnit,
                upgradeDelta * GetHitCount(term) * targetMultiplier * damageUnit,
                targetMultiplier,
                confidence,
                "Damage converted through damageUnitValue."),
            "block" => Direct(
                term,
                amount * calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage") * damageUnit,
                upgradeDelta * calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage") * damageUnit,
                targetMultiplier,
                confidence,
                "Block converted to damage-equivalent value by layer."),
            "draw" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "draw", 0m),
                targetMultiplier,
                confidence,
                "Draw valued from resourceValues.draw."),
            "drawNextTurn" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "draw", 0m)
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnDrawMultiplier", 0.75m),
                targetMultiplier,
                confidence,
                "Next-turn draw discounted by resourceValues.nextTurnDrawMultiplier."),
            "energyGain" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "energy", 0m),
                targetMultiplier,
                confidence,
                "Immediate energy valued from resourceValues.energy."),
            "energyNextTurn" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "energy", 0m)
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnEnergyMultiplier", 1m),
                targetMultiplier,
                confidence,
                "Next-turn energy discounted by resourceValues.nextTurnEnergyMultiplier."),
            "starGain" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "star", 0m),
                targetMultiplier,
                confidence,
                "Stars valued from resourceValues.star."),
            "starNextTurn" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "star", 0m)
                    * calibration.GetNamedValue(calibration.ResourceValues, "nextTurnStarMultiplier", 0.75m),
                targetMultiplier,
                confidence,
                "Next-turn stars discounted by resourceValues.nextTurnStarMultiplier."),
            "starCost" => Direct(
                term,
                0m,
                0m,
                targetMultiplier,
                confidence,
                "Star cost is modeled as a play constraint, not direct value."),
            "forge" => Resource(
                term,
                amount,
                upgradeDelta,
                calibration.GetNamedValue(calibration.ResourceValues, "forge", 0m),
                targetMultiplier,
                confidence,
                "Forge valued from resourceValues.forge."),
            "hpLoss" => Resource(
                term,
                amount,
                upgradeDelta,
                -calibration.GetNamedValue(calibration.ResourceValues, "selfHpLossPenalty", 0m),
                1m,
                confidence,
                "Self HP loss penalty from resourceValues.selfHpLossPenalty."),
            "debuffVulnerable" => Vulnerable(term, amount, upgradeDelta, targetMultiplier, confidence, calibration, layer),
            "debuffWeak" => Weak(term, amount, upgradeDelta, targetMultiplier, confidence, calibration, layer),
            "debuffPoison" => Power(term, "Poison", amount, upgradeDelta, targetMultiplier, confidence, calibration),
            "power" => Power(term, PowerKey(term), amount, upgradeDelta, targetMultiplier, confidence, calibration),
            "keyword" => Keyword(term, amount: 1m, upgradeOnly: false, targetMultiplier, confidence, calibration),
            "keywordOnUpgrade" => Keyword(term, amount: 1m, upgradeOnly: true, targetMultiplier, confidence, calibration),
            "scalingDamagePerCardTag" => ScalingDamage(term, amount, upgradeDelta, targetMultiplier, confidence, calibration, damageUnit),
            "scalingDamage" => ScalingDamage(term, amount, upgradeDelta, targetMultiplier, confidence * 0.75m, calibration, damageUnit),
            _ => null
        };
    }

    private static CardValueContribution Direct(
        CardEffectTerm term,
        decimal baseValue,
        decimal upgradeValue,
        decimal targetMultiplier,
        decimal confidence,
        string description)
    {
        return new CardValueContribution(
            term.Kind,
            term.Source,
            term.Amount,
            term.UpgradeDelta,
            baseValue,
            upgradeValue,
            targetMultiplier,
            confidence,
            term.Parameter,
            description);
    }

    private static CardValueContribution Resource(
        CardEffectTerm term,
        decimal amount,
        decimal upgradeDelta,
        decimal unitValue,
        decimal targetMultiplier,
        decimal confidence,
        string description)
    {
        return Direct(term, amount * unitValue, upgradeDelta * unitValue, targetMultiplier, confidence, description);
    }

    private static CardValueContribution Power(
        CardEffectTerm term,
        string key,
        decimal amount,
        decimal upgradeDelta,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration)
    {
        bool hasSpecificValue = calibration.PowerValues.ContainsKey(key);
        decimal unitValue = hasSpecificValue
            ? calibration.PowerValues[key]
            : calibration.GetNamedValue(calibration.PowerValues, "generic", 0m);
        return Direct(
            term,
            amount * unitValue * targetMultiplier,
            upgradeDelta * unitValue * targetMultiplier,
            targetMultiplier,
            confidence,
            hasSpecificValue
                ? $"Power/debuff valued from powerValues.{key}."
                : $"Power/debuff valued from powerValues.generic fallback for {key}.");
    }

    private static CardValueContribution Weak(
        CardEffectTerm term,
        decimal amount,
        decimal upgradeDelta,
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
        decimal upgradedMultiplier = GetDebuffStackMultiplier(amount + upgradeDelta, calibration);
        return Direct(
            term,
            unitValue * baseMultiplier * targetMultiplier,
            unitValue * (upgradedMultiplier - baseMultiplier) * targetMultiplier,
            targetMultiplier,
            confidence,
            "Weak valued as equivalent prevented damage from defensePressure, damageReduction, and blockToDamage.");
    }

    private static CardValueContribution Vulnerable(
        CardEffectTerm term,
        decimal amount,
        decimal upgradeDelta,
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
        decimal upgradedMultiplier = GetDebuffStackMultiplier(amount + upgradeDelta, calibration);
        return Direct(
            term,
            unitValue * baseMultiplier * targetMultiplier,
            unitValue * (upgradedMultiplier - baseMultiplier) * targetMultiplier,
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
        CardEffectTerm term,
        decimal amount,
        bool upgradeOnly,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration)
    {
        string key = term.Parameter ?? "generic";
        bool hasSpecificValue = calibration.KeywordValues.ContainsKey(key);
        decimal unitValue = hasSpecificValue
            ? calibration.KeywordValues[key]
            : calibration.GetNamedValue(calibration.KeywordValues, "generic", 0m);
        return Direct(
            term,
            upgradeOnly ? 0m : amount * unitValue,
            upgradeOnly ? amount * unitValue : 0m,
            targetMultiplier,
            confidence,
            hasSpecificValue
                ? $"Keyword valued from keywordValues.{key}."
                : $"Keyword valued from keywordValues.generic fallback for {key}.");
    }

    private static CardValueContribution ScalingDamage(
        CardEffectTerm term,
        decimal amount,
        decimal upgradeDelta,
        decimal targetMultiplier,
        decimal confidence,
        ValueCalibration calibration,
        decimal damageUnit)
    {
        string key = term.Parameter ?? "generic";
        bool hasSpecificValue = calibration.ScalingAssumptions.ContainsKey(key);
        decimal multiplier = hasSpecificValue
            ? calibration.ScalingAssumptions[key]
            : calibration.GetNamedValue(calibration.ScalingAssumptions, "generic", 1m);

        return Direct(
            term,
            amount * multiplier * targetMultiplier * damageUnit,
            upgradeDelta * multiplier * targetMultiplier * damageUnit,
            targetMultiplier,
            confidence,
            hasSpecificValue
                ? $"Scaling damage uses scalingAssumptions.{key}."
                : $"Scaling damage uses scalingAssumptions.generic fallback for {key}.");
    }

    private static decimal GetTargetMultiplier(string? targetType, ValueCalibration calibration)
    {
        return targetType switch
        {
            "AllEnemies" => Sqrt(calibration.GetNamedValue(calibration.TargetingPenalties, "enemyCountAssumption", 2.25m)),
            "RandomEnemy" => calibration.GetNamedValue(calibration.TargetingPenalties, "randomTargetMultiplier", 1m),
            _ => 1m
        };
    }

    private static decimal GetHitCount(CardEffectTerm term)
    {
        return term.HitCount ?? 1;
    }

    private static string PowerKey(CardEffectTerm term)
    {
        const string prefix = "power:";
        return term.Parameter is not null && term.Parameter.StartsWith(prefix, StringComparison.Ordinal)
            ? term.Parameter[prefix.Length..]
            : "generic";
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
}
