namespace CardValueOverlay.Modeling.Estimation;

public sealed class DefenseCalibrationEstimator
{
    public DefenseCalibrationReport Estimate(
        IReadOnlyList<EnemyExpectationProfile> enemies,
        ValueCalibration calibration)
    {
        List<string> warnings = [];
        if (enemies.Count == 0)
        {
            warnings.Add("No enemy expectations were provided.");
            return EmptyReport(warnings);
        }

        int needsReview = enemies.Count(enemy => enemy.Warnings.Count > 0 || enemy.Confidence < 0.7);
        decimal averageDamage = Average(enemies, Asc10AverageDamagePerMove);
        decimal ascensionAverageDamage = averageDamage;
        decimal averageAttackRate = Average(enemies, enemy => enemy.AttackMoveRate);
        decimal averageWeak = Average(enemies, enemy => enemy.ExpectedWeakPerMove);
        decimal averageVulnerable = Average(enemies, enemy => enemy.ExpectedVulnerablePerMove);
        decimal averageFrail = Average(enemies, enemy => enemy.ExpectedFrailPerMove);
        decimal averageStrengthGain = Average(enemies, enemy => enemy.ExpectedStrengthGainPerMove);

        if (needsReview > 0)
        {
            warnings.Add($"{needsReview} enemy expectation profiles need review before using this as a final calibration source.");
        }

        IReadOnlyList<FightDefenseExpectation> fightExpectations = calibration.ExpectedCombatTurns
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FightDefenseExpectation(
                pair.Key,
                Round(pair.Value),
                Round(averageDamage * pair.Value),
                Round(averageDamage * pair.Value),
                Round(averageWeak * pair.Value),
                Round(averageVulnerable * pair.Value),
                Round(averageFrail * pair.Value),
                Round(averageStrengthGain * pair.Value)))
            .ToArray();

        int[] layers = calibration.LayerBreakpoints.Length > 0
            ? calibration.LayerBreakpoints
            : calibration.BlockToDamage.Keys.Select(int.Parse).Order().ToArray();

        IReadOnlyList<LayerDefensePressure> layerPressures = layers
            .Select(layer =>
            {
                decimal ascensionMix = 1m;
                bool hasManualPressure = calibration.DefensePressure.Count > 0;
                decimal effectiveDamage = hasManualPressure
                    ? calibration.GetLayeredValue(calibration.DefensePressure, layer, "defensePressure")
                    : averageDamage;
                decimal blockToDamage = calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage");
                decimal damageUnit = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnitValue");
                return new LayerDefensePressure(
                    layer,
                    hasManualPressure ? "manualDefensePressure" : "enemyExpectationAverage",
                    Round(ascensionMix),
                    Round(effectiveDamage),
                    Round(blockToDamage),
                    Round(damageUnit),
                    Round(blockToDamage * damageUnit),
                    blockToDamage == 0m ? 0m : Round(effectiveDamage / blockToDamage));
            })
            .ToArray();

        return new DefenseCalibrationReport(
            enemies.Count,
            needsReview,
            Round(averageDamage),
            Round(ascensionAverageDamage),
            Percentile(enemies.Select(Asc10AverageDamagePerMove), 0.50m),
            Percentile(enemies.Select(Asc10AverageDamagePerMove), 0.75m),
            Percentile(enemies.Select(Asc10AverageDamagePerMove), 0.90m),
            Round(enemies.Max(Asc10AverageDamagePerMove)),
            Round(averageAttackRate),
            Round(averageWeak),
            Round(averageVulnerable),
            Round(averageFrail),
            Round(averageStrengthGain),
            fightExpectations,
            layerPressures,
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "enemy_expectations.generated.json + model_calibration.json defense calibration report v1");
    }

    private static decimal Asc10AverageDamagePerMove(EnemyExpectationProfile enemy)
    {
        return enemy.AscensionAverageDamagePerMove ?? enemy.AverageDamagePerMove;
    }

    private static DefenseCalibrationReport EmptyReport(IReadOnlyList<string> warnings)
    {
        return new DefenseCalibrationReport(
            0,
            0,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            [],
            [],
            warnings,
            "enemy_expectations.generated.json + model_calibration.json defense calibration report v1");
    }

    private static decimal Average(
        IReadOnlyList<EnemyExpectationProfile> enemies,
        Func<EnemyExpectationProfile, decimal> selector)
    {
        return enemies.Count == 0 ? 0m : enemies.Average(selector);
    }

    private static decimal Percentile(IEnumerable<decimal> values, decimal percentile)
    {
        decimal[] sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0m;
        }

        decimal position = (sorted.Length - 1) * percentile;
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return Round(sorted[lowerIndex]);
        }

        decimal ratio = position - lowerIndex;
        return Round(sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * ratio));
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
}
