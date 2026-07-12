namespace CardValueOverlay.Core.Configuration;

public sealed record RealtimeSimulationSettings(int Branch, int TurnDepth, int Runs)
{
    public const int MinimumBranch = 1;
    public const int MaximumBranch = 5;
    public const int DefaultBranch = 3;

    public const int MinimumTurnDepth = 4;
    public const int MaximumTurnDepth = 12;
    public const int DefaultTurnDepth = 8;

    public const int MinimumRuns = 20;
    public const int MaximumRuns = 100;
    public const int DefaultRuns = 36;

    public string CacheKey => $"branch{Branch}|depth{TurnDepth}|runs{Runs}";

    public static RealtimeSimulationSettings Normalize(int branch, int turnDepth, int runs)
    {
        return new RealtimeSimulationSettings(
            Math.Clamp(branch, MinimumBranch, MaximumBranch),
            Math.Clamp(turnDepth, MinimumTurnDepth, MaximumTurnDepth),
            Math.Clamp(runs, MinimumRuns, MaximumRuns));
    }
}
