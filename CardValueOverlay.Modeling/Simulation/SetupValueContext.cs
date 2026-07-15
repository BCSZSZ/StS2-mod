using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// The horizon a setup value is observed at. Matches the simulator's shortline /
/// midline / longline (4 / 8 / 12 turn) reporting horizons.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SetupHorizon
{
    Shortline,
    Midline,
    Longline
}

/// <summary>
/// The stateless inputs a <see cref="SetupValueProviderKind.Function"/> provider is
/// allowed to read: the card's own extracted fields plus the horizon. Providers must
/// be pure over this context so that a resolved setup value is a per-card constant
/// (cheap, deterministic, cacheable). State-dependent value (current strength,
/// vulnerable, energy sequencing, synergy) stays in the simulator's realized layer and
/// must never leak into a provider.
/// </summary>
public readonly record struct SetupValueContext(
    string? CardType,
    double Draw,
    double DrawNextTurn,
    int EnergyGain,
    int EnergyNextTurn,
    int StarGain,
    int StarNextTurn,
    SetupHorizon Horizon);

/// <summary>Per-horizon measured values a <see cref="SetupValueProviderKind.Source"/> slot reads.</summary>
public sealed record HorizonValues
{
    public double? Shortline { get; init; }

    public double? Midline { get; init; }

    public double? Longline { get; init; }

    public double? For(SetupHorizon horizon) => horizon switch
    {
        SetupHorizon.Shortline => Shortline,
        SetupHorizon.Midline => Midline,
        SetupHorizon.Longline => Longline,
        _ => null
    };
}
