namespace CardValueOverlay.Modeling.Estimation;

public sealed record DefenseCalibrationReport(
    int EnemyCount,
    int NeedsReviewCount,
    decimal AverageDamagePerMove,
    decimal AscensionAverageDamagePerMove,
    decimal MedianDamagePerMove,
    decimal P75DamagePerMove,
    decimal P90DamagePerMove,
    decimal MaxDamagePerMove,
    decimal AverageAttackMoveRate,
    decimal AverageWeakPerMove,
    decimal AverageVulnerablePerMove,
    decimal AverageFrailPerMove,
    decimal AverageStrengthGainPerMove,
    IReadOnlyList<FightDefenseExpectation> FightExpectations,
    IReadOnlyList<LayerDefensePressure> LayerPressures,
    IReadOnlyList<string> Warnings,
    string Provenance);
