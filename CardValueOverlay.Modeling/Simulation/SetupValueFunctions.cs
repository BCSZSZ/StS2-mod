namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Named, STATELESS setup-value functions of a card's own fields plus horizon. These
/// re-express today's scattered hard-coded mechanisms - the Power floor, the star
/// bonus, and the draw/energy/star resource-reference proxy - as explicit,
/// per-card-selectable providers. Functions must stay pure over
/// <see cref="SetupValueContext"/>: a resolved setup value has to be a per-card
/// constant so it can serve as both a beam-entry score and a decision prior without
/// recomputation. Anything state-dependent belongs to the simulator's realized layer.
///
/// The resource price constants mirror <c>DeckMonteCarloSimulator</c>'s private
/// <c>*ResourceReferenceValues</c>; on the Batch 4 deletion this file becomes their
/// single canonical home.
/// </summary>
public static class SetupValueFunctions
{
    /// <summary>Flat reachability value for Powers (formerly the hard-coded floor).</summary>
    public const double PowerFloor = 99d;

    /// <summary>Value of one star gained (formerly <c>StarSetupPriorityValuePerStar</c>).</summary>
    public const double StarUnitValue = 5d;

    private const double NextTurnMultiplier = 0.75d;

    private readonly record struct ResourcePrices(double Draw, double Energy, double Star);

    private static readonly ResourcePrices Shortline = new(Draw: 5.1d, Energy: 8.8d, Star: 2.7d);
    private static readonly ResourcePrices Midline = new(Draw: 5.2d, Energy: 10.0d, Star: 5.3d);
    private static readonly ResourcePrices Longline = new(Draw: 5.1d, Energy: 11.2d, Star: 6.3d);

    private static readonly IReadOnlyDictionary<string, Func<SetupValueContext, double>> Registry =
        new Dictionary<string, Func<SetupValueContext, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = _ => 0d,
            ["powerFloor"] = _ => PowerFloor,
            ["star"] = ctx => (ctx.StarGain + ctx.StarNextTurn) * StarUnitValue,
            ["resource"] = Resource
        };

    public static bool IsKnown(string name) => Registry.ContainsKey(name);

    public static bool TryEvaluate(string name, SetupValueContext context, out double value)
    {
        if (Registry.TryGetValue(name, out Func<SetupValueContext, double>? function))
        {
            value = function(context);
            return true;
        }

        value = 0d;
        return false;
    }

    private static double Resource(SetupValueContext context)
    {
        ResourcePrices prices = context.Horizon switch
        {
            SetupHorizon.Shortline => Shortline,
            SetupHorizon.Longline => Longline,
            _ => Midline
        };

        double immediate =
            (context.Draw * prices.Draw)
            + (context.EnergyGain * prices.Energy)
            + (context.StarGain * prices.Star);
        double nextTurn =
            (context.DrawNextTurn * prices.Draw)
            + (context.EnergyNextTurn * prices.Energy)
            + (context.StarNextTurn * prices.Star);
        return immediate + (nextTurn * NextTurnMultiplier);
    }
}
