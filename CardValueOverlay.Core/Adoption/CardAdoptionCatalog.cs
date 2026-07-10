using System.Text.Json;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Adoption;

public sealed class CardAdoptionCatalog
{
    public int SchemaVersion { get; init; }
    public int TotalRuns { get; init; }
    public Dictionary<string, CardAdoptionEntry> Cards { get; init; } = new(StringComparer.OrdinalIgnoreCase);

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

        return new CardAdoptionCatalog
        {
            SchemaVersion = parsed.SchemaVersion,
            TotalRuns = parsed.TotalRuns,
            Cards = new Dictionary<string, CardAdoptionEntry>(parsed.Cards, StringComparer.OrdinalIgnoreCase)
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
        return new CardAdoptionDisplayStats(
            form.AppearanceProbability,
            form.PickRate,
            card.TotalRunsWith > 0 ? card.AvgCopiesWhenPresent : null);
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
    double? AvgCopiesWhenPresent);
