using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed class OverlayDisplayModeJsonConverter : JsonConverter<OverlayDisplayMode>
{
    public override OverlayDisplayMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("overlay.displayMode must be a string.");
        }

        string? value = reader.GetString();
        return value?.Trim() switch
        {
            "fixedText" => OverlayDisplayMode.FixedText,
            "cardName" => OverlayDisplayMode.CardName,
            "manualValue" => OverlayDisplayMode.ManualValue,
            "effectiveValue" => OverlayDisplayMode.EffectiveValue,
            _ => throw new JsonException($"Unknown overlay.displayMode '{value}'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        OverlayDisplayMode value,
        JsonSerializerOptions options)
    {
        string text = value switch
        {
            OverlayDisplayMode.FixedText => "fixedText",
            OverlayDisplayMode.CardName => "cardName",
            OverlayDisplayMode.ManualValue => "manualValue",
            OverlayDisplayMode.EffectiveValue => "effectiveValue",
            _ => throw new JsonException($"Unknown overlay display mode '{value}'.")
        };

        writer.WriteStringValue(text);
    }
}
