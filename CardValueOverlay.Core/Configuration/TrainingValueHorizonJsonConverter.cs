using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed class TrainingValueHorizonJsonConverter : JsonConverter<TrainingValueHorizon>
{
    public override TrainingValueHorizon Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("training value horizon must be a string.");
        }

        string? value = reader.GetString();
        return value?.Trim() switch
        {
            "shortline" => TrainingValueHorizon.Shortline,
            "midline" => TrainingValueHorizon.Midline,
            "longline" => TrainingValueHorizon.Longline,
            _ => throw new JsonException($"Unknown training value horizon '{value}'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        TrainingValueHorizon value,
        JsonSerializerOptions options)
    {
        string text = value switch
        {
            TrainingValueHorizon.Shortline => "shortline",
            TrainingValueHorizon.Midline => "midline",
            TrainingValueHorizon.Longline => "longline",
            _ => throw new JsonException($"Unknown training value horizon '{value}'.")
        };

        writer.WriteStringValue(text);
    }
}
