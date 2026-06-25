namespace CardValueOverlay.Modeling.Extraction;

public sealed record ModelingExtractionOptions
{
    public string GameRoot { get; init; } =
        "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2";

    public string Sts2DataDir { get; init; } =
        "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64";

    public string OutputRoot { get; init; } = "data";

    public string? IlSpyPath { get; init; }
}
