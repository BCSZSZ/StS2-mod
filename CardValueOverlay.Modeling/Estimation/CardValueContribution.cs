namespace CardValueOverlay.Modeling.Estimation;

public sealed record CardValueContribution(
    string TermKind,
    string Source,
    decimal? Amount,
    decimal? UpgradeDelta,
    decimal BaseValue,
    decimal UpgradeValue,
    decimal TargetMultiplier,
    decimal Confidence,
    string? Parameter,
    string Description);
