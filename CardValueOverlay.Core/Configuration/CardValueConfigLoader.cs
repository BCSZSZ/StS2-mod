using System.Text.Json;
using System.Text.Json.Serialization;

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

        if (!OverlaySettings.TryParseDisplayMode(config.Overlay.DisplayModeKey, out _))
        {
            errors.Add($"overlay.displayMode '{config.Overlay.DisplayModeKey}' is not supported.");
        }

        if (!OverlaySettings.TryParseValueHorizon(config.Overlay.ValueHorizonKey, out _))
        {
            errors.Add($"overlay.valueHorizon '{config.Overlay.ValueHorizonKey}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(config.Overlay.FixedText))
        {
            warnings.Add("overlay.fixedText is empty; fixedText mode will render nothing.");
        }

        if (config.Cards.Count == 0)
        {
            warnings.Add("cards is empty; training card values will not render yet.");
        }

        foreach ((string cardKey, CardValueEntry entry) in config.Cards)
        {
            if (!entry.TrainingValues.HasAnyValue)
            {
                warnings.Add($"cards.{cardKey} has no trainingValues.");
            }

            WarnIfMissingTrainingHorizon(warnings, $"cards.{cardKey}.trainingValues.unupgraded", entry.TrainingValues.Unupgraded);
            WarnIfMissingTrainingHorizon(warnings, $"cards.{cardKey}.trainingValues.upgraded", entry.TrainingValues.Upgraded);
            WarnIfInvalidGenerationMetadata(warnings, $"cards.{cardKey}.generation", entry.Generation);
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
            foreach (int layer in entry.FixedValues.Keys)
            {
                if (layer < 1)
                {
                    errors.Add($"commonParameters.{parameterKey}.fixedValues layer {layer} must be 1 or greater.");
                }
            }

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

    private static void WarnIfMissingTrainingHorizon(
        List<string> warnings,
        string path,
        TrainingHorizonValues values)
    {
        if (values.HasAnyValue && !values.HasAllValues)
        {
            warnings.Add($"{path} should include shortline, midline, and longline values.");
        }
    }

    private static void WarnIfInvalidGenerationMetadata(
        List<string> warnings,
        string path,
        CardValueGenerationMetadata? generation)
    {
        if (generation is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(generation.Method))
        {
            warnings.Add($"{path}.method is empty.");
        }
        else if (!CardValueGenerationMethods.IsKnown(generation.Method))
        {
            warnings.Add($"{path}.method should be monteCarlo or estimate.");
        }

        TrainingHorizonTimestamps? updatedAt = generation.UpdatedAt;
        if (updatedAt is null || !updatedAt.HasAnyValue)
        {
            warnings.Add($"{path}.updatedAt is empty.");
            return;
        }

        if (!updatedAt.HasAllValues)
        {
            warnings.Add($"{path}.updatedAt should include shortline, midline, and longline timestamps.");
        }

        WarnIfInvalidTimestamp(warnings, $"{path}.updatedAt.shortline", updatedAt.Shortline);
        WarnIfInvalidTimestamp(warnings, $"{path}.updatedAt.midline", updatedAt.Midline);
        WarnIfInvalidTimestamp(warnings, $"{path}.updatedAt.longline", updatedAt.Longline);
    }

    private static void WarnIfInvalidTimestamp(List<string> warnings, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!DateTimeOffset.TryParse(value, out _))
        {
            warnings.Add($"{path} should be an ISO-8601 timestamp with an offset.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        return options;
    }
}
