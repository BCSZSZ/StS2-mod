using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed record TrainingHorizonTimestamps
{
    public string? Shortline { get; init; }

    public string? Midline { get; init; }

    public string? Longline { get; init; }

    public string? Resolve(TrainingValueHorizon horizon)
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
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(Shortline)
        || !string.IsNullOrWhiteSpace(Midline)
        || !string.IsNullOrWhiteSpace(Longline);

    [JsonIgnore]
    public bool HasAllValues =>
        !string.IsNullOrWhiteSpace(Shortline)
        && !string.IsNullOrWhiteSpace(Midline)
        && !string.IsNullOrWhiteSpace(Longline);
}
