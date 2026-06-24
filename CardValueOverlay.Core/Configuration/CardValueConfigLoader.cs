using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public static class CardValueConfigLoader
{
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static CardValueConfig LoadFromFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CardValueConfig>(stream, JsonOptions) ?? CardValueConfig.CreateDefault();
    }

    public static CardValueConfig LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<CardValueConfig>(json, JsonOptions) ?? CardValueConfig.CreateDefault();
    }

    public static string ToJson(CardValueConfig config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static ConfigValidationResult Validate(CardValueConfig config)
    {
        List<string> errors = [];
        List<string> warnings = [];

        if (config.SchemaVersion != 1)
        {
            warnings.Add($"Unknown schemaVersion {config.SchemaVersion}; loader will still try to use known fields.");
        }

        if (config.Overlay.MaxLines is < 1 or > 3)
        {
            errors.Add("overlay.maxLines must be between 1 and 3.");
        }

        if (string.IsNullOrWhiteSpace(config.Overlay.FixedText))
        {
            warnings.Add("overlay.fixedText is empty; fixedText mode will render nothing.");
        }

        if (config.Cards.Count == 0)
        {
            warnings.Add("cards is empty; manual card values will not render yet.");
        }

        foreach (string requiredParameter in new[]
                 {
                     CommonParameterIds.DeckCount,
                     CommonParameterIds.CardsDrawnPerTurn,
                     CommonParameterIds.TurnsPerShuffleCycle
                 })
        {
            if (!config.CommonParameters.ContainsKey(requiredParameter))
            {
                warnings.Add($"commonParameters.{requiredParameter} is missing.");
            }
        }

        return new ConfigValidationResult(errors, warnings);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
