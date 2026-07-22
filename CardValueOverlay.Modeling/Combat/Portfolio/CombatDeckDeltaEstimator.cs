using System.Collections.Concurrent;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record CombatDeckDeltaOptions(
    int MinimumSamples = 12,
    int MaximumSamples = 240,
    int DegreeOfParallelism = 4,
    bool UseBaselineCache = true);

public sealed class CombatDeckDeltaEstimator
{
    private readonly CombatCardCatalog _cards;
    private readonly IReadOnlyDictionary<string, CombatMonsterDefinition> _monsters;
    private readonly HpContinuationCatalog _hp;
    private readonly CombatBaselineCache _cache;

    public CombatDeckDeltaEstimator(
        CombatCardCatalog cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        HpContinuationCatalog hp,
        CombatBaselineCache? cache = null)
    {
        _cards = cards;
        _monsters = monsters;
        _hp = hp;
        _cache = cache ?? new CombatBaselineCache();
    }

    public CombatDeckDeltaReport Estimate(
        CombatSamplePlan plan,
        IReadOnlyDictionary<string, CompiledCombatDeck> decks,
        IReadOnlyDictionary<string, EncounterCombatDefinition> encounters,
        CombatCardDefinition candidate,
        IReadOnlyList<int> horizons,
        CombatDeckDeltaOptions options,
        string combatModelHash)
    {
        if (!candidate.IsSupported)
        {
            throw new InvalidOperationException($"Candidate '{candidate.StableKey}' is unsupported: {string.Join("; ", candidate.UnsupportedReasons)}");
        }
        if (options.MinimumSamples <= 0 || options.MaximumSamples < options.MinimumSamples)
        {
            throw new InvalidOperationException("Invalid paired sample bounds.");
        }

        CombatSample[] supported = plan.Samples
            .Where(sample => sample.Supported && decks.ContainsKey(sample.DeckRunId) && encounters.ContainsKey(sample.EncounterId))
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .Take(options.MaximumSamples)
            .ToArray();
        ConcurrentBag<CombatPairSampleResult> pairs = [];
        ParallelOptions parallel = new() { MaxDegreeOfParallelism = Math.Clamp(options.DegreeOfParallelism, 1, 4) };
        Parallel.ForEach(supported, parallel, sample =>
        {
            CompiledCombatDeck baselineDeck = decks[sample.DeckRunId];
            CompiledCombatDeck candidateDeck = baselineDeck with
            {
                DeckId = $"{baselineDeck.DeckId}+{candidate.StableKey}",
                CardDefinitionIds = [.. baselineDeck.CardDefinitionIds, candidate.DefinitionId]
            };
            EncounterCombatDefinition encounter = encounters[sample.EncounterId];
            HpContinuationContext hpContext = _hp.Get(sample.HpContextId);
            foreach (int horizon in horizons.Distinct().Order())
            {
                CombatSimulationOptions solverOptions = new() { HorizonTurns = horizon };
                string cacheKey = _cache.BuildKey(
                    baselineDeck,
                    sample,
                    horizon,
                    solverOptions,
                    _hp.ContentHash,
                    combatModelHash);
                CombatContextResult baseline;
                if (!options.UseBaselineCache || !_cache.TryRead(cacheKey, out baseline!))
                {
                    baseline = new CombatSimulationRunner(_cards, _monsters).EvaluateContext(
                        sample,
                        baselineDeck,
                        encounter,
                        hpContext,
                        horizon);
                    if (options.UseBaselineCache && baseline.SolverStatus != CombatSolveStatus.ExactBudgetExceeded)
                    {
                        _cache.Write(cacheKey, baseline);
                    }
                }

                CombatContextResult candidateResult = new CombatSimulationRunner(_cards, _monsters).EvaluateContext(
                    sample,
                    candidateDeck,
                    encounter,
                    hpContext,
                    horizon);
                pairs.Add(new CombatPairSampleResult(
                    sample,
                    horizon,
                    baseline,
                    candidateResult,
                    candidateResult.Metrics.Value - baseline.Metrics.Value));
            }
        });

        CombatHorizonDeltaReport[] horizonReports = horizons.Distinct().Order()
            .Select(horizon => AggregateHorizon(horizon, pairs.Where(pair => pair.Horizon == horizon).ToArray(), plan))
            .ToArray();
        string[] unsupported = plan.Samples.Where(sample => !sample.Supported)
            .Select(sample => $"{sample.SampleId}: {sample.UnsupportedReason}")
            .ToArray();
        bool enoughSamples = horizonReports.All(report => report.Cells.Sum(cell => cell.SampleCount) >= options.MinimumSamples);
        return new CombatDeckDeltaReport(
            1,
            new CombatSimulationOptions().SemanticsVersion,
            DateTimeOffset.UtcNow.ToString("O"),
            plan.PortfolioId,
            plan.PortfolioHash,
            _hp.Calibration.CalibrationId,
            _hp.ContentHash,
            combatModelHash,
            candidate.StableKey,
            10,
            plan.Seed,
            false,
            enoughSamples ? "research-review" : "insufficient-supported-samples",
            horizonReports,
            unsupported,
            [.. plan.Warnings, "runtimeCandidate is hard-coded false in Phase 1.", "Primary dEV remains null while target weights and HP parameters are priors."]);
    }

    private CombatHorizonDeltaReport AggregateHorizon(
        int horizon,
        IReadOnlyList<CombatPairSampleResult> pairs,
        CombatSamplePlan plan)
    {
        List<CombatCellDeltaReport> cells = [];
        foreach (IGrouping<string, CombatSample> sampleGroup in plan.Samples.GroupBy(sample => sample.CellId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            CombatSample firstSample = sampleGroup.First();
            CombatPairSampleResult[] values = pairs.Where(pair => pair.Sample.CellId == sampleGroup.Key).ToArray();
            HpContinuationContext hpContext = _hp.Get(firstSample.HpContextId);
            double[] weights = values.Select(value => value.Sample.ImportanceWeight).ToArray();
            double[] deltas = values.Select(value => value.Delta).ToArray();
            (double mean, double cellLow, double cellHigh) = WeightedConfidenceInterval(deltas, weights);
            double supportedFraction = sampleGroup.Count() == 0 ? 0d : values.Length / (double)sampleGroup.Count();
            cells.Add(new CombatCellDeltaReport(
                sampleGroup.Key,
                firstSample.Act,
                firstSample.Tier,
                horizon,
                hpContext.LossBudget,
                sampleGroup.Sum(value => value.TargetProbability),
                sampleGroup.Sum(value => value.ProposalProbability),
                values.Length,
                CombatPortfolioSampler.ComputeEffectiveSampleSize(weights),
                supportedFraction,
                mean,
                cellLow,
                cellHigh,
                Risk(values.Select(value => value.Baseline.Metrics), hpContext.LossBudget),
                Risk(values.Select(value => value.Candidate.Metrics), hpContext.LossBudget),
                WeightedMetrics(values.Select(value => value.Baseline.Metrics).ToArray(), weights),
                WeightedMetrics(values.Select(value => value.Candidate.Metrics).ToArray(), weights),
                values.Length == 0 ? 0d : values.Count(value => value.Baseline.SolverStatus == CombatSolveStatus.Exact && value.Candidate.SolverStatus == CombatSolveStatus.Exact) / (double)values.Length,
                values.Length == 0 ? 0d : values.Count(value => value.Baseline.SolverStatus == CombatSolveStatus.SparseEstimate || value.Candidate.SolverStatus == CombatSolveStatus.SparseEstimate) / (double)values.Length,
                values.Length == 0 ? 0d : values.Count(value => value.Baseline.SolverStatus == CombatSolveStatus.ExactBudgetExceeded || value.Candidate.SolverStatus == CombatSolveStatus.ExactBudgetExceeded) / (double)values.Length));
        }

        double supportedMass = cells.Sum(cell => cell.TargetWeight * cell.SupportedFraction);
        double[] cellValues = cells.Select(cell => cell.DeltaEv).ToArray();
        double[] cellWeights = cells.Select(cell => cell.TargetWeight * cell.SupportedFraction).ToArray();
        (double research, double low, double high) = WeightedConfidenceInterval(cellValues, cellWeights);
        return new CombatHorizonDeltaReport(
            horizon,
            null,
            research,
            low,
            high,
            supportedMass,
            CombatPortfolioSampler.ComputeEffectiveSampleSize(pairs.Select(pair => pair.Sample.ImportanceWeight)),
            cells);
    }

    private static CombatRiskSummary Risk(IEnumerable<CombatPhysicalMetrics> metrics, int budget)
    {
        CombatPhysicalMetrics[] values = metrics.ToArray();
        if (values.Length == 0) return new CombatRiskSummary(0, 0, 0, 0, 0);
        double[] loss = values.Select(value => value.PlayerHpLost + value.ReferenceTailHpLoss).Order().ToArray();
        int p90Index = Math.Clamp((int)Math.Ceiling(loss.Length * 0.9) - 1, 0, loss.Length - 1);
        double p90 = loss[p90Index];
        double[] tail = loss.Where(value => value >= p90).ToArray();
        return new CombatRiskSummary(
            loss.Average(),
            p90,
            tail.Average(),
            loss.Count(value => value > budget) / (double)loss.Length,
            values.Average(value => value.DeathProbability));
    }

    private static CombatPhysicalMetrics WeightedMetrics(IReadOnlyList<CombatPhysicalMetrics> values, IReadOnlyList<double> weights)
    {
        double sum = weights.Sum();
        if (sum <= 0) return CombatPhysicalMetrics.Zero;
        CombatPhysicalMetrics result = CombatPhysicalMetrics.Zero;
        for (int index = 0; index < values.Count; index++) result += values[index].Scale(weights[index] / sum);
        return result;
    }

    private static (double Mean, double Low, double High) WeightedConfidenceInterval(
        IReadOnlyList<double> values,
        IReadOnlyList<double> weights)
    {
        if (values.Count == 0) return (0, 0, 0);
        double weightSum = weights.Sum();
        if (weightSum <= 0) return (0, 0, 0);
        double[] normalized = weights.Select(weight => weight / weightSum).ToArray();
        double mean = values.Select((value, index) => value * normalized[index]).Sum();
        if (values.Count == 1) return (mean, mean, mean);
        double variance = values.Select((value, index) => normalized[index] * normalized[index] * Math.Pow(value - mean, 2)).Sum();
        double correction = 1d - normalized.Sum(weight => weight * weight);
        double standardError = correction > 0 ? Math.Sqrt(variance / correction) : 0d;
        double half = 1.96d * standardError;
        return (mean, mean - half, mean + half);
    }
}
