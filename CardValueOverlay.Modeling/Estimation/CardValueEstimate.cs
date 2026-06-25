namespace CardValueOverlay.Modeling.Estimation;

public sealed record CardValueEstimate(
    string ModelId,
    string TypeName,
    string FullTypeName,
    int? Cost,
    string? CardType,
    string? Rarity,
    string? TargetType,
    int Layer,
    decimal? CostBaseline,
    decimal EstimatedValue,
    decimal UpgradedEstimatedValue,
    decimal SmithValue,
    double Confidence,
    IReadOnlyList<CardValueContribution> Contributions,
    IReadOnlyList<string> Warnings,
    string Provenance);
