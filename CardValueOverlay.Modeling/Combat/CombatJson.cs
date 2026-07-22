using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Combat;

public static class CombatJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256Files(IEnumerable<string> paths)
    {
        string manifest = string.Join('|', paths
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetFileName(path)}:{Sha256File(path)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
