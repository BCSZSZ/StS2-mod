using System.Text.Json;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Adoption;

public sealed class CardAdoptionCatalog
{
    internal const int CopyDistributionMinimumRunsWith = 30;

    private static readonly HashSet<string> CopyDistributionExcludedCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "CARD.ASCENDERS_BANE",
        "CARD.DEFEND_REGENT",
        "CARD.FALLING_STAR",
        "CARD.STRIKE_REGENT",
        "CARD.VENERATE"
    };

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
            Distributions = CardAdoptionDistributions.FromCards(cards, parsed.TotalRuns)
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
            form.ShopBuyRate,
            card.TotalRunsWith > 0 ? card.AvgCopiesWhenPresent : null,
            card.DistributionEligible
                ? Distributions.Appearance.Band(appearanceProbability)
                : CardAdoptionStatBand.Unknown,
            card.DistributionEligible
                ? Distributions.PickRate.Band(form.PickRate)
                : CardAdoptionStatBand.Unknown,
            card.DistributionEligible
                ? Distributions.ShopBuyRate.Band(form.ShopBuyRate)
                : CardAdoptionStatBand.Unknown,
            IsCopyDistributionEligible(normalized, card)
                ? Distributions.AvgCopiesWhenPresent.Band(card.AvgCopiesWhenPresent)
                : CardAdoptionStatBand.Unknown);
    }

    internal static bool IsCopyDistributionEligible(string cardKey, CardAdoptionEntry card)
    {
        return card.DistributionEligible
            && card.TotalRunsWith >= CopyDistributionMinimumRunsWith
            && !CopyDistributionExcludedCards.Contains(cardKey);
    }
}

public sealed class CardAdoptionEntry
{
    public bool DistributionEligible { get; init; } = true;
    public IReadOnlyList<string> Pools { get; init; } = [];
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
    public int ShopOfferCount { get; init; }
    public int ShopBuyCount { get; init; }
    public double? ShopBuyRate { get; init; }
}

public sealed record CardAdoptionDisplayStats(
    double AppearanceProbability,
    double? PickRate,
    double? ShopBuyRate,
    double? AvgCopiesWhenPresent,
    CardAdoptionStatBand AppearanceBand,
    CardAdoptionStatBand PickRateBand,
    CardAdoptionStatBand ShopBuyRateBand,
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
    PercentileBands ShopBuyRate,
    PercentileBands AvgCopiesWhenPresent)
{
    public static CardAdoptionDistributions Empty { get; } = new(
        PercentileBands.Empty,
        PercentileBands.Empty,
        PercentileBands.Empty,
        PercentileBands.Empty);

    public static CardAdoptionDistributions FromCards(
        IReadOnlyDictionary<string, CardAdoptionEntry> cards,
        int totalRuns)
    {
        List<double> appearance = [];
        List<double> pickRate = [];
        List<double> shopBuyRate = [];
        List<double> averageCopies = [];

        foreach (KeyValuePair<string, CardAdoptionEntry> item in cards)
        {
            CardAdoptionEntry card = item.Value;
            if (!card.DistributionEligible)
            {
                continue;
            }

            if (totalRuns > 0 && card.TotalRunsWith > 0)
            {
                appearance.Add((double)card.TotalRunsWith / totalRuns);
            }

            AddForm(card.Plus0);
            AddForm(card.Plus1);

            if (CardAdoptionCatalog.IsCopyDistributionEligible(item.Key, card))
            {
                averageCopies.Add(card.AvgCopiesWhenPresent);
            }

            void AddForm(CardAdoptionFormStats form)
            {
                if (form.PickRate is double rate && rate > 0d)
                {
                    pickRate.Add(rate);
                }

                if (form.ShopBuyRate is double buyRate && buyRate > 0d)
                {
                    shopBuyRate.Add(buyRate);
                }
            }
        }

        return new CardAdoptionDistributions(
            PercentileBands.FromValues(appearance),
            PercentileBands.FromValues(pickRate),
            PercentileBands.FromValues(shopBuyRate),
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
