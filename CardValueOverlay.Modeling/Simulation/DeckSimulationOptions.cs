using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record DeckSimulationOptions
{
    public const int DefaultBranchWidth = 3;

    public const int DefaultFullBranchDecisionDepth = 8;

    public const int DefaultResolvedPlaySafetyCap = 64;

    public const int DefaultDeterministicPlayChainCap = 32;

    public const int DefaultSearchNodeBudgetPerTurn = 500_000;

    // Exact cache keys include branch RNG state. Real-deck profiling found no safe hits, so keep
    // the bounded cache opt-in instead of paying dictionary overhead on every production node.
    public const int DefaultTranspositionCapacityPerTurn = 0;

    public int Turns { get; init; } = 8;

    public int Runs { get; init; } = 2000;

    public int RunDegreeOfParallelism { get; init; } = 1;

    public int Seed { get; init; } = 1;

    public int HandSize { get; init; } = 5;

    public int MaxHandSize { get; init; } = 10;

    public int BaseEnergy { get; init; } = 3;

    public int BaseStars { get; init; } = 3;

    public bool StarsPersistBetweenTurns { get; init; } = true;

    /// <summary>
    /// Hard safety limit for all resolved plays in one turn, including deterministic forced plays
    /// and ordinary searched plays. This is not the branch-search depth.
    /// </summary>
    public int MaxCardsPlayedPerTurn { get; init; } = DefaultResolvedPlaySafetyCap;

    public int MaxBranchingCards { get; init; } = DefaultBranchWidth;

    /// <summary>
    /// Number of ordinary branch decisions that retain the configured branching width. Forced
    /// energy, zero-cost, draw, Void Form, and Power plays do not consume this budget. Ordinary
    /// decisions after this depth continue through the single best-scored candidate until
    /// <see cref="MaxCardsPlayedPerTurn"/> is reached.
    /// </summary>
    public int MaxFullyBranchedCardsPlayedPerTurn { get; init; } = DefaultFullBranchDecisionDepth;

    /// <summary>
    /// Safety limit for consecutive deterministic plays resolved by one search call. Reaching the
    /// limit does not end the turn and does not consume ordinary branch depth; the remaining legal
    /// cards return to ordinary search.
    /// </summary>
    public int MaxDeterministicPlayChain { get; init; } = DefaultDeterministicPlayChainCap;

    /// <summary>
    /// Deterministic per-turn node budget. Once exhausted, ordinary search degrades to branch one
    /// but continues resolving legal plays up to <see cref="MaxCardsPlayedPerTurn"/>.
    /// </summary>
    public int MaxSearchNodesPerTurn { get; init; } = DefaultSearchNodeBudgetPerTurn;

    /// <summary>
    /// Maximum exact search-policy states cached during one turn. Zero disables the cache.
    /// </summary>
    public int TranspositionCapacityPerTurn { get; init; } = DefaultTranspositionCapacityPerTurn;

    /// <summary>
    /// Detects repeated play-phase state patterns. Positive-value loops remain playable up to the
    /// resolved-play safety cap instead of being discarded as no-ops.
    /// </summary>
    public bool EnableLoopDetection { get; init; } = true;

    /// <summary>
    /// Enables the single-future-turn preview for card-object decisions that declare that horizon.
    /// Zero disables it; positive values enable the one supported next turn. Nested previews set
    /// this to zero so card-object lookahead never recursively invokes itself.
    /// </summary>
    [JsonIgnore]
    public int CardObjectLookaheadTurns { get; init; } = 1;

    /// <summary>
    /// Beam width used inside the bounded current-turn/next-turn continuation preview. This is kept
    /// smaller than the main play beam because the preview is repeated for several object targets.
    /// </summary>
    [JsonIgnore]
    public int CardObjectLookaheadBranchingCards { get; init; } = 2;

    /// <summary>
    /// Maximum number of remaining plays searched by one card-object continuation preview.
    /// </summary>
    [JsonIgnore]
    public int CardObjectLookaheadCardsPlayed { get; init; } = 6;

    public decimal PmfBucketSize { get; init; } = 1m;

    [JsonIgnore]
    public IReadOnlyList<SimulationCard> CardLibrary { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyCollection<string> BlockedPlayModelIds { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyCollection<int> BlockedPlayInstanceIds { get; init; } = [];

    /// <summary>
    /// Optional stable identities for starting cards. Realtime counterfactual simulations pass
    /// identities derived from one full-deck snapshot so adding/removing/replacing one card does
    /// not renumber every surviving card.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<int> StartingInstanceIds { get; init; } = [];

    /// <summary>
    /// Uses a deterministic per-instance random priority for starting and reshuffle order. Shared
    /// cards then preserve relative order across paired counterfactual simulations.
    /// </summary>
    [JsonIgnore]
    public bool CounterfactualStableShuffle { get; init; }

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

    /// <summary>
    /// Optional thread-safe aggregate of the actual Top-B union guaranteed-admission width selected
    /// at search nodes. Intended for offline performance audits; realtime simulation leaves it null.
    /// </summary>
    [JsonIgnore]
    public SearchBranchDiagnosticsCollector? SearchBranchDiagnostics { get; init; }

    /// <summary>
    /// Optional high-overhead offline profiler that records per-run/per-turn slow-tail detail plus
    /// card, Power, generation-pool, loop, and fallback hotspots. Realtime simulations leave it null.
    /// </summary>
    [JsonIgnore]
    public SearchSlowTailProfiler? SlowTailProfiler { get; init; }

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
    /// Keeps the chosen play sequence. Full reports and tracked-card probes require it; pure EV
    /// sampling disables it so search nodes carry only numeric aggregates.
    /// </summary>
    [JsonIgnore]
    internal bool CollectSearchPlayTrace { get; init; } = true;

    /// <summary>
    /// Records the candidate cards considered by move and transform effects and whether each
    /// candidate was selected on the chosen search line. This is intentionally opt-in because
    /// card-object effects are explored across many search branches and the diagnostic objects add
    /// allocation.
    /// </summary>
    [JsonIgnore]
    public bool CollectCardObjectDiagnostics { get; init; }

    internal string? TrackedDrawModelId { get; init; }

    internal IReadOnlySet<int>? TrackedStartingInstanceIds { get; init; }

    internal SearchWorkBudget? ActiveSearchWorkBudget { get; init; }

    internal SearchTurnProfile? ActiveSearchTurnProfile { get; init; }

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

internal sealed class SearchWorkBudget(int maximumNodes)
{
    private int nodes;

    public bool EnterNode()
    {
        return ++nodes > Math.Max(1, maximumNodes);
    }
}
