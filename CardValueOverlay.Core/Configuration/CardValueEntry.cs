namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueEntry
{
    public double? ManualValue { get; init; }

    public string? Note { get; init; }
}
