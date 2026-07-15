namespace CardValueOverlay.Core.Configuration;

public sealed record RealtimeSimulationSettings(
    int Branch,
    int TurnDepth,
    int MinRuns,
    int MaxRuns,
    int ComplexCardMinRuns,
    int ConfidenceLevelPercent,
    bool EarlyStoppingEnabled)
{
    public const int MinimumBranch = 1;
    public const int MaximumBranch = 5;
    public const int DefaultBranch = 3;

    public const int MinimumTurnDepth = 4;
    public const int MaximumTurnDepth = 6;
    public const int DefaultTurnDepth = 6;

    public const int MinimumAllowedRuns = 15;
    public const int MaximumAllowedRuns = 60;
    public const int RunBatchSize = 15;
    public const int DefaultMinRuns = 15;
    public const int DefaultMaxRuns = 60;
    public const int DefaultComplexCardMinRuns = 30;

    public const int MinimumConfidenceLevelPercent = 80;
    public const int MaximumConfidenceLevelPercent = 99;
    public const int DefaultConfidenceLevelPercent = 95;
    public const bool DefaultEarlyStoppingEnabled = true;

    public string CacheKey =>
        $"branch{Branch}|depth{TurnDepth}|minRuns{MinRuns}|maxRuns{MaxRuns}" +
        $"|complexMinRuns{ComplexCardMinRuns}|confidence{ConfidenceLevelPercent}" +
        $"|earlyStop{(EarlyStoppingEnabled ? 1 : 0)}";

    public int EffectiveMinimumRuns(bool complexCard)
    {
        return complexCard ? Math.Max(MinRuns, ComplexCardMinRuns) : MinRuns;
    }

    public int PlannedStoppingLooks(bool complexCard)
    {
        int minimum = EffectiveMinimumRuns(complexCard);
        return ((MaxRuns - minimum) / RunBatchSize) + 1;
    }

    public static RealtimeSimulationSettings Normalize(
        int branch,
        int turnDepth,
        int minRuns,
        int maxRuns,
        int complexCardMinRuns,
        int confidenceLevelPercent,
        bool earlyStoppingEnabled)
    {
        int normalizedMinRuns = NormalizeRuns(minRuns);
        int normalizedMaxRuns = Math.Max(normalizedMinRuns, NormalizeRuns(maxRuns));
        int normalizedComplexMinRuns = Math.Clamp(
            NormalizeRuns(complexCardMinRuns),
            normalizedMinRuns,
            normalizedMaxRuns);
        return new RealtimeSimulationSettings(
            Math.Clamp(branch, MinimumBranch, MaximumBranch),
            Math.Clamp(turnDepth, MinimumTurnDepth, MaximumTurnDepth),
            normalizedMinRuns,
            normalizedMaxRuns,
            normalizedComplexMinRuns,
            Math.Clamp(
                confidenceLevelPercent,
                MinimumConfidenceLevelPercent,
                MaximumConfidenceLevelPercent),
            earlyStoppingEnabled);
    }

    private static int NormalizeRuns(int runs)
    {
        int clamped = Math.Clamp(runs, MinimumAllowedRuns, MaximumAllowedRuns);
        return clamped - (clamped % RunBatchSize);
    }
}
