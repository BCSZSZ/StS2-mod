namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardFactCatalogEntry(
    string ModelId,
    string TypeName,
    string FullTypeName,
    int? Cost,
    string? CardType,
    string? Rarity,
    string? TargetType,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Tags,
    IReadOnlyList<DynamicVarFact> DynamicVars,
    IReadOnlyList<UpgradeOperationFact> UpgradeOperations,
    IReadOnlyList<CardActionFact> Actions,
    IReadOnlyList<CardRawOperation> RawOperations,
    IReadOnlyList<string> Unresolved,
    string Provenance,
    double Confidence);
