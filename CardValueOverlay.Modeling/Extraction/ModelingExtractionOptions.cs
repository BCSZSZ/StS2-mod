namespace CardValueOverlay.Modeling.Extraction;

public sealed record ModelingExtractionOptions
{
    public string GameRoot { get; init; } = MachineProfilePaths.DefaultSts2Path;

    public string Sts2DataDir { get; init; } = MachineProfilePaths.DefaultSts2DataDir;

    public string OutputRoot { get; init; } = "data";

    public string? IlSpyPath { get; init; }

    public string? DecompileOutputRoot { get; init; }
}
