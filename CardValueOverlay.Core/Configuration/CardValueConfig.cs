namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueConfig
{
    public const int SupportedSchemaVersion = 2;

    public int SchemaVersion { get; init; } = SupportedSchemaVersion;

    public OverlaySettings Overlay { get; init; } = new();

    public Dictionary<string, CardValueEntry> Cards { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CommonParameterEntry> CommonParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static CardValueConfig CreateDefault() => new()
    {
        CommonParameters = new Dictionary<string, CommonParameterEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [CommonParameterIds.DeckCount] = new() { Note = "Runtime-computed later." },
            [CommonParameterIds.CardsDrawnPerTurn] = new()
            {
                FixedValues = new LayeredValueTable { [1] = 5 },
                Note = "Default placeholder."
            },
            [CommonParameterIds.TurnsPerShuffleCycle] = new() { Note = "Formula intentionally unspecified." }
        }
    };
}
