using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed class LayeredValueTableJsonConverter : JsonConverter<LayeredValueTable>
{
    public override LayeredValueTable Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Layered value tables must be objects keyed by layer.");
        }

        LayeredValueTable table = new();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return table;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a layer property name.");
            }

            string? propertyName = reader.GetString();
            if (!int.TryParse(propertyName, out int layer))
            {
                throw new JsonException($"Layer '{propertyName}' is not an integer.");
            }

            if (layer < 1)
            {
                throw new JsonException($"Layer '{propertyName}' must be 1 or greater.");
            }

            reader.Read();
            table[layer] = reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble();
        }

        throw new JsonException("Unexpected end of layered value table.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        LayeredValueTable value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((int layer, double? resolvedValue) in value.OrderBy(pair => pair.Key))
        {
            writer.WritePropertyName(layer.ToString());
            if (resolvedValue.HasValue)
            {
                writer.WriteNumberValue(resolvedValue.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        writer.WriteEndObject();
    }
}
