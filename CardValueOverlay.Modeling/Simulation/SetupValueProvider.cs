using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// How a single setup-value slot obtains its number. Mirrors the intent of
/// <see cref="CardValueOverlay.Core.Values.ValueSource"/> on the simulator side: a
/// per-card, per-slot setup prior can be a hand-set constant, a named stateless
/// function of the card's own fields, or a reference to a measured table
/// (direct-play / deck-delta). <see cref="Inherit"/> means "use the shared default"
/// (the other slot if it is set, otherwise the measured source).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SetupValueProviderKind
{
    Inherit,
    Constant,
    Function,
    Source
}

/// <summary>
/// One setup-value slot's source description. Exactly one of the payload fields is
/// meaningful per <see cref="Kind"/>. This is deliberately a leaf descriptor: value
/// combinators (e.g. max-with-measured floors) are a resolver-level policy, decided
/// during migration, not encoded here.
/// </summary>
public sealed record SetupValueProvider
{
    public static readonly SetupValueProvider Inherit = new();

    public SetupValueProviderKind Kind { get; init; } = SetupValueProviderKind.Inherit;

    /// <summary>Used when <see cref="Kind"/> is <see cref="SetupValueProviderKind.Constant"/>.</summary>
    public double? Constant { get; init; }

    /// <summary>
    /// Named function id when <see cref="Kind"/> is <see cref="SetupValueProviderKind.Function"/>;
    /// see <see cref="SetupValueFunctions"/> (for example "star" or "resource").
    /// </summary>
    public string? Function { get; init; }

    /// <summary>
    /// Named measured-table id when <see cref="Kind"/> is <see cref="SetupValueProviderKind.Source"/>.
    /// Null selects the default measured table (the form's own measured values).
    /// </summary>
    public string? Source { get; init; }

    public static SetupValueProvider FromConstant(double value) =>
        new() { Kind = SetupValueProviderKind.Constant, Constant = value };

    public static SetupValueProvider FromFunction(string function) =>
        new() { Kind = SetupValueProviderKind.Function, Function = function };

    public static SetupValueProvider FromSource(string? source = null) =>
        new() { Kind = SetupValueProviderKind.Source, Source = source };
}
