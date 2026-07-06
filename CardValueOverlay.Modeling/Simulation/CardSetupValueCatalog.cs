using System.Text.Json;

namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Per-card setup-value catalog (the target single source of truth). Keyed by base
/// <c>ModelId</c>; each entry carries an unupgraded and an upgraded form. A form holds
/// optional <c>beam</c> / <c>play</c> providers plus the measured per-horizon values a
/// <see cref="SetupValueProviderKind.Source"/> slot reads. This is the sole setup-prior source:
/// <see cref="SimulationCardLibraryBuilder"/> resolves it for every card, and the runtime service
/// loads it from the packaged <c>card_setup_values.json</c>.
/// </summary>
public sealed record CardSetupValueCatalog
{
    public const int CurrentSchemaVersion = 1;

    public static readonly CardSetupValueCatalog Empty = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? Source { get; init; }

    public string? GeneratedAt { get; init; }

    public IReadOnlyDictionary<string, CardSetupValueEntry> Cards { get; init; } =
        new Dictionary<string, CardSetupValueEntry>(StringComparer.OrdinalIgnoreCase);

    public CardSetupValueForm? Resolve(string modelId, int upgradeLevel)
    {
        string baseModelId = BaseModelId(modelId);
        if (!Cards.TryGetValue(baseModelId, out CardSetupValueEntry? entry))
        {
            return null;
        }

        return upgradeLevel > 0
            ? entry.Upgraded
            : entry.Unupgraded;
    }

    public static CardSetupValueCatalog LoadOrEmpty(string path, JsonSerializerOptions jsonOptions)
    {
        return File.Exists(path)
            ? Parse(File.ReadAllText(path), jsonOptions)
            : Empty;
    }

    public static CardSetupValueCatalog Parse(string json, JsonSerializerOptions jsonOptions)
    {
        CardSetupValueCatalog catalog =
            JsonSerializer.Deserialize<CardSetupValueCatalog>(json, jsonOptions)
            ?? Empty;
        if (catalog.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Card setup value schemaVersion={catalog.SchemaVersion}; expected {CurrentSchemaVersion}.");
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

public sealed record CardSetupValueEntry
{
    public string? TypeName { get; init; }

    public CardSetupValueForm? Unupgraded { get; init; }

    public CardSetupValueForm? Upgraded { get; init; }
}

public sealed record CardSetupValueForm
{
    /// <summary>Beam-entry (reachability) provider. Null inherits the shared default.</summary>
    public SetupValueProvider? Beam { get; init; }

    /// <summary>Line-valuation (decision) provider. Null inherits the shared default.</summary>
    public SetupValueProvider? Play { get; init; }

    /// <summary>Measured per-horizon values a <see cref="SetupValueProviderKind.Source"/> slot reads.</summary>
    public HorizonValues? Measured { get; init; }
}
