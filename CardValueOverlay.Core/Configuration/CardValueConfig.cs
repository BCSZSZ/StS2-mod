namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueConfig
{
    public int SchemaVersion { get; init; } = 1;

    public OverlaySettings Overlay { get; init; } = new();

    public Dictionary<string, CardValueEntry> Cards { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CommonParameterEntry> CommonParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static CardValueConfig CreateDefault() => new()
    {
        CommonParameters = new Dictionary<string, CommonParameterEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [CommonParameterIds.DeckCount] = new() { Note = "Runtime-computed later." },
            [CommonParameterIds.CardsDrawnPerTurn] = new() { FixedValue = 5, Note = "Default placeholder." },
            [CommonParameterIds.TurnsPerShuffleCycle] = new() { Note = "Formula intentionally unspecified." }
        }
    };
}
