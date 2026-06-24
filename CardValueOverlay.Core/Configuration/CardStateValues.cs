namespace CardValueOverlay.Core.Configuration;

public sealed record CardStateValues
{
    public LayeredValueTable Unupgraded { get; init; } = new();

    public LayeredValueTable Upgraded { get; init; } = new();

    public double? Resolve(CardUpgradeState state, int layer)
    {
        LayeredValueTable? table = state switch
        {
            CardUpgradeState.Unupgraded => Unupgraded,
            CardUpgradeState.Upgraded => Upgraded,
            _ => null
        };

        return table?.Resolve(layer);
    }

    public bool HasAnyValue => Unupgraded.HasAnyValue || Upgraded.HasAnyValue;
}
