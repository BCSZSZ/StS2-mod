namespace CardValueOverlay.Modeling.Simulation;

/// <summary>Which provider kind produced a resolved slot value (for diagnostics/provenance).</summary>
public enum SetupValueSource
{
    Constant,
    Function,
    Source
}

public readonly record struct ResolvedSetupValue(
    double Beam,
    double Play,
    SetupValueSource BeamSource,
    SetupValueSource PlaySource);

/// <summary>
/// Resolves a card's per-slot setup-value providers into concrete (beam, play) numbers.
/// Mirrors the intent of <see cref="CardValueOverlay.Core.Values.ValueResolver"/> on the
/// simulator side: one per-card override point, each slot sourced from a constant, a
/// named function, or a measured table.
///
/// Default rule (so <c>beam == play</c> unless you say otherwise): an unspecified slot
/// mirrors the other slot; if both are unspecified, both fall back to the measured
/// source (which is 0 when the form or its measured value is absent).
///
/// This is scaffolding - nothing in the live simulator calls it yet.
/// </summary>
public sealed class SetupValueResolver
{
    private readonly CardSetupValueCatalog catalog;

    public SetupValueResolver(CardSetupValueCatalog catalog)
    {
        this.catalog = catalog ?? CardSetupValueCatalog.Empty;
    }

    public ResolvedSetupValue Resolve(string modelId, int upgradeLevel, SetupValueContext context) =>
        Resolve(catalog.Resolve(modelId, upgradeLevel), context);

    public static ResolvedSetupValue Resolve(CardSetupValueForm? form, SetupValueContext context)
    {
        SetupValueProvider beam = form?.Beam ?? form?.Play ?? SetupValueProvider.FromSource();
        SetupValueProvider play = form?.Play ?? form?.Beam ?? SetupValueProvider.FromSource();
        (double beamValue, SetupValueSource beamSource) = Evaluate(beam, form, context);
        (double playValue, SetupValueSource playSource) = Evaluate(play, form, context);

        return new ResolvedSetupValue(beamValue, playValue, beamSource, playSource);
    }

    private static (double Value, SetupValueSource Source) Evaluate(
        SetupValueProvider provider,
        CardSetupValueForm? form,
        SetupValueContext context)
    {
        switch (provider.Kind)
        {
            case SetupValueProviderKind.Constant:
                return (provider.Constant ?? 0d, SetupValueSource.Constant);

            case SetupValueProviderKind.Function:
                if (provider.Function is null
                    || !SetupValueFunctions.TryEvaluate(provider.Function, context, out double functionValue))
                {
                    throw new InvalidOperationException(
                        $"Unknown setup-value function '{provider.Function}'.");
                }

                return (functionValue, SetupValueSource.Function);

            case SetupValueProviderKind.Source:
            case SetupValueProviderKind.Inherit:
            default:
                // A fully-inherited slot with no measured fallback resolves to the measured
                // source (0 when the form or the horizon value is absent).
                return (form?.Measured?.For(context.Horizon) ?? 0d, SetupValueSource.Source);
        }
    }
}
