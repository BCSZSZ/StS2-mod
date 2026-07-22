using System.Text;

namespace CardValueOverlay.Modeling.Combat;

public static class SemanticRandomStreams
{
    public static ulong ForDeckShuffle(ulong runKey, int shuffleCycle, string stableCardId) =>
        Derive(runKey, "deck-shuffle", shuffleCycle, stableCardId);

    public static ulong ForMonsterTransition(ulong runKey, string monsterStableId, int transitionOrdinal) =>
        Derive(runKey, "monster-transition", transitionOrdinal, monsterStableId);

    public static ulong ForGeneratedOutcome(ulong runKey, string sourceCardStableId, int playOrdinal) =>
        Derive(runKey, "generated-outcome", playOrdinal, sourceCardStableId);

    public static ulong ForPlanningChance(ulong runKey, int ordinal, string stableKey) =>
        Derive(runKey, "planning-chance", ordinal, stableKey);

    public static ulong ForEvaluationChance(ulong runKey, int ordinal, string stableKey) =>
        Derive(runKey, "evaluation-chance", ordinal, stableKey);

    public static ulong Derive(ulong root, string family, int ordinal, string stableKey)
    {
        ulong hash = root ^ 0x9E3779B97F4A7C15UL;
        foreach (byte value in Encoding.UTF8.GetBytes($"{family}\0{ordinal}\0{stableKey}"))
        {
            hash ^= value;
            hash *= 1099511628211UL;
            hash ^= hash >> 32;
        }
        return Mix(hash);
    }

    public static int Index(ulong key, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        return (int)(Mix(key) % (uint)count);
    }

    public static double UnitDouble(ulong key) => (Mix(key) >> 11) * (1d / (1UL << 53));

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
