namespace CardValueOverlay.Modeling.Estimation;

public sealed record EncounterLayerPressureSegment(
    int ActNumber,
    string ActLabel,
    int StartLayer,
    int EndLayer,
    string SegmentKind,
    IReadOnlyList<string> IncludedCategories,
    int EncounterCount,
    int NeedsReviewCount,
    decimal AverageOpeningDamage,
    decimal AverageOpeningDamagePerTurn,
    decimal AverageSustainDamage,
    decimal AverageSustainDamagePerTurn,
    decimal AveragePeakDamage,
    decimal AverageScalingDeltaPerTurn,
    decimal AverageWeightedPressure,
    IReadOnlyList<string> EncounterTypeNames,
    IReadOnlyList<string> Warnings);
