namespace CardValueOverlay.Modeling.Extraction;

public sealed record IntentCatalogEntry(
    string TypeName,
    string FullTypeName,
    string IntentKind,
    string SourceAssembly,
    string Provenance,
    double Confidence);
