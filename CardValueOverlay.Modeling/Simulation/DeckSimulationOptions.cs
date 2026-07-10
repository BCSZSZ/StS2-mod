using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record DeckSimulationOptions
{
    public int Turns { get; init; } = 8;

    public int Runs { get; init; } = 2000;

    public int RunDegreeOfParallelism { get; init; } = 1;

    public int Seed { get; init; } = 1;

    public int HandSize { get; init; } = 5;

    public int MaxHandSize { get; init; } = 10;

    public int BaseEnergy { get; init; } = 3;

    public int BaseStars { get; init; } = 3;

    public bool StarsPersistBetweenTurns { get; init; } = true;

    public int MaxCardsPlayedPerTurn { get; init; } = 16;

    public int MaxBranchingCards { get; init; } = 64;

    public decimal PmfBucketSize { get; init; } = 1m;

    [JsonIgnore]
    public IReadOnlyList<SimulationCard> CardLibrary { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyCollection<string> BlockedPlayModelIds { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyCollection<int> BlockedPlayInstanceIds { get; init; } = [];

    [JsonIgnore]
    public GeneratedCardPoolCatalog GeneratedCardPools { get; init; } = GeneratedCardPoolCatalog.Empty;

    [JsonIgnore]
    public ISearchCardScorer? SearchCardScorer { get; init; }

    // Learned line evaluator (brain 2). When set, the search ranks lines by
    // realized value + V(leaf state) instead of realized value + setup-priority
    // + resource-reference proxy. Mutually exclusive in intent with setup priority.
    [JsonIgnore]
    public IStateValueEstimator? StateValue { get; init; }

    [JsonIgnore]
    public SearchPolicyDataCollector? SearchPolicyCollector { get; init; }

    [JsonIgnore]
    public string SearchPolicySource { get; init; } = "simulation";

    [JsonIgnore]
    public SearchPolicyGroupMetadata? SearchPolicyMetadata { get; init; }

    /// <summary>
    /// When false, the simulator skips building per-play card-value credit/attribution
    /// events entirely. Expected-value math (turn value and search decisions) is
    /// unaffected; only the reporting attribution is omitted. Defaults to true so that
    /// <see cref="DeckMonteCarloSimulator.Simulate"/> callers keep full attribution.
    /// </summary>
    [JsonIgnore]
    public bool CollectAttribution { get; init; } = true;

    /// <summary>
    /// When set (and <see cref="CollectAttribution"/> is true), only this model id is
    /// retained in the report's card-value-credit accumulators. Used by direct
    /// play-value estimation, which only ever reads the single probe card's row.
    /// </summary>
    [JsonIgnore]
    public string? TrackedCreditModelId { get; init; }

    /// <summary>
    /// When set, each worker thread of the parallel run loop lowers itself to this priority.
    /// The in-game realtime service passes <c>BelowNormal</c> during combat so the extra
    /// background cores never preempt the game's render/logic thread, and <c>Normal</c>
    /// otherwise so a thread lowered during a previous fight is restored. Null (the default)
    /// leaves thread priority untouched, so offline tooling is unaffected. Priority never
    /// changes the computed numbers, only OS scheduling.
    /// </summary>
    [JsonIgnore]
    public System.Threading.ThreadPriority? WorkerThreadPriority { get; init; }
}
