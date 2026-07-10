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
    IReadOnlyList<CardPlayTurnSummary> PlayedCardsByTurn,
    IReadOnlyList<CardValueCreditSummary> CardValueCredits,
    IReadOnlyList<CardValueCreditTurnSummary> CardValueCreditsByTurn,
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

public sealed record CardPlayTurnSummary(
    int Turn,
    string ModelId,
    string TypeName,
    int PlayCount,
    decimal AveragePlaysPerRun,
    decimal AverageValuePerPlay);

public sealed record CardValueCreditSummary(
    string ModelId,
    string TypeName,
    int DirectPlayCount,
    decimal DirectValue,
    decimal ForgeRealizedValue,
    decimal PowerRealizedValue,
    decimal EnergyRealizedValue,
    decimal StarRealizedValue,
    decimal TotalCreditedValue,
    decimal AverageDirectValuePerPlay,
    decimal AverageForgeRealizedValuePerPlay,
    decimal AveragePowerRealizedValuePerPlay,
    decimal AverageEnergyRealizedValuePerPlay,
    decimal AverageStarRealizedValuePerPlay,
    decimal AverageCreditedValuePerPlay);

public sealed record CardValueCreditTurnSummary(
    int Turn,
    string ModelId,
    string TypeName,
    int DirectPlayCount,
    decimal DirectValue,
    decimal ForgeRealizedValue,
    decimal PowerRealizedValue,
    decimal EnergyRealizedValue,
    decimal StarRealizedValue,
    decimal TotalCreditedValue,
    decimal AverageCreditedValuePerPlay);

public sealed record ResourceMarginalEstimate(
    string Name,
    decimal BaseExpectedValue,
    decimal VariantExpectedValue,
    decimal ExpectedValueDelta,
    decimal PerTurnDelta,
    string Description);

public sealed record TrackedCardSimulationReport(
    IReadOnlyList<TrackedCardTurnSummary> Turns);

public sealed record DeckInstanceTrackingReport(
    IReadOnlyList<decimal> ExpectedTurnValues,
    IReadOnlyList<int[]> StartingInstancePlayCountsByTurn,
    IReadOnlyList<int> InputDeckIndicesByStartingInstance);

public sealed record TrackedCardTurnSummary(
    int Turn,
    decimal ExpectedValue,
    int PlayCount,
    int DirectPlayCount,
    decimal DirectValue,
    decimal ForgeRealizedValue,
    decimal PowerRealizedValue,
    decimal EnergyRealizedValue,
    decimal StarRealizedValue,
    decimal TotalCreditedValue);
