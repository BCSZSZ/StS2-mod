using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record DeckSimulationOptions
{
    public int Turns { get; init; } = 8;

    public int Runs { get; init; } = 2000;

    public int Seed { get; init; } = 1;

    public int HandSize { get; init; } = 5;

    public int BaseEnergy { get; init; } = 3;

    public int BaseStars { get; init; } = 3;

    public bool StarsPersistBetweenTurns { get; init; } = true;

    public int MaxCardsPlayedPerTurn { get; init; } = 16;

    public int MaxBranchingCards { get; init; } = 64;

    public decimal PmfBucketSize { get; init; } = 1m;

    [JsonIgnore]
    public IReadOnlyList<SimulationCard> CardLibrary { get; init; } = [];
}
