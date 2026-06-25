namespace CardValueOverlay.Modeling.Extraction;

public sealed record LocalizationCatalog(
    string Source,
    string Status,
    string Note,
    IReadOnlyList<string> ExpectedTables);
