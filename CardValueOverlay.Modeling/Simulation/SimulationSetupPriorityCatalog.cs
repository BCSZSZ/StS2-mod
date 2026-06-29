using System.Text.Json;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record SimulationSetupPriorityCatalog
{
    public const int CurrentSchemaVersion = 1;

    public static readonly SimulationSetupPriorityCatalog Empty = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? Source { get; init; }

    public string? GeneratedAt { get; init; }

    public string? SetupSourceHorizon { get; init; }

    public IReadOnlyDictionary<string, SimulationSetupPriorityEntry> Cards { get; init; } =
        new Dictionary<string, SimulationSetupPriorityEntry>(StringComparer.OrdinalIgnoreCase);

    public decimal? Resolve(string modelId, int upgradeLevel)
    {
        string baseModelId = BaseModelId(modelId);
        if (!Cards.TryGetValue(baseModelId, out SimulationSetupPriorityEntry? entry))
        {
            return null;
        }

        return upgradeLevel > 0
            ? entry.Upgraded
            : entry.Unupgraded;
    }

    public static SimulationSetupPriorityCatalog LoadOrEmpty(string path, JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        SimulationSetupPriorityCatalog catalog =
            JsonSerializer.Deserialize<SimulationSetupPriorityCatalog>(File.ReadAllText(path), jsonOptions)
            ?? Empty;
        if (catalog.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Simulation setup priority schemaVersion={catalog.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        return catalog with
        {
            Cards = catalog.Cards.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string BaseModelId(string modelId)
    {
        int separator = modelId.LastIndexOf('+');
        return separator > 0
            ? modelId[..separator]
            : modelId;
    }
}

public sealed record SimulationSetupPriorityEntry
{
    public string? TypeName { get; init; }

    public decimal? Unupgraded { get; init; }

    public decimal? Upgraded { get; init; }
}
