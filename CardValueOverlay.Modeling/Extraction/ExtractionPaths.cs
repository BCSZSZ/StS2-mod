namespace CardValueOverlay.Modeling.Extraction;

public sealed record ExtractionPaths(
    string GameRoot,
    string Sts2DataDir,
    string Sts2DllPath,
    string Sts2XmlPath,
    string ReleaseInfoPath,
    string OutputRoot,
    string ExtractedOutputRoot,
    string GeneratedOutputRoot,
    string ManualTagsRoot,
    string IlSpyPath)
{
    public static ExtractionPaths FromOptions(ModelingExtractionOptions options)
    {
        string gameRoot = Normalize(options.GameRoot);
        string dataDir = Normalize(options.Sts2DataDir);
        string outputRoot = Normalize(options.OutputRoot);
        string ilSpyPath = ResolveIlSpyPath(options.IlSpyPath);

        return new ExtractionPaths(
            gameRoot,
            dataDir,
            Path.Combine(dataDir, "sts2.dll"),
            Path.Combine(dataDir, "sts2.xml"),
            Path.Combine(gameRoot, "release_info.json"),
            outputRoot,
            Path.Combine(outputRoot, "extracted"),
            Path.Combine(outputRoot, "generated"),
            Path.Combine(outputRoot, "manual-tags"),
            ilSpyPath);
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ResolveIlSpyPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Normalize(explicitPath);
        }

        string? envPath = Environment.GetEnvironmentVariable("ILSPYCMD_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Normalize(envPath);
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".dotnet", "tools", "ilspycmd.exe");
    }
}
