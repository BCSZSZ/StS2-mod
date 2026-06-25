namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardEffectTerm(
    string Kind,
    decimal? Amount,
    decimal? UpgradeDelta,
    int? HitCount,
    string? TargetType,
    string? Parameter,
    string Source,
    double Confidence);
