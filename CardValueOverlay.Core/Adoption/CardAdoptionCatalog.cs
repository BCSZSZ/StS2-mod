using System.Text.Json;
using System.Text.Json.Serialization;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Adoption;

public sealed class CardAdoptionCatalog
{
    internal const int CopyDistributionMinimumRunsWith = 30;

    public int SchemaVersion { get; init; }
    public int TotalRuns { get; init; }
    public CardAdoptionScope Scope { get; init; } = new();
    public Dictionary<string, CardAdoptionEntry> Cards { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CardAdoptionDistributions> DistributionsByGroup { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static CardAdoptionCatalog LoadFromJson(string json)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        CardAdoptionCatalog parsed = JsonSerializer.Deserialize<CardAdoptionCatalog>(json, options)
            ?? throw new InvalidDataException("Card adoption JSON is empty.");
        if (parsed.SchemaVersion != 3)
        {
            throw new InvalidDataException($"Unsupported card adoption schema version {parsed.SchemaVersion}.");
        }

        return Create(parsed.TotalRuns, parsed.Scope, parsed.Cards);
    }

    public static CardAdoptionCatalog Create(
        int totalRuns,
        CardAdoptionScope scope,
        IReadOnlyDictionary<string, CardAdoptionEntry> sourceCards)
    {
        Dictionary<string, CardAdoptionEntry> cards = sourceCards.ToDictionary(
            item => item.Key,
            item => new CardAdoptionEntry
            {
                Pools = item.Value.Pools,
                Variants = new Dictionary<string, CardAdoptionVariant>(
                    item.Value.Variants,
                    StringComparer.OrdinalIgnoreCase)
            },
            StringComparer.OrdinalIgnoreCase);
        if (cards.Any(item => item.Value.Variants.Count == 0))
        {
            throw new InvalidDataException("Every card adoption entry must contain at least one statistics variant.");
        }

        return new CardAdoptionCatalog
        {
            SchemaVersion = 3,
            TotalRuns = totalRuns,
            Scope = scope,
            Cards = cards,
            DistributionsByGroup = CardAdoptionDistributions.FromCards(cards)
        };
    }

    public CardAdoptionDisplayStats? Resolve(
        string cardKey,
        CardUpgradeState upgradeState,
        string? currentCharacterKey)
    {
        string normalized = cardKey.StartsWith("CARD.", StringComparison.OrdinalIgnoreCase)
            ? cardKey
            : $"CARD.{cardKey}";
        if (!Cards.TryGetValue(normalized, out CardAdoptionEntry? card))
        {
            return null;
        }

        CardAdoptionVariant? variant = ResolveVariant(card, currentCharacterKey);
        if (variant is null)
        {
            return null;
        }

        CardAdoptionFormStats form = upgradeState == CardUpgradeState.Upgraded
            ? variant.Plus1
            : variant.Plus0;
        double appearanceProbability = variant.SampleRuns > 0 && variant.TotalRunsWith > 0
            ? (double)variant.TotalRunsWith / variant.SampleRuns
            : 0d;
        CardAdoptionDistributions distributions = CardAdoptionDistributions.Empty;
        bool hasDistribution = false;
        if (!string.IsNullOrWhiteSpace(variant.DistributionGroup)
            && DistributionsByGroup.TryGetValue(
                variant.DistributionGroup,
                out CardAdoptionDistributions? resolvedDistributions))
        {
            distributions = resolvedDistributions;
            hasDistribution = true;
        }
        return new CardAdoptionDisplayStats(
            variant.SampleRuns,
            appearanceProbability,
            form.OfferCount,
            form.PickRate,
            form.ShopOfferCount,
            form.ShopBuyRate,
            variant.TotalRunsWith > 0 ? variant.AvgCopiesWhenPresent : null,
            hasDistribution
                ? distributions.Appearance.Band(appearanceProbability)
                : CardAdoptionStatBand.Unknown,
            hasDistribution
                ? distributions.PickRate.Band(form.PickRate)
                : CardAdoptionStatBand.Unknown,
            hasDistribution
                ? distributions.ShopBuyRate.Band(form.ShopBuyRate)
                : CardAdoptionStatBand.Unknown,
            IsCopyDistributionEligible(variant)
                ? distributions.AvgCopiesWhenPresent.Band(variant.AvgCopiesWhenPresent)
                : CardAdoptionStatBand.Unknown);
    }

    private static CardAdoptionVariant? ResolveVariant(
        CardAdoptionEntry card,
        string? currentCharacterKey)
    {
        if (!string.IsNullOrWhiteSpace(currentCharacterKey))
        {
            string normalized = currentCharacterKey.StartsWith("CHARACTER.", StringComparison.OrdinalIgnoreCase)
                ? currentCharacterKey
                : $"CHARACTER.{currentCharacterKey}";
            if (card.Variants.TryGetValue(normalized, out CardAdoptionVariant? characterVariant))
            {
                return characterVariant;
            }
        }

        if (card.Variants.TryGetValue("all", out CardAdoptionVariant? allVariant))
        {
            return allVariant;
        }

        return card.Variants.Count == 1 ? card.Variants.Values.First() : null;
    }

    internal static bool IsCopyDistributionEligible(CardAdoptionVariant variant)
    {
        return variant.CopyDistributionEligible
            && !string.IsNullOrWhiteSpace(variant.DistributionGroup)
            && variant.TotalRunsWith >= CopyDistributionMinimumRunsWith
            && variant.AvgCopiesWhenPresent > 0d;
    }
}

public sealed class CardAdoptionScope
{
    public CardAdoptionScopeFilters Filters { get; init; } = new();
}

public sealed class CardAdoptionScopeFilters
{
    public int? Ascension { get; init; }
    public string? Win { get; init; }
    public int? Players { get; init; }

    [JsonPropertyName("game_mode")]
    public string? GameMode { get; init; }

    [JsonPropertyName("build_id")]
    public string? BuildId { get; init; }

    [JsonPropertyName("build_ids")]
    public string? BuildIds { get; init; }

    public string? Character { get; init; }
    public List<string> Characters { get; init; } = [];
}

public sealed class CardAdoptionEntry
{
    public IReadOnlyList<string> Pools { get; init; } = [];
    public Dictionary<string, CardAdoptionVariant> Variants { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CardAdoptionVariant
{
    public int SampleRuns { get; init; }
    public string? DistributionGroup { get; init; }
    public bool CopyDistributionEligible { get; init; }
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
    int SampleRuns,
    double AppearanceProbability,
    int OfferCount,
    double? PickRate,
    int ShopOfferCount,
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

    public static Dictionary<string, CardAdoptionDistributions> FromCards(
        IReadOnlyDictionary<string, CardAdoptionEntry> cards)
    {
        return cards
            .SelectMany(item => item.Value.Variants.Values)
            .Where(variant => !string.IsNullOrWhiteSpace(variant.DistributionGroup))
            .GroupBy(
                variant => variant.DistributionGroup!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                FromGroup,
                StringComparer.OrdinalIgnoreCase);
    }

    private static CardAdoptionDistributions FromGroup(
        IEnumerable<CardAdoptionVariant> variants)
    {
        List<double> appearance = [];
        List<double> pickRate = [];
        List<double> shopBuyRate = [];
        List<double> averageCopies = [];

        foreach (CardAdoptionVariant variant in variants)
        {
            if (variant.SampleRuns > 0 && variant.TotalRunsWith > 0)
            {
                appearance.Add((double)variant.TotalRunsWith / variant.SampleRuns);
            }

            AddForm(variant.Plus0);
            AddForm(variant.Plus1);

            if (CardAdoptionCatalog.IsCopyDistributionEligible(variant))
            {
                averageCopies.Add(variant.AvgCopiesWhenPresent);
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
