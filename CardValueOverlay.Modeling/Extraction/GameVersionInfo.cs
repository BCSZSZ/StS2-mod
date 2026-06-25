namespace CardValueOverlay.Modeling.Extraction;

public sealed record GameVersionInfo(
    string? Version,
    string? Commit,
    string? Branch,
    string? Date,
    int? MainAssemblyHash,
    string Sts2DllPath,
    long Sts2DllSize,
    DateTimeOffset Sts2DllLastWriteTimeUtc,
    string Sts2XmlPath,
    long Sts2XmlSize,
    DateTimeOffset Sts2XmlLastWriteTimeUtc,
    string ExtractedAtUtc);
