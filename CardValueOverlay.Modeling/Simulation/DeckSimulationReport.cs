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
    IReadOnlyList<CardMoveChoiceSummary> CardMoveChoices,
    IReadOnlyList<CardTransformChoiceSummary> CardTransformChoices,
    IReadOnlyList<ResourceMarginalEstimate> MarginalEstimates,
    IReadOnlyList<string> Warnings,
    string Provenance)
{
    public StarPlayDiagnosticsReport? StarPlayDiagnostics { get; init; }
}

public sealed record StarPlayDiagnosticsReport(
    StarCardCategoryFlowSummary StarGainCards,
    StarCardCategoryFlowSummary StarCostCards,
    IReadOnlyList<StarCardFlowSummary> Cards,
    int RunsWithAnyStarCardPlay,
    int RunsFirstStarCardWasGainOnly,
    int RunsFirstStarCardWasCostOnly,
    int RunsFirstStarCardWasGainAndCost,
    decimal FirstStarCardWasGainProbability,
    int StarShortageBlockedCardCount,
    int StarShortageBlockedCardCountWithMissedPriorGainOpportunity,
    decimal MissedPriorGainOpportunityProbabilityPerBlockedCard,
    int StarShortageBlockedTurnCount,
    int StarShortageBlockedTurnCountWithMissedPriorGainOpportunity,
    decimal MissedPriorGainOpportunityProbabilityPerBlockedTurn,
    int RunsWithStarShortageBlock,
    int RunsWithStarShortageBlockAndMissedPriorGainOpportunity,
    decimal MissedPriorGainOpportunityProbabilityPerBlockedRun);

public sealed record StarCardCategoryFlowSummary(
    int DrawCount,
    int PlayCount,
    decimal PlaysPerDraw,
    int RunsWithDraw,
    int RunsWithPlay);

public sealed record StarCardFlowSummary(
    string ModelId,
    string TypeName,
    bool GainsStars,
    bool CostsStars,
    int DrawCount,
    int PlayCount,
    decimal PlaysPerDraw,
    int RunsWithDraw,
    int RunsWithPlay);

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
    decimal AverageValuePerPlay,
    decimal AveragePositionInTurn,
    int MinimumPositionInTurn,
    int MaximumPositionInTurn);

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

public sealed record CardMoveChoiceSummary(
    string SourceModelId,
    string SourceTypeName,
    string CandidateModelId,
    string CandidateTypeName,
    string FromPile,
    string ToPile,
    int CandidateSeenCount,
    int MoveCount,
    decimal MoveRate,
    decimal AverageCandidateScore,
    decimal? AverageMovedCandidateScore,
    decimal? AverageRetainedCandidateScore,
    decimal MinimumCandidateScore,
    decimal MaximumCandidateScore);

public sealed record CardTransformChoiceSummary(
    string SourceModelId,
    string SourceTypeName,
    string CandidateModelId,
    string CandidateTypeName,
    string ReplacementModelId,
    string ReplacementTypeName,
    int CandidateSeenCount,
    int TransformCount,
    decimal TransformRate,
    decimal AverageCandidateScore,
    decimal? AverageTransformedCandidateScore,
    decimal? AverageRetainedCandidateScore,
    decimal MinimumCandidateScore,
    decimal MaximumCandidateScore,
    decimal AverageReplacementScore);

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

public sealed record ExpectedValueSampleBatch(
    int StartRun,
    IReadOnlyList<double> TotalValuesByRun);

public sealed record TrackedCardTurnSummary(
    int Turn,
    decimal ExpectedValue,
    int DrawCount,
    int PlayCount,
    int DirectPlayCount,
    decimal DirectValue,
    decimal ForgeRealizedValue,
    decimal PowerRealizedValue,
    decimal EnergyRealizedValue,
    decimal StarRealizedValue,
    decimal TotalCreditedValue);
