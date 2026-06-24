namespace CardValueOverlay.Core.Configuration;

public sealed record CommonParameterEntry
{
    public double? FixedValue { get; init; }

    public string? Note { get; init; }
}
