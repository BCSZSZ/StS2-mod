namespace CardValueOverlay.Modeling.Simulation;

public sealed record GeneratedCardPoolCatalog
{
    public static GeneratedCardPoolCatalog Empty { get; } = new();

    public int Version { get; init; } = 1;

    public Dictionary<string, string[]> Pools { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> RequirePool(string poolId)
    {
        if (!Pools.TryGetValue(poolId, out string[]? entries) || entries.Length == 0)
        {
            throw new InvalidOperationException($"Generated card pool '{poolId}' is missing or empty.");
        }

        return entries;
    }
}
