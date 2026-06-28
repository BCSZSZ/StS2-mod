namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueEntry
{
    public string? TypeName { get; init; }

    public string? Name { get; init; }

    public string? LocalizedNameZhs { get; init; }

    public IReadOnlyList<string> Pools { get; init; } = [];

    public CardTrainingValues TrainingValues { get; init; } = new();

    public string? Note { get; init; }

    public double? ResolveTrainingValue(CardUpgradeState state, TrainingValueHorizon horizon)
    {
        return TrainingValues.Resolve(state, horizon);
    }
}
