namespace CardValueOverlay.Modeling.Estimation;

public sealed record EnemyMoveExpectation(
    string StateId,
    decimal Damage,
    decimal? AscensionDamage,
    decimal Block,
    decimal? AscensionBlock,
    decimal Weak,
    decimal Vulnerable,
    decimal Frail,
    decimal StrengthGain,
    decimal AttackHitCount,
    decimal Weight,
    double Confidence,
    IReadOnlyList<string> Warnings);
