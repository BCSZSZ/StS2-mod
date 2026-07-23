using System.Text.Json;
using CardValueOverlay.Core.Adoption;

namespace CardValueOverlay.Core.Ancient;

public sealed class AncientChoiceCatalog
{
    public int SchemaVersion { get; init; }
    public Dictionary<string, AncientChoiceCharacterStats> Characters { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static AncientChoiceCatalog LoadFromJson(string json)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        AncientChoiceCatalog parsed = JsonSerializer.Deserialize<AncientChoiceCatalog>(json, options)
            ?? throw new InvalidDataException("Ancient choice JSON is empty.");
        if (parsed.SchemaVersion != 4)
        {
            throw new InvalidDataException($"Unsupported ancient choice schema version {parsed.SchemaVersion}.");
        }

        return Create(parsed.Characters);
    }

    public static AncientChoiceCatalog Create(
        IReadOnlyDictionary<string, AncientChoiceCharacterStats> sourceCharacters)
    {
        Dictionary<string, AncientChoiceCharacterStats> characters = sourceCharacters.ToDictionary(
            item => item.Key,
                item => AncientChoiceCharacterStats.Create(
                    item.Value.SampleRuns,
                    item.Value.Wins,
                    item.Value.TotalChoiceScreens,
                    item.Value.Choices),
            StringComparer.OrdinalIgnoreCase);
        return new AncientChoiceCatalog
        {
            SchemaVersion = 4,
            Characters = characters
        };
    }

    public AncientChoiceDisplayStats? Resolve(string textKey, string? characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey)
            || !Characters.TryGetValue(characterKey, out AncientChoiceCharacterStats? character))
        {
            return null;
        }

        return character.Resolve(textKey);
    }

    public static string NormalizeTextKey(string textKey)
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

public sealed class AncientChoiceCharacterStats
{
    public int SampleRuns { get; init; }
    public int Wins { get; init; }
    public int TotalChoiceScreens { get; init; }
    public Dictionary<string, AncientChoiceEntry> Choices { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    private PercentileBands PickRateDistribution { get; init; } = PercentileBands.Empty;

    public static AncientChoiceCharacterStats Create(
        int sampleRuns,
        int wins,
        int totalChoiceScreens,
        IReadOnlyDictionary<string, AncientChoiceEntry> sourceChoices)
    {
        Dictionary<string, AncientChoiceEntry> choices = sourceChoices.ToDictionary(
            item => item.Key,
            item => Normalize(item.Key, item.Value),
            StringComparer.OrdinalIgnoreCase);
        return new AncientChoiceCharacterStats
        {
            SampleRuns = sampleRuns,
            Wins = wins,
            TotalChoiceScreens = totalChoiceScreens,
            Choices = choices,
            PickRateDistribution = PercentileBands.FromValues(choices.Values
                .Select(choice => choice.PickRate)
                .OfType<double>()
                .Where(rate => rate > 0d))
        };
    }

    internal AncientChoiceDisplayStats? Resolve(string textKey)
    {
        string normalized = AncientChoiceCatalog.NormalizeTextKey(textKey);
        if (normalized.Length == 0 || !Choices.TryGetValue(normalized, out AncientChoiceEntry? choice))
        {
            return null;
        }

        return new AncientChoiceDisplayStats(
            choice.OfferCount,
            choice.PickCount,
            choice.PickRate,
            choice.PickRate is double pickRate
                ? PickRateDistribution.Band(pickRate)
                : CardAdoptionStatBand.Unknown,
            choice.PickedWinCount,
            choice.PickedWinRate);
    }

    private static AncientChoiceEntry Normalize(string textKey, AncientChoiceEntry choice)
    {
        if (choice.OfferCount < 0
            || choice.PickCount < 0
            || choice.PickedWinCount < 0
            || choice.PickCount > choice.OfferCount
            || choice.PickedWinCount > choice.PickCount)
        {
            throw new InvalidDataException(
                $"Ancient choice {textKey} has inconsistent counts: "
                + $"wins={choice.PickedWinCount}, picks={choice.PickCount}, offers={choice.OfferCount}.");
        }

        return new AncientChoiceEntry
        {
            OfferCount = choice.OfferCount,
            PickCount = choice.PickCount,
            PickRate = Ratio(choice.PickCount, choice.OfferCount),
            PickedWinCount = choice.PickedWinCount,
            PickedWinRate = Ratio(choice.PickedWinCount, choice.PickCount)
        };
    }

    private static double? Ratio(int numerator, int denominator) =>
        denominator > 0 ? (double)numerator / denominator : null;
}

public sealed class AncientChoiceEntry
{
    public int OfferCount { get; init; }
    public int PickCount { get; init; }
    public double? PickRate { get; init; }
    public int PickedWinCount { get; init; }
    public double? PickedWinRate { get; init; }
}

public sealed record AncientChoiceDisplayStats(
    int OfferCount,
    int PickCount,
    double? PickRate,
    CardAdoptionStatBand PickRateBand,
    int PickedWinCount,
    double? PickedWinRate);
