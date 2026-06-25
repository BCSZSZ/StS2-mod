namespace CardValueOverlay.Modeling.Simulation;

public sealed record DeckSimulationReport(
    int DeckSize,
    int PlayableDeckSize,
    DeckSimulationOptions Options,
    decimal TotalExpectedValue,
    decimal TotalVariance,
    IReadOnlyList<TurnSimulationSummary> Turns,
    IReadOnlyList<TurnCovariance> TurnCovariances,
    IReadOnlyList<CardPlaySummary> PlayedCards,
    IReadOnlyList<ResourceMarginalEstimate> MarginalEstimates,
    IReadOnlyList<string> Warnings,
    string Provenance);

public sealed record TurnSimulationSummary(
    int Turn,
    decimal ExpectedValue,
    decimal Variance,
    decimal StandardDeviation,
    decimal P10,
    decimal P25,
    decimal P50,
    decimal P75,
    decimal P90,
    decimal AverageCardsDrawn,
    decimal AverageCardsPlayed,
    decimal AverageEnergySpent,
    decimal AverageEnergyGained,
    decimal AverageEnergyWasted,
    decimal AverageStarsSpent,
    decimal AverageStarsGained,
    decimal AverageStarsWasted,
    decimal AverageUnplayedIntrinsicValue,
    IReadOnlyList<ProbabilityBucket> EmpiricalPmf);

public sealed record ProbabilityBucket(
    decimal Value,
    int Count,
    decimal Probability);

public sealed record TurnCovariance(
    int FirstTurn,
    int SecondTurn,
    decimal Covariance);

public sealed record CardPlaySummary(
    string ModelId,
    string TypeName,
    int PlayCount,
    decimal AveragePlaysPerRun,
    decimal AverageValuePerPlay);

public sealed record ResourceMarginalEstimate(
    string Name,
    decimal BaseExpectedValue,
    decimal VariantExpectedValue,
    decimal ExpectedValueDelta,
    decimal PerTurnDelta,
    string Description);
