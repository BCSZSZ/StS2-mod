using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record DeckSimulationOptions
{
    public int Turns { get; init; } = 8;

    public int Runs { get; init; } = 1000;

    public int Seed { get; init; } = 1;

    public int HandSize { get; init; } = 5;

    public int BaseEnergy { get; init; } = 3;

    public int BaseStars { get; init; }

    public bool StarsPersistBetweenTurns { get; init; }

    public int MaxCardsPlayedPerTurn { get; init; } = 16;

    public int MaxBranchingCards { get; init; } = 8;

    public decimal PmfBucketSize { get; init; } = 1m;

    [JsonIgnore]
    public IReadOnlyList<SimulationCard> CardLibrary { get; init; } = [];
}
