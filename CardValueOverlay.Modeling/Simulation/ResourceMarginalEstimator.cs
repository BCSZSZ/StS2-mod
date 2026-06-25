namespace CardValueOverlay.Modeling.Simulation;

public sealed class ResourceMarginalEstimator
{
    public IReadOnlyList<ResourceMarginalEstimate> Estimate(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options,
        DeckSimulationReport baseReport)
    {
        (string Name, DeckSimulationOptions Options, string Description)[] variants =
        [
            (
                "+1 energy/turn",
                options with { BaseEnergy = options.BaseEnergy + 1 },
                "Adds one spendable energy at the start of every simulated turn."
            ),
            (
                "+1 star/turn",
                options with { BaseStars = options.BaseStars + 1 },
                "Adds one spendable star at the start of every simulated turn."
            ),
            (
                "+1 draw/turn",
                options with { HandSize = options.HandSize + 1 },
                "Draws one extra card at the start of every simulated turn."
            )
        ];

        DeckMonteCarloSimulator simulator = new();
        return variants
            .Select(variant =>
            {
                DeckSimulationReport variantReport = simulator.Simulate(deck, variant.Options);
                decimal delta = variantReport.TotalExpectedValue - baseReport.TotalExpectedValue;
                return new ResourceMarginalEstimate(
                    variant.Name,
                    baseReport.TotalExpectedValue,
                    variantReport.TotalExpectedValue,
                    Round(delta),
                    Round(delta / options.Turns),
                    variant.Description);
            })
            .ToArray();
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
}
