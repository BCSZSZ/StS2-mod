using System.Text.Json;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Adoption;

public sealed class CardAdoptionCatalog
{
    public int SchemaVersion { get; init; }
    public int TotalRuns { get; init; }
    public Dictionary<string, CardAdoptionEntry> Cards { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    private CardAdoptionDistributions Distributions { get; init; } = CardAdoptionDistributions.Empty;

    public static CardAdoptionCatalog LoadFromJson(string json)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        CardAdoptionCatalog parsed = JsonSerializer.Deserialize<CardAdoptionCatalog>(json, options)
            ?? throw new InvalidDataException("Card adoption JSON is empty.");
        if (parsed.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported card adoption schema version {parsed.SchemaVersion}.");
        }

        Dictionary<string, CardAdoptionEntry> cards = new(parsed.Cards, StringComparer.OrdinalIgnoreCase);
        return new CardAdoptionCatalog
        {
            SchemaVersion = parsed.SchemaVersion,
            TotalRuns = parsed.TotalRuns,
            Cards = cards,
            Distributions = CardAdoptionDistributions.FromCards(cards.Values, parsed.TotalRuns)
        };
    }

    public CardAdoptionDisplayStats? Resolve(string cardKey, CardUpgradeState upgradeState)
    {
        string normalized = cardKey.StartsWith("CARD.", StringComparison.OrdinalIgnoreCase)
            ? cardKey
            : $"CARD.{cardKey}";
        if (!Cards.TryGetValue(normalized, out CardAdoptionEntry? card))
        {
            return null;
        }

        CardAdoptionFormStats form = upgradeState == CardUpgradeState.Upgraded
            ? card.Plus1
            : card.Plus0;
        double appearanceProbability = TotalRuns > 0 && card.TotalRunsWith > 0
            ? (double)card.TotalRunsWith / TotalRuns
            : 0d;
        return new CardAdoptionDisplayStats(
            appearanceProbability,
            form.PickRate,
            card.TotalRunsWith > 0 ? card.AvgCopiesWhenPresent : null,
            Distributions.Appearance.Band(appearanceProbability),
            Distributions.PickRate.Band(form.PickRate),
            card.TotalRunsWith > 0
                ? Distributions.AvgCopiesWhenPresent.Band(card.AvgCopiesWhenPresent)
                : CardAdoptionStatBand.Unknown);
    }
}

public sealed class CardAdoptionEntry
{
    public int TotalRunsWith { get; init; }
    public int TotalCopies { get; init; }
    public double AvgCopiesWhenPresent { get; init; }
    public CardAdoptionFormStats Plus0 { get; init; } = new();
    public CardAdoptionFormStats Plus1 { get; init; } = new();
}

public sealed class CardAdoptionFormStats
{
    public int FinalRunCount { get; init; }
    public double AppearanceProbability { get; init; }
    public int OfferCount { get; init; }
    public int PickCount { get; init; }
    public double? PickRate { get; init; }
}

public sealed record CardAdoptionDisplayStats(
    double AppearanceProbability,
    double? PickRate,
    double? AvgCopiesWhenPresent,
    CardAdoptionStatBand AppearanceBand,
    CardAdoptionStatBand PickRateBand,
    CardAdoptionStatBand AvgCopiesWhenPresentBand);

public enum CardAdoptionStatBand
{
    Unknown,
    Low,
    Middle,
    High
}

internal sealed record CardAdoptionDistributions(
    PercentileBands Appearance,
    PercentileBands PickRate,
    PercentileBands AvgCopiesWhenPresent)
{
    public static CardAdoptionDistributions Empty { get; } = new(
        PercentileBands.Empty,
        PercentileBands.Empty,
        PercentileBands.Empty);

    public static CardAdoptionDistributions FromCards(IEnumerable<CardAdoptionEntry> cards, int totalRuns)
    {
        List<double> appearance = [];
        List<double> pickRate = [];
        List<double> averageCopies = [];

        foreach (CardAdoptionEntry card in cards)
        {
            if (totalRuns > 0 && card.TotalRunsWith > 0)
            {
                appearance.Add((double)card.TotalRunsWith / totalRuns);
            }

            AddForm(card.Plus0);
            AddForm(card.Plus1);

            if (card.TotalRunsWith > 0)
            {
                averageCopies.Add(card.AvgCopiesWhenPresent);
            }

            void AddForm(CardAdoptionFormStats form)
            {
                if (form.PickRate is double rate && rate > 0d)
                {
                    pickRate.Add(rate);
                }
            }
        }

        return new CardAdoptionDistributions(
            PercentileBands.FromValues(appearance),
            PercentileBands.FromValues(pickRate),
            PercentileBands.FromValues(averageCopies));
    }
}

internal sealed record PercentileBands(double? LowerQuartile, double? UpperQuartile)
{
    public static PercentileBands Empty { get; } = new(null, null);

    public static PercentileBands FromValues(IEnumerable<double> values)
    {
        double[] sorted = values
            .Where(double.IsFinite)
            .OrderBy(value => value)
            .ToArray();
        if (sorted.Length == 0)
        {
            return Empty;
        }

        return new PercentileBands(
            Quantile(sorted, 0.25),
            Quantile(sorted, 0.75));
    }

    public CardAdoptionStatBand Band(double? value)
    {
        if (value is not double resolved
            || resolved <= 0d
            || LowerQuartile is not double lower
            || UpperQuartile is not double upper
            || upper <= lower)
        {
            return CardAdoptionStatBand.Unknown;
        }

        if (resolved >= upper)
        {
            return CardAdoptionStatBand.High;
        }

        return resolved <= lower
            ? CardAdoptionStatBand.Low
            : CardAdoptionStatBand.Middle;
    }

    private static double Quantile(IReadOnlyList<double> sorted, double percentile)
    {
        double position = (sorted.Count - 1) * percentile;
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        double weight = position - lowerIndex;
        return sorted[lowerIndex] * (1d - weight) + sorted[upperIndex] * weight;
    }
}
