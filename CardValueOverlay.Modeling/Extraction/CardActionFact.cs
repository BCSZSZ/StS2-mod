namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardActionFact(
    string Kind,
    decimal? Amount,
    string? DynamicVarName,
    int? HitCount,
    string? TargetType,
    string? Parameter,
    string Source,
    SourceEvidence Evidence,
    double Confidence);

public sealed record DynamicVarFact(
    string Name,
    string Kind,
    decimal? Amount,
    string? Parameter,
    SourceEvidence Evidence);

public sealed record UpgradeOperationFact(
    string Kind,
    string Name,
    decimal? Amount,
    string? Parameter,
    string? Condition,
    SourceEvidence Evidence);

public sealed record CardRawOperation(
    string Kind,
    string Operation,
    string? Parameter,
    SourceEvidence Evidence);
