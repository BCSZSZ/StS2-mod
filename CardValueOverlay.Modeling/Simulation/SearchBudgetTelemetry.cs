namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Low-overhead aggregate for runtime search-budget diagnostics. It records once per completed
/// simulated turn; unlike the offline branch and slow-tail profilers, it adds no work to each
/// search node.
/// </summary>
public sealed class SearchBudgetTelemetryCollector
{
    private long turnCount;
    private long totalNodes;
    private long budgetLimitedTurns;
    private int maximumNodes;

    internal void RecordTurn(int nodes, bool budgetLimited)
    {
        Interlocked.Increment(ref turnCount);
        Interlocked.Add(ref totalNodes, nodes);
        if (budgetLimited)
        {
            Interlocked.Increment(ref budgetLimitedTurns);
        }

        int observedMaximum = Volatile.Read(ref maximumNodes);
        while (nodes > observedMaximum)
        {
            int prior = Interlocked.CompareExchange(ref maximumNodes, nodes, observedMaximum);
            if (prior == observedMaximum)
            {
                break;
            }

            observedMaximum = prior;
        }
    }

    public SearchBudgetTelemetrySnapshot Snapshot() => new(
        Interlocked.Read(ref turnCount),
        Interlocked.Read(ref totalNodes),
        Interlocked.Read(ref budgetLimitedTurns),
        Volatile.Read(ref maximumNodes));
}

public sealed record SearchBudgetTelemetrySnapshot(
    long TurnCount,
    long TotalNodes,
    long BudgetLimitedTurns,
    int MaximumNodes)
{
    public double AverageNodes => TurnCount <= 0 ? 0d : (double)TotalNodes / TurnCount;
}
