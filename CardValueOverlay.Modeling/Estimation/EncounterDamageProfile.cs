namespace CardValueOverlay.Modeling.Estimation;

public sealed record EncounterDamageProfile(
    string ModelId,
    string TypeName,
    IReadOnlyList<string> ActTypeNames,
    IReadOnlyList<int> ActNumbers,
    string Category,
    int TurnCount,
    decimal OpeningDamage,
    decimal OpeningDamagePerTurn,
    decimal SustainDamage,
    decimal SustainDamagePerTurn,
    decimal PeakDamage,
    decimal ScalingDeltaPerTurn,
    decimal WeightedPressure,
    IReadOnlyList<decimal> TurnDamages,
    int MonsterSlotCount,
    bool HasConditionalMonsterSelection,
    double Confidence,
    IReadOnlyList<string> Warnings);
