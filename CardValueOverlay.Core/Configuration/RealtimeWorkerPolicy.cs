namespace CardValueOverlay.Core.Configuration;

/// <summary>
/// Selects conservative parallelism and cooperative batch sizes for in-game background
/// simulations. Combat work is deliberately serial and advances one deterministic run at a time;
/// non-combat screens may use a small processor-scaled batch without consuming most of the machine.
/// </summary>
public static class RealtimeWorkerPolicy
{
    public const int MaximumNonCombatRunDegree = 4;

    public const int MaximumCombatRunDegree = 1;

    public static int ResolveRunDegree(int logicalProcessorCount, bool inCombat, int turns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(logicalProcessorCount, 1);

        int horizonMaximum = turns switch
        {
            4 => MaximumNonCombatRunDegree,
            8 => 2,
            12 => 1,
            _ => throw new ArgumentOutOfRangeException(
                nameof(turns),
                turns,
                "Unsupported realtime horizon.")
        };
        int maximumRunDegree = inCombat
            ? MaximumCombatRunDegree
            : Math.Min(MaximumNonCombatRunDegree, horizonMaximum);
        int scaledDegree = inCombat ? 1 : logicalProcessorCount / 4;
        return Math.Clamp(scaledDegree, 1, maximumRunDegree);
    }

    public static int ResolveRunsPerSlice(int logicalProcessorCount, bool inCombat, int turns)
    {
        return ResolveRunDegree(logicalProcessorCount, inCombat, turns);
    }
}
