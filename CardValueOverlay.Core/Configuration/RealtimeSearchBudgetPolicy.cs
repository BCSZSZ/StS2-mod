namespace CardValueOverlay.Core.Configuration;

/// <summary>
/// Deterministic per-turn search-node limits for the three realtime overlay horizons. Ordinary
/// decks stay below these caps; expensive tails degrade through the simulator's fair anytime
/// scheduler instead of monopolizing the background worker.
/// </summary>
public static class RealtimeSearchBudgetPolicy
{
    public const int ShortlineMaxSearchNodesPerTurn = 250_000;

    public const int MidlineMaxSearchNodesPerTurn = 60_000;

    public const int LonglineMaxSearchNodesPerTurn = 100_000;

    public static int ResolveMaxSearchNodesPerTurn(int turns)
    {
        return turns switch
        {
            4 => ShortlineMaxSearchNodesPerTurn,
            8 => MidlineMaxSearchNodesPerTurn,
            12 => LonglineMaxSearchNodesPerTurn,
            _ => throw new ArgumentOutOfRangeException(nameof(turns), turns, "Unsupported realtime horizon.")
        };
    }
}
