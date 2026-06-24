namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueEntry
{
    public CardStateValues ManualValues { get; init; } = new();

    public CardStateValues SmithValues { get; init; } = new();

    public string? Note { get; init; }

    public double? ResolveManualLayerValue(CardUpgradeState state, int layer)
    {
        return ManualValues.Resolve(state, layer);
    }

    public double? ResolveSmithLayerValue(CardUpgradeState state, int layer)
    {
        return SmithValues.Resolve(state, layer);
    }
}
