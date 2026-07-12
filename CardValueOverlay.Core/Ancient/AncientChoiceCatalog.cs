using System.Text.Json;
using CardValueOverlay.Core.Adoption;

namespace CardValueOverlay.Core.Ancient;

public sealed class AncientChoiceCatalog
{
    public int SchemaVersion { get; init; }
    public int TotalChoiceScreens { get; init; }
    public Dictionary<string, AncientChoiceEntry> Choices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    private PercentileBands PickRateDistribution { get; init; } = PercentileBands.Empty;

    public static AncientChoiceCatalog LoadFromJson(string json)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        AncientChoiceCatalog parsed = JsonSerializer.Deserialize<AncientChoiceCatalog>(json, options)
            ?? throw new InvalidDataException("Ancient choice JSON is empty.");
        if (parsed.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported ancient choice schema version {parsed.SchemaVersion}.");
        }

        Dictionary<string, AncientChoiceEntry> choices = new(parsed.Choices, StringComparer.OrdinalIgnoreCase);
        return new AncientChoiceCatalog
        {
            SchemaVersion = parsed.SchemaVersion,
            TotalChoiceScreens = parsed.TotalChoiceScreens,
            Choices = choices,
            PickRateDistribution = PercentileBands.FromValues(choices.Values
                .Select(choice => choice.PickRate)
                .Where(rate => rate > 0d))
        };
    }

    public AncientChoiceDisplayStats? Resolve(string textKey)
    {
        string normalized = NormalizeTextKey(textKey);
        if (normalized.Length == 0 || !Choices.TryGetValue(normalized, out AncientChoiceEntry? choice))
        {
            return null;
        }

        return new AncientChoiceDisplayStats(
            choice.OfferCount,
            choice.PickCount,
            choice.PickRate,
            PickRateDistribution.Band(choice.PickRate));
    }

    private static string NormalizeTextKey(string textKey)
    {
        string trimmed = textKey.Trim();
        trimmed = trimmed.EndsWith(".title", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^".title".Length]
            : trimmed;
        const string OptionsMarker = ".options.";
        int markerIndex = trimmed.LastIndexOf(OptionsMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            return trimmed[(markerIndex + OptionsMarker.Length)..];
        }

        int lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0
            ? trimmed[(lastDot + 1)..]
            : trimmed;
    }
}

public sealed class AncientChoiceEntry
{
    public int OfferCount { get; init; }
    public int PickCount { get; init; }
    public double PickRate { get; init; }
}

public sealed record AncientChoiceDisplayStats(
    int OfferCount,
    int PickCount,
    double PickRate,
    CardAdoptionStatBand PickRateBand);
