namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardEffectTermCatalogEntry(
    string ModelId,
    string TypeName,
    string FullTypeName,
    int? Cost,
    string? CardType,
    string? Rarity,
    string? TargetType,
    IReadOnlyList<CardEffectTerm> Terms,
    IReadOnlyList<string> Unresolved,
    string Provenance,
    double Confidence);
