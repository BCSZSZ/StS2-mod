namespace CardValueOverlay.Core.Analysis;

public sealed record AverageExpectationResult(
    int RequestedCount,
    int ValuedCount,
    int MissingCount,
    double? Average,
    IReadOnlyList<string> MissingKeys);
