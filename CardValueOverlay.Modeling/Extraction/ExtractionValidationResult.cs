namespace CardValueOverlay.Modeling.Extraction;

public sealed record ExtractionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static ExtractionValidationResult Validate(ExtractionPaths paths)
    {
        List<string> errors = [];
        List<string> warnings = [];

        RequireDirectory(paths.GameRoot, "game root", errors);
        RequireDirectory(paths.Sts2DataDir, "StS2 data directory", errors);
        RequireFile(paths.Sts2DllPath, "sts2.dll", errors);
        RequireFile(paths.Sts2XmlPath, "sts2.xml", errors);
        RequireFile(paths.ReleaseInfoPath, "release_info.json", errors);
        RequireFile(paths.IlSpyPath, "ilspycmd", errors);

        if (paths.OutputRoot.Contains(paths.GameRoot, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Output root is inside the game directory; prefer a repository-local data directory.");
        }

        return new ExtractionValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void RequireDirectory(string path, string label, List<string> errors)
    {
        if (!Directory.Exists(path))
        {
            errors.Add($"{label} not found: {path}");
        }
    }

    private static void RequireFile(string path, string label, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label} not found: {path}");
        }
    }
}
