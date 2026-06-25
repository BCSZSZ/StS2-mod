namespace CardValueOverlay.Modeling.Extraction;

public sealed record ModelCatalogEntry(
    string ModelKind,
    string TypeName,
    string FullTypeName,
    string ModelId,
    string SourceAssembly,
    string Provenance,
    double Confidence);
