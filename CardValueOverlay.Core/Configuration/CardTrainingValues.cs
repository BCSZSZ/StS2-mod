using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed record CardTrainingValues
{
    public TrainingHorizonValues Unupgraded { get; init; } = new();

    public TrainingHorizonValues Upgraded { get; init; } = new();

    public double? Resolve(CardUpgradeState state, TrainingValueHorizon horizon)
    {
        TrainingHorizonValues? values = state switch
        {
            CardUpgradeState.Unupgraded => Unupgraded,
            CardUpgradeState.Upgraded => Upgraded,
            _ => null
        };

        return values?.Resolve(horizon);
    }

    [JsonIgnore]
    public bool HasAnyValue => Unupgraded.HasAnyValue || Upgraded.HasAnyValue;
}
