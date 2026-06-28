namespace CardValueOverlay.Core.Configuration;

public sealed record TrainingValueMetadata
{
    public string? Source { get; init; }

    public string? GeneratedAt { get; init; }

    public int? DeckCount { get; init; }

    public int? RunsPerDeck { get; init; }

    public int? MaxCardsPlayedPerTurn { get; init; }

    public int? MaxBranchingCards { get; init; }

    public Dictionary<string, int> Horizons { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Note { get; init; }
}
