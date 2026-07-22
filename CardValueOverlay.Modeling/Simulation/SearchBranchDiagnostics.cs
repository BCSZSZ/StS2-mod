using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Optional thread-safe counters for the actual candidate width selected at search nodes. The
/// simulator pays only one nullable check when diagnostics are disabled; offline benchmarks opt in.
/// </summary>
public sealed class SearchBranchDiagnosticsCollector
{
    private const int HistogramOverflowBucket = 64;
    private readonly long[] selectedBranchHistogram = new long[HistogramOverflowBucket + 1];
    private readonly long[] fullyBranchedSelectedBranchHistogram = new long[HistogramOverflowBucket + 1];
    private readonly ConcurrentDictionary<string, long> selectiveThirdBranchPrunedCards =
        new(StringComparer.Ordinal);
    private long decisionNodes;
    private long fullyBranchedDecisionNodes;
    private long greedyDecisionNodes;
    private long baseBranches;
    private long selectedBranches;
    private long extraBranches;
    private long fullyBranchedBaseBranches;
    private long fullyBranchedSelectedBranches;
    private long fullyBranchedExtraBranches;
    private long extraAdmissionNodes;
    private long generatedCandidateNodes;
    private long generatedCandidates;
    private long equivalentGeneratedCandidatesMerged;
    private long generatedCandidateMergeNodes;
    private long generatedChoiceScreens;
    private long generatedChoiceCandidates;
    private long generatedChoiceSkips;
    private long forcedPlayNodes;
    private long loopDetectionHits;
    private long positiveResourceLoopHits;
    private long prunedLoopHits;
    private long searchNodes;
    private long stateClones;
    private long playTraceNodes;
    private long workBudgetFallbackNodes;
    private long fairCandidateBudgetScopes;
    private long fairCandidateBudgetFallbackNodes;
    private long transpositionLookups;
    private long transpositionHits;
    private long transpositionStores;
    private long selectiveThirdBranchEligibleNodes;
    private long selectiveThirdBranchProtectedNodes;
    private long selectiveThirdBranchPrunedNodes;
    private long selectiveThirdBranchGapMilliTotal;
    private int maxDeterministicChain;
    private int maxSelectedBranches;

    public void Record(int baseBranchCount, int selectedBranchCount, bool fullyBranched)
    {
        if (baseBranchCount < 0 || selectedBranchCount < baseBranchCount)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedBranchCount));
        }

        int extraBranchCount = selectedBranchCount - baseBranchCount;
        Interlocked.Increment(ref decisionNodes);
        Interlocked.Add(ref baseBranches, baseBranchCount);
        Interlocked.Add(ref selectedBranches, selectedBranchCount);
        Interlocked.Add(ref extraBranches, extraBranchCount);
        if (extraBranchCount > 0)
        {
            Interlocked.Increment(ref extraAdmissionNodes);
        }

        if (fullyBranched)
        {
            Interlocked.Increment(ref fullyBranchedDecisionNodes);
            Interlocked.Add(ref fullyBranchedBaseBranches, baseBranchCount);
            Interlocked.Add(ref fullyBranchedSelectedBranches, selectedBranchCount);
            Interlocked.Add(ref fullyBranchedExtraBranches, extraBranchCount);
            Interlocked.Increment(
                ref fullyBranchedSelectedBranchHistogram[Math.Min(selectedBranchCount, HistogramOverflowBucket)]);
        }
        else
        {
            Interlocked.Increment(ref greedyDecisionNodes);
        }

        Interlocked.Increment(ref selectedBranchHistogram[Math.Min(selectedBranchCount, HistogramOverflowBucket)]);
        int observedMaximum = Volatile.Read(ref maxSelectedBranches);
        while (selectedBranchCount > observedMaximum)
        {
            int prior = Interlocked.CompareExchange(
                ref maxSelectedBranches,
                selectedBranchCount,
                observedMaximum);
            if (prior == observedMaximum)
            {
                break;
            }

            observedMaximum = prior;
        }
    }

    public void RecordForcedPlay()
    {
        Interlocked.Increment(ref forcedPlayNodes);
    }

    public void RecordGeneratedCandidateMerging(int candidateCount, int mergedCount)
    {
        if (candidateCount < 0
            || mergedCount < 0
            || (candidateCount == 0 && mergedCount != 0)
            || (candidateCount > 0 && mergedCount >= candidateCount))
        {
            throw new ArgumentOutOfRangeException(nameof(mergedCount));
        }

        if (candidateCount == 0)
        {
            return;
        }

        Interlocked.Increment(ref generatedCandidateNodes);
        Interlocked.Add(ref generatedCandidates, candidateCount);
        Interlocked.Add(ref equivalentGeneratedCandidatesMerged, mergedCount);
        if (mergedCount > 0)
        {
            Interlocked.Increment(ref generatedCandidateMergeNodes);
        }
    }

    public void RecordGeneratedChoice(int candidateCount, bool skipped)
    {
        if (candidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        }

        Interlocked.Increment(ref generatedChoiceScreens);
        Interlocked.Add(ref generatedChoiceCandidates, candidateCount);
        if (skipped)
        {
            Interlocked.Increment(ref generatedChoiceSkips);
        }
    }

    public void RecordLoop(bool positiveResourceLoop)
    {
        Interlocked.Increment(ref loopDetectionHits);
        if (positiveResourceLoop)
        {
            Interlocked.Increment(ref positiveResourceLoopHits);
        }
    }

    public void RecordPrunedLoop()
    {
        Interlocked.Increment(ref prunedLoopHits);
    }

    public void RecordSearchNode(bool workBudgetFallback, bool fairCandidateBudgetFallback)
    {
        Interlocked.Increment(ref searchNodes);
        if (workBudgetFallback)
        {
            Interlocked.Increment(ref workBudgetFallbackNodes);
        }
        if (fairCandidateBudgetFallback)
        {
            Interlocked.Increment(ref fairCandidateBudgetFallbackNodes);
        }
    }

    public void RecordFairCandidateBudgetScope()
    {
        Interlocked.Increment(ref fairCandidateBudgetScopes);
    }

    public void RecordStateClone()
    {
        Interlocked.Increment(ref stateClones);
    }

    public void RecordPlayTraceNode()
    {
        Interlocked.Increment(ref playTraceNodes);
    }

    public void RecordDeterministicChain(int length)
    {
        int observedMaximum = Volatile.Read(ref maxDeterministicChain);
        while (length > observedMaximum)
        {
            int prior = Interlocked.CompareExchange(ref maxDeterministicChain, length, observedMaximum);
            if (prior == observedMaximum)
            {
                break;
            }

            observedMaximum = prior;
        }
    }

    public void RecordTranspositionLookup(bool hit)
    {
        Interlocked.Increment(ref transpositionLookups);
        if (hit)
        {
            Interlocked.Increment(ref transpositionHits);
        }
    }

    public void RecordTranspositionStore()
    {
        Interlocked.Increment(ref transpositionStores);
    }

    public void RecordSelectiveThirdBranch(
        bool protectedByEngine,
        bool pruned,
        double scoreGap,
        string thirdCandidateTypeName)
    {
        Interlocked.Increment(ref selectiveThirdBranchEligibleNodes);
        Interlocked.Add(
            ref selectiveThirdBranchGapMilliTotal,
            Math.Max(0L, (long)Math.Round(scoreGap * 1000d, MidpointRounding.AwayFromZero)));
        if (protectedByEngine)
        {
            Interlocked.Increment(ref selectiveThirdBranchProtectedNodes);
        }
        if (pruned)
        {
            Interlocked.Increment(ref selectiveThirdBranchPrunedNodes);
            selectiveThirdBranchPrunedCards.AddOrUpdate(thirdCandidateTypeName, 1L, static (_, count) => count + 1L);
        }
    }

    public SearchBranchDiagnosticsSnapshot Snapshot()
    {
        Dictionary<int, long> histogram = [];
        Dictionary<int, long> fullyBranchedHistogram = [];
        for (int branches = 0; branches < selectedBranchHistogram.Length; branches++)
        {
            long count = Interlocked.Read(ref selectedBranchHistogram[branches]);
            if (count > 0)
            {
                histogram[branches] = count;
            }

            long fullyBranchedCount = Interlocked.Read(ref fullyBranchedSelectedBranchHistogram[branches]);
            if (fullyBranchedCount > 0)
            {
                fullyBranchedHistogram[branches] = fullyBranchedCount;
            }
        }

        return new SearchBranchDiagnosticsSnapshot(
            Interlocked.Read(ref decisionNodes),
            Interlocked.Read(ref fullyBranchedDecisionNodes),
            Interlocked.Read(ref greedyDecisionNodes),
            Interlocked.Read(ref baseBranches),
            Interlocked.Read(ref selectedBranches),
            Interlocked.Read(ref extraBranches),
            Interlocked.Read(ref fullyBranchedBaseBranches),
            Interlocked.Read(ref fullyBranchedSelectedBranches),
            Interlocked.Read(ref fullyBranchedExtraBranches),
            Interlocked.Read(ref extraAdmissionNodes),
            Interlocked.Read(ref generatedCandidateNodes),
            Interlocked.Read(ref generatedCandidates),
            Interlocked.Read(ref equivalentGeneratedCandidatesMerged),
            Interlocked.Read(ref generatedCandidateMergeNodes),
            Interlocked.Read(ref generatedChoiceScreens),
            Interlocked.Read(ref generatedChoiceCandidates),
            Interlocked.Read(ref generatedChoiceSkips),
            Interlocked.Read(ref forcedPlayNodes),
            Interlocked.Read(ref loopDetectionHits),
            Interlocked.Read(ref positiveResourceLoopHits),
            Interlocked.Read(ref prunedLoopHits),
            Interlocked.Read(ref searchNodes),
            Interlocked.Read(ref stateClones),
            Interlocked.Read(ref playTraceNodes),
            Interlocked.Read(ref workBudgetFallbackNodes),
            Interlocked.Read(ref fairCandidateBudgetScopes),
            Interlocked.Read(ref fairCandidateBudgetFallbackNodes),
            Interlocked.Read(ref transpositionLookups),
            Interlocked.Read(ref transpositionHits),
            Interlocked.Read(ref transpositionStores),
            Interlocked.Read(ref selectiveThirdBranchEligibleNodes),
            Interlocked.Read(ref selectiveThirdBranchProtectedNodes),
            Interlocked.Read(ref selectiveThirdBranchPrunedNodes),
            Interlocked.Read(ref selectiveThirdBranchGapMilliTotal),
            Volatile.Read(ref maxDeterministicChain),
            Volatile.Read(ref maxSelectedBranches),
            new ReadOnlyDictionary<int, long>(histogram),
            new ReadOnlyDictionary<int, long>(fullyBranchedHistogram),
            new ReadOnlyDictionary<string, long>(
                selectiveThirdBranchPrunedCards
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }
}

public sealed record SearchBranchDiagnosticsSnapshot(
    long DecisionNodes,
    long FullyBranchedDecisionNodes,
    long GreedyDecisionNodes,
    long BaseBranches,
    long SelectedBranches,
    long ExtraBranches,
    long FullyBranchedBaseBranches,
    long FullyBranchedSelectedBranches,
    long FullyBranchedExtraBranches,
    long ExtraAdmissionNodes,
    long GeneratedCandidateNodes,
    long GeneratedCandidates,
    long EquivalentGeneratedCandidatesMerged,
    long GeneratedCandidateMergeNodes,
    long GeneratedChoiceScreens,
    long GeneratedChoiceCandidates,
    long GeneratedChoiceSkips,
    long ForcedPlayNodes,
    long LoopDetectionHits,
    long PositiveResourceLoopHits,
    long PrunedLoopHits,
    long SearchNodes,
    long StateClones,
    long PlayTraceNodes,
    long WorkBudgetFallbackNodes,
    long FairCandidateBudgetScopes,
    long FairCandidateBudgetFallbackNodes,
    long TranspositionLookups,
    long TranspositionHits,
    long TranspositionStores,
    long SelectiveThirdBranchEligibleNodes,
    long SelectiveThirdBranchProtectedNodes,
    long SelectiveThirdBranchPrunedNodes,
    long SelectiveThirdBranchGapMilliTotal,
    int MaxDeterministicChain,
    int MaxSelectedBranches,
    IReadOnlyDictionary<int, long> SelectedBranchHistogram,
    IReadOnlyDictionary<int, long> FullyBranchedSelectedBranchHistogram,
    IReadOnlyDictionary<string, long> SelectiveThirdBranchPrunedCards)
{
    public double AverageBaseBranches => Divide(BaseBranches, DecisionNodes);

    public double AverageSelectedBranches => Divide(SelectedBranches, DecisionNodes);

    public double AverageExtraBranches => Divide(ExtraBranches, DecisionNodes);

    public double AverageFullyBranchedBaseBranches =>
        Divide(FullyBranchedBaseBranches, FullyBranchedDecisionNodes);

    public double AverageFullyBranchedSelectedBranches =>
        Divide(FullyBranchedSelectedBranches, FullyBranchedDecisionNodes);

    public double AverageFullyBranchedExtraBranches =>
        Divide(FullyBranchedExtraBranches, FullyBranchedDecisionNodes);

    public double ExtraAdmissionNodeRate => Divide(ExtraAdmissionNodes, DecisionNodes);

    public double EquivalentGeneratedCandidateMergeRate =>
        Divide(EquivalentGeneratedCandidatesMerged, GeneratedCandidates);

    public double TranspositionHitRate => Divide(TranspositionHits, TranspositionLookups);

    public double SelectiveThirdBranchProtectedRate =>
        Divide(SelectiveThirdBranchProtectedNodes, SelectiveThirdBranchEligibleNodes);

    public double SelectiveThirdBranchPrunedRate =>
        Divide(SelectiveThirdBranchPrunedNodes, SelectiveThirdBranchEligibleNodes);

    public double AverageSelectiveThirdBranchScoreGap =>
        Divide(SelectiveThirdBranchGapMilliTotal, SelectiveThirdBranchEligibleNodes) / 1000d;

    public int SelectedBranchP95 => Percentile95(SelectedBranchHistogram, DecisionNodes);

    public int FullyBranchedSelectedBranchP95 =>
        Percentile95(FullyBranchedSelectedBranchHistogram, FullyBranchedDecisionNodes);

    private static double Divide(long numerator, long denominator)
    {
        return denominator == 0 ? 0d : (double)numerator / denominator;
    }

    private static int Percentile95(IReadOnlyDictionary<int, long> histogram, long total)
    {
        if (total <= 0)
        {
            return 0;
        }

        long threshold = (long)Math.Ceiling(total * 0.95d);
        long cumulative = 0;
        foreach (KeyValuePair<int, long> bucket in histogram.OrderBy(pair => pair.Key))
        {
            cumulative += bucket.Value;
            if (cumulative >= threshold)
            {
                return bucket.Key;
            }
        }

        return histogram.Count == 0 ? 0 : histogram.Keys.Max();
    }
}
