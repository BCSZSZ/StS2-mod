namespace CardValueOverlay.Modeling.Combat;

public sealed record CombatPhysicalMetrics(
    double Value,
    double ActualEnemyHpDamage,
    double EnemyHpRestored,
    double HpUtility,
    double PlayerHpLost,
    double OverkillDamage,
    double UnusedPlayerBlock,
    double DeathProbability,
    double ReferenceTailHpLoss,
    double TurnsElapsed)
{
    public static CombatPhysicalMetrics operator +(CombatPhysicalMetrics left, CombatPhysicalMetrics right) => new(
        left.Value + right.Value,
        left.ActualEnemyHpDamage + right.ActualEnemyHpDamage,
        left.EnemyHpRestored + right.EnemyHpRestored,
        left.HpUtility + right.HpUtility,
        left.PlayerHpLost + right.PlayerHpLost,
        left.OverkillDamage + right.OverkillDamage,
        left.UnusedPlayerBlock + right.UnusedPlayerBlock,
        left.DeathProbability + right.DeathProbability,
        left.ReferenceTailHpLoss + right.ReferenceTailHpLoss,
        left.TurnsElapsed + right.TurnsElapsed);

    public CombatPhysicalMetrics Scale(double factor) => new(
        Value * factor,
        ActualEnemyHpDamage * factor,
        EnemyHpRestored * factor,
        HpUtility * factor,
        PlayerHpLost * factor,
        OverkillDamage * factor,
        UnusedPlayerBlock * factor,
        DeathProbability * factor,
        ReferenceTailHpLoss * factor,
        TurnsElapsed * factor);

    public static CombatPhysicalMetrics Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record CombatContextResult(
    string SampleId,
    int Horizon,
    CombatPhysicalMetrics Metrics,
    CombatSolveStatus SolverStatus,
    long CanonicalStates,
    long MemoHits,
    long DecisionNodes,
    long ChanceNodes,
    long OutcomeBranches,
    TimeSpan WallTime,
    long AllocatedBytes);
