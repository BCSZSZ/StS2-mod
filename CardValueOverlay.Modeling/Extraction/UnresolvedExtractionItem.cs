namespace CardValueOverlay.Modeling.Extraction;

public sealed record UnresolvedExtractionItem(
    string Area,
    string Severity,
    string Message,
    string RecommendedAction);
