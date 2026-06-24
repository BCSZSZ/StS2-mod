namespace CardValueOverlay.Core.Configuration;

public sealed record CommonParameterEntry
{
    public LayeredValueTable FixedValues { get; init; } = new();

    public string? Note { get; init; }

    public double? ResolveFixedLayerValue(int layer)
    {
        return FixedValues.Resolve(layer);
    }
}
