using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed record TrainingHorizonValues
{
    public double? Shortline { get; init; }

    public double? Midline { get; init; }

    public double? Longline { get; init; }

    public double? Resolve(TrainingValueHorizon horizon)
    {
        return horizon switch
        {
            TrainingValueHorizon.Shortline => Shortline,
            TrainingValueHorizon.Midline => Midline,
            TrainingValueHorizon.Longline => Longline,
            _ => null
        };
    }

    [JsonIgnore]
    public bool HasAnyValue => Shortline.HasValue || Midline.HasValue || Longline.HasValue;

    [JsonIgnore]
    public bool HasAllValues => Shortline.HasValue && Midline.HasValue && Longline.HasValue;
}
