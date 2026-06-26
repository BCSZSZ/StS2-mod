namespace CardValueOverlay.Modeling.Extraction;

public sealed record SourceEvidence(
    string SourceFile,
    string? Method,
    int? Line,
    string Fragment,
    double Confidence);
