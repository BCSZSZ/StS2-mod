using System.Text.Json;

namespace CardValueOverlay.Modeling.Extraction;

public static class MachineProfilePaths
{
    private const string DefaultWindowsSts2Path =
        "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2";

    public static string DefaultSts2Path =>
        FirstPath(
            GetActiveProfilePath("sts2Path"),
            EnvironmentPath("STS2_PATH"),
            DefaultWindowsSts2Path);

    public static string DefaultSts2DataDir =>
        FirstPath(
            GetActiveProfilePath("sts2DataDir"),
            Path.Combine(DefaultSts2Path, "data_sts2_windows_x86_64"));

    public static string DefaultSts2XmlPath => Path.Combine(DefaultSts2DataDir, "sts2.xml");

    public static string? DefaultIlSpycmdPath =>
        FirstPathOrNull(
            GetActiveProfilePath("ilspycmdPath"),
            EnvironmentPath("ILSPYCMD_PATH"),
            EnvironmentPath("LIAO_ILSPYCMD"));

    public static string? GetActiveProfilePath(string propertyName)
    {
        string? profileName = EnvironmentPath("STS2_MOD_PROFILE");
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        string? profileJson = EnvironmentPath(profileName);
        if (string.IsNullOrWhiteSpace(profileJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(profileJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? EnvironmentPath(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
    }

    private static string FirstPath(params string?[] paths)
    {
        return FirstPathOrNull(paths)
            ?? throw new InvalidOperationException("At least one fallback path is required.");
    }

    private static string? FirstPathOrNull(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }
}
