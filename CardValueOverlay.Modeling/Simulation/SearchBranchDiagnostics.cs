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
    private long forcedPlayNodes;
    private long loopDetectionHits;
    private long positiveResourceLoopHits;
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

    public void RecordLoop(bool positiveResourceLoop)
    {
        Interlocked.Increment(ref loopDetectionHits);
        if (positiveResourceLoop)
        {
            Interlocked.Increment(ref positiveResourceLoopHits);
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
            Interlocked.Read(ref forcedPlayNodes),
            Interlocked.Read(ref loopDetectionHits),
            Interlocked.Read(ref positiveResourceLoopHits),
            Volatile.Read(ref maxSelectedBranches),
            new ReadOnlyDictionary<int, long>(histogram),
            new ReadOnlyDictionary<int, long>(fullyBranchedHistogram));
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
    long ForcedPlayNodes,
    long LoopDetectionHits,
    long PositiveResourceLoopHits,
    int MaxSelectedBranches,
    IReadOnlyDictionary<int, long> SelectedBranchHistogram,
    IReadOnlyDictionary<int, long> FullyBranchedSelectedBranchHistogram)
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
