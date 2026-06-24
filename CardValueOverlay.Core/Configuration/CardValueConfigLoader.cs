using System.Text.Json;

namespace CardValueOverlay.Core.Configuration;

public static class CardValueConfigLoader
{
    public static CardValueConfig LoadFromFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CardValueConfig>(stream, CreateJsonOptions())
            ?? CardValueConfig.CreateDefault();
    }

    public static CardValueConfig LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<CardValueConfig>(json, CreateJsonOptions())
            ?? CardValueConfig.CreateDefault();
    }

    public static string ToJson(CardValueConfig config)
    {
        return JsonSerializer.Serialize(config, CreateJsonOptions());
    }

    public static ConfigValidationResult Validate(CardValueConfig config)
    {
        List<string> errors = [];
        List<string> warnings = [];

        if (config.SchemaVersion != CardValueConfig.SupportedSchemaVersion)
        {
            errors.Add($"schemaVersion must be {CardValueConfig.SupportedSchemaVersion}.");
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

        foreach ((string cardKey, CardValueEntry entry) in config.Cards)
        {
            if (!entry.ManualValues.HasAnyValue
                && !entry.SmithValues.HasAnyValue)
            {
                warnings.Add($"cards.{cardKey} has no manualValues or smithValues.");
            }

            if (entry.ManualValues.HasAnyValue
                && (!entry.ManualValues.Unupgraded.HasAnyValue || !entry.ManualValues.Upgraded.HasAnyValue))
            {
                warnings.Add($"cards.{cardKey}.manualValues should include both unupgraded and upgraded values.");
            }

            if (entry.SmithValues.HasAnyValue
                && (!entry.SmithValues.Unupgraded.HasAnyValue || !entry.SmithValues.Upgraded.HasAnyValue))
            {
                warnings.Add($"cards.{cardKey}.smithValues should include both unupgraded and upgraded values.");
            }

            WarnIfMissingBaseLayer(warnings, $"cards.{cardKey}.manualValues.unupgraded", entry.ManualValues.Unupgraded);
            WarnIfMissingBaseLayer(warnings, $"cards.{cardKey}.manualValues.upgraded", entry.ManualValues.Upgraded);
            WarnIfMissingBaseLayer(warnings, $"cards.{cardKey}.smithValues.unupgraded", entry.SmithValues.Unupgraded);
            WarnIfMissingBaseLayer(warnings, $"cards.{cardKey}.smithValues.upgraded", entry.SmithValues.Upgraded);
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

        foreach ((string parameterKey, CommonParameterEntry entry) in config.CommonParameters)
        {
            WarnIfMissingBaseLayer(warnings, $"commonParameters.{parameterKey}.fixedValues", entry.FixedValues);
        }

        return new ConfigValidationResult(errors, warnings);
    }

    private static void WarnIfMissingBaseLayer(
        List<string> warnings,
        string path,
        LayeredValueTable table)
    {
        if (table.HasAnyValue && !table.HasBaseLayer)
        {
            warnings.Add($"{path} should include layer 1 as its base threshold.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        options.Converters.Add(new OverlayDisplayModeJsonConverter());
        options.Converters.Add(new LayeredValueTableJsonConverter());
        return options;
    }
}
