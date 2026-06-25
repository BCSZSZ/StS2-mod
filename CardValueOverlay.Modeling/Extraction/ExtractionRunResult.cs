namespace CardValueOverlay.Modeling.Extraction;

public sealed record ExtractionRunResult(
    GameVersionInfo GameVersion,
    IReadOnlyList<ModelCatalogEntry> Cards,
    IReadOnlyList<ModelCatalogEntry> Enemies,
    IReadOnlyList<ModelCatalogEntry> Encounters,
    IReadOnlyList<IntentCatalogEntry> Intents,
    LocalizationCatalog Localization,
    IReadOnlyList<UnresolvedExtractionItem> UnresolvedItems);
