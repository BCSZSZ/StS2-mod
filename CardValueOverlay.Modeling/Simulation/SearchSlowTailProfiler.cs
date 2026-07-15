using System.Collections.Concurrent;
using System.Diagnostics;

namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Optional offline profiler for locating slow Monte Carlo runs and turns. The simulator does not
/// create it by default; benchmark tooling opts in explicitly because per-card dictionaries add
/// measurable overhead to the search hot path.
/// </summary>
public sealed class SearchSlowTailProfiler
{
    private readonly ConcurrentBag<SearchTurnProfileSnapshot> completedTurns = [];

    internal SearchTurnProfile StartTurn(int run, int turn) => new(this, run, turn);

    internal void Complete(SearchTurnProfileSnapshot snapshot) => completedTurns.Add(snapshot);

    public SearchSlowTailProfileSnapshot Snapshot() => new(
        completedTurns
            .OrderBy(value => value.Run)
            .ThenBy(value => value.Turn)
            .ToArray());
}

public sealed record SearchSlowTailProfileSnapshot(
    IReadOnlyList<SearchTurnProfileSnapshot> Turns);

public sealed record SearchTurnProfileSnapshot(
    int Run,
    int Turn,
    double ElapsedMilliseconds,
    double ExpectedValue,
    int CardsPlayed,
    long SearchNodes,
    long DecisionNodes,
    long FullyBranchedDecisionNodes,
    long GreedyDecisionNodes,
    long ForcedPlayNodes,
    long StateClones,
    long WorkBudgetFallbackNodes,
    long LoopDetectionHits,
    long PositiveResourceLoopHits,
    long PrunedLoopHits,
    long GeneratedCards,
    IReadOnlyDictionary<string, SearchCardHotspotSnapshot> CardHotspots,
    IReadOnlyDictionary<string, long> ForcedCardPlays,
    IReadOnlyDictionary<string, long> ActivePowerExposures,
    IReadOnlyDictionary<string, GeneratedPoolHotspotSnapshot> GeneratedPools,
    IReadOnlyList<SearchCandidateSubtreeSnapshot> LargestCandidateSubtrees);

public sealed record SearchCardHotspotSnapshot(
    long Evaluations,
    long TotalDescendantNodes,
    long MaximumDescendantNodes);

public sealed record GeneratedPoolHotspotSnapshot(
    long Events,
    long RequestedCards);

public sealed record SearchCandidateSubtreeSnapshot(
    string CardTypeName,
    int ResolvedPlayDepth,
    long DescendantNodes,
    double ElapsedMilliseconds,
    IReadOnlyList<string> CandidatePath);

internal sealed class SearchTurnProfile(
    SearchSlowTailProfiler owner,
    int run,
    int turn)
{
    private const int RetainedCandidateSubtrees = 16;
    private readonly long startedAt = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, MutableCardHotspot> cardHotspots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> forcedCardPlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> activePowerExposures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableGeneratedPoolHotspot> generatedPools = new(StringComparer.Ordinal);
    private readonly List<SearchCandidateSubtreeSnapshot> largestCandidateSubtrees = new(RetainedCandidateSubtrees);
    private readonly List<string> candidatePath = [];

    public long SearchNodes { get; private set; }

    public long DecisionNodes { get; private set; }

    public long FullyBranchedDecisionNodes { get; private set; }

    public long GreedyDecisionNodes { get; private set; }

    public long ForcedPlayNodes { get; private set; }

    public long StateClones { get; private set; }

    public long WorkBudgetFallbackNodes { get; private set; }

    public long LoopDetectionHits { get; private set; }

    public long PositiveResourceLoopHits { get; private set; }

    public long PrunedLoopHits { get; private set; }

    public long GeneratedCards { get; private set; }

    public void RecordSearchNode(bool workBudgetFallback)
    {
        SearchNodes++;
        if (workBudgetFallback)
        {
            WorkBudgetFallbackNodes++;
        }
    }

    public void RecordDecision(bool fullyBranched)
    {
        DecisionNodes++;
        if (fullyBranched)
        {
            FullyBranchedDecisionNodes++;
        }
        else
        {
            GreedyDecisionNodes++;
        }
    }

    public void RecordForcedCard(string cardTypeName)
    {
        ForcedPlayNodes++;
        forcedCardPlays[cardTypeName] = forcedCardPlays.GetValueOrDefault(cardTypeName) + 1;
    }

    public void RecordStateClone() => StateClones++;

    public void RecordLoop(bool positiveResourceLoop)
    {
        LoopDetectionHits++;
        if (positiveResourceLoop)
        {
            PositiveResourceLoopHits++;
        }
    }

    public void RecordPrunedLoop() => PrunedLoopHits++;

    public long BeginCandidate(string cardTypeName)
    {
        candidatePath.Add(cardTypeName);
        return Stopwatch.GetTimestamp();
    }

    public void CompleteCandidate(
        string cardTypeName,
        int resolvedPlayDepth,
        long descendantNodes,
        long startedAt)
    {
        IReadOnlyList<string> path = candidatePath.ToArray();
        RecordCandidate(
            cardTypeName,
            resolvedPlayDepth,
            descendantNodes,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            path);
        if (candidatePath.Count > 0)
        {
            candidatePath.RemoveAt(candidatePath.Count - 1);
        }
    }

    public void RecordTailCandidate(
        string cardTypeName,
        int resolvedPlayDepth,
        long descendantNodes,
        long startedAt,
        IReadOnlyList<string> tailPath)
    {
        RecordCandidate(
            cardTypeName,
            resolvedPlayDepth,
            descendantNodes,
            startedAt == 0 ? 0d : Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            [.. candidatePath, .. tailPath]);
    }

    private void RecordCandidate(
        string cardTypeName,
        int resolvedPlayDepth,
        long descendantNodes,
        double elapsedMilliseconds,
        IReadOnlyList<string> path)
    {
        if (!cardHotspots.TryGetValue(cardTypeName, out MutableCardHotspot? hotspot))
        {
            hotspot = new MutableCardHotspot();
            cardHotspots.Add(cardTypeName, hotspot);
        }

        hotspot.Evaluations++;
        hotspot.TotalDescendantNodes += descendantNodes;
        hotspot.MaximumDescendantNodes = Math.Max(hotspot.MaximumDescendantNodes, descendantNodes);

        SearchCandidateSubtreeSnapshot candidate = new(
            cardTypeName,
            resolvedPlayDepth,
            descendantNodes,
            elapsedMilliseconds,
            path);
        if (largestCandidateSubtrees.Count < RetainedCandidateSubtrees)
        {
            largestCandidateSubtrees.Add(candidate);
            return;
        }

        int smallestIndex = 0;
        for (int index = 1; index < largestCandidateSubtrees.Count; index++)
        {
            if (largestCandidateSubtrees[index].DescendantNodes
                < largestCandidateSubtrees[smallestIndex].DescendantNodes)
            {
                smallestIndex = index;
            }
        }

        if (candidate.DescendantNodes > largestCandidateSubtrees[smallestIndex].DescendantNodes)
        {
            largestCandidateSubtrees[smallestIndex] = candidate;
        }
    }

    public void RecordPowerExposure(string powerKind)
    {
        activePowerExposures[powerKind] = activePowerExposures.GetValueOrDefault(powerKind) + 1;
    }

    public void RecordGeneratedPool(string poolId, int requestedCards)
    {
        if (!generatedPools.TryGetValue(poolId, out MutableGeneratedPoolHotspot? hotspot))
        {
            hotspot = new MutableGeneratedPoolHotspot();
            generatedPools.Add(poolId, hotspot);
        }

        hotspot.Events++;
        hotspot.RequestedCards += Math.Max(0, requestedCards);
        GeneratedCards += Math.Max(0, requestedCards);
    }

    public void Complete(double expectedValue, int cardsPlayed)
    {
        owner.Complete(new SearchTurnProfileSnapshot(
            run,
            turn,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            expectedValue,
            cardsPlayed,
            SearchNodes,
            DecisionNodes,
            FullyBranchedDecisionNodes,
            GreedyDecisionNodes,
            ForcedPlayNodes,
            StateClones,
            WorkBudgetFallbackNodes,
            LoopDetectionHits,
            PositiveResourceLoopHits,
            PrunedLoopHits,
            GeneratedCards,
            cardHotspots.ToDictionary(
                pair => pair.Key,
                pair => new SearchCardHotspotSnapshot(
                    pair.Value.Evaluations,
                    pair.Value.TotalDescendantNodes,
                    pair.Value.MaximumDescendantNodes),
                StringComparer.Ordinal),
            new Dictionary<string, long>(forcedCardPlays, StringComparer.Ordinal),
            new Dictionary<string, long>(activePowerExposures, StringComparer.Ordinal),
            generatedPools.ToDictionary(
                pair => pair.Key,
                pair => new GeneratedPoolHotspotSnapshot(
                    pair.Value.Events,
                    pair.Value.RequestedCards),
                StringComparer.Ordinal),
            largestCandidateSubtrees
                .OrderByDescending(value => value.DescendantNodes)
                .ToArray()));
    }

    private sealed class MutableCardHotspot
    {
        public long Evaluations { get; set; }

        public long TotalDescendantNodes { get; set; }

        public long MaximumDescendantNodes { get; set; }
    }

    private sealed class MutableGeneratedPoolHotspot
    {
        public long Events { get; set; }

        public long RequestedCards { get; set; }
    }
}
