namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardForm(
    string ModelId,
    string TypeName,
    string FullTypeName,
    int UpgradeLevel,
    int? Cost,
    string? CardType,
    string? Rarity,
    string? TargetType,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CardActionFact> Actions,
    IReadOnlyList<string> Unresolved,
    string Provenance,
    double Confidence);

