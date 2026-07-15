using System.Buffers;
using System.Diagnostics;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class DeckMonteCarloSimulator
{
    private const double NextTurnExplicitResourceReferenceMultiplier = 0.75d;
    private const double SearchValueComparisonTolerance = 1e-9d;

    // Lowers (or restores) the current parallel worker thread to the priority requested by the
    // options. The in-game realtime service uses this so combat-time background simulation runs on
    // BelowNormal-priority pool threads that the OS always lets the game preempt. Guarded so it is a
    // cheap no-op once the thread already matches, and best-effort (ignores hosts that reject it).
    // Never affects the computed result - priority is purely an OS-scheduling hint.
    private static void ApplyWorkerPriority(DeckSimulationOptions options)
    {
        if (options.WorkerThreadPriority is System.Threading.ThreadPriority priority
            && System.Threading.Thread.CurrentThread.Priority != priority)
        {
            try
            {
                System.Threading.Thread.CurrentThread.Priority = priority;
            }
            catch
            {
                // Priority is a best-effort hint; ignore platforms/hosts that reject the change.
            }
        }
    }

    // Bounds nested free plays (auto-play chains / replays that themselves auto-play) so the play
    // path stays recursion-safe. A card at this depth resolves its own effects but triggers no
    // further nested auto-play.
    private const int MaxNestedPlayDepth = 3;

    // Default forward horizon (in turns) for the teacher route-value Q. The teacher forces a
    // candidate card, then rolls the game forward this many turns at the teacher beam width, so
    // engine/persistent-power payoff is realized in the label instead of relying on a setup prior.
    private const int DefaultTeacherForwardTurns = 4;

    // Default number of forward rollouts averaged per candidate for the teacher-Q label. >1 denoises
    // the label (variance drops ~1/sqrt(K)); see docs/modeling/search-policy-round1-results.md.
    private const int DefaultTeacherRollouts = 1;

    private static readonly ExplicitResourceReferenceValues ShortlineResourceReferenceValues = new(
        Draw: 5.1d,
        Energy: 8.8d,
        Star: 2.7d);

    private static readonly ExplicitResourceReferenceValues MidlineResourceReferenceValues = new(
        Draw: 5.2d,
        Energy: 10.0d,
        Star: 5.3d);

    private static readonly ExplicitResourceReferenceValues LonglineResourceReferenceValues = new(
        Draw: 5.1d,
        Energy: 11.2d,
        Star: 6.3d);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        IReadOnlyList<SimulationCard>,
        GeneratedLibraryContinuationStats> GeneratedLibraryContinuationCache = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        SimulationCard,
        StableCardSearchIdentity> SearchCardIdentityCache = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ulong>
        StableSearchStringHashCache = new(StringComparer.Ordinal);

    public IReadOnlyList<decimal> SimulateExpectedTurnValues(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        return SimulateExpectedTurnValuesCore(deck, options, collectStartingInstancePlays: false)
            .ExpectedTurnValues;
    }

    public ExpectedValueSampleBatch SimulateExpectedTotalSamples(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options,
        int startRun,
        int runCount)
    {
        if (startRun < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startRun));
        }

        if (runCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runCount));
        }

        (IReadOnlyList<SimulationCard> simulationDeck, IReadOnlyList<int> stableIds) =
            NormalizeStartingDeckWithStableIds(deck, options.StartingInstanceIds);
        DeckSimulationOptions sampleOptions = options with
        {
            Runs = runCount,
            CollectAttribution = false,
            CollectSearchPlayTrace = false,
            StartingInstanceIds = stableIds
        };
        Validate(simulationDeck, sampleOptions);

        int[] runSeeds = BuildRunSeeds(options.Seed, startRun, runCount);
        double[] samples = new double[runCount];

        void SimulateRun(int localRun)
        {
            int absoluteRun = startRun + localRun;
            int runSeed = runSeeds[localRun];
            FastRandom rng = new(runSeed);
            SimulationState state = SimulationState.Create(simulationDeck, rng, sampleOptions, runSeed);
            double totalValue = 0d;
            for (int turn = 1; turn <= sampleOptions.Turns; turn++)
            {
                TurnTrialSummary summary = PlayTurn(state, sampleOptions, rng, absoluteRun, turn);
                totalValue += summary.Value;
            }

            samples[localRun] = totalValue;
        }

        int degree = Math.Max(1, sampleOptions.RunDegreeOfParallelism);
        if (degree <= 1)
        {
            for (int run = 0; run < runCount; run++)
            {
                SimulateRun(run);
            }
        }
        else
        {
            Parallel.For(
                0,
                runCount,
                new ParallelOptions { MaxDegreeOfParallelism = degree },
                run =>
                {
                    ApplyWorkerPriority(sampleOptions);
                    SimulateRun(run);
                });
        }

        return new ExpectedValueSampleBatch(startRun, samples);
    }

    private static int[] BuildRunSeeds(int seed, int startRun, int runCount)
    {
        FastRandom seedRng = new(seed);
        int[] result = new int[runCount];
        for (int run = 0; run < startRun + runCount; run++)
        {
            int runSeed = seedRng.Next();
            if (run >= startRun)
            {
                result[run - startRun] = runSeed;
            }
        }

        return result;
    }

    public DeckInstanceTrackingReport SimulateExpectedTurnValuesAndStartingInstancePlays(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        return SimulateExpectedTurnValuesCore(deck, options, collectStartingInstancePlays: true);
    }

    private DeckInstanceTrackingReport SimulateExpectedTurnValuesCore(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options,
        bool collectStartingInstancePlays)
    {
        IReadOnlyList<SimulationCard> simulationDeck;
        IReadOnlyList<int> inputDeckIndices;
        if (collectStartingInstancePlays)
        {
            (simulationDeck, inputDeckIndices) = NormalizeStartingDeckWithInputIndices(deck);
        }
        else
        {
            simulationDeck = NormalizeStartingDeck(deck);
            inputDeckIndices = [];
        }
        Validate(simulationDeck, options);

        // Expected-value sampling never reads attribution; skip building credit events entirely.
        options = options with
        {
            CollectAttribution = false,
            CollectSearchPlayTrace = collectStartingInstancePlays,
            StartingInstanceIds = collectStartingInstancePlays ? [] : options.StartingInstanceIds
        };
        double[] turnValueSums = new double[options.Turns];
        int[,] instancePlayCounts = new int[
            options.Turns,
            collectStartingInstancePlays ? simulationDeck.Count : 0];
        FastRandom seedRng = new(options.Seed);
        int[] runSeeds = Enumerable.Range(0, options.Runs)
            .Select(_ => seedRng.Next())
            .ToArray();

        void AddTurn(
            int turnIndex,
            TurnTrialSummary summary,
            double[] localTurnValueSums,
            int[,] localInstancePlayCounts)
        {
            localTurnValueSums[turnIndex] += summary.Value;
            if (!collectStartingInstancePlays)
            {
                return;
            }

            foreach (PlayEvent played in summary.PlayedCards)
            {
                if (played.InstanceId >= 0 && played.InstanceId < simulationDeck.Count)
                {
                    localInstancePlayCounts[turnIndex, played.InstanceId]++;
                }
            }

        }

        int runDegreeOfParallelism = Math.Max(1, options.RunDegreeOfParallelism);
        if (runDegreeOfParallelism <= 1)
        {
            for (int run = 0; run < options.Runs; run++)
            {
                FastRandom rng = new(runSeeds[run]);
                SimulationState state = SimulationState.Create(simulationDeck, rng, options, runSeeds[run]);
                for (int turn = 1; turn <= options.Turns; turn++)
                {
                    TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                    AddTurn(
                        turn - 1,
                        summary,
                        turnValueSums,
                        instancePlayCounts);
                }
            }
        }
        else
        {
            object sumLock = new();
            Parallel.For(
                0,
                options.Runs,
                new ParallelOptions { MaxDegreeOfParallelism = runDegreeOfParallelism },
                () =>
                {
                    ApplyWorkerPriority(options);
                    return new ExpectedValueLocalSums(
                        options.Turns,
                        collectStartingInstancePlays ? simulationDeck.Count : 0);
                },
                (run, _, local) =>
                {
                    FastRandom rng = new(runSeeds[run]);
                    SimulationState state = SimulationState.Create(simulationDeck, rng, options, runSeeds[run]);
                    for (int turn = 1; turn <= options.Turns; turn++)
                    {
                        TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                        AddTurn(
                            turn - 1,
                            summary,
                            local.TurnValueSums,
                            local.InstancePlayCounts);
                    }

                    return local;
                },
                local =>
                {
                    lock (sumLock)
                    {
                        for (int turn = 0; turn < options.Turns; turn++)
                        {
                            turnValueSums[turn] += local.TurnValueSums[turn];
                            for (int instance = 0; instance < instancePlayCounts.GetLength(1); instance++)
                            {
                                instancePlayCounts[turn, instance] += local.InstancePlayCounts[turn, instance];
                            }
                        }
                    }
                });
        }

        decimal[] expectedTurnValues = turnValueSums
            .Select(sum => Round(sum / options.Runs))
            .ToArray();
        int[][] playCountsByTurn = collectStartingInstancePlays
            ? Enumerable.Range(0, options.Turns)
                .Select(turn => Enumerable.Range(0, simulationDeck.Count)
                    .Select(instance => instancePlayCounts[turn, instance])
                    .ToArray())
                .ToArray()
            : [];
        return new DeckInstanceTrackingReport(
            expectedTurnValues,
            playCountsByTurn,
            inputDeckIndices);
    }

    public TrackedCardSimulationReport SimulateTrackedCard(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options,
        string trackedModelId,
        bool collectCredits)
    {
        IReadOnlyList<SimulationCard> simulationDeck = NormalizeStartingDeck(deck);
        Validate(simulationDeck, options);

        IReadOnlyList<int> startingInstanceIds = options.StartingInstanceIds.Count == simulationDeck.Count
            ? options.StartingInstanceIds
            : Enumerable.Range(0, simulationDeck.Count).ToArray();
        HashSet<int> trackedStartingInstanceIds = simulationDeck
            .Select((card, index) => (card, instanceId: startingInstanceIds[index]))
            .Where(item => MatchesModelId(item.card, trackedModelId))
            .Select(item => item.instanceId)
            .ToHashSet();
        options = options with
        {
            CollectAttribution = collectCredits,
            CollectSearchPlayTrace = true,
            StartingInstanceIds = startingInstanceIds,
            TrackedDrawModelId = trackedModelId,
            TrackedStartingInstanceIds = trackedStartingInstanceIds
        };
        double[] turnValueSums = new double[options.Turns];
        int[] drawCounts = new int[options.Turns];
        int[] playCounts = new int[options.Turns];
        int[] directPlayCounts = new int[options.Turns];
        double[] directValueSums = new double[options.Turns];
        double[] forgeValueSums = new double[options.Turns];
        double[] powerValueSums = new double[options.Turns];
        double[] energyValueSums = new double[options.Turns];
        double[] starValueSums = new double[options.Turns];
        FastRandom seedRng = new(options.Seed);
        int[] runSeeds = Enumerable.Range(0, options.Runs)
            .Select(_ => seedRng.Next())
            .ToArray();

        void AddTurn(
            int turnIndex,
            TurnTrialSummary summary,
            double[] localTurnValueSums,
            int[] localDrawCounts,
            int[] localPlayCounts,
            int[] localDirectPlayCounts,
            double[] localDirectValueSums,
            double[] localForgeValueSums,
            double[] localPowerValueSums,
            double[] localEnergyValueSums,
            double[] localStarValueSums,
            int previousDrawCount,
            SimulationState state)
        {
            localTurnValueSums[turnIndex] += summary.Value;
            localDrawCounts[turnIndex] += state.TrackedDrawCount - previousDrawCount;
            foreach (PlayEvent played in summary.PlayedCards)
            {
                if (trackedStartingInstanceIds.Contains(played.InstanceId)
                    && MatchesModelId(played.Card, trackedModelId))
                {
                    localPlayCounts[turnIndex]++;
                }
            }

            if (!collectCredits)
            {
                return;
            }

            foreach (CardValueCreditEvent credit in summary.ValueCredits)
            {
                if (!MatchesReportedModelId(credit.ModelId, trackedModelId))
                {
                    continue;
                }

                if (credit.CountsAsDirectPlay)
                {
                    localDirectPlayCounts[turnIndex]++;
                }

                localDirectValueSums[turnIndex] += credit.DirectValue;
                localForgeValueSums[turnIndex] += credit.ForgeRealizedValue;
                localPowerValueSums[turnIndex] += credit.PowerRealizedValue;
                localEnergyValueSums[turnIndex] += credit.EnergyRealizedValue;
                localStarValueSums[turnIndex] += credit.StarRealizedValue;
            }
        }

        int runDegreeOfParallelism = options.SearchPolicyCollector is null
            ? Math.Max(1, options.RunDegreeOfParallelism)
            : 1;
        if (runDegreeOfParallelism <= 1)
        {
            for (int run = 0; run < options.Runs; run++)
            {
                FastRandom rng = new(runSeeds[run]);
                SimulationState state = SimulationState.Create(simulationDeck, rng, options, runSeeds[run]);
                int previousDrawCount = 0;
                for (int turn = 1; turn <= options.Turns; turn++)
                {
                    TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                    AddTurn(
                        turn - 1,
                        summary,
                        turnValueSums,
                        drawCounts,
                        playCounts,
                        directPlayCounts,
                        directValueSums,
                        forgeValueSums,
                        powerValueSums,
                        energyValueSums,
                        starValueSums,
                        previousDrawCount,
                        state);
                    previousDrawCount = state.TrackedDrawCount;
                }
            }
        }
        else
        {
            object sumLock = new();
            Parallel.For(
                0,
                options.Runs,
                new ParallelOptions { MaxDegreeOfParallelism = runDegreeOfParallelism },
                () => { ApplyWorkerPriority(options); return new TrackedCardLocalSums(options.Turns); },
                (run, _, local) =>
                {
                    FastRandom rng = new(runSeeds[run]);
                    SimulationState state = SimulationState.Create(simulationDeck, rng, options, runSeeds[run]);
                    int previousDrawCount = 0;
                    for (int turn = 1; turn <= options.Turns; turn++)
                    {
                        TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                        AddTurn(
                            turn - 1,
                            summary,
                            local.TurnValueSums,
                            local.DrawCounts,
                            local.PlayCounts,
                            local.DirectPlayCounts,
                            local.DirectValueSums,
                            local.ForgeValueSums,
                            local.PowerValueSums,
                            local.EnergyValueSums,
                            local.StarValueSums,
                            previousDrawCount,
                            state);
                        previousDrawCount = state.TrackedDrawCount;
                    }

                    return local;
                },
                local =>
                {
                    lock (sumLock)
                    {
                        for (int turn = 0; turn < options.Turns; turn++)
                        {
                            turnValueSums[turn] += local.TurnValueSums[turn];
                            drawCounts[turn] += local.DrawCounts[turn];
                            playCounts[turn] += local.PlayCounts[turn];
                            directPlayCounts[turn] += local.DirectPlayCounts[turn];
                            directValueSums[turn] += local.DirectValueSums[turn];
                            forgeValueSums[turn] += local.ForgeValueSums[turn];
                            powerValueSums[turn] += local.PowerValueSums[turn];
                            energyValueSums[turn] += local.EnergyValueSums[turn];
                            starValueSums[turn] += local.StarValueSums[turn];
                        }
                    }
                });
        }

        return new TrackedCardSimulationReport(
            Enumerable.Range(0, options.Turns)
                .Select(index =>
                {
                    decimal directValue = Round(directValueSums[index]);
                    decimal forgeValue = Round(forgeValueSums[index]);
                    decimal powerValue = Round(powerValueSums[index]);
                    decimal energyValue = Round(energyValueSums[index]);
                    decimal starValue = Round(starValueSums[index]);
                    return new TrackedCardTurnSummary(
                        index + 1,
                        Round(turnValueSums[index] / options.Runs),
                        drawCounts[index],
                        playCounts[index],
                        directPlayCounts[index],
                        directValue,
                        forgeValue,
                        powerValue,
                        energyValue,
                        starValue,
                        directValue + forgeValue + powerValue + energyValue + starValue);
                })
                .ToArray());
    }

    public DeckSimulationReport Simulate(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        IReadOnlyList<SimulationCard> simulationDeck = NormalizeStartingDeck(deck);
        int ignoredStartingSovereignBlades = deck.Count - simulationDeck.Count;
        Validate(simulationDeck, options);

        string? trackedCreditModelId = options.CollectAttribution ? options.TrackedCreditModelId : null;
        double[,] turnValues = new double[options.Runs, options.Turns];
        List<TurnTrialSummary>[] turnSamples = Enumerable.Range(0, options.Turns)
            .Select(_ => new List<TurnTrialSummary>(options.Runs))
            .ToArray();
        Dictionary<string, CardPlayAccumulator> cardPlayAccumulators = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int Turn, string ModelId), CardPlayTurnAccumulator> cardPlayByTurnAccumulators = [];
        Dictionary<string, CardValueCreditAccumulator> cardValueCreditAccumulators = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int Turn, string ModelId), CardValueCreditTurnAccumulator> cardValueCreditByTurnAccumulators = [];
        Dictionary<(string SourceModelId, string CandidateModelId, string FromPile, string ToPile), CardMoveChoiceAccumulator>
            cardMoveChoiceAccumulators = [];
        Dictionary<(string SourceModelId, string CandidateModelId, string ReplacementModelId), CardTransformChoiceAccumulator>
            cardTransformChoiceAccumulators = [];
        FastRandom seedRng = new(options.Seed);
        int[] runSeeds = new int[options.Runs];
        for (int run = 0; run < options.Runs; run++)
        {
            runSeeds[run] = seedRng.Next();
        }

        // Search-policy collection mutates shared collector state, so keep those runs serial.
        int runDegreeOfParallelism = options.SearchPolicyCollector is null
            ? Math.Max(1, options.RunDegreeOfParallelism)
            : 1;
        TurnTrialSummary[][] perRun = new TurnTrialSummary[options.Runs][];
        void SimulateRun(int run)
        {
            FastRandom rng = new(runSeeds[run]);
            SimulationState state = SimulationState.Create(simulationDeck, rng, options, runSeeds[run]);
            TurnTrialSummary[] turns = new TurnTrialSummary[options.Turns];
            for (int turn = 1; turn <= options.Turns; turn++)
            {
                turns[turn - 1] = PlayTurn(state, options, rng, run, turn);
            }

            perRun[run] = turns;
        }

        if (runDegreeOfParallelism <= 1)
        {
            for (int run = 0; run < options.Runs; run++)
            {
                SimulateRun(run);
            }
        }
        else
        {
            Parallel.For(
                0,
                options.Runs,
                new ParallelOptions { MaxDegreeOfParallelism = runDegreeOfParallelism },
                run =>
                {
                    ApplyWorkerPriority(options);
                    SimulateRun(run);
                });
        }

        // Accumulation is sequential and order-independent; the parallel phase above only
        // produces per-run samples, which makes results identical to the serial path.
        for (int run = 0; run < options.Runs; run++)
        {
            for (int turn = 1; turn <= options.Turns; turn++)
            {
                TurnTrialSummary summary = perRun[run][turn - 1];
                turnValues[run, turn - 1] = (double)summary.Value;
                turnSamples[turn - 1].Add(summary);

                for (int playIndex = 0; playIndex < summary.PlayedCards.Count; playIndex++)
                {
                    PlayEvent played = summary.PlayedCards[playIndex];
                    int positionInTurn = playIndex + 1;
                    SimulationCard card = played.Card;
                    string reportModelId = card.ReportModelId;
                    string reportTypeName = card.ReportTypeName;
                    if (!cardPlayAccumulators.TryGetValue(reportModelId, out CardPlayAccumulator? accumulator))
                    {
                        accumulator = new CardPlayAccumulator(reportModelId, reportTypeName);
                        cardPlayAccumulators.Add(reportModelId, accumulator);
                    }

                    accumulator.PlayCount++;
                    accumulator.TotalValue += played.Value;
                    accumulator.TotalPositionInTurn += positionInTurn;
                    accumulator.MinimumPositionInTurn = Math.Min(accumulator.MinimumPositionInTurn, positionInTurn);
                    accumulator.MaximumPositionInTurn = Math.Max(accumulator.MaximumPositionInTurn, positionInTurn);

                    (int Turn, string ModelId) turnKey = (turn, reportModelId);
                    if (!cardPlayByTurnAccumulators.TryGetValue(turnKey, out CardPlayTurnAccumulator? turnAccumulator))
                    {
                        turnAccumulator = new CardPlayTurnAccumulator(turn, reportModelId, reportTypeName);
                        cardPlayByTurnAccumulators.Add(turnKey, turnAccumulator);
                    }

                    turnAccumulator.PlayCount++;
                    turnAccumulator.TotalValue += played.Value;

                    foreach (CardMoveChoiceEvent choice in played.MoveChoices)
                    {
                        var key = (choice.SourceModelId, choice.CandidateModelId, choice.FromPile, choice.ToPile);
                        if (!cardMoveChoiceAccumulators.TryGetValue(key, out CardMoveChoiceAccumulator? moveAccumulator))
                        {
                            moveAccumulator = new CardMoveChoiceAccumulator(
                                choice.SourceModelId,
                                choice.SourceTypeName,
                                choice.CandidateModelId,
                                choice.CandidateTypeName,
                                choice.FromPile,
                                choice.ToPile);
                            cardMoveChoiceAccumulators.Add(key, moveAccumulator);
                        }

                        moveAccumulator.CandidateSeenCount++;
                        if (choice.WasMoved)
                        {
                            moveAccumulator.MoveCount++;
                            moveAccumulator.MovedCandidateScoreSum += choice.CandidateScore;
                        }
                        else
                        {
                            moveAccumulator.RetainedCandidateScoreSum += choice.CandidateScore;
                        }

                        moveAccumulator.CandidateScoreSum += choice.CandidateScore;
                        moveAccumulator.MinimumCandidateScore = Math.Min(
                            moveAccumulator.MinimumCandidateScore,
                            choice.CandidateScore);
                        moveAccumulator.MaximumCandidateScore = Math.Max(
                            moveAccumulator.MaximumCandidateScore,
                            choice.CandidateScore);
                    }

                    foreach (CardTransformChoiceEvent choice in played.TransformChoices)
                    {
                        var key = (choice.SourceModelId, choice.CandidateModelId, choice.ReplacementModelId);
                        if (!cardTransformChoiceAccumulators.TryGetValue(key, out CardTransformChoiceAccumulator? transformAccumulator))
                        {
                            transformAccumulator = new CardTransformChoiceAccumulator(
                                choice.SourceModelId,
                                choice.SourceTypeName,
                                choice.CandidateModelId,
                                choice.CandidateTypeName,
                                choice.ReplacementModelId,
                                choice.ReplacementTypeName);
                            cardTransformChoiceAccumulators.Add(key, transformAccumulator);
                        }

                        transformAccumulator.CandidateSeenCount++;
                        if (choice.WasTransformed)
                        {
                            transformAccumulator.TransformCount++;
                            transformAccumulator.TransformedCandidateScoreSum += choice.CandidateScore;
                        }
                        else
                        {
                            transformAccumulator.RetainedCandidateScoreSum += choice.CandidateScore;
                        }

                        transformAccumulator.CandidateScoreSum += choice.CandidateScore;
                        transformAccumulator.MinimumCandidateScore = Math.Min(
                            transformAccumulator.MinimumCandidateScore,
                            choice.CandidateScore);
                        transformAccumulator.MaximumCandidateScore = Math.Max(
                            transformAccumulator.MaximumCandidateScore,
                            choice.CandidateScore);
                        transformAccumulator.ReplacementScoreSum += choice.ReplacementScore;
                    }
                }

                foreach (CardValueCreditEvent credit in summary.ValueCredits)
                {
                    if (trackedCreditModelId is not null
                        && !MatchesReportedModelId(credit.ModelId, trackedCreditModelId))
                    {
                        continue;
                    }

                    AddCredit(cardValueCreditAccumulators, credit);
                    AddTurnCredit(cardValueCreditByTurnAccumulators, turn, credit);
                }
            }
        }

        IReadOnlyList<TurnSimulationSummary> turnSummaries = turnSamples
            .Select((samples, index) => BuildTurnSummary(index + 1, samples, (double)options.PmfBucketSize))
            .ToArray();
        IReadOnlyList<TurnCovariance> covariances = BuildCovariances(turnValues, turnSummaries);
        IReadOnlyList<CardPlaySummary> playedCards = cardPlayAccumulators.Values
            .OrderByDescending(item => item.PlayCount)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(item => new CardPlaySummary(
                item.ModelId,
                item.TypeName,
                item.PlayCount,
                Round((double)item.PlayCount / options.Runs),
                item.PlayCount == 0 ? 0m : Round(item.TotalValue / item.PlayCount),
                item.PlayCount == 0 ? 0m : Round((double)item.TotalPositionInTurn / item.PlayCount),
                item.PlayCount == 0 ? 0 : item.MinimumPositionInTurn,
                item.MaximumPositionInTurn))
            .ToArray();
        IReadOnlyList<CardPlayTurnSummary> playedCardsByTurn = cardPlayByTurnAccumulators.Values
            .OrderBy(item => item.Turn)
            .ThenByDescending(item => item.PlayCount)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(item => new CardPlayTurnSummary(
                item.Turn,
                item.ModelId,
                item.TypeName,
                item.PlayCount,
                Round((double)item.PlayCount / options.Runs),
                item.PlayCount == 0 ? 0m : Round(item.TotalValue / item.PlayCount)))
            .ToArray();
        IReadOnlyList<CardValueCreditSummary> cardValueCredits = cardValueCreditAccumulators.Values
            .OrderByDescending(item => item.TotalCreditedValue)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(item => new CardValueCreditSummary(
                item.ModelId,
                item.TypeName,
                item.DirectPlayCount,
                Round(item.DirectValue),
                Round(item.ForgeRealizedValue),
                Round(item.PowerRealizedValue),
                Round(item.EnergyRealizedValue),
                Round(item.StarRealizedValue),
                Round(item.TotalCreditedValue),
                item.DirectPlayCount == 0 ? 0m : Round(item.DirectValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.ForgeRealizedValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.PowerRealizedValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.EnergyRealizedValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.StarRealizedValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.TotalCreditedValue / item.DirectPlayCount)))
            .ToArray();
        IReadOnlyList<CardValueCreditTurnSummary> cardValueCreditsByTurn = cardValueCreditByTurnAccumulators.Values
            .OrderBy(item => item.Turn)
            .ThenByDescending(item => item.TotalCreditedValue)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(item => new CardValueCreditTurnSummary(
                item.Turn,
                item.ModelId,
                item.TypeName,
                item.DirectPlayCount,
                Round(item.DirectValue),
                Round(item.ForgeRealizedValue),
                Round(item.PowerRealizedValue),
                Round(item.EnergyRealizedValue),
                Round(item.StarRealizedValue),
                Round(item.TotalCreditedValue),
                item.DirectPlayCount == 0 ? 0m : Round(item.TotalCreditedValue / item.DirectPlayCount)))
            .ToArray();
        IReadOnlyList<CardMoveChoiceSummary> cardMoveChoices = cardMoveChoiceAccumulators.Values
            .OrderByDescending(item => item.MoveCount)
            .ThenByDescending(item => item.CandidateSeenCount)
            .ThenBy(item => item.CandidateTypeName, StringComparer.Ordinal)
            .Select(item => new CardMoveChoiceSummary(
                item.SourceModelId,
                item.SourceTypeName,
                item.CandidateModelId,
                item.CandidateTypeName,
                item.FromPile,
                item.ToPile,
                item.CandidateSeenCount,
                item.MoveCount,
                Round((double)item.MoveCount / item.CandidateSeenCount),
                Round(item.CandidateScoreSum / item.CandidateSeenCount),
                item.MoveCount == 0
                    ? null
                    : Round(item.MovedCandidateScoreSum / item.MoveCount),
                item.CandidateSeenCount == item.MoveCount
                    ? null
                    : Round(item.RetainedCandidateScoreSum / (item.CandidateSeenCount - item.MoveCount)),
                Round(item.MinimumCandidateScore),
                Round(item.MaximumCandidateScore)))
            .ToArray();
        IReadOnlyList<CardTransformChoiceSummary> cardTransformChoices = cardTransformChoiceAccumulators.Values
            .OrderByDescending(item => item.TransformCount)
            .ThenByDescending(item => item.CandidateSeenCount)
            .ThenBy(item => item.CandidateTypeName, StringComparer.Ordinal)
            .Select(item => new CardTransformChoiceSummary(
                item.SourceModelId,
                item.SourceTypeName,
                item.CandidateModelId,
                item.CandidateTypeName,
                item.ReplacementModelId,
                item.ReplacementTypeName,
                item.CandidateSeenCount,
                item.TransformCount,
                Round((double)item.TransformCount / item.CandidateSeenCount),
                Round(item.CandidateScoreSum / item.CandidateSeenCount),
                item.TransformCount == 0
                    ? null
                    : Round(item.TransformedCandidateScoreSum / item.TransformCount),
                item.CandidateSeenCount == item.TransformCount
                    ? null
                    : Round(item.RetainedCandidateScoreSum / (item.CandidateSeenCount - item.TransformCount)),
                Round(item.MinimumCandidateScore),
                Round(item.MaximumCandidateScore),
                Round(item.ReplacementScoreSum / item.CandidateSeenCount)))
            .ToArray();
        decimal totalExpectedValue = Round((double)turnSummaries.Sum(turn => turn.ExpectedValue));
        decimal totalVariance = RoundTotalVariance(turnSummaries, covariances);

        return new DeckSimulationReport(
            simulationDeck.Count,
            simulationDeck.Count(card => card.IsPlayable),
            options,
            totalExpectedValue,
            totalVariance,
            turnSummaries,
            covariances,
            playedCards,
            playedCardsByTurn,
            cardValueCredits,
            cardValueCreditsByTurn,
            cardMoveChoices,
            cardTransformChoices,
            [],
            BuildWarnings(simulationDeck, ignoredStartingSovereignBlades),
            "sampled-lookahead Monte Carlo deck simulator v1");
    }

    private static void AddCredit(
        Dictionary<string, CardValueCreditAccumulator> accumulators,
        CardValueCreditEvent credit)
    {
        if (!accumulators.TryGetValue(credit.ModelId, out CardValueCreditAccumulator? accumulator))
        {
            accumulator = new CardValueCreditAccumulator(credit.ModelId, credit.TypeName);
            accumulators.Add(credit.ModelId, accumulator);
        }

        if (credit.CountsAsDirectPlay)
        {
            accumulator.DirectPlayCount++;
        }

        accumulator.DirectValue += credit.DirectValue;
        accumulator.ForgeRealizedValue += credit.ForgeRealizedValue;
        accumulator.PowerRealizedValue += credit.PowerRealizedValue;
        accumulator.EnergyRealizedValue += credit.EnergyRealizedValue;
        accumulator.StarRealizedValue += credit.StarRealizedValue;
    }

    private static void AddTurnCredit(
        Dictionary<(int Turn, string ModelId), CardValueCreditTurnAccumulator> accumulators,
        int turn,
        CardValueCreditEvent credit)
    {
        (int Turn, string ModelId) key = (turn, credit.ModelId);
        if (!accumulators.TryGetValue(key, out CardValueCreditTurnAccumulator? accumulator))
        {
            accumulator = new CardValueCreditTurnAccumulator(turn, credit.ModelId, credit.TypeName);
            accumulators.Add(key, accumulator);
        }

        if (credit.CountsAsDirectPlay)
        {
            accumulator.DirectPlayCount++;
        }

        accumulator.DirectValue += credit.DirectValue;
        accumulator.ForgeRealizedValue += credit.ForgeRealizedValue;
        accumulator.PowerRealizedValue += credit.PowerRealizedValue;
        accumulator.EnergyRealizedValue += credit.EnergyRealizedValue;
        accumulator.StarRealizedValue += credit.StarRealizedValue;
    }

    private static IReadOnlyList<SimulationCard> NormalizeStartingDeck(IReadOnlyList<SimulationCard> deck)
    {
        return deck
            .Where(card => !IsSovereignBlade(card))
            .ToArray();
    }

    private static bool MatchesModelId(SimulationCard card, string modelId)
    {
        return string.Equals(card.ModelId, modelId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.ReportModelId, modelId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesReportedModelId(string reportedModelId, string requestedModelId)
    {
        return string.Equals(reportedModelId, requestedModelId, StringComparison.OrdinalIgnoreCase)
            || reportedModelId.StartsWith($"{requestedModelId}@", StringComparison.OrdinalIgnoreCase);
    }

    private static string SourceModelId(SimulationCard card)
    {
        return card.ReportModelId;
    }

    private static string SourceTypeName(SimulationCard card)
    {
        return card.ReportTypeName;
    }

    private static (IReadOnlyList<SimulationCard> Deck, IReadOnlyList<int> InputDeckIndices)
        NormalizeStartingDeckWithInputIndices(IReadOnlyList<SimulationCard> deck)
    {
        List<SimulationCard> normalized = new(deck.Count);
        List<int> inputDeckIndices = new(deck.Count);
        for (int index = 0; index < deck.Count; index++)
        {
            if (IsSovereignBlade(deck[index]))
            {
                continue;
            }

            normalized.Add(deck[index]);
            inputDeckIndices.Add(index);
        }

        return (normalized, inputDeckIndices);
    }

    private static (IReadOnlyList<SimulationCard> Deck, IReadOnlyList<int> StableIds)
        NormalizeStartingDeckWithStableIds(
            IReadOnlyList<SimulationCard> deck,
            IReadOnlyList<int> requestedStableIds)
    {
        bool hasStableIds = requestedStableIds.Count > 0;
        if (hasStableIds && requestedStableIds.Count != deck.Count)
        {
            throw new ArgumentException("Starting instance ids must match the input deck count.", nameof(requestedStableIds));
        }

        List<SimulationCard> normalized = new(deck.Count);
        List<int> stableIds = new(deck.Count);
        for (int index = 0; index < deck.Count; index++)
        {
            if (IsSovereignBlade(deck[index]))
            {
                continue;
            }

            normalized.Add(deck[index]);
            stableIds.Add(hasStableIds ? requestedStableIds[index] : index);
        }

        if (stableIds.Count != stableIds.Distinct().Count())
        {
            throw new ArgumentException("Starting instance ids must be distinct.", nameof(requestedStableIds));
        }

        return (normalized, stableIds);
    }

    private static TurnTrialSummary PlayTurn(
        SimulationState state,
        DeckSimulationOptions options,
        FastRandom rng,
        int run,
        int turn)
    {
        bool ownsSlowTailProfile = options.ActiveSearchTurnProfile is null;
        SearchTurnProfile? slowTailProfile = options.ActiveSearchTurnProfile
            ?? options.SlowTailProfiler?.StartTurn(run, turn);
        options = options.ActiveSearchWorkBudget is null
            ? options with
            {
                ActiveSearchWorkBudget = new SearchWorkBudget(options.MaxSearchNodesPerTurn),
                ActiveSearchTurnProfile = slowTailProfile
            }
            : options with { ActiveSearchTurnProfile = slowTailProfile };
        FiniteHorizonContext horizon = new(options.Turns, turn);
        bool collect = options.CollectAttribution;
        state.TurnEnded = false;
        state.CardsPlayedThisTurn = 0;
        state.AttacksPlayedThisTurn = 0;
        state.SkillsPlayedThisTurn = 0;
        int queuedEnergy = state.NextTurnEnergy;
        int queuedStars = state.NextTurnStars;
        int queuedDraw = state.NextTurnDraw;
        state.CurrentTurnEnergySources.Clear();
        if (state.TrackAttributionSources)
        {
            state.CurrentTurnEnergySources.AddRange(state.NextTurnEnergySources);
        }

        state.NextTurnEnergySources.Clear();
        IReadOnlyList<ResourceSourceCredit> queuedStarSources = state.TrackAttributionSources
            ? state.NextTurnStarSources.ToArray()
            : [];
        state.NextTurnStarSources.Clear();
        double delayedBlockValue = state.NextTurnBlockDecisionValue;
        IReadOnlyList<CardValueCreditEvent> delayedBlockCredits = collect
            ? DelayedDirectCredits(state.NextTurnBlockCredits)
            : [];
        state.NextTurnEnergy = 0;
        state.NextTurnStars = 0;
        state.NextTurnDraw = 0;
        state.NextTurnBlock = 0;
        state.NextTurnBlockDecisionValue = 0d;
        state.NextTurnBlockCredits.Clear();
        state.Energy = options.BaseEnergy + queuedEnergy;
        if (options.StarsPersistBetweenTurns)
        {
            state.Stars += queuedStars;
        }
        else
        {
            state.BaseStarsRemaining = options.BaseStars;
            state.StarSources.Clear();
            state.Stars = options.BaseStars + queuedStars;
        }

        if (state.TrackAttributionSources)
        {
            state.StarSources.AddRange(queuedStarSources);
        }

        // Stars gained AT THE START of this turn count toward "stars gained this turn" for
        // conditional-hit scaling (Radiate). In persist mode the Regent's combat-start star
        // gain lands on turn 1 only (so a turn-1 Radiate naturally sees the base 3), and later
        // turns count only queued next-turn stars. In non-persist mode the base stars are
        // (re)gained every turn.
        state.StarsGainedThisTurn = queuedStars
            + (options.StarsPersistBetweenTurns
                ? (turn == 1 ? options.BaseStars : 0)
                : options.BaseStars);
        ExpireEnemyVulnerable(state);
        IReadOnlyList<PowerResolution> turnStartResolutions = queuedStars > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, queuedStars))
            : [];
        double turnStartValue = turnStartResolutions.Sum(resolution => resolution.Value)
            + delayedBlockValue;
        IReadOnlyList<CardValueCreditEvent> turnStartCredits = collect
            ?
            [
                .. PowerCredits(turnStartResolutions),
                .. StarTriggerCredits(queuedStarSources, turnStartResolutions.Sum(resolution => resolution.Value)),
                .. delayedBlockCredits
            ]
            : [];
        PowerEventResult turnStartPowerResult = ResolveTurnStartPowers(state);

        PowerEventResult beforeDrawResult = ResolveBeforeHandDrawPowers(state, options, rng);
        FreePlayResult imbuedAutoPlayResult = turn == 1
            ? ResolveImbuedAutoPlays(state, rng, options)
            : FreePlayResult.Empty;
        int drawCount = options.HandSize + queuedDraw + HandDrawBonus(state);
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        PowerEventResult playerTurnStartResult = ResolveAfterPlayerTurnStartPowers(state, options, rng);
        ArmGuaranteedSearchAdmission(state.Hand);
        SearchResult result = Search(
            state.Clone(),
            options,
            horizon,
            run,
            turn,
            resolvedPlays: 0,
            fullBranchDecisions: 0,
            rng.Next());
        state.CopyFrom(result.State);
        IReadOnlyList<PlayEvent> playedCards = MaterializePlayTrace(result.PlayTrace);

        double unplayedIntrinsicValue = state.Hand
            .Where(card => card.Card.IsPlayable && card.Card.IntrinsicValue > 0d)
            .Sum(card => card.Card.IntrinsicValue);
        int energyWasted = Math.Max(0, state.Energy);
        int starsWasted = Math.Max(0, state.Stars);
        PowerEventResult turnEndPowerResult = ResolveTurnEndPowers(state);
        FinishTurn(state);
        state.LastTurnCardsPlayed = result.CardsPlayed;
        IReadOnlyList<CardValueCreditEvent> valueCredits;
        if (collect)
        {
            double resourceAttributionValue = turnStartResolutions.Sum(resolution => resolution.Value)
                + turnStartPowerResult.Value
                + beforeDrawResult.Value
                + imbuedAutoPlayResult.Value
                + drawResult.Value
                + playerTurnStartResult.Value
                + result.Value
                + turnEndPowerResult.Value;
            IReadOnlyList<CardValueCreditEvent> energyCredits = EnergyCredits(
                state.CurrentTurnEnergySources,
                resourceAttributionValue,
                options.BaseEnergy,
                result.EnergySpent);
            valueCredits =
            [
                .. turnStartCredits,
                .. PowerCredits(turnStartPowerResult.PowerResolutions),
                .. turnStartPowerResult.ValueCredits,
                .. PowerCredits(beforeDrawResult.PowerResolutions),
                .. beforeDrawResult.ValueCredits,
                .. imbuedAutoPlayResult.Credits,
                .. PowerCredits(drawResult.PowerResolutions),
                .. drawResult.ValueCredits,
                .. PowerCredits(playerTurnStartResult.PowerResolutions),
                .. playerTurnStartResult.ValueCredits,
                .. playedCards.SelectMany(card => card.ValueCredits),
                .. PowerCredits(turnEndPowerResult.PowerResolutions),
                .. turnEndPowerResult.ValueCredits,
                .. energyCredits
            ];
        }
        else
        {
            valueCredits = [];
        }

        state.CurrentTurnEnergySources.Clear();

        double totalTurnValue = turnStartValue
            + turnStartPowerResult.Value
            + beforeDrawResult.Value
            + imbuedAutoPlayResult.Value
            + drawResult.Value
            + playerTurnStartResult.Value
            + result.Value
            + turnEndPowerResult.Value;
        int totalCardsPlayed = result.CardsPlayed + imbuedAutoPlayResult.CardsPlayed;
        TurnTrialSummary summary = new(
            turn,
            totalTurnValue,
            drawResult.CardsDrawn + result.CardsDrawn,
            totalCardsPlayed,
            result.EnergySpent,
            result.EnergyGained,
            energyWasted,
            result.StarSpent,
            result.StarGained,
            starsWasted,
            unplayedIntrinsicValue,
            playedCards,
            valueCredits);
        if (ownsSlowTailProfile)
        {
            slowTailProfile?.Complete(totalTurnValue, totalCardsPlayed);
        }

        return summary;
    }

    private static SearchResult Search(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        int run,
        int turn,
        int resolvedPlays,
        int fullBranchDecisions,
        int seed,
        bool useFiniteHorizonLeafValue = true,
        SearchSession? session = null)
    {
        session ??= new SearchSession(options);
        SearchPrefix deterministicPrefix = new();
        int deterministicChain = 0;
        bool workBudgetFallback = false;

        // A learned line evaluator (options.StateValue) supplies the forward value V(s)
        // only at genuine turn-end leaves; a full line is then valued as
        // (realized value + V(turn-end leaf)). V(s) is an optimal-continuation value on
        // a much larger scale (~200) than a single play's realized delta (~6-30), so a
        // mid-turn "stop here" option must NOT be valued by V - otherwise V's error
        // swamps the play delta and the search stops immediately. Under a learned
        // evaluator, an internal node with playable cards seeds best at -inf so a
        // complete line always wins (matches setup mode's play-propensity, where a
        // positive per-play decisionValue already beats the 0 seed).
        while (true)
        {
            workBudgetFallback |= session.EnterNode(options.SearchBranchDiagnostics);
            if (state.TurnEnded || resolvedPlays >= options.MaxCardsPlayedPerTurn)
            {
                options.SearchBranchDiagnostics?.RecordDeterministicChain(deterministicChain);
                return deterministicPrefix.Apply(CreateLeafResult(
                    state,
                    options,
                    horizon,
                    useFiniteHorizonLeafValue));
            }

            bool useTranspositions = session.UsesTranspositions(options);
            SearchStateFingerprint fingerprint = options.EnableLoopDetection || useTranspositions
                ? ComputeSearchStateFingerprint(state, includeExactHash: useTranspositions)
                : default;
            if (options.EnableLoopDetection)
            {
                ulong signature = fingerprint.LoopHash;
                if (session.TryFindLoop(
                        resolvedPlays,
                        signature,
                        state.Energy,
                        state.Stars,
                        out int priorEnergy,
                        out int priorStars))
                {
                    bool positiveResourceLoop = state.Energy > priorEnergy || state.Stars > priorStars;
                    options.SearchBranchDiagnostics?.RecordLoop(positiveResourceLoop);
                    options.ActiveSearchTurnProfile?.RecordLoop(positiveResourceLoop);
                    if (!positiveResourceLoop
                        && state.Energy <= priorEnergy
                        && state.Stars <= priorStars)
                    {
                        options.SearchBranchDiagnostics?.RecordPrunedLoop();
                        options.ActiveSearchTurnProfile?.RecordPrunedLoop();
                        options.SearchBranchDiagnostics?.RecordDeterministicChain(deterministicChain);
                        return deterministicPrefix.Apply(CreateLeafResult(
                            state,
                            options,
                            horizon,
                            useFiniteHorizonLeafValue));
                    }
                }

                session.RecordLoopState(resolvedPlays, signature, state.Energy, state.Stars);
            }

            CardCandidateSet legalPlayableCards = SelectPlayableCards(
                state,
                options,
                turn,
                session.PlayableBuffer(resolvedPlays, state.Hand.Count));
            bool allowDeterministicPlay = deterministicChain < options.MaxDeterministicPlayChain;
            ForcedPlayPolicyResult policy = ApplyForcedPlayPolicy(
                state,
                horizon,
                legalPlayableCards,
                session.PolicyBuffer(resolvedPlays, legalPlayableCards.Count),
                allowDeterministicPlay);
            legalPlayableCards = policy.OrdinaryCandidates;
            if (policy.ForcedCard is not { } forcedCard)
            {
                options.SearchBranchDiagnostics?.RecordDeterministicChain(deterministicChain);
                return deterministicPrefix.Apply(SearchOrdinaryCandidates(
                    state,
                    options,
                    horizon,
                    run,
                    turn,
                    resolvedPlays,
                    fullBranchDecisions,
                    seed,
                    useFiniteHorizonLeafValue,
                    session,
                    workBudgetFallback,
                    legalPlayableCards,
                    policy,
                    fingerprint));
            }

            options.SearchBranchDiagnostics?.RecordForcedPlay();
            options.ActiveSearchTurnProfile?.RecordForcedCard(forcedCard.Card.TypeName);
            FastRandom branchRng = new(DeriveSeed(seed, resolvedPlays, forcedCard.InstanceId));
            PlayOutcome play = PlayCard(
                state,
                forcedCard,
                branchRng,
                options,
                horizon,
                run,
                turn,
                resolvedPlays,
                seed);
            deterministicPrefix.Append(play, options.SearchBranchDiagnostics);
            resolvedPlays++;
            deterministicChain++;
            seed = branchRng.Next();
        }
    }

    private static SearchResult SearchOrdinaryCandidates(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        int run,
        int turn,
        int resolvedPlays,
        int fullBranchDecisions,
        int seed,
        bool useFiniteHorizonLeafValue,
        SearchSession session,
        bool workBudgetFallback,
        CardCandidateSet legalPlayableCards,
        ForcedPlayPolicyResult policy,
        SearchStateFingerprint fingerprint)
    {
        if (options.SearchPolicyCollector is { } collector
            && collector.CanCollect
            && legalPlayableCards.Count > 1)
        {
            SearchPolicyDecisionGroup? group = BuildSearchPolicyDecisionGroup(
                state,
                options,
                legalPlayableCards,
                collector,
                run,
                turn,
                resolvedPlays,
                fullBranchDecisions,
                seed);
            if (group is not null)
            {
                collector.TryAdd(group);
            }
        }

        SearchCandidateSet playableCards = SelectTopPlayableCards(
            state,
            options,
            legalPlayableCards,
            workBudgetFallback ? options.MaxFullyBranchedCardsPlayedPerTurn : fullBranchDecisions,
            horizon,
            policy.RequiredOrdinaryCandidate,
            session.SearchCandidateBuffer(resolvedPlays));

        SearchResult best = CreateOrdinaryStopResult(
            state,
            options,
            horizon,
            useFiniteHorizonLeafValue,
            playableCards.Count,
            policy.MustPlay);
        bool consumesChoiceDepth = playableCards.Count > 1;
        if (playableCards.Count == 1
            && (workBudgetFallback
                || fullBranchDecisions >= options.MaxFullyBranchedCardsPlayedPerTurn)
            && !options.CollectSearchPlayTrace
            && options.SearchPolicyCollector is null
            && !session.UsesTranspositions(options))
        {
            return SearchGreedyTail(
                state,
                options,
                horizon,
                run,
                turn,
                resolvedPlays,
                fullBranchDecisions,
                seed,
                useFiniteHorizonLeafValue,
                session,
                playableCards.Candidates[0].Card,
                best);
        }

        SearchTranspositionKey transpositionKey = new(
            fingerprint.LoopHash,
            fingerprint.ExactHash,
            seed,
            resolvedPlays,
            fullBranchDecisions,
            turn,
            horizon.FutureTurns,
            useFiniteHorizonLeafValue,
            workBudgetFallback);
        if (session.TryGetTransposition(
                transpositionKey,
                options,
                options.SearchBranchDiagnostics,
                out SearchPolicyCacheEntry cachedPolicy))
        {
            if (cachedPolicy.Stop)
            {
                return best;
            }

            for (int candidateIndex = 0; candidateIndex < playableCards.Count; candidateIndex++)
            {
                if (playableCards.Candidates[candidateIndex].Card.InstanceId != cachedPolicy.InstanceId)
                {
                    continue;
                }

                SearchResult cachedCandidate = EvaluateSearchCandidate(
                    state,
                    options,
                    horizon,
                    run,
                    turn,
                    resolvedPlays,
                    fullBranchDecisions,
                    seed,
                    useFiniteHorizonLeafValue,
                    session,
                    consumesChoiceDepth,
                    playableCards.Candidates[candidateIndex].Card);
                if (IsBetter(cachedCandidate, best))
                {
                    return cachedCandidate with
                    {
                        State = session.CaptureBestState(cachedCandidate.State, resolvedPlays)
                    };
                }

                break;
            }
        }

        int bestActionInstanceId = -1;
        for (int candidateIndex = 0; candidateIndex < playableCards.Count; candidateIndex++)
        {
            DeckCardInstance card = playableCards.Candidates[candidateIndex].Card;
            SearchResult candidate = EvaluateSearchCandidate(
                state,
                options,
                horizon,
                run,
                turn,
                resolvedPlays,
                fullBranchDecisions,
                seed,
                useFiniteHorizonLeafValue,
                session,
                consumesChoiceDepth,
                card);

            if (IsBetter(candidate, best))
            {
                bestActionInstanceId = card.InstanceId;
                best = candidate with
                {
                    State = session.CaptureBestState(candidate.State, resolvedPlays)
                };
            }
        }

        session.StoreTransposition(
            transpositionKey,
            new SearchPolicyCacheEntry(bestActionInstanceId < 0, bestActionInstanceId),
            options,
            options.SearchBranchDiagnostics);

        return best;
    }

    private static SearchResult SearchGreedyTail(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        int run,
        int turn,
        int resolvedPlays,
        int fullBranchDecisions,
        int seed,
        bool useFiniteHorizonLeafValue,
        SearchSession session,
        DeckCardInstance firstCard,
        SearchResult firstStop)
    {
        GreedyTailStep[] steps = ArrayPool<GreedyTailStep>.Shared.Rent(
            options.MaxCardsPlayedPerTurn + 1);
        int stepCount = 0;
        SearchTurnProfile? slowTailProfile = options.ActiveSearchTurnProfile;

        try
        {
            SimulationState tailState = session.CloneState(
                state,
                resolvedPlays + 1,
                options.SearchBranchDiagnostics);
            DeckCardInstance ordinaryCard = FindHandCard(tailState, firstCard.InstanceId);
            long candidateStartNodes = slowTailProfile?.SearchNodes ?? 0;
            long candidateStartedAt = slowTailProfile is null ? 0 : Stopwatch.GetTimestamp();

            while (true)
            {
                SimulationState? stopState = firstStop.DecisionValue == double.NegativeInfinity
                    ? null
                    : stepCount == 0
                        ? state
                        : session.CaptureGreedyTailStopState(
                            tailState,
                            resolvedPlays,
                            options.SearchBranchDiagnostics);
                double stopDecisionValue = firstStop.DecisionValue;

                FastRandom branchRng = new(DeriveSeed(seed, resolvedPlays, ordinaryCard.InstanceId));
                PlayOutcome ordinaryPlay = PlayCard(
                    tailState,
                    ordinaryCard,
                    branchRng,
                    options,
                    horizon,
                    run,
                    turn,
                    resolvedPlays,
                    seed);
                steps[stepCount++] = new GreedyTailStep(
                    ordinaryPlay,
                    stopState,
                    stopDecisionValue,
                    ordinaryCard.Card.TypeName,
                    resolvedPlays,
                    candidateStartNodes,
                    candidateStartedAt);
                resolvedPlays++;
                seed = branchRng.Next();
                int deterministicChain = 0;
                bool workBudgetFallback = false;

                while (true)
                {
                    workBudgetFallback |= session.EnterNode(options.SearchBranchDiagnostics);
                    if (tailState.TurnEnded || resolvedPlays >= options.MaxCardsPlayedPerTurn)
                    {
                        options.SearchBranchDiagnostics?.RecordDeterministicChain(deterministicChain);
                        return FoldGreedyTail(
                            steps,
                            stepCount,
                            CreateLeafResult(
                                tailState,
                                options,
                                horizon,
                                useFiniteHorizonLeafValue),
                            options);
                    }

                    bool useTranspositions = session.UsesTranspositions(options);
                    SearchStateFingerprint fingerprint = options.EnableLoopDetection || useTranspositions
                        ? ComputeSearchStateFingerprint(tailState, includeExactHash: useTranspositions)
                        : default;
                    if (options.EnableLoopDetection)
                    {
                        ulong signature = fingerprint.LoopHash;
                        if (session.TryFindLoop(
                                resolvedPlays,
                                signature,
                                tailState.Energy,
                                tailState.Stars,
                                out int priorEnergy,
                                out int priorStars))
                        {
                            bool positiveResourceLoop = tailState.Energy > priorEnergy
                                || tailState.Stars > priorStars;
                            options.SearchBranchDiagnostics?.RecordLoop(positiveResourceLoop);
                            options.ActiveSearchTurnProfile?.RecordLoop(positiveResourceLoop);
                            if (!positiveResourceLoop
                                && tailState.Energy <= priorEnergy
                                && tailState.Stars <= priorStars)
                            {
                                options.SearchBranchDiagnostics?.RecordPrunedLoop();
                                options.ActiveSearchTurnProfile?.RecordPrunedLoop();
                                options.SearchBranchDiagnostics?.RecordDeterministicChain(deterministicChain);
                                return FoldGreedyTail(
                                    steps,
                                    stepCount,
                                    CreateLeafResult(
                                        tailState,
                                        options,
                                        horizon,
                                        useFiniteHorizonLeafValue),
                                    options);
                            }
                        }

                        session.RecordLoopState(
                            resolvedPlays,
                            signature,
                            tailState.Energy,
                            tailState.Stars);
                    }

                    CardCandidateSet legalPlayableCards = SelectPlayableCards(
                        tailState,
                        options,
                        turn,
                        session.PlayableBuffer(resolvedPlays, tailState.Hand.Count));
                    bool allowDeterministicPlay = deterministicChain < options.MaxDeterministicPlayChain;
                    ForcedPlayPolicyResult policy = ApplyForcedPlayPolicy(
                        tailState,
                        horizon,
                        legalPlayableCards,
                        session.PolicyBuffer(resolvedPlays, legalPlayableCards.Count),
                        allowDeterministicPlay);
                    legalPlayableCards = policy.OrdinaryCandidates;
                    if (policy.ForcedCard is { } forcedCard)
                    {
                        options.SearchBranchDiagnostics?.RecordForcedPlay();
                        options.ActiveSearchTurnProfile?.RecordForcedCard(forcedCard.Card.TypeName);
                        branchRng = new FastRandom(DeriveSeed(seed, resolvedPlays, forcedCard.InstanceId));
                        PlayOutcome forcedPlay = PlayCard(
                            tailState,
                            forcedCard,
                            branchRng,
                            options,
                            horizon,
                            run,
                            turn,
                            resolvedPlays,
                            seed);
                        steps[stepCount++] = new GreedyTailStep(
                            forcedPlay,
                            null,
                            double.NegativeInfinity,
                            string.Empty,
                            resolvedPlays,
                            0,
                            0);
                        resolvedPlays++;
                        deterministicChain++;
                        seed = branchRng.Next();
                        continue;
                    }

                    options.SearchBranchDiagnostics?.RecordDeterministicChain(deterministicChain);
                    SearchCandidateSet playableCards = SelectTopPlayableCards(
                        tailState,
                        options,
                        legalPlayableCards,
                        workBudgetFallback
                            ? options.MaxFullyBranchedCardsPlayedPerTurn
                            : fullBranchDecisions,
                        horizon,
                        policy.RequiredOrdinaryCandidate,
                        session.SearchCandidateBuffer(resolvedPlays));
                    SearchResult currentStop = CreateOrdinaryStopResult(
                        tailState,
                        options,
                        horizon,
                        useFiniteHorizonLeafValue,
                        playableCards.Count,
                        policy.MustPlay);

                    if (playableCards.Count != 1)
                    {
                        SearchResult suffix = SearchOrdinaryCandidates(
                            tailState,
                            options,
                            horizon,
                            run,
                            turn,
                            resolvedPlays,
                            fullBranchDecisions,
                            seed,
                            useFiniteHorizonLeafValue,
                            session,
                            workBudgetFallback,
                            legalPlayableCards,
                            policy,
                            fingerprint);
                        return FoldGreedyTail(steps, stepCount, suffix, options);
                    }

                    firstStop = currentStop;
                    ordinaryCard = playableCards.Candidates[0].Card;
                    candidateStartNodes = slowTailProfile?.SearchNodes ?? 0;
                    candidateStartedAt = slowTailProfile is null ? 0 : Stopwatch.GetTimestamp();
                    break;
                }
            }
        }
        finally
        {
            Array.Clear(steps, 0, stepCount);
            ArrayPool<GreedyTailStep>.Shared.Return(steps);
        }
    }

    private static SearchResult FoldGreedyTail(
        GreedyTailStep[] steps,
        int stepCount,
        SearchResult suffix,
        DeckSimulationOptions options)
    {
        SearchTurnProfile? slowTailProfile = options.ActiveSearchTurnProfile;
        for (int index = stepCount - 1; index >= 0; index--)
        {
            GreedyTailStep step = steps[index];
            SearchResult candidate = PrependPlay(
                step.Play,
                suffix,
                options.SearchBranchDiagnostics);
            if (step.StopState is not null)
            {
                SearchResult stop = new(
                    step.StopState,
                    0d,
                    step.StopDecisionValue,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    null);
                suffix = IsBetter(candidate, stop) ? candidate : stop;
            }
            else
            {
                suffix = candidate;
            }

            if (step.CardTypeName.Length > 0 && slowTailProfile is not null)
            {
                List<string> tailPath = [];
                for (int pathIndex = index; pathIndex < stepCount; pathIndex++)
                {
                    if (steps[pathIndex].CardTypeName.Length > 0)
                    {
                        tailPath.Add(steps[pathIndex].CardTypeName);
                    }
                }

                slowTailProfile.RecordTailCandidate(
                    step.CardTypeName,
                    step.ResolvedPlayDepth,
                    slowTailProfile.SearchNodes - step.ProfileStartNodes,
                    step.ProfileStartedAt,
                    tailPath);
            }
        }

        return suffix;
    }

    private static SearchResult CreateOrdinaryStopResult(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        bool useFiniteHorizonLeafValue,
        int playableCardCount,
        bool mustPlay)
    {
        double seedDecisionValue;
        if (mustPlay)
        {
            seedDecisionValue = double.NegativeInfinity;
        }
        else if (options.StateValue is null)
        {
            // Voluntary stop is a genuine turn-end leaf. Compare it with the exact current turn-end
            // Power payoff plus the bounded remaining-turn continuation of persistent state.
            seedDecisionValue = useFiniteHorizonLeafValue
                ? FiniteHorizonLeafDecisionValue(state, options, horizon)
                : 0d;
        }
        else if (playableCardCount == 0)
        {
            // No further play possible: this state is an effective leaf -> value it with V.
            seedDecisionValue = options.StateValue.Evaluate(BuildContextFeatures(state, options));
        }
        else
        {
            // Playable cards remain: forbid voluntary stop so a complete line always wins.
            seedDecisionValue = double.NegativeInfinity;
        }

        return new SearchResult(state, 0d, seedDecisionValue, 0, 0, 0, 0, 0, 0, null);
    }

    private static SearchResult EvaluateSearchCandidate(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        int run,
        int turn,
        int resolvedPlays,
        int fullBranchDecisions,
        int seed,
        bool useFiniteHorizonLeafValue,
        SearchSession session,
        bool consumesChoiceDepth,
        DeckCardInstance card)
    {
        SearchTurnProfile? slowTailProfile = options.ActiveSearchTurnProfile;
        long searchNodesBeforeCandidate = slowTailProfile?.SearchNodes ?? 0;
        long candidateStartedAt = slowTailProfile?.BeginCandidate(card.Card.TypeName) ?? 0;
        SimulationState next = session.CloneState(
            state,
            resolvedPlays + 1,
            options.SearchBranchDiagnostics);
        DeckCardInstance nextCard = FindHandCard(next, card.InstanceId);
        FastRandom branchRng = new(DeriveSeed(seed, resolvedPlays, card.InstanceId));
        PlayOutcome play = PlayCard(
            next,
            nextCard,
            branchRng,
            options,
            horizon,
            run,
            turn,
            resolvedPlays,
            seed);
        SearchResult suffix = Search(
            next,
            options,
            horizon,
            run,
            turn,
            resolvedPlays + 1,
            fullBranchDecisions + (consumesChoiceDepth ? 1 : 0),
            branchRng.Next(),
            useFiniteHorizonLeafValue,
            session);
        SearchResult result = PrependPlay(play, suffix, options.SearchBranchDiagnostics);
        slowTailProfile?.CompleteCandidate(
            card.Card.TypeName,
            resolvedPlays,
            slowTailProfile.SearchNodes - searchNodesBeforeCandidate,
            candidateStartedAt);
        return result;
    }

    private static SearchResult CreateLeafResult(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        bool useFiniteHorizonLeafValue)
    {
        double leafValue = options.StateValue is null
            ? (useFiniteHorizonLeafValue ? FiniteHorizonLeafDecisionValue(state, options, horizon) : 0d)
            : options.StateValue.Evaluate(BuildContextFeatures(state, options));
        return new SearchResult(state, 0d, leafValue, 0, 0, 0, 0, 0, 0, null);
    }

    private static SearchResult PrependPlay(
        PlayOutcome play,
        SearchResult suffix,
        SearchBranchDiagnosticsCollector? diagnostics)
    {
        if (play.Event is not null)
        {
            diagnostics?.RecordPlayTraceNode();
        }

        return new SearchResult(
            suffix.State,
            play.Value + suffix.Value,
            play.DecisionValue + suffix.DecisionValue,
            1 + suffix.CardsPlayed,
            play.CardsDrawn + suffix.CardsDrawn,
            play.EnergySpent + suffix.EnergySpent,
            play.EnergyGained + suffix.EnergyGained,
            play.StarSpent + suffix.StarSpent,
            play.StarGained + suffix.StarGained,
            play.Event is null ? suffix.PlayTrace : new PlayTraceNode(play.Event, suffix.PlayTrace));
    }

    private static IReadOnlyList<PlayEvent> MaterializePlayTrace(PlayTraceNode? trace)
    {
        if (trace is null)
        {
            return [];
        }

        List<PlayEvent> plays = [];
        for (PlayTraceNode? node = trace; node is not null; node = node.Next)
        {
            plays.Add(node.Play);
        }

        return plays;
    }

    private static ForcedPlayPolicyResult ApplyForcedPlayPolicy(
        SimulationState state,
        FiniteHorizonContext horizon,
        CardCandidateSet legalPlayableCards,
        DeckCardInstance[] policyBuffer,
        bool allowDeterministicPlay)
    {
        bool finalTurn = horizon.FutureTurns == 0;
        int eligibleCount = 0;
        for (int index = 0; index < legalPlayableCards.Count; index++)
        {
            DeckCardInstance card = legalPlayableCards[index];
            if (!finalTurn || !card.Card.IsPower)
            {
                policyBuffer[eligibleCount++] = card;
            }
        }

        CardCandidateSet eligible = new(policyBuffer, eligibleCount);

        DeckCardInstance? forced = null;
        int bestNetEnergyGain = 0;
        for (int index = 0; index < eligible.Count; index++)
        {
            DeckCardInstance card = eligible[index];
            if (card.Card.IsPower || card.Card.EndsTurn)
            {
                continue;
            }

            int netEnergyGain = ImmediateNetEnergyGain(card, state);
            if (netEnergyGain > bestNetEnergyGain
                || (netEnergyGain == bestNetEnergyGain
                    && netEnergyGain > 0
                    && (forced is null || card.InstanceId < forced.InstanceId)))
            {
                forced = card;
                bestNetEnergyGain = netEnergyGain;
            }
        }

        if (allowDeterministicPlay && forced is not null)
        {
            return new ForcedPlayPolicyResult(forced, CardCandidateSet.Empty);
        }

        forced = null;
        for (int index = 0; index < eligible.Count; index++)
        {
            DeckCardInstance card = eligible[index];
            if (IsGeneralZeroCostCard(card, state)
                && (forced is null || card.InstanceId < forced.InstanceId))
            {
                forced = card;
            }
        }

        if (allowDeterministicPlay && forced is not null)
        {
            return new ForcedPlayPolicyResult(forced, CardCandidateSet.Empty);
        }

        forced = null;
        for (int index = 0; index < eligible.Count; index++)
        {
            DeckCardInstance card = eligible[index];
            if (IsSafeZeroCostDraw(card, state)
                && (forced is null || card.InstanceId < forced.InstanceId))
            {
                forced = card;
            }
        }

        if (allowDeterministicPlay && forced is not null)
        {
            return new ForcedPlayPolicyResult(forced, CardCandidateSet.Empty);
        }

        // Do not let ordinary search spend a zero-cost draw before enough cards exist
        // to resolve its complete draw. Later plays can populate the discard pile; this
        // policy is evaluated again after every play and forces the draw once ready.
        int retainedCount = 0;
        for (int index = 0; index < eligible.Count; index++)
        {
            DeckCardInstance card = eligible[index];
            if (!IsZeroCostDrawWaitingForCards(card, state))
            {
                policyBuffer[retainedCount++] = card;
            }
        }

        eligible = new CardCandidateSet(policyBuffer, retainedCount);

        DeckCardInstance? voidForm = null;
        for (int index = 0; index < eligible.Count; index++)
        {
            DeckCardInstance card = eligible[index];
            if (IsVoidForm(card.Card)
                && (voidForm is null || card.InstanceId < voidForm.InstanceId))
            {
                voidForm = card;
            }
        }

        if (voidForm is not null)
        {
            int reserve = EffectiveEnergyCost(voidForm, state);
            if (state.Energy <= reserve)
            {
                return allowDeterministicPlay
                    ? new ForcedPlayPolicyResult(voidForm, CardCandidateSet.Empty)
                    : new ForcedPlayPolicyResult(
                        null,
                        CardCandidateSet.Single(policyBuffer, voidForm),
                        MustPlay: true);
            }

            int surplus = state.Energy - reserve;
            int reserveSafeCount = 0;
            for (int index = 0; index < eligible.Count; index++)
            {
                DeckCardInstance card = eligible[index];
                if (card.InstanceId == voidForm.InstanceId)
                {
                    continue;
                }

                if (EffectiveEnergyCost(card, state) <= surplus)
                {
                    policyBuffer[reserveSafeCount++] = card;
                }
            }

            CardCandidateSet reserveSafeCandidates = new(policyBuffer, reserveSafeCount);
            DeckCardInstance? reserveSafePower = SelectForcedPower(reserveSafeCandidates);
            if (allowDeterministicPlay && reserveSafePower is not null)
            {
                return new ForcedPlayPolicyResult(reserveSafePower, CardCandidateSet.Empty);
            }

            if (reserveSafeCount == 0)
            {
                return allowDeterministicPlay
                    ? new ForcedPlayPolicyResult(voidForm, CardCandidateSet.Empty)
                    : new ForcedPlayPolicyResult(
                        null,
                        CardCandidateSet.Single(policyBuffer, voidForm),
                        MustPlay: true);
            }

            policyBuffer[reserveSafeCount++] = voidForm;
            return new ForcedPlayPolicyResult(
                null,
                new CardCandidateSet(policyBuffer, reserveSafeCount),
                RequiredOrdinaryCandidate: voidForm,
                MustPlay: true);
        }

        forced = allowDeterministicPlay ? SelectForcedPower(eligible) : null;
        return forced is null
            ? new ForcedPlayPolicyResult(null, eligible)
            : new ForcedPlayPolicyResult(forced, CardCandidateSet.Empty);
    }

    private static DeckCardInstance? SelectForcedPower(CardCandidateSet cards)
    {
        DeckCardInstance? selected = null;
        for (int index = 0; index < cards.Count; index++)
        {
            DeckCardInstance card = cards[index];
            if (!card.Card.IsPower || (selected is not null && ComparePowerPriority(card, selected) >= 0))
            {
                continue;
            }

            selected = card;
        }

        return selected;
    }

    private static int ComparePowerPriority(DeckCardInstance left, DeckCardInstance right)
    {
        int comparison = left.Card.PowerPlayPriority.CompareTo(right.Card.PowerPlayPriority);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            CardBehaviorCatalog.BaseTypeName(left.Card.TypeName),
            CardBehaviorCatalog.BaseTypeName(right.Card.TypeName));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(left.Card.ModelId, right.Card.ModelId);
        return comparison != 0 ? comparison : left.InstanceId.CompareTo(right.InstanceId);
    }

    private static int ImmediateNetEnergyGain(DeckCardInstance card, SimulationState state)
    {
        return card.Card.EnergyGain - EffectiveEnergyCost(card, state);
    }

    private static bool IsGeneralZeroCostCard(DeckCardInstance card, SimulationState state)
    {
        return !card.Card.IsPower
            && !card.Card.EndsTurn
            && card.Card.Draw <= 0
            && !card.Card.DrawsToHandFull
            && EffectiveEnergyCost(card, state) == 0;
    }

    private static bool IsSafeZeroCostDraw(DeckCardInstance card, SimulationState state)
    {
        if (!IsZeroCostDraw(card, state))
        {
            return false;
        }

        int handAfterPlay = state.Hand.Count - 1;
        int availableSlots = Math.Max(0, state.MaxHandSize - handAfterPlay);
        int intendedDraw = IntendedDrawAfterPlay(card, availableSlots);
        int availableCards = state.DrawPile.Count + state.DiscardPile.Count;
        return intendedDraw <= availableSlots && intendedDraw <= availableCards;
    }

    private static bool IsZeroCostDrawWaitingForCards(DeckCardInstance card, SimulationState state)
    {
        if (!IsZeroCostDraw(card, state))
        {
            return false;
        }

        int handAfterPlay = state.Hand.Count - 1;
        int availableSlots = Math.Max(0, state.MaxHandSize - handAfterPlay);
        int intendedDraw = IntendedDrawAfterPlay(card, availableSlots);
        return state.DrawPile.Count + state.DiscardPile.Count < intendedDraw;
    }

    private static bool IsZeroCostDraw(DeckCardInstance card, SimulationState state)
    {
        return !card.Card.IsPower
            && !card.Card.EndsTurn
            && EffectiveEnergyCost(card, state) == 0
            && (card.Card.Draw > 0 || card.Card.DrawsToHandFull);
    }

    private static int IntendedDrawAfterPlay(DeckCardInstance card, int availableSlots)
    {
        return card.Card.DrawsToHandFull ? availableSlots : Math.Max(0, card.Card.Draw);
    }

    private static bool IsVoidForm(SimulationCard card)
    {
        return card.IsPower
            && string.Equals(
                CardBehaviorCatalog.BaseTypeName(card.TypeName),
                "VoidForm",
                StringComparison.OrdinalIgnoreCase);
    }

    private static SearchStateFingerprint ComputeSearchStateFingerprint(
        SimulationState state,
        bool includeExactHash)
    {
        const ulong loopOffset = 14695981039346656037UL;
        const ulong exactOffset = 1099511628211UL;
        ulong loopHash = loopOffset;
        ulong exactHash = exactOffset;

        if (includeExactHash)
        {
            AddExactSearchHash(ref exactHash, state.Energy);
            AddExactSearchHash(ref exactHash, state.Stars);
        }

        AddSharedSearchHash(ref loopHash, ref exactHash, state.BaseStarsRemaining, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.NextTurnEnergy, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.NextTurnStars, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.NextTurnDraw, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.NextTurnBlock, includeExactHash);
        AddSharedSearchHash(
            ref loopHash,
            ref exactHash,
            BitConverter.DoubleToInt64Bits(state.NextTurnBlockDecisionValue),
            includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.NextGeneratedInstanceId, includeExactHash);
        if (includeExactHash)
        {
            AddExactSearchHash(ref exactHash, state.NextPlayEventId);
        }

        AddSharedSearchHash(ref loopHash, ref exactHash, state.EnemyVulnerable, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.PlayerFrail, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.GeneratedCardsCreated, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.LastTurnCardsPlayed, includeExactHash);
        // These monotonic counters advance on every cycle. They remain in the exact cache key, but
        // excluding them from the structural loop key is what lets a resource-neutral repeated
        // card/pile state terminate instead of running to the 64-play cap.
        if (includeExactHash)
        {
            AddExactSearchHash(ref exactHash, state.CardsPlayedThisTurn);
            AddExactSearchHash(ref exactHash, state.CardsPlayedThisCombat);
            AddExactSearchHash(ref exactHash, state.AttacksPlayedThisTurn);
            AddExactSearchHash(ref exactHash, state.SkillsPlayedThisTurn);
        }

        AddSharedSearchHash(ref loopHash, ref exactHash, state.StarsGainedThisTurn, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.TurnEnded ? 1 : 0, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.RunSeed, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.ShuffleCycle, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, state.CounterfactualStableShuffle ? 1 : 0, includeExactHash);
        AddSharedSearchHash(
            ref loopHash,
            ref exactHash,
            unchecked((long)state.CombatCardGenerationRandom.State),
            includeExactHash);
        AddSharedSearchHash(
            ref loopHash,
            ref exactHash,
            unchecked((long)StableSearchStringHash(state.CharacterPoolName ?? string.Empty)),
            includeExactHash);

        AddSearchPileHash(ref loopHash, ref exactHash, state.DrawPile, 1, includeExactHash);
        AddSearchPileHash(ref loopHash, ref exactHash, state.Hand, 2, includeExactHash);
        AddSearchPileHash(ref loopHash, ref exactHash, state.DiscardPile, 3, includeExactHash);
        AddSearchPileHash(ref loopHash, ref exactHash, state.ExhaustPile, 4, includeExactHash);
        AddSharedSearchHash(ref loopHash, ref exactHash, 5, includeExactHash);
        AddSharedSearchHash(
            ref loopHash,
            ref exactHash,
            unchecked((long)state.ActivePowers.SearchStateHash),
            includeExactHash);

        AddSearchResourceSources(ref loopHash, ref exactHash, state.StrengthSources, 11, includeExactHash);
        AddSearchResourceSources(ref loopHash, ref exactHash, state.DexteritySources, 12, includeExactHash);
        AddSearchResourceSources(ref loopHash, ref exactHash, state.FastenSources, 13, includeExactHash);
        AddSearchResourceSources(ref loopHash, ref exactHash, state.ParrySources, 14, includeExactHash);
        AddSearchResourceSources(ref loopHash, ref exactHash, state.SeekingEdgeSources, 15, includeExactHash);
        AddSearchResourceSources(ref loopHash, ref exactHash, state.SwordSageSources, 16, includeExactHash);

        return new SearchStateFingerprint(loopHash, exactHash);
    }

    private static void AddSearchPileHash(
        ref ulong loopHash,
        ref ulong exactHash,
        SimulationCardPile pile,
        int separator,
        bool includeExactHash)
    {
        AddSharedSearchHash(ref loopHash, ref exactHash, separator, includeExactHash);
        AddSharedSearchHash(
            ref loopHash,
            ref exactHash,
            unchecked((long)pile.SearchStateHash),
            includeExactHash);
    }

    private static void AddSearchResourceSources(
        ref ulong loopHash,
        ref ulong exactHash,
        IReadOnlyList<ResourceSourceCredit>? sources,
        int separator,
        bool includeExactHash)
    {
        AddSharedSearchHash(ref loopHash, ref exactHash, separator, includeExactHash);
        if (sources is null)
        {
            return;
        }

        foreach (ResourceSourceCredit source in sources)
        {
            AddSharedSearchHash(
                ref loopHash,
                ref exactHash,
                unchecked((long)StableSearchStringHash(source.SourceModelId)),
                includeExactHash);
            AddSharedSearchHash(ref loopHash, ref exactHash, BitConverter.DoubleToInt64Bits(source.Amount), includeExactHash);
        }
    }

    private static void AddSharedSearchHash(
        ref ulong loopHash,
        ref ulong exactHash,
        long value,
        bool includeExactHash)
    {
        AddExactSearchHash(ref loopHash, value);
        if (includeExactHash)
        {
            AddExactSearchHash(ref exactHash, value);
        }
    }

    private static void AddExactSearchHash(ref ulong hash, long value)
    {
        unchecked
        {
            // SplitMix64 avalanches one complete scalar, then an order-sensitive rotate/multiply
            // folds it into the state hash. The previous byte-at-a-time FNV loop performed eight
            // dependent iterations for every integer, double, and cached card identity in every
            // search node. Both are 64-bit fingerprints; this keeps the same collision envelope
            // while reducing the hot operation to a fixed handful of arithmetic instructions.
            ulong mixed = (ulong)value + 0x9E3779B97F4A7C15UL;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;
            hash ^= mixed;
            hash = ((hash << 27) | (hash >> 37)) * 0x3C79AC492BA7B653UL
                + 0x1C69B3F74AC4AE35UL;
        }
    }

    private static ulong StableSearchStringHash(string value)
    {
        return StableSearchStringHashCache.GetOrAdd(value, static text =>
        {
            ulong hash = 14695981039346656037UL;
            foreach (char character in text)
            {
                AddExactSearchHash(ref hash, character);
            }

            return hash;
        });
    }

    private static CardCandidateSet SelectPlayableCards(
        SimulationState state,
        DeckSimulationOptions options,
        int turn,
        DeckCardInstance[] playableBuffer)
    {
        int playableCount = 0;
        foreach (DeckCardInstance card in state.Hand)
        {
            if (CanPlay(
                card.Card,
                state,
                options,
                card.InstanceId,
                card.BonusDrawCostReduction,
                card.CostOverrideThisCombat,
                card.BonusUntilPlayedCostReduction,
                card.FreeThisTurn,
                turn))
            {
                playableBuffer[playableCount++] = card;
            }
        }

        return new CardCandidateSet(playableBuffer, playableCount);
    }

    private static SearchCandidateSet SelectTopPlayableCards(
        SimulationState state,
        DeckSimulationOptions options,
        CardCandidateSet legalPlayableCards,
        int fullBranchDecisions,
        FiniteHorizonContext horizon,
        DeckCardInstance? requiredCandidate,
        SearchCandidate[] selected)
    {
        bool fullyBranched = fullBranchDecisions < options.MaxFullyBranchedCardsPlayedPerTurn;
        int branchLimit = !fullyBranched
            ? 1
            : options.MaxBranchingCards;
        int limit = Math.Min(Math.Max(0, branchLimit), legalPlayableCards.Count);
        if (limit == 0)
        {
            return SearchCandidateSet.Empty;
        }

        int selectedCount = 0;
        for (int cardIndex = 0; cardIndex < legalPlayableCards.Count; cardIndex++)
        {
            DeckCardInstance card = legalPlayableCards[cardIndex];
            double score = ScoreSearchCard(card, state, options, horizon);
            int insertIndex = 0;
            while (insertIndex < selectedCount
                && (selected[insertIndex].Score > score
                    || (selected[insertIndex].Score == score
                        && selected[insertIndex].Card.InstanceId < card.InstanceId)))
            {
                insertIndex++;
            }

            if (insertIndex >= limit)
            {
                continue;
            }

            int lastIndex = Math.Min(selectedCount, limit - 1);
            for (int index = lastIndex; index > insertIndex; index--)
            {
                selected[index] = selected[index - 1];
            }

            selected[insertIndex] = new SearchCandidate(card, score);
            if (selectedCount < limit)
            {
                selectedCount++;
            }
        }

        bool requiredCandidateSelected = false;
        if (requiredCandidate is not null)
        {
            for (int index = 0; index < selectedCount; index++)
            {
                if (selected[index].Card.InstanceId == requiredCandidate.InstanceId)
                {
                    requiredCandidateSelected = true;
                    break;
                }
            }
        }

        if (requiredCandidate is not null && !requiredCandidateSelected)
        {
            selected[selectedCount - 1] = new SearchCandidate(
                requiredCandidate,
                ScoreSearchCard(requiredCandidate, state, options, horizon));
        }

        // A flagged card's first legal availability is never lost, but at most one candidate beyond
        // Top-B is admitted at a node. Missing flagged cards remain armed for descendant nodes. This
        // preserves the ordinary Top-B candidates and eventually explores every first-availability
        // card without multiplying one node into Top-B+k when several explicit generators are drawn.
        DeckCardInstance? extraAdmission = null;
        double extraAdmissionScore = double.NegativeInfinity;
        for (int cardIndex = 0; cardIndex < legalPlayableCards.Count; cardIndex++)
        {
            DeckCardInstance card = legalPlayableCards[cardIndex];
            if (!card.PendingGuaranteedSearchAdmission)
            {
                continue;
            }

            bool alreadySelected = false;
            for (int index = 0; index < selectedCount; index++)
            {
                if (selected[index].Card.InstanceId == card.InstanceId)
                {
                    alreadySelected = true;
                    break;
                }
            }

            if (alreadySelected)
            {
                card.PendingGuaranteedSearchAdmission = false;
                continue;
            }

            double score = ScoreSearchCard(card, state, options, horizon);
            if (extraAdmission is null
                || score > extraAdmissionScore
                || (score == extraAdmissionScore && card.InstanceId < extraAdmission.InstanceId))
            {
                extraAdmission = card;
                extraAdmissionScore = score;
            }
        }

        if (extraAdmission is not null)
        {
            extraAdmission.PendingGuaranteedSearchAdmission = false;
            selected[selectedCount++] = new SearchCandidate(extraAdmission, extraAdmissionScore);
        }

        int baseSelectedCount = Math.Min(limit, selectedCount);
        options.SearchBranchDiagnostics?.Record(baseSelectedCount, selectedCount, fullyBranched);
        options.ActiveSearchTurnProfile?.RecordDecision(fullyBranched);

        return new SearchCandidateSet(selected, selectedCount);
    }

    private static void ArmGuaranteedSearchAdmission(IEnumerable<DeckCardInstance> cards)
    {
        foreach (DeckCardInstance card in cards)
        {
            if (UsesGuaranteedSearchAdmission(card.Card))
            {
                card.PendingGuaranteedSearchAdmission = true;
            }
        }
    }

    private static bool UsesGuaranteedSearchAdmission(SimulationCard card)
    {
        return card.SearchAdmission == SearchAdmissionPolicy.OncePerHandAvailability;
    }

    private static double ScoreSearchCard(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon)
    {
        if (options.SearchCardScorer is { } scorer)
        {
            // The learned scorer supplies the base ordering; the instance enchantment prior remains
            // an enforced simulator rule so older models cannot silently discard enchanted cards.
            return scorer.Score(BuildSearchCardScoringContext(card, state, options))
                + EnchantmentBeamSetupDecisionValue(
                    card,
                    state,
                    ResourceReferenceValuesForTurns(horizon.RemainingTurns),
                    options);
        }

        // Heuristic beam: add cross-card synergy bonuses so a narrow beam keeps enabler cards
        // (e.g. skills that pump a skills-scaling attack in hand) ranked ahead of alternatives.
        return CardSearchScore(card, state, options, horizon)
            + SearchSynergyBonus(card.Card, state, options);
    }

    // Cross-card synergy framework for the heuristic play-search. Each hook inspects the card being
    // scored plus the full state (crucially, OTHER cards in hand) and returns a search-score bonus
    // capturing coupling that the per-card heuristic cannot see on its own. These bonuses bias ONLY
    // the search beam/ordering - they are never added to realized value (PlayCard/PlayValue) - so an
    // over- or under-estimate can only change which lines the beam explores, never the reported EV.
    // Register additional couplings by adding a hook to this list.
    private sealed record SearchSynergyHook(string Name, Func<SimulationCard, SimulationState, DeckSimulationOptions, double> Score);

    private static readonly IReadOnlyList<SearchSynergyHook> SearchSynergyHooks =
    [
        new SearchSynergyHook("conditionalScalingEnabler", ConditionalScalingEnablerBonus)
    ];

    private static double SearchSynergyBonus(SimulationCard card, SimulationState state, DeckSimulationOptions options)
    {
        double bonus = 0d;
        foreach (SearchSynergyHook hook in SearchSynergyHooks)
        {
            bonus += hook.Score(card, state, options);
        }

        return bonus;
    }

    // Hook: playing an ENABLER card is worth more when a conditional-hit-scaling payoff is in hand
    // and playable this turn. A skill enables +1 hit on each skills-scaling attack (LunarBlast); a
    // star gain enables +StarGain hits on each stars-scaling attack (Radiate). The bonus equals the
    // marginal scaling value unlocked, using the payoff card's own scaling fields (no hard-coding),
    // so the "play enablers first, then the scaling attack" line survives a narrow beam.
    private static double ConditionalScalingEnablerBonus(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        bool isSkill = string.Equals(card.CardType, "Skill", StringComparison.OrdinalIgnoreCase);
        int starGain = card.StarGain;
        if (!isSkill && starGain <= 0)
        {
            return 0d;
        }

        double bonus = 0d;
        foreach (DeckCardInstance handCard in state.Hand)
        {
            SimulationCard payoff = handCard.Card;
            if (ReferenceEquals(payoff, card)
                || payoff.ScalingDamageKind is null
                || payoff.ScalingDamagePerUnit <= 0d
                || !CanPlay(
                    payoff,
                    state,
                    options,
                    handCard.InstanceId,
                    handCard.BonusDrawCostReduction,
                    handCard.CostOverrideThisCombat,
                    handCard.BonusUntilPlayedCostReduction,
                    handCard.FreeThisTurn))
            {
                continue;
            }

            double perUnitValue = payoff.ScalingDamagePerUnit
                * payoff.ScalingDamageTargetMultiplier
                * payoff.DamageUnitValue;
            if (isSkill && payoff.ScalingDamageKind == "skillsPlayedThisTurn")
            {
                bonus += perUnitValue;
            }
            else if (starGain > 0 && payoff.ScalingDamageKind == "starsGainedThisTurn")
            {
                bonus += perUnitValue * starGain;
            }
        }

        return bonus;
    }

    private static SearchPolicyDecisionGroup? BuildSearchPolicyDecisionGroup(
        SimulationState state,
        DeckSimulationOptions options,
        IReadOnlyList<DeckCardInstance> legalPlayableCards,
        SearchPolicyDataCollector collector,
        int run,
        int turn,
        int actionsPlayed,
        int fullBranchDecisions,
        int seed)
    {
        if (!collector.CanCollect || legalPlayableCards.Count <= 1)
        {
            return null;
        }

        SearchPolicyGroupMetadata metadata = options.SearchPolicyMetadata
            ?? new SearchPolicyGroupMetadata(
                "<unknown>",
                -1,
                "simulation",
                Math.Max(1, options.MaxBranchingCards),
                Math.Max(1, options.MaxCardsPlayedPerTurn),
                DefaultTeacherForwardTurns,
                DefaultTeacherRollouts);
        metadata = metadata with
        {
            TeacherMaxBranchingCards = Math.Max(1, metadata.TeacherMaxBranchingCards),
            TeacherMaxCardsPlayedPerTurn = Math.Max(1, metadata.TeacherMaxCardsPlayedPerTurn),
            TeacherForwardTurns = Math.Max(1, metadata.TeacherForwardTurns),
            TeacherRollouts = Math.Max(1, metadata.TeacherRollouts)
        };

        List<SearchPolicyActionSample> unranked = [];
        foreach (DeckCardInstance card in legalPlayableCards)
        {
            double teacherRouteValue = TeacherRouteDecisionValue(
                state,
                options,
                metadata,
                card,
                run,
                turn,
                actionsPlayed,
                fullBranchDecisions,
                seed);
            unranked.Add(new SearchPolicyActionSample(
                card.Card.ReportModelId,
                card.Card.ReportTypeName,
                card.InstanceId,
                BuildActionFeatures(card, state, options),
                (double)CardSearchScore(card.Card, state, options),
                (double)teacherRouteValue,
                TeacherRank: 0));
        }

        if (unranked.Count <= 1)
        {
            return null;
        }

        int[] ranks = new int[unranked.Count];
        (SearchPolicyActionSample Action, int Index)[] ranked = unranked
            .Select((action, index) => (action, index))
            .OrderByDescending(item => item.action.TeacherRouteValue)
            .ThenByDescending(item => item.action.HeuristicScore)
            .ThenBy(item => item.action.InstanceId)
            .ToArray();
        for (int index = 0; index < ranked.Length; index++)
        {
            ranks[ranked[index].Index] = index + 1;
        }

        IReadOnlyList<SearchPolicyActionSample> actions = unranked
            .Select((action, index) => action with { TeacherRank = ranks[index] })
            .ToArray();
        return new SearchPolicyDecisionGroup(
            SearchPolicyDecisionGroup.CurrentSchemaVersion,
            GroupId: string.Empty,
            options.SearchPolicySource,
            run,
            turn,
            actionsPlayed,
            BuildContextFeatures(state, options),
            actions,
            ranked[0].Index,
            metadata);
    }

    private static double TeacherRouteDecisionValue(
        SimulationState state,
        DeckSimulationOptions options,
        SearchPolicyGroupMetadata metadata,
        DeckCardInstance firstCard,
        int run,
        int turn,
        int actionsPlayed,
        int fullBranchDecisions,
        int seed)
    {
        DeckSimulationOptions teacherOptions = options with
        {
            MaxBranchingCards = metadata.TeacherMaxBranchingCards,
            MaxCardsPlayedPerTurn = metadata.TeacherMaxCardsPlayedPerTurn,
            SearchCardScorer = null,
            SearchPolicyCollector = null,
            SearchPolicySource = "teacher",
            SearchPolicyMetadata = null,
            CollectAttribution = false,
            CollectSearchPlayTrace = false
        };
        int forwardTurns = Math.Max(1, metadata.TeacherForwardTurns);
        int rollouts = Math.Max(1, metadata.TeacherRollouts);

        // Common random numbers: the DECISION (not the card) seeds the rollouts, so every candidate
        // shares the same K random scenarios - score differences reflect the forced first play, not
        // draw luck. Averaging K independent rollouts DENOISES the teacher-Q label: a single forward
        // rollout's RNG otherwise makes "best card" inconsistent across look-alike states (the
        // round-1 diagnostic showed this noise caps top2Recall at ~0.74).
        int decisionSeed = DeriveSeed(seed, turn, actionsPlayed);
        double sum = 0d;
        for (int k = 0; k < rollouts; k++)
        {
            sum += SingleTeacherRollout(
                state, teacherOptions, firstCard, run, turn, actionsPlayed, fullBranchDecisions, forwardTurns,
                DeriveSeed(decisionSeed, k, 0));
        }

        return sum / rollouts;
    }

    private static double SingleTeacherRollout(
        SimulationState state,
        DeckSimulationOptions teacherOptions,
        DeckCardInstance firstCard,
        int run,
        int turn,
        int actionsPlayed,
        int fullBranchDecisions,
        int forwardTurns,
        int rolloutSeed)
    {
        // Force this card as the next play, finish the current turn's play phase at the teacher beam
        // width, then roll the game forward. Rank by REALIZED value (no setup/resource prior). Search
        // a CLONE so its base-case result never aliases `next` (a self-CopyFrom would wipe the piles).
        SimulationState next = state.Clone();
        DeckCardInstance nextCard = FindHandCard(next, firstCard.InstanceId);
        FastRandom rng = new(rolloutSeed);
        PlayOutcome play = PlayCard(
            next,
            nextCard,
            rng,
            teacherOptions,
            new FiniteHorizonContext(teacherOptions.Turns, turn),
            run,
            turn,
            actionsPlayed,
            rolloutSeed);
        SearchResult suffix = Search(
            next.Clone(),
            teacherOptions,
            new FiniteHorizonContext(teacherOptions.Turns, turn),
            run,
            turn,
            actionsPlayed + 1,
            fullBranchDecisions + 1,
            rng.Next());
        next.CopyFrom(suffix.State);
        double total = play.Value + suffix.Value;
        PowerEventResult turnEnd = ResolveTurnEndPowers(next);
        total += turnEnd.Value;
        next.LastTurnCardsPlayed = suffix.CardsPlayed + 1;
        FinishTurn(next);
        next.CurrentTurnEnergySources.Clear();
        for (int offset = 1; offset < forwardTurns; offset++)
        {
            TurnTrialSummary summary = PlayTurn(next, teacherOptions, rng, run, turn + offset);
            total += summary.Value;
        }

        return total;
    }

    private static SearchCardScoringContext BuildSearchCardScoringContext(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, double> feature in BuildContextFeatures(state, options))
        {
            features[feature.Key] = feature.Value;
        }

        foreach (KeyValuePair<string, double> feature in BuildActionFeatures(card, state, options))
        {
            features[feature.Key] = feature.Value;
        }

        AddFeature(
            features,
            "card.enchantmentBeamSetupValue",
            EnchantmentBeamSetupDecisionValue(card, state, options));
        AddFeature(features, "card.enchantmentAmount", EnchantmentAmount(card));
        AddFeature(features, "card.enchantmentDisabled", card.EnchantmentDisabled);
        if (card.Card.Enchantment is { } enchantment)
        {
            AddFeature(features, $"card.enchantment.{NormalizeFeatureKey(enchantment.Key)}", true);
        }

        return new SearchCardScoringContext(card.Card.ReportModelId, card.Card.ReportTypeName, features);
    }

    private static IReadOnlyDictionary<string, double> BuildContextFeatures(
        SimulationState state,
        DeckSimulationOptions? options = null)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal);
        AddFeature(features, "context.energy", state.Energy);
        AddFeature(features, "context.stars", state.Stars);
        AddFeature(features, "context.baseStarsRemaining", state.BaseStarsRemaining);
        AddFeature(features, "context.handCount", state.Hand.Count);
        AddFeature(features, "context.playableHandCount", state.Hand.Count(card =>
            CanPlay(
                card.Card,
                state,
                options,
                card.InstanceId,
                card.BonusDrawCostReduction,
                card.CostOverrideThisCombat,
                card.BonusUntilPlayedCostReduction,
                card.FreeThisTurn)));
        AddFeature(features, "context.attackHandCount", state.Hand.Count(card => card.Card.IsAttack));
        AddFeature(features, "context.powerHandCount", state.Hand.Count(card => card.Card.IsPower));
        AddFeature(features, "context.drawPileCount", state.DrawPile.Count);
        AddFeature(features, "context.discardPileCount", state.DiscardPile.Count);
        AddFeature(features, "context.exhaustPileCount", state.ExhaustPile.Count);
        AddFeature(features, "context.activePowerCount", state.ActivePowers.Count);
        AddFeature(features, "context.enemyVulnerable", state.EnemyVulnerable);
        AddFeature(features, "context.playerFrail", state.PlayerFrail);
        AddFeature(features, "context.cardsPlayedThisTurn", state.CardsPlayedThisTurn);
        AddFeature(features, "context.cardsPlayedThisCombat", state.CardsPlayedThisCombat);
        AddFeature(features, "context.attacksPlayedThisTurn", state.AttacksPlayedThisTurn);
        AddFeature(features, "context.lastTurnCardsPlayed", state.LastTurnCardsPlayed);
        AddFeature(features, "context.nextTurnEnergy", state.NextTurnEnergy);
        AddFeature(features, "context.nextTurnStars", state.NextTurnStars);
        AddFeature(features, "context.nextTurnDraw", state.NextTurnDraw);
        AddFeature(features, "context.nextTurnBlock", state.NextTurnBlock);
        AddFeature(features, "context.generatedCardsCreated", state.GeneratedCardsCreated);
        AddFeature(features, "context.strength", SumSources(state.StrengthSources));
        AddFeature(features, "context.dexterity", SumSources(state.DexteritySources));
        AddFeature(features, "context.fasten", SumSources(state.FastenSources));
        AddFeature(features, "context.parry", SumSources(state.ParrySources));
        AddFeature(features, "context.seekingEdge", SumSources(state.SeekingEdgeSources));
        AddFeature(features, "context.swordSage", SumSources(state.SwordSageSources));
        AddFeature(features, "context.vigor", SumSources(state.VigorSources));

        foreach (ActivePowerKind kind in Enum.GetValues<ActivePowerKind>())
        {
            AddFeature(features, $"context.power.{kind}", state.ActivePowers
                .Where(power => power.Kind == kind)
                .Sum(power => power.Amount));
        }

        return features;
    }

    private static IReadOnlyDictionary<string, double> BuildActionFeatures(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return BuildActionFeatures(
            card.Card,
            state,
            options,
            card.InstanceId,
            card.BonusDrawCostReduction,
            card.CostOverrideThisCombat,
            card.BonusUntilPlayedCostReduction,
            card.FreeThisTurn);
    }

    private static IReadOnlyDictionary<string, double> BuildActionFeatures(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options,
        int instanceId = -1,
        int drawCostReduction = 0,
        int? costOverrideThisCombat = null,
        int untilPlayedCostReduction = 0,
        bool freeThisTurn = false)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal);
        AddFeature(features, "card.energyCost", card.EnergyCost);
        AddFeature(features, "card.effectiveEnergyCost", EffectiveEnergyCost(
            card,
            state,
            drawCostReduction,
            freeThisTurn,
            costOverrideThisCombat,
            untilPlayedCostReduction));
        AddFeature(features, "card.starCost", card.StarCost);
        AddFeature(features, "card.effectiveStarCost", EffectiveStarCost(card, state));
        AddFeature(features, "card.freeThisTurn", freeThisTurn);
        AddFeature(features, "card.hasExplicitStarCost", card.HasExplicitStarCost);
        AddFeature(features, "card.hasStarCostX", card.HasStarCostX);
        AddFeature(features, "card.intrinsicValue", card.IntrinsicValue);
        AddFeature(features, "card.damageValue", card.DamageValue);
        AddFeature(features, "card.dynamicScalingDamageValue", DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: false));
        AddFeature(features, "card.scalingDamageBase", card.ScalingDamageBase);
        AddFeature(features, "card.scalingDamagePerUnit", card.ScalingDamagePerUnit);
        AddFeature(features, "card.scalingDamageTargetMultiplier", card.ScalingDamageTargetMultiplier);
        AddFeature(features, "card.baseDamage", card.BaseDamage);
        AddFeature(features, "card.damageModifierMultiplier", card.DamageModifierMultiplier);
        AddFeature(features, "card.effectiveDamageModifierMultiplier", EffectiveDamageModifierMultiplier(card, state));
        AddFeature(features, "card.damageUnitValue", card.DamageUnitValue);
        AddFeature(features, "card.baseBlock", card.BaseBlock);
        AddFeature(features, "card.blockEffectCount", card.BlockEffectCount);
        AddFeature(features, "card.blockValuePerBlock", card.BlockValuePerBlock);
        AddFeature(features, "card.draw", card.Draw);
        AddFeature(features, "card.drawNextTurn", card.DrawNextTurn);
        AddFeature(features, "card.blockNextTurn", card.BlockNextTurn);
        AddFeature(features, "card.energyGain", card.EnergyGain);
        AddFeature(features, "card.energyNextTurn", card.EnergyNextTurn);
        AddFeature(features, "card.starGain", card.StarGain);
        AddFeature(features, "card.starNextTurn", card.StarNextTurn);
        AddFeature(features, "card.forge", card.Forge);
        AddFeature(features, "card.vulnerable", card.Vulnerable);
        AddFeature(features, "card.xCostDamageValue", XCostDamageValue(card, state));
        AddFeature(features, "card.xCostDamageModifierMultiplier", XCostDamageModifierMultiplier(card, state));
        AddFeature(features, "card.vulnerableBonus", VulnerableBonus(
            card.DamageValue + DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: false) + XCostDamageValue(card, state),
            state));
        AddFeature(features, "card.heuristicScore", CardSearchScore(card, state, options));
        AddFeature(features, "card.staticEstimatedValue", card.StaticEstimatedValue);
        AddFeature(features, "card.beamSetupValue", card.BeamSetupValue);
        AddFeature(features, "card.playSetupValue", card.PlaySetupValue);
        IReadOnlyList<DynamicSetupDescriptor> dynamicSetups = DynamicSetupsForCard(card);
        ExplicitResourceReferenceValues resourceReferenceValues = ResourceReferenceValuesForTurns(options.Turns);
        AddFeature(features, "card.dynamicBeamSetupValue", DynamicSetupDecisionValue(
            card,
            state,
            resourceReferenceValues,
            DynamicSetupSlot.Beam,
            includeDynamicSetup: true));
        AddFeature(features, "card.dynamicPlaySetupValue", DynamicSetupDecisionValue(
            card,
            state,
            resourceReferenceValues,
            DynamicSetupSlot.Play,
            includeDynamicSetup: true));
        AddFeature(features, "card.dynamicSetup.count", dynamicSetups.Count);
        AddFeature(features, "card.dynamicSetup.hasBeam", HasDynamicSetupSlot(dynamicSetups, CardBehaviorCatalog.BeamSetupSlot));
        AddFeature(features, "card.dynamicSetup.hasPlay", HasDynamicSetupSlot(dynamicSetups, CardBehaviorCatalog.PlaySetupSlot));
        AddFeature(features, "card.upgradeLevel", card.UpgradeLevel);
        AddFeature(features, "card.layer", card.Layer);
        AddFeature(features, "card.isPlayable", card.IsPlayable);
        AddFeature(features, "card.canPlay", CanPlay(
            card,
            state,
            options,
            instanceId,
            drawCostReduction,
            costOverrideThisCombat,
            untilPlayedCostReduction,
            freeThisTurn));
        AddFeature(features, "card.isAttack", card.IsAttack);
        AddFeature(features, "card.isPower", card.IsPower);
        AddFeature(features, "card.exhausts", HasEffectiveExhaust(card));
        AddFeature(features, "card.ethereal", card.Ethereal);
        AddFeature(features, "card.retain", HasEffectiveRetain(card));
        AddFeature(features, "card.innate", HasEffectiveInnate(card));
        AddFeature(features, "card.hasXCostDamageAction", HasXCostDamage(card));
        AddFeature(features, "card.isSovereignBlade", IsSovereignBlade(card));
        AddFeature(features, "card.isHeavenlyDrill", IsHeavenlyDrill(card));
        AddFeature(features, "card.hasMoveCardAction", card.Actions.Any(action => action.Kind == "moveCardBetweenPiles"));
        AddFeature(features, "card.hasTransformCardAction", card.Actions.Any(action => action.Kind == "transformCard"));
        AddFeature(features, "card.hasCreateCardAction", card.Actions.Any(action => action.Kind is "createCard" or "createCardChoices"));

        foreach (IGrouping<string, CardActionFact> group in card.Actions.GroupBy(action => action.Kind, StringComparer.Ordinal))
        {
            string key = NormalizeFeatureKey(group.Key);
            AddFeature(features, $"card.action.{key}.count", group.Count());
            AddFeature(features, $"card.action.{key}.amount", group.Sum(action => (double)(action.Amount ?? 0m)));
        }

        foreach (DynamicSetupDescriptor setup in dynamicSetups)
        {
            string key = NormalizeFeatureKey(setup.Key);
            AddFeature(features, $"card.dynamicSetup.{key}", true);
        }

        return features;
    }

    // P3: called many times per candidate per search node (strength/dex/vigor/... modifiers). Plain
    // indexed loop avoids the LINQ Sum delegate + enumerator allocation on a hot path.
    private static double SumSources(IReadOnlyList<ResourceSourceCredit> sources)
    {
        double total = 0d;
        for (int i = 0; i < sources.Count; i++)
        {
            total += sources[i].Amount;
        }

        return total;
    }

    private static void AddPowerResolutionValues(
        ref double total,
        IReadOnlyList<PowerResolution> resolutions)
    {
        for (int index = 0; index < resolutions.Count; index++)
        {
            total += resolutions[index].Value;
        }
    }

    private static void AddFeature(IDictionary<string, double> features, string name, bool value)
    {
        features[name] = value ? 1d : 0d;
    }

    private static void AddFeature(IDictionary<string, double> features, string name, int value)
    {
        features[name] = value;
    }

    private static void AddFeature(IDictionary<string, double> features, string name, double value)
    {
        features[name] = (double)value;
    }

    private static string NormalizeFeatureKey(string value)
    {
        char[] chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }

    private static bool HasDynamicSetupSlot(IReadOnlyList<DynamicSetupDescriptor> setups, string slot)
    {
        return setups.Any(setup => setup.Slots.Contains(slot, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DynamicSetupDescriptor> DynamicSetupsForCard(SimulationCard card)
    {
        return card.DynamicSetups.Count > 0
            ? card.DynamicSetups
            : CardBehaviorCatalog.ForCard(card).DynamicSetups;
    }

    private static bool HasEnchantment(SimulationCard card, string key)
    {
        return card.Enchantment is not null
            && string.Equals(card.Enchantment.Key, key, StringComparison.OrdinalIgnoreCase);
    }

    private static int EnchantmentAmount(SimulationCard card)
    {
        return card.Enchantment is null ? 0 : Math.Max(1, card.Enchantment.Amount);
    }

    private static int EnchantmentAmount(DeckCardInstance instance)
    {
        return instance.EnchantmentAmount > 0 ? instance.EnchantmentAmount : EnchantmentAmount(instance.Card);
    }

    private static bool IsRuntimeSupportedEnchantment(SimulationCard card)
    {
        return card.Enchantment is { IsRuntimeSupported: true };
    }

    private static bool HasEffectiveRetain(SimulationCard card)
    {
        return card.Retain
            || HasEnchantment(card, "STEADY")
            || HasEnchantment(card, "ROYALLY_APPROVED");
    }

    private static bool HasEffectiveInnate(SimulationCard card)
    {
        return card.Innate || HasEnchantment(card, "ROYALLY_APPROVED");
    }

    private static bool HasEffectiveExhaust(SimulationCard card)
    {
        if (HasEnchantment(card, "SOULS_POWER"))
        {
            return false;
        }

        return card.Exhausts || HasEnchantment(card, "GOOPY");
    }

    private static DeckCardInstance FindHandCard(SimulationState state, int instanceId)
    {
        foreach (DeckCardInstance card in state.Hand)
        {
            if (card.InstanceId == instanceId)
            {
                return card;
            }
        }

        throw new InvalidOperationException($"Card instance {instanceId} was not found in the cloned hand.");
    }

    private static PlayOutcome PlayCard(
        SimulationState state,
        DeckCardInstance card,
        FastRandom rng,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon,
        int run,
        int turn,
        int actionsPlayed,
        int seed)
    {
        bool collect = options.CollectAttribution;
        RecordActivePowerExposures(state, options);
        state.Hand.Remove(card);
        SimulationCard playedCard = card.Card;
        int playId = state.NextPlayEventId++;
        int attackSkillPlaysBeforePlay = state.AttacksPlayedThisTurn + state.SkillsPlayedThisTurn;
        int energyCost = EffectiveEnergyCost(
            playedCard,
            state,
            card.BonusDrawCostReduction,
            card.FreeThisTurn,
            card.CostOverrideThisCombat,
            card.BonusUntilPlayedCostReduction);
        int starCost = EffectiveStarCost(playedCard, state);
        PowerEventResult beforeCardPlayedResult = ResolveBeforeCardPlayedPowers(state);
        PlayValueResult playValue = PlayValue(card, state, collect);
        CardObjectDecisionProfile? cardObjectDecision = CardBehaviorCatalog.ForCard(playedCard).CardObjectDecision;
        double playSetupValue = cardObjectDecision?.ReplaceStaticPlaySetup == true
            ? 0d
            : PlaySetupDecisionValue(playedCard, state, options, horizon);
        state.Energy -= energyCost;
        state.Stars -= starCost;
        IReadOnlyList<ResourceSourceCredit> consumedStarSources = ConsumeAttributableStars(state, starCost);
        PowerEventResult energySpentResult = energyCost > 0
            ? ResolveEnergySpentPowers(state, energyCost)
            : PowerEventResult.Empty;
        IReadOnlyList<PowerResolution> starSpentResolutions = starCost > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarSpent, starCost, card))
            : [];
        state.Energy += playedCard.EnergyGain;
        if (playedCard.EnergyGain > 0 && state.TrackAttributionSources)
        {
            state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                SourceModelId(playedCard),
                SourceTypeName(playedCard),
                playedCard.EnergyGain));
        }

        state.Stars += playedCard.StarGain;
        state.StarsGainedThisTurn += playedCard.StarGain;
        IReadOnlyList<ResourceSourceCredit> starGainSources = playedCard.StarGain > 0
            && state.TrackAttributionSources
            ? [new ResourceSourceCredit(SourceModelId(playedCard), SourceTypeName(playedCard), playedCard.StarGain)]
            : [];
        if (starGainSources.Count > 0)
        {
            state.StarSources.AddRange(starGainSources);
        }

        IReadOnlyList<PowerResolution> starGainedResolutions = playedCard.StarGain > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, playedCard.StarGain, card))
            : [];
        state.NextTurnEnergy += playedCard.EnergyNextTurn;
        if (playedCard.EnergyNextTurn > 0 && state.TrackAttributionSources)
        {
            state.NextTurnEnergySources.Add(new ResourceSourceCredit(
                SourceModelId(playedCard),
                SourceTypeName(playedCard),
                playedCard.EnergyNextTurn));
        }

        state.NextTurnStars += playedCard.StarNextTurn;
        if (playedCard.StarNextTurn > 0 && state.TrackAttributionSources)
        {
            state.NextTurnStarSources.Add(new ResourceSourceCredit(
                SourceModelId(playedCard),
                SourceTypeName(playedCard),
                playedCard.StarNextTurn));
        }

        state.NextTurnDraw += playedCard.DrawNextTurn;
        if (playedCard.BlockNextTurn > 0)
        {
            state.NextTurnBlock += playedCard.BlockNextTurn;
            double delayedValue = playedCard.BlockNextTurn * playedCard.BlockValuePerBlock;
            state.NextTurnBlockDecisionValue += delayedValue;
            if (state.TrackAttributionSources)
            {
                state.NextTurnBlockCredits.Add(new DelayedValueCredit(
                    SourceModelId(playedCard),
                    SourceTypeName(playedCard),
                    delayedValue));
            }
        }

        ApplyEnemyVulnerable(state, playedCard);
        ResolveBeforeForgeCardActions(state, playedCard, options);
        int forgeAmount = playedCard.Forge + DynamicForgeAmount(playedCard, state);
        PowerEventResult forgeResult = ApplyForge(state, forgeAmount, card, playId);
        int drawCount = playedCard.DrawsToHandFull
            ? Math.Max(0, state.MaxHandSize - state.Hand.Count)
            : playedCard.Draw;
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        List<CardMoveChoiceEvent>? moveChoices = options.CollectCardObjectDiagnostics ? [] : null;
        List<CardTransformChoiceEvent>? transformChoices = options.CollectCardObjectDiagnostics ? [] : null;
        CardObjectActionResult cardObjectResult = ResolveCardObjectActions(
            state,
            card,
            rng,
            options,
            moveChoices,
            transformChoices,
            new CardObjectSearchContext(run, turn, actionsPlayed, seed));
        SimulationCard? transformedPlayedCard = cardObjectResult.TransformedSource;
        PowerEventResult generatedCardResult = ResolveGeneratedCardActions(state, card, options);
        FreePlayResult autoPlay = ResolveAutoPlayActions(state, card, rng, options, depth: 0);
        double autoPlayValue = autoPlay.Value;
        EnchantmentPlayResult enchantmentResult = ResolveOnPlayEnchantment(state, card, rng, options);
        // HiddenGem: enchant a random draw-pile card, then realize any replays already enchanted onto
        // THIS instance by fully RE-PLAYING it through the real OnPlay path (recomputes damage/scaling,
        // re-gains stars, re-draws, re-triggers powers) instead of multiplying a precomputed value.
        ResolveReplayGrant(state, playedCard, rng);
        FreePlayResult bonusReplay = ResolvePostPlayReplays(state, card, rng, options, depth: 0);
        moveChoices?.AddRange(autoPlay.MoveChoices);
        moveChoices?.AddRange(bonusReplay.MoveChoices);
        transformChoices?.AddRange(autoPlay.TransformChoices);
        transformChoices?.AddRange(bonusReplay.TransformChoices);
        ResolveAfterCardPlayedEnchantment(card);

        InstallPower(state, card);
        PowerEventResult afterCardPlayedResult = ResolveAfterCardPlayedPowers(state, card, rng, options);
        double powerValue = 0d;
        AddPowerResolutionValues(ref powerValue, beforeCardPlayedResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, starSpentResolutions);
        AddPowerResolutionValues(ref powerValue, starGainedResolutions);
        AddPowerResolutionValues(ref powerValue, energySpentResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, forgeResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, drawResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, generatedCardResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, enchantmentResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, afterCardPlayedResult.PowerResolutions);
        double value = playValue.Value + powerValue + autoPlayValue + enchantmentResult.Value + bonusReplay.Value;
        // A learned line evaluator (options.StateValue) supplies the forward value at
        // the leaf, so a played card contributes only its realized value here; the
        // hand-curated setup-priority + resource-reference proxy is used only when no
        // learned evaluator is present.
        double decisionValue = options.StateValue is null
            ? value
                + playSetupValue
                + cardObjectResult.DecisionValueAdjustment
                + ExplicitResourceReferenceValue(
                    playedCard,
                    ResourceReferenceValuesForTurns(horizon.RemainingTurns),
                    includeNextTurn: false)
                - (cardObjectDecision is null ? 0d : 0.000000001d)
            : value;
        IReadOnlyList<CardValueCreditEvent> valueCredits;
        if (collect)
        {
            IReadOnlyList<PowerResolution> powerResolutions =
            [
                .. beforeCardPlayedResult.PowerResolutions,
                .. starSpentResolutions,
                .. starGainedResolutions,
                .. energySpentResult.PowerResolutions,
                .. forgeResult.PowerResolutions,
                .. drawResult.PowerResolutions,
                .. generatedCardResult.PowerResolutions,
                .. enchantmentResult.PowerResolutions,
                .. afterCardPlayedResult.PowerResolutions
            ];
            IReadOnlyList<CardValueCreditEvent> starCredits =
            [
                .. StarSpendCredits(
                    consumedStarSources,
                    playValue.Value + starSpentResolutions.Sum(resolution => resolution.Value),
                    starCost),
                .. StarTriggerCredits(starGainSources, starGainedResolutions.Sum(resolution => resolution.Value))
            ];
            valueCredits =
            [
                .. BuildValueCredits(
                    card,
                    playValue.DirectValue,
                    [
                        .. playValue.ValueCredits,
                        .. PowerCredits(powerResolutions),
                        .. beforeCardPlayedResult.ValueCredits,
                        .. energySpentResult.ValueCredits,
                        .. forgeResult.ValueCredits,
                        .. drawResult.ValueCredits,
                        .. generatedCardResult.ValueCredits,
                        .. enchantmentResult.ValueCredits,
                        .. afterCardPlayedResult.ValueCredits
                    ],
                    starCredits),
                .. autoPlay.Credits,
                .. bonusReplay.Credits
            ];
        }
        else
        {
            valueCredits = [];
        }

        MovePlayedCardToResultPile(state, card, playedCard, transformedPlayedCard, attackSkillPlaysBeforePlay);

        if (playedCard.IsAttack)
        {
            state.AttacksPlayedThisTurn++;
        }
        else if (string.Equals(playedCard.CardType, "Skill", StringComparison.OrdinalIgnoreCase))
        {
            state.SkillsPlayedThisTurn++;
        }

        if (playedCard.EndsTurn)
        {
            // VoidForm: forces the end of the current turn (the search stops adding plays this turn).
            state.TurnEnded = true;
        }

        state.CardsPlayedThisCombat++;
        PlayEvent? playEvent = options.CollectSearchPlayTrace
            ? new PlayEvent(
                card.InstanceId,
                playedCard,
                value,
                decisionValue,
                drawResult.CardsDrawn + enchantmentResult.CardsDrawn,
                energyCost,
                playedCard.EnergyGain + enchantmentResult.EnergyGained,
                starCost,
                playedCard.StarGain,
                valueCredits,
                moveChoices ?? [],
                transformChoices ?? [])
            : null;
        return new PlayOutcome(
            value,
            decisionValue,
            drawResult.CardsDrawn + enchantmentResult.CardsDrawn,
            energyCost,
            playedCard.EnergyGain + enchantmentResult.EnergyGained,
            starCost,
            playedCard.StarGain,
            playEvent);
    }

    private static PlayValueResult PlayValue(
        DeckCardInstance instance,
        SimulationState state,
        bool collectCredits)
    {
        SimulationCard card = instance.Card;
        double xCostDamageValue = XCostDamageValue(card, state);
        double scalingDamageValue = DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: true);
        // KinglyPunch: damage accrued from prior draws adds to this single-target hit's value.
        double drawScalingDamageValue = instance.BonusDrawDamage * card.DamageUnitValue;
        double enchantDamageAdditiveValue = EnchantmentDamageAdditiveValue(instance);
        double directDamageValue = card.DamageValue
            + scalingDamageValue
            + xCostDamageValue
            + drawScalingDamageValue
            + enchantDamageAdditiveValue;
        double enchantDamageMultiplier = EnchantmentDamageMultiplier(card);
        double enchantDamageMultiplicativeValue = card.IsAttack && enchantDamageMultiplier != 1d
            ? directDamageValue * (enchantDamageMultiplier - 1d)
            : 0d;
        directDamageValue += enchantDamageMultiplicativeValue;
        double vulnerableBonus = VulnerableBonus(directDamageValue, state);
        double enchantBlockValue = EnchantmentBlockAdditiveValue(instance);
        double enchantOnPlayBlockValue = EnchantmentOnPlayBlockValue(card);
        double enchantDebuffValue = EnchantmentDebuffValue(card);
        double enchantHpLossPenalty = EnchantmentHpLossPenaltyValue(card);
        double directValue = card.IntrinsicValue
            + scalingDamageValue
            + xCostDamageValue
            + drawScalingDamageValue
            + enchantDamageAdditiveValue
            + enchantDamageMultiplicativeValue
            + enchantBlockValue
            + enchantOnPlayBlockValue
            + enchantDebuffValue
            + enchantHpLossPenalty
            + ReflectApproximationValue(card)
            + StrengthLossDefenseValue(card)
            + HpLossPenaltyValue(card)
            + FrailBlockPenaltyValue(card, state);
        List<CardValueCreditEvent>? credits = collectCredits ? [] : null;
        double modifierValue = AddSovereignBladePowerCredits(credits, card, state);
        double creditedVulnerableBonus = AddVulnerableSourceCredits(credits, state, vulnerableBonus);
        if (vulnerableBonus > 0d && creditedVulnerableBonus == 0d)
        {
            directValue += vulnerableBonus;
        }

        modifierValue += creditedVulnerableBonus;

        double damageModifierMultiplier = EffectiveDamageModifierMultiplier(card, state)
            + XCostDamageModifierMultiplier(card, state);
        if (card.IsAttack && damageModifierMultiplier > 0d)
        {
            modifierValue += AddPowerModifierCredits(credits, state.StrengthSources, damageModifierMultiplier * card.DamageUnitValue);
            modifierValue += AddPowerModifierCredits(credits, state.VigorSources, damageModifierMultiplier * card.DamageUnitValue);
            state.VigorSources.Clear();
        }

        if (IsSovereignBlade(card))
        {
            modifierValue += AddConquerorPowerCredits(credits, state, directValue + modifierValue);
        }

        if (card.BaseBlock > 0d && card.BlockEffectCount > 0)
        {
            int count = card.BlockEffectCount;
            modifierValue += AddPowerModifierCredits(credits, state.DexteritySources, count * card.BlockValuePerBlock);
            if (card.HasTag("Defend"))
            {
                modifierValue += AddPowerModifierCredits(credits, state.FastenSources, count * card.BlockValuePerBlock);
            }
        }

        return new PlayValueResult(
            directValue,
            directValue + modifierValue,
            credits ?? []);
    }

    private static double EnchantmentDamageAdditiveValue(DeckCardInstance instance)
    {
        SimulationCard card = instance.Card;
        if (!card.IsAttack)
        {
            return 0d;
        }

        int amount = EnchantmentAmount(instance);
        double additive = HasEnchantment(card, "SHARP") ? amount : 0d;
        additive += HasEnchantment(card, "INKY") ? 1d : 0d;
        additive += HasEnchantment(card, "TEZCATARAS_EMBER") ? 3d : 0d;
        additive += HasEnchantment(card, "MOMENTUM") ? instance.EnchantmentBonusDamage : 0d;
        additive += HasEnchantment(card, "VIGOROUS") && !instance.EnchantmentDisabled ? amount : 0d;
        if (additive <= 0d)
        {
            return 0d;
        }

        double multiplier = card.DamageModifierMultiplier > 0d ? card.DamageModifierMultiplier : 1d;
        return additive * multiplier * card.DamageUnitValue;
    }

    private static double EnchantmentDamageMultiplier(SimulationCard card)
    {
        if (HasEnchantment(card, "INSTINCT"))
        {
            return 2d;
        }

        if (HasEnchantment(card, "CORRUPTED"))
        {
            return 1.5d;
        }

        return 1d;
    }

    private static double EnchantmentBlockAdditiveValue(DeckCardInstance instance)
    {
        SimulationCard card = instance.Card;
        if (card.BlockEffectCount <= 0)
        {
            return 0d;
        }

        double additive = HasEnchantment(card, "NIMBLE") ? EnchantmentAmount(instance) : 0d;
        if (HasEnchantment(card, "GOOPY"))
        {
            additive += Math.Max(0, instance.EnchantmentAmount - 1);
        }

        return additive <= 0d
            ? 0d
            : additive * card.BlockEffectCount * card.BlockValuePerBlock;
    }

    private static double EnchantmentOnPlayBlockValue(SimulationCard card)
    {
        return HasEnchantment(card, "ADROIT")
            ? EnchantmentAmount(card) * card.BlockValuePerBlock
            : 0d;
    }

    private static double EnchantmentDebuffValue(SimulationCard card)
    {
        if (!HasEnchantment(card, "INKY"))
        {
            return 0d;
        }

        double targetMultiplier = string.Equals(card.TargetType, "AllEnemies", StringComparison.OrdinalIgnoreCase)
            ? card.AoeDamageMultiplier
            : 1d;
        return 8d * 0.25d * card.BlockValuePerBlock * targetMultiplier;
    }

    private static double EnchantmentHpLossPenaltyValue(SimulationCard card)
    {
        return HasEnchantment(card, "CORRUPTED") ? -2d : 0d;
    }

    private static double AddSovereignBladePowerCredits(
        List<CardValueCreditEvent>? credits,
        SimulationCard card,
        SimulationState state)
    {
        if (!IsSovereignBlade(card))
        {
            return 0d;
        }

        double baseDamage = card.BaseDamage > 0d ? card.BaseDamage : card.DamageValue;
        double targetMultiplier = SovereignBladeTargetMultiplier(card, state);
        double total = 0d;
        if (targetMultiplier > 1d)
        {
            total += AddPowerModifierCredits(credits, state.SeekingEdgeSources, baseDamage * (targetMultiplier - 1d) * card.DamageUnitValue);
        }

        total += AddPowerModifierCredits(credits, state.SwordSageSources, baseDamage * targetMultiplier * card.DamageUnitValue);
        total += AddPowerModifierCredits(credits, state.ParrySources, card.BlockValuePerBlock);
        return total;
    }

    private static double EffectiveDamageModifierMultiplier(SimulationCard card, SimulationState state)
    {
        if (!IsSovereignBlade(card))
        {
            return card.DamageModifierMultiplier;
        }

        double replayCount = 1d + state.SwordSageSources.Sum(source => source.Amount);
        return SovereignBladeTargetMultiplier(card, state) * replayCount;
    }

    private static double AddConquerorPowerCredits(
        List<CardValueCreditEvent>? credits,
        SimulationState state,
        double doubledValue)
    {
        if (doubledValue <= 0d)
        {
            return 0d;
        }

        ActivePower? conqueror = state.ActivePowers.FirstOrDefault(power =>
            power.Kind == ActivePowerKind.Conqueror
            && power.Amount > 0d);
        if (conqueror is null)
        {
            return 0d;
        }

        credits?.Add(new CardValueCreditEvent(
            conqueror.SourceModelId,
            conqueror.SourceTypeName,
            0d,
            0d,
            doubledValue,
            0d,
            0d,
            CountsAsDirectPlay: false));
        return doubledValue;
    }

    private static double XCostDamageValue(SimulationCard card, SimulationState state)
    {
        int energy = XCostEnergy(card, state);
        return XCostDamageValue(card, state, energy);
    }

    private static double XCostDamageValue(SimulationCard card, SimulationState state, int energy)
    {
        if (!HasXCostDamage(card))
        {
            return 0d;
        }

        if (energy <= 0)
        {
            return 0d;
        }

        return card.Actions
            .Where(IsXCostDamageAction)
            .Sum(action => ((double)(action.Amount ?? 0m))
                * XCostHitCount(card, energy)
                * (action.HitCount ?? 1)
                * ActionTargetDamageMultiplier(card, action)
                * card.DamageUnitValue);
    }

    private static double XCostDamageModifierMultiplier(SimulationCard card, SimulationState state)
    {
        int energy = XCostEnergy(card, state);
        return XCostDamageModifierMultiplier(card, state, energy);
    }

    private static double XCostDamageModifierMultiplier(SimulationCard card, SimulationState state, int energy)
    {
        if (!HasXCostDamage(card))
        {
            return 0d;
        }

        if (energy <= 0)
        {
            return 0d;
        }

        return card.Actions
            .Where(IsXCostDamageAction)
            .Sum(action => XCostHitCount(card, energy)
                * (action.HitCount ?? 1)
                * ActionTargetDamageMultiplier(card, action));
    }

    private static int XCostEnergy(SimulationCard card, SimulationState state)
    {
        return HasXCostDamage(card) ? Math.Max(0, state.Energy) : 0;
    }

    private static double DynamicScalingDamageValue(
        SimulationCard card,
        SimulationState state,
        bool includePlayedCardIfMissing)
    {
        if (card.ScalingDamageKind is null || card.ScalingDamagePerUnit <= 0d)
        {
            return 0d;
        }

        double multiplier = card.ScalingDamageKind switch
        {
            "starCostCardCount" => StarCostCardCount(state)
                + (includePlayedCardIfMissing && HasAnyStarCost(card) ? 1 : 0),
            "cardsPlayedThisCombat" => state.CardsPlayedThisCombat,
            "drawPileCount" => state.DrawPile.Count,
            "generatedCardsCreated" => state.GeneratedCardsCreated,
            // LunarBlast: hits = Skill cards played this turn (before this attack).
            "skillsPlayedThisTurn" => state.SkillsPlayedThisTurn,
            // Radiate: hits = stars gained this turn, including this card's own star gain.
            "starsGainedThisTurn" => state.StarsGainedThisTurn
                + (includePlayedCardIfMissing ? card.StarGain : 0),
            _ => 0d
        };
        if (multiplier <= 0d)
        {
            return 0d;
        }

        return card.ScalingDamagePerUnit
            * multiplier
            * card.ScalingDamageTargetMultiplier
            * card.DamageUnitValue;
    }

    private static int StarCostCardCount(SimulationState state)
    {
        int count = 0;
        foreach (DeckCardInstance instance in state.DrawPile)
        {
            if (HasAnyStarCost(instance.Card)) { count++; }
        }

        foreach (DeckCardInstance instance in state.Hand)
        {
            if (HasAnyStarCost(instance.Card)) { count++; }
        }

        foreach (DeckCardInstance instance in state.DiscardPile)
        {
            if (HasAnyStarCost(instance.Card)) { count++; }
        }

        foreach (DeckCardInstance instance in state.ExhaustPile)
        {
            if (HasAnyStarCost(instance.Card)) { count++; }
        }

        return count;
    }

    private static bool HasAnyStarCost(SimulationCard card)
    {
        return card.HasExplicitStarCost || card.HasStarCostX;
    }

    private static int XCostHitCount(SimulationCard card, int energy)
    {
        if (energy <= 0)
        {
            return 0;
        }

        int hitCount = energy;
        if (IsHeavenlyDrill(card) && energy >= 4)
        {
            hitCount *= 2;
        }

        return hitCount;
    }

    private static double ActionTargetDamageMultiplier(SimulationCard card, CardActionFact action)
    {
        return action.TargetType switch
        {
            "AllEnemies" => card.AoeDamageMultiplier,
            _ => 1d
        };
    }

    // P8: HasXCostDamage / ReflectApproximationValue / StrengthLossDefenseValue / HpLossPenaltyValue
    // are pure functions of the immutable SimulationCard (they only scan card.Actions and read card
    // fields), yet the play-search re-evaluates them for every candidate card at every search node -
    // millions of times per estimation, each allocating a LINQ iterator. Memoize the derived values
    // once per SimulationCard via a weak-keyed table (mirrors GeneratedPoolCandidateCache); the cache
    // is read-only for callers and output-identical to the original scans. Weak keys let generated /
    // transformed card instances be collected normally (no leak, no unbounded growth).
    private sealed record CardDerivedData(
        bool HasXCostDamage,
        double ReflectApproximationValue,
        double StrengthLossDefenseValue,
        double HpLossPenaltyValue);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SimulationCard, CardDerivedData> CardDerivedCache = new();

    private static CardDerivedData GetCardDerived(SimulationCard card)
    {
        return CardDerivedCache.GetValue(card, static c => new CardDerivedData(
            c.Actions.Any(IsXCostDamageAction),
            ComputeReflectApproximationValue(c),
            ComputeStrengthLossDefenseValue(c),
            ComputeHpLossPenaltyValue(c)));
    }

    private static double ReflectApproximationValue(SimulationCard card)
    {
        return GetCardDerived(card).ReflectApproximationValue;
    }

    private static double ComputeReflectApproximationValue(SimulationCard card)
    {
        return HasPowerAction(card, "Reflect")
            ? card.BaseBlock * card.DamageUnitValue
            : 0d;
    }

    private static double StrengthLossDefenseValue(SimulationCard card)
    {
        return GetCardDerived(card).StrengthLossDefenseValue;
    }

    private static double ComputeStrengthLossDefenseValue(SimulationCard card)
    {
        return card.Actions
            .Where(action => action.Kind == "power")
            .Where(action => PowerKey(action.Parameter) is "DyingStar" or "CrushUnder" or "DarkShackles")
            .Sum(action => ((double)(action.Amount ?? 0m)) * 1.2d * card.DamageUnitValue);
    }

    private static double HpLossPenaltyValue(SimulationCard card)
    {
        return GetCardDerived(card).HpLossPenaltyValue;
    }

    private static double ComputeHpLossPenaltyValue(SimulationCard card)
    {
        return card.Actions
            .Where(action => action.Kind == "hpLoss")
            .Sum(action => -((double)(action.Amount ?? 0m)) * 1.5d);
    }

    private static double FrailBlockPenaltyValue(SimulationCard card, SimulationState state)
    {
        if (state.PlayerFrail <= 0)
        {
            return 0d;
        }

        double lostBlock = card.Actions
            .Where(action => action.Kind == "block")
            .Select(action => (double)(action.Amount ?? 0m))
            .Sum(amount => amount - Math.Floor(amount * 0.75d));
        if (lostBlock <= 0d && card.BaseBlock > 0d)
        {
            lostBlock = card.BaseBlock - Math.Floor(card.BaseBlock * 0.75d);
        }

        return -lostBlock * card.BlockValuePerBlock;
    }

    private static double SovereignBladeTargetMultiplier(SimulationCard card, SimulationState state)
    {
        return state.SeekingEdgeSources.Count > 0
            ? card.AoeDamageMultiplier
            : 1d;
    }

    private static double AddPowerModifierCredits(
        List<CardValueCreditEvent>? credits,
        IReadOnlyList<ResourceSourceCredit> sources,
        double valuePerAmount)
    {
        double total = 0d;
        foreach (ResourceSourceCredit source in sources)
        {
            double value = source.Amount * valuePerAmount;
            if (value == 0d)
            {
                continue;
            }

            total += value;
            credits?.Add(new CardValueCreditEvent(
                source.SourceModelId,
                source.SourceTypeName,
                0d,
                0d,
                value,
                0d,
                0d,
                CountsAsDirectPlay: false));
        }

        return total;
    }

    private static double VulnerableBonus(double damageValue, SimulationState state)
    {
        if (state.EnemyVulnerable <= 0 || damageValue <= 0d)
        {
            return 0d;
        }

        return Math.Floor(damageValue * 0.5d);
    }

    private static double AddVulnerableSourceCredits(
        List<CardValueCreditEvent>? credits,
        SimulationState state,
        double vulnerableBonus)
    {
        if (vulnerableBonus <= 0d || state.EnemyVulnerableSources.Count == 0)
        {
            return 0d;
        }

        ResourceSourceCredit source = state.EnemyVulnerableSources.First(source => source.Amount > 0d);
        credits?.Add(new CardValueCreditEvent(
            source.SourceModelId,
            source.SourceTypeName,
            0d,
            0d,
            vulnerableBonus,
            0d,
            0d,
            CountsAsDirectPlay: false));
        return vulnerableBonus;
    }

    private static IReadOnlyList<CardValueCreditEvent> BuildValueCredits(
        DeckCardInstance card,
        double directValue,
        IReadOnlyList<CardValueCreditEvent> powerCredits,
        IReadOnlyList<CardValueCreditEvent> starCredits)
    {
        List<CardValueCreditEvent> credits = [];
        double forgeRealizedValue = 0d;
        foreach (ForgeSourceCredit forgeCredit in card.ForgeCredits)
        {
            forgeRealizedValue += forgeCredit.Amount;
            credits.Add(new CardValueCreditEvent(
                forgeCredit.SourceModelId,
                forgeCredit.SourceTypeName,
                0d,
                forgeCredit.Amount,
                0d,
                0d,
                0d,
                CountsAsDirectPlay: false));
        }

        credits.Insert(0, new CardValueCreditEvent(
            SourceModelId(card.Card),
            SourceTypeName(card.Card),
            directValue - forgeRealizedValue,
            0d,
            0d,
            0d,
            0d,
            CountsAsDirectPlay: true));
        credits.AddRange(powerCredits);
        credits.AddRange(starCredits);
        return credits;
    }

    private static IReadOnlyList<CardValueCreditEvent> PowerCredits(IReadOnlyList<PowerResolution> resolutions)
    {
        return resolutions
            .Where(resolution => resolution.Value != 0d)
            .Select(resolution => new CardValueCreditEvent(
                resolution.SourceModelId,
                resolution.SourceTypeName,
                0d,
                0d,
                resolution.Value,
                0d,
                0d,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> DelayedDirectCredits(IReadOnlyList<DelayedValueCredit> delayedCredits)
    {
        return delayedCredits
            .Where(credit => credit.Value != 0d)
            .Select(credit => new CardValueCreditEvent(
                credit.SourceModelId,
                credit.SourceTypeName,
                credit.Value,
                0d,
                0d,
                0d,
                0d,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> EnergyCredits(
        IReadOnlyList<ResourceSourceCredit> energySources,
        double turnPlayedValue,
        int baseEnergy,
        int actualEnergySpent)
    {
        double totalExtraEnergy = energySources.Sum(source => source.Amount);
        int extraEnergyNeeded = actualEnergySpent - baseEnergy;
        if (energySources.Count == 0
            || totalExtraEnergy <= 0d
            || extraEnergyNeeded <= 0
            || actualEnergySpent <= 0
            || turnPlayedValue <= 0d)
        {
            return [];
        }

        double usefulExtraEnergy = Math.Min(totalExtraEnergy, extraEnergyNeeded);
        double totalEnergyCredit = turnPlayedValue * usefulExtraEnergy / actualEnergySpent;
        return energySources
            .GroupBy(source => (source.SourceModelId, source.SourceTypeName))
            .Select(group => new CardValueCreditEvent(
                group.Key.SourceModelId,
                group.Key.SourceTypeName,
                0d,
                0d,
                0d,
                totalEnergyCredit * group.Sum(source => source.Amount) / totalExtraEnergy,
                0d,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> StarSpendCredits(
        IReadOnlyList<ResourceSourceCredit> consumedSources,
        double attributedValue,
        int totalStarsSpent)
    {
        if (consumedSources.Count == 0 || attributedValue <= 0d || totalStarsSpent <= 0)
        {
            return [];
        }

        return consumedSources
            .GroupBy(source => (source.SourceModelId, source.SourceTypeName))
            .Select(group => new CardValueCreditEvent(
                group.Key.SourceModelId,
                group.Key.SourceTypeName,
                0d,
                0d,
                0d,
                0d,
                attributedValue * group.Sum(source => source.Amount) / totalStarsSpent,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> StarTriggerCredits(
        IReadOnlyList<ResourceSourceCredit> triggerSources,
        double triggeredValue)
    {
        double totalSourceStars = triggerSources.Sum(source => source.Amount);
        if (triggerSources.Count == 0 || triggeredValue <= 0d || totalSourceStars <= 0d)
        {
            return [];
        }

        return triggerSources
            .GroupBy(source => (source.SourceModelId, source.SourceTypeName))
            .Select(group => new CardValueCreditEvent(
                group.Key.SourceModelId,
                group.Key.SourceTypeName,
                0d,
                0d,
                0d,
                0d,
                triggeredValue * group.Sum(source => source.Amount) / totalSourceStars,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<ResourceSourceCredit> ConsumeAttributableStars(SimulationState state, int amount)
    {
        if (amount <= 0)
        {
            return [];
        }

        int baseStarsConsumed = Math.Min(state.BaseStarsRemaining, amount);
        state.BaseStarsRemaining -= baseStarsConsumed;
        double attributableAmount = amount - baseStarsConsumed;
        if (attributableAmount <= 0d || !state.TrackAttributionSources)
        {
            return [];
        }

        return ConsumeSourceAmounts(state.StarSources, attributableAmount);
    }

    private static IReadOnlyList<ResourceSourceCredit> ConsumeSourceAmounts(
        List<ResourceSourceCredit> sources,
        double amount)
    {
        double available = sources.Sum(source => source.Amount);
        double consumedTotal = Math.Min(amount, available);
        if (consumedTotal <= 0d || available <= 0d)
        {
            return [];
        }

        List<ResourceSourceCredit> consumed = [];
        for (int i = 0; i < sources.Count; i++)
        {
            ResourceSourceCredit source = sources[i];
            double sourceConsumed = consumedTotal * source.Amount / available;
            if (sourceConsumed <= 0d)
            {
                continue;
            }

            consumed.Add(source with { Amount = sourceConsumed });
            sources[i] = source with { Amount = source.Amount - sourceConsumed };
        }

        sources.RemoveAll(source => source.Amount <= 0.000001d);
        return consumed;
    }

    private static void ApplyEnemyVulnerable(SimulationState state, SimulationCard sourceCard)
    {
        if (sourceCard.Vulnerable <= 0)
        {
            return;
        }

        state.EnemyVulnerable += sourceCard.Vulnerable;
        if (state.TrackAttributionSources)
        {
            state.EnemyVulnerableSources.Add(new ResourceSourceCredit(
                SourceModelId(sourceCard),
                SourceTypeName(sourceCard),
                sourceCard.Vulnerable));
        }
    }

    private static void ExpireEnemyVulnerable(SimulationState state)
    {
        if (state.EnemyVulnerable <= 0)
        {
            state.EnemyVulnerableSources.Clear();
            return;
        }

        state.EnemyVulnerable = Math.Max(0, state.EnemyVulnerable - 1);
        ConsumeEnemyVulnerableDuration(state.EnemyVulnerableSources, 1d);
        if (state.EnemyVulnerable == 0)
        {
            state.EnemyVulnerableSources.Clear();
        }
    }

    private static void ConsumeEnemyVulnerableDuration(List<ResourceSourceCredit> sources, double amount)
    {
        double remaining = amount;
        while (remaining > 0d && sources.Count > 0)
        {
            ResourceSourceCredit source = sources[0];
            double consumed = Math.Min(remaining, source.Amount);
            remaining -= consumed;
            double nextAmount = source.Amount - consumed;
            if (nextAmount <= 0d)
            {
                sources.RemoveAt(0);
            }
            else
            {
                sources[0] = source with { Amount = nextAmount };
            }
        }
    }

    private static IReadOnlyList<PowerResolution> DispatchPowerEvent(SimulationState state, SimulationEvent simulationEvent)
    {
        if (state.ActivePowers.Count == 0)
        {
            return [];
        }

        List<PowerResolution>? resolutions = null;
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Behavior is not null)
            {
                IReadOnlyList<PowerResolution> resolved = power.Behavior.Resolve(simulationEvent, power);
                if (resolved.Count > 0)
                {
                    (resolutions ??= []).AddRange(resolved);
                }
            }
        }

        return resolutions ?? (IReadOnlyList<PowerResolution>)[];
    }

    private static void RecordActivePowerExposures(
        SimulationState state,
        DeckSimulationOptions options)
    {
        SearchTurnProfile? profile = options.ActiveSearchTurnProfile;
        if (profile is null)
        {
            return;
        }

        foreach (ActivePower power in state.ActivePowers)
        {
            profile.RecordPowerExposure(power.Kind.ToString());
        }
    }

    private static PowerEventResult ResolveTurnStartPowers(SimulationState state)
    {
        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
        foreach (ActivePower power in state.ActivePowers)
        {
            switch (power.Kind)
            {
                case ActivePowerKind.Furnace:
                    PowerEventResult forgeResult = ApplyForgeFromPower(state, (int)power.Amount, power);
                    resolutions.AddRange(forgeResult.PowerResolutions);
                    credits.AddRange(forgeResult.ValueCredits);
                    break;
                case ActivePowerKind.Genesis:
                    GainStarsFromPower(state, (int)power.Amount, power, resolutions, credits);
                    break;
                case ActivePowerKind.Orbit:
                    break;
                case ActivePowerKind.Plating:
                    if (power.Counter > 0 && power.Amount > 0d)
                    {
                        power.Amount -= 1d;
                    }
                    break;
                case ActivePowerKind.PrepTime:
                    state.VigorSources.Add(new ResourceSourceCredit(power.SourceModelId, power.SourceTypeName, power.Amount));
                    break;
                case ActivePowerKind.RollingBoulder:
                    if (power.Amount > 0d)
                    {
                        double value = power.Amount * power.SourceCard.DamageUnitValue * power.SourceCard.AoeDamageMultiplier;
                        resolutions.Add(new PowerResolution(power.SourceModelId, power.SourceTypeName, value));
                        power.Amount += power.SecondaryAmount;
                    }
                    break;
                case ActivePowerKind.VoidForm:
                    power.Counter = 0;
                    break;
            }
        }

        return new PowerEventResult(resolutions, credits);
    }

    private static PowerEventResult ResolveBeforeHandDrawPowers(
        SimulationState state,
        DeckSimulationOptions options,
        FastRandom rng)
    {
        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
        double additionalValue = 0d;
        int powerCount = state.ActivePowers.Count;
        for (int index = 0; index < powerCount; index++)
        {
            ActivePower power = state.ActivePowers[index];
            switch (power.Kind)
            {
                case ActivePowerKind.Mayhem:
                    FreePlayResult mayhemResult = ResolveMayhemAutoPlays(state, (int)power.Amount, rng, options);
                    additionalValue += mayhemResult.Value;
                    credits.AddRange(mayhemResult.Credits);
                    break;
                case ActivePowerKind.SpectrumShift:
                    PowerEventResult spectrumResult = GenerateCardsToHandFromGeneratedPool(
                        state,
                        options,
                        "spectrumShift.colorless",
                        (int)power.Amount,
                        distinct: true,
                        upgradeGenerated: false);
                    resolutions.AddRange(spectrumResult.PowerResolutions);
                    credits.AddRange(spectrumResult.ValueCredits);
                    break;
            }
        }

        return new PowerEventResult(resolutions, credits, additionalValue);
    }

    private static FreePlayResult ResolveMayhemAutoPlays(
        SimulationState state,
        int count,
        FastRandom rng,
        DeckSimulationOptions options)
    {
        if (count <= 0)
        {
            return FreePlayResult.Empty;
        }

        List<DeckCardInstance> selected = [];
        for (int index = 0; index < count; index++)
        {
            ShuffleDrawPileIfNecessary(state, rng);
            if (state.DrawPile.Count == 0)
            {
                break;
            }

            DeckCardInstance card = state.DrawPile[0];
            state.DrawPile.RemoveAt(0);
            selected.Add(card);
        }

        if (selected.Count == 0)
        {
            return FreePlayResult.Empty;
        }

        bool collect = options.CollectAttribution;
        double total = 0d;
        List<CardValueCreditEvent>? credits = collect ? [] : null;
        foreach (DeckCardInstance instance in selected)
        {
            if (instance.Card.Unplayable)
            {
                MovePlayedCardToResultPile(
                    state,
                    instance,
                    instance.Card,
                    transformedPlayedCard: null,
                    attackSkillPlaysBeforePlay: state.AttacksPlayedThisTurn + state.SkillsPlayedThisTurn);
                continue;
            }

            FreePlayResult result = ResolveFreeCardPlay(state, instance, rng, options, depth: 0);
            total += result.Value;
            credits?.AddRange(result.Credits);
            MovePlayedCardToResultPile(
                state,
                instance,
                instance.Card,
                transformedPlayedCard: null,
                attackSkillPlaysBeforePlay: result.AttackSkillPlaysBeforePlay);
        }

        return new FreePlayResult(total, credits ?? (IReadOnlyList<CardValueCreditEvent>)[], CardsPlayed: selected.Count);
    }

    private static FreePlayResult ResolveImbuedAutoPlays(
        SimulationState state,
        FastRandom rng,
        DeckSimulationOptions options)
    {
        List<DeckCardInstance> selected = state.DrawPile
            .Where(instance => HasEnchantment(instance.Card, "IMBUED"))
            .OrderBy(instance => instance.InstanceId)
            .ToList();
        if (selected.Count == 0)
        {
            return FreePlayResult.Empty;
        }

        bool collect = options.CollectAttribution;
        double total = 0d;
        List<CardValueCreditEvent>? credits = collect ? [] : null;
        foreach (DeckCardInstance instance in selected)
        {
            state.DrawPile.Remove(instance);
            FreePlayResult result = ResolveFreeCardPlay(state, instance, rng, options, depth: 0);
            total += result.Value;
            credits?.AddRange(result.Credits);
            MovePlayedCardToResultPile(
                state,
                instance,
                instance.Card,
                transformedPlayedCard: null,
                attackSkillPlaysBeforePlay: result.AttackSkillPlaysBeforePlay);
        }

        return new FreePlayResult(total, credits ?? (IReadOnlyList<CardValueCreditEvent>)[], CardsPlayed: selected.Count);
    }

    private static PowerEventResult ResolveBeforeCardPlayedPowers(SimulationState state)
    {
        // P8: fires per card play. Result is almost always empty (no TheSealedThrone power). Skip the
        // Where iterator + the two eager List allocations; allocate only on the first matching power.
        List<PowerResolution>? resolutions = null;
        List<CardValueCreditEvent>? credits = null;
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind != ActivePowerKind.TheSealedThrone)
            {
                continue;
            }

            resolutions ??= [];
            credits ??= [];
            GainStarsFromPower(state, (int)power.Amount, power, resolutions, credits);
        }

        return resolutions is null ? PowerEventResult.Empty : new PowerEventResult(resolutions, credits!);
    }

    private static int HandDrawBonus(SimulationState state)
    {
        int bonus = 0;
        foreach (ActivePower power in state.ActivePowers)
        {
            switch (power.Kind)
            {
                case ActivePowerKind.Tyranny:
                    bonus += (int)power.Amount;
                    break;
                case ActivePowerKind.PaleBlueDot:
                    if (state.LastTurnCardsPlayed >= 5)
                    {
                        bonus += (int)power.Amount;
                    }
                    break;
            }
        }

        return bonus;
    }

    private static PowerEventResult ResolveAfterPlayerTurnStartPowers(
        SimulationState state,
        DeckSimulationOptions options,
        FastRandom rng)
    {
        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.Tyranny))
        {
            ExhaustLowestValueCardsFromHand(state, (int)power.Amount);
        }

        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.Entropy))
        {
            TransformLowestValueCardsFromGeneratedPool(
                state,
                options,
                rng,
                "entropy.sunStrike",
                (int)power.Amount,
                upgradeGenerated: false);
        }

        return PowerEventResult.Empty;
    }

    private static PowerEventResult ResolveTurnEndPowers(SimulationState state)
    {
        List<PowerResolution>? resolutions = null;
        foreach (ActivePower power in state.ActivePowers)
        {
            switch (power.Kind)
            {
                case ActivePowerKind.Plating:
                    if (power.Amount > 0d)
                    {
                        (resolutions ??= []).Add(new PowerResolution(
                            power.SourceModelId,
                            power.SourceTypeName,
                            power.Amount * power.SourceCard.BlockValuePerBlock));
                        power.Counter = 1;
                    }
                    break;
                case ActivePowerKind.Panache:
                    power.Counter = 0;
                    break;
                case ActivePowerKind.Thorns:
                    (resolutions ??= []).Add(new PowerResolution(
                        power.SourceModelId,
                        power.SourceTypeName,
                        power.Amount * power.SourceCard.AoeDamageMultiplier * power.SourceCard.DamageUnitValue));
                    break;
                case ActivePowerKind.TheBomb:
                    if (power.Counter > 1)
                    {
                        power.Counter--;
                    }
                    else
                    {
                        (resolutions ??= []).Add(new PowerResolution(
                            power.SourceModelId,
                            power.SourceTypeName,
                            power.Amount * power.SourceCard.AoeDamageMultiplier * power.SourceCard.DamageUnitValue));
                        power.Counter = 0;
                    }
                    break;
            }
        }

        state.ActivePowers.RemoveAll(power => power.Kind == ActivePowerKind.TheBomb && power.Counter <= 0);
        return resolutions is null
            ? PowerEventResult.Empty
            : new PowerEventResult(resolutions, []);
    }

    private static double FiniteHorizonLeafDecisionValue(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon)
    {
        double currentTurnEndValue = EstimateCurrentTurnEndPowerValue(state);
        double queuedNextTurnValue = 0d;
        if (horizon.HasFutureTurn)
        {
            ExplicitResourceReferenceValues resourceValues = ResourceReferenceValuesForTurns(horizon.FutureTurns);
            queuedNextTurnValue =
                (state.NextTurnDraw * resourceValues.Draw * NextTurnExplicitResourceReferenceMultiplier)
                + (state.NextTurnEnergy * resourceValues.Energy * NextTurnExplicitResourceReferenceMultiplier)
                + (state.NextTurnStars * resourceValues.Star * NextTurnExplicitResourceReferenceMultiplier)
                + state.NextTurnBlockDecisionValue;
        }

        return currentTurnEndValue
            + queuedNextTurnValue
            + EstimatePersistentContinuationValue(state, options, horizon);
    }

    private static double EstimateCurrentTurnEndPowerValue(SimulationState state)
    {
        double value = 0d;
        foreach (ActivePower power in state.ActivePowers)
        {
            value += power.Kind switch
            {
                ActivePowerKind.Plating when power.Amount > 0d =>
                    power.Amount * power.SourceCard.BlockValuePerBlock,
                ActivePowerKind.Thorns =>
                    power.Amount * power.SourceCard.AoeDamageMultiplier * power.SourceCard.DamageUnitValue,
                ActivePowerKind.TheBomb when power.Counter <= 1 =>
                    power.Amount * power.SourceCard.AoeDamageMultiplier * power.SourceCard.DamageUnitValue,
                _ => 0d
            };
        }

        return value;
    }

    /// <summary>
    /// Phase-1 finite-horizon leaf evaluator. It prices only persistent mechanics already represented
    /// by simulator state. It is deliberately analytic and allocation-bounded: no recursive future
    /// search and no learned state value. Phase 2 can replace this with V(state, remainingTurns)
    /// without changing Power admission or terminal semantics.
    /// </summary>
    private static double EstimatePersistentContinuationValue(
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon)
    {
        int futureTurns = horizon.FutureTurns;
        if (futureTurns <= 0)
        {
            return 0d;
        }

        if (state.ActivePowers.Count == 0
            && state.StrengthSources.Count == 0
            && state.DexteritySources.Count == 0
            && state.FastenSources.Count == 0
            && state.ParrySources.Count == 0
            && state.SeekingEdgeSources.Count == 0
            && state.SwordSageSources.Count == 0
            && state.EnemyVulnerable <= 1)
        {
            return 0d;
        }

        ExplicitResourceReferenceValues resourceValues = ResourceReferenceValuesForTurns(futureTurns);
        FutureTurnOpportunityProfile profile = BuildFutureTurnOpportunityProfile(state, options);
        double generatedAttackStrengthValuePerPoint = profile.GeneratedAttackStrengthValuePerPoint > 0d
            ? profile.GeneratedAttackStrengthValuePerPoint
            : profile.StrengthValuePerPoint;
        double genesisStarsPerTurn = 0d;
        double generatedCardsPerTurn = profile.ExpectedGeneratedCards;
        foreach (ActivePower power in state.ActivePowers)
        {
            switch (power.Kind)
            {
                case ActivePowerKind.Genesis:
                    genesisStarsPerTurn += Math.Max(0d, power.Amount);
                    break;
                case ActivePowerKind.Calamity:
                    generatedCardsPerTurn += Math.Max(0d, power.Amount) * profile.ExpectedAttacksPlayed;
                    break;
                case ActivePowerKind.Entropy:
                case ActivePowerKind.SpectrumShift:
                    generatedCardsPerTurn += Math.Max(0d, power.Amount);
                    break;
            }
        }

        FutureTurnOpportunityProfile triggeredProfile = profile;
        if (genesisStarsPerTurn > 0d
            || state.NextTurnStars > 0
            || generatedCardsPerTurn != profile.ExpectedGeneratedCards)
        {
            triggeredProfile = profile with
            {
                ExpectedStarsGained = profile.ExpectedStarsGained
                    + genesisStarsPerTurn
                    + (state.NextTurnStars / (double)futureTurns),
                ExpectedStarGainEvents = profile.ExpectedStarGainEvents
                    + (genesisStarsPerTurn > 0d ? 1d : 0d)
                    + (state.NextTurnStars > 0 ? 1d / futureTurns : 0d),
                ExpectedGeneratedCards = generatedCardsPerTurn
            };
        }

        double persistentStrength = SumSources(state.StrengthSources);
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind == ActivePowerKind.Monologue && power.Counter > 0)
            {
                persistentStrength -= power.Counter;
            }
        }

        double perTurnModifierValue =
            (Math.Max(0d, persistentStrength) * profile.StrengthValuePerPoint)
            + (Math.Max(0d, SumSources(state.DexteritySources)) * profile.DexterityValuePerPoint)
            + (Math.Max(0d, SumSources(state.FastenSources)) * profile.FastenValuePerPoint)
            + (Math.Max(0d, SumSources(state.ParrySources)) * profile.ParryValuePerPoint)
            + (Math.Max(0d, SumSources(state.SeekingEdgeSources)) * profile.SeekingEdgeValuePerPoint)
            + (Math.Max(0d, SumSources(state.SwordSageSources)) * profile.SwordSageValuePerPoint);
        double value = perTurnModifierValue * futureTurns;
        int vulnerableFutureTurns = Math.Min(futureTurns, Math.Max(0, state.EnemyVulnerable - 1));
        value += profile.ExpectedAttackDirectValue * 0.5d * vulnerableFutureTurns;

        foreach (ActivePower power in state.ActivePowers)
        {
            double amount = Math.Max(0d, power.Amount);
            switch (power.Kind)
            {
                case ActivePowerKind.Persistent:
                    value += (power.Behavior?.EstimateFutureTurnValue(triggeredProfile, power) ?? 0d)
                        * futureTurns;
                    break;
                case ActivePowerKind.Arsenal:
                    value += generatedCardsPerTurn
                        * amount
                        * generatedAttackStrengthValuePerPoint
                        * futureTurns
                        * (futureTurns + 1d)
                        / 2d;
                    break;
                case ActivePowerKind.Automation:
                    value += profile.ExpectedDraws * amount * resourceValues.Energy * futureTurns / 10d;
                    break;
                case ActivePowerKind.Calamity:
                    value += amount
                        * profile.ExpectedAttacksPlayed
                        * profile.GeneratedCardValue
                        * futureTurns;
                    break;
                case ActivePowerKind.Conqueror:
                    value += profile.SovereignBladeDirectValue
                        * Math.Min(futureTurns, Math.Max(0d, amount - 1d));
                    break;
                case ActivePowerKind.Entropy:
                    value += amount * profile.GeneratedCardValue * 0.35d * futureTurns;
                    break;
                case ActivePowerKind.Furnace:
                    value += amount * profile.ValuedSovereignBladeCount * futureTurns;
                    break;
                case ActivePowerKind.Genesis:
                    value += amount * resourceValues.Star * futureTurns;
                    break;
                case ActivePowerKind.Mayhem:
                    value += amount * profile.AveragePlayableCardValue * futureTurns;
                    break;
                case ActivePowerKind.Nostalgia:
                    value += Math.Min(amount, profile.ExpectedAttacksPlayed + profile.ExpectedSkillsPlayed)
                        * profile.AveragePlayableCardValue
                        * 0.35d
                        * futureTurns;
                    break;
                case ActivePowerKind.Orbit:
                    value += profile.ExpectedEnergySpent * amount * resourceValues.Energy * futureTurns / 4d;
                    break;
                case ActivePowerKind.PaleBlueDot:
                    if (profile.ExpectedCardsPlayed >= 5d)
                    {
                        value += amount * resourceValues.Draw * futureTurns;
                    }
                    break;
                case ActivePowerKind.Panache:
                    value += profile.ExpectedCardsPlayed
                        * amount
                        * power.SourceCard.DamageUnitValue
                        * power.SourceCard.AoeDamageMultiplier
                        * futureTurns
                        / 5d;
                    break;
                case ActivePowerKind.PillarOfCreation:
                    value += generatedCardsPerTurn
                        * amount
                        * power.SourceCard.BlockValuePerBlock
                        * futureTurns;
                    break;
                case ActivePowerKind.Plating:
                    for (int offset = 1; offset <= futureTurns; offset++)
                    {
                        value += Math.Max(0d, amount - offset) * power.SourceCard.BlockValuePerBlock;
                    }
                    break;
                case ActivePowerKind.PrepTime:
                    value += amount * profile.StrengthValuePerPoint * futureTurns;
                    break;
                case ActivePowerKind.RollingBoulder:
                    value += ((amount * futureTurns)
                        + (power.SecondaryAmount * futureTurns * (futureTurns - 1d) / 2d))
                        * power.SourceCard.DamageUnitValue
                        * power.SourceCard.AoeDamageMultiplier;
                    break;
                case ActivePowerKind.SpectrumShift:
                    value += amount * profile.GeneratedCardValue * futureTurns;
                    break;
                case ActivePowerKind.Stratagem:
                    value += amount
                        * profile.AveragePlayableCardValue
                        * Math.Min(1d, profile.ExpectedDraws / Math.Max(1d, profile.ActiveCardCount))
                        * futureTurns;
                    break;
                case ActivePowerKind.TheSealedThrone:
                    value += Math.Min(amount, profile.ExpectedStarsSpent) * resourceValues.Star * futureTurns;
                    break;
                case ActivePowerKind.TheBomb:
                    int remainingCounter = power.Counter - 1;
                    if (remainingCounter > 0 && remainingCounter <= futureTurns)
                    {
                        value += amount
                            * power.SourceCard.DamageUnitValue
                            * power.SourceCard.AoeDamageMultiplier;
                    }
                    break;
                case ActivePowerKind.Thorns:
                    value += amount
                        * power.SourceCard.DamageUnitValue
                        * power.SourceCard.AoeDamageMultiplier
                        * futureTurns;
                    break;
                case ActivePowerKind.Tyranny:
                    value += amount
                        * Math.Max(0d, resourceValues.Draw - (profile.LowestPlayableCardValue * 0.25d))
                        * futureTurns;
                    break;
                case ActivePowerKind.VoidForm:
                    value += Math.Min(amount, profile.ExpectedCardsPlayed)
                        * profile.AverageEnergyCost
                        * resourceValues.Energy
                        * futureTurns;
                    break;
            }
        }

        return value;
    }

    private static FutureTurnOpportunityProfile BuildFutureTurnOpportunityProfile(
        SimulationState state,
        DeckSimulationOptions options)
    {
        int handDrawBonus = HandDrawBonus(state);
        bool hasSeekingEdge = state.SeekingEdgeSources.Count > 0;
        if (state.TryGetFutureTurnOpportunityProfile(
                state.NextTurnEnergy,
                handDrawBonus,
                hasSeekingEdge,
                out FutureTurnOpportunityProfile cached))
        {
            return cached;
        }

        GeneratedLibraryContinuationStats generatedLibrary = GeneratedLibraryContinuationCache.GetValue(
            options.CardLibrary,
            static library => BuildGeneratedLibraryContinuationStats(library));

        int cardCount = 0;
        double positiveCostTotal = 0d;
        foreach (DeckCardInstance instance in NonExhaustCards(state))
        {
            if (!instance.Card.IsPlayable)
            {
                continue;
            }

            cardCount++;
            positiveCostTotal += Math.Max(0, instance.Card.EnergyCost);
        }

        double expectedDraws = Math.Min(cardCount, Math.Max(0, options.HandSize + handDrawBonus));
        double drawProbability = cardCount == 0 ? 0d : expectedDraws / cardCount;
        double expectedPositiveCostDemand = drawProbability * positiveCostTotal;
        double positiveCostPlayScale = expectedPositiveCostDemand <= 0d
            ? 1d
            : Math.Min(1d, Math.Max(0d, options.BaseEnergy + state.NextTurnEnergy) / expectedPositiveCostDemand);

        double cardsPlayed = 0d;
        double attacks = 0d;
        double attackDirectValue = 0d;
        double skills = 0d;
        double starsSpent = 0d;
        double starSpendEvents = 0d;
        double starsGained = 0d;
        double starGainEvents = 0d;
        double energySpent = 0d;
        double energyCostTotal = 0d;
        double strengthPerPoint = 0d;
        double dexterityPerPoint = 0d;
        double fastenPerPoint = 0d;
        double parryPerPoint = 0d;
        double seekingEdgePerPoint = 0d;
        double swordSagePerPoint = 0d;
        double sovereignBladeDirectValue = 0d;
        double expectedGeneratedCards = 0d;
        double averageValueTotal = 0d;
        double lowestValue = double.PositiveInfinity;
        int bladeCount = 0;

        foreach (DeckCardInstance instance in NonExhaustCards(state))
        {
            SimulationCard card = instance.Card;
            if (!card.IsPlayable)
            {
                continue;
            }

            double playProbability = drawProbability * (card.EnergyCost > 0 ? positiveCostPlayScale : 1d);
            double cardValue = Math.Max(0d, Math.Max(card.IntrinsicValue, card.StaticEstimatedValue));
            cardsPlayed += playProbability;
            energySpent += playProbability * Math.Max(0, card.EnergyCost);
            energyCostTotal += playProbability * Math.Max(0, card.EnergyCost);
            averageValueTotal += cardValue;
            lowestValue = Math.Min(lowestValue, cardValue);
            if (card.IsAttack)
            {
                attacks += playProbability;
                attackDirectValue += playProbability * Math.Max(0d, card.DamageValue);
                strengthPerPoint += playProbability * Math.Max(1d, card.DamageModifierMultiplier) * card.DamageUnitValue;
            }
            else if (IsSkillCard(card))
            {
                skills += playProbability;
            }

            if (card.BaseBlock > 0d && card.BlockEffectCount > 0)
            {
                dexterityPerPoint += playProbability * card.BlockEffectCount * card.BlockValuePerBlock;
                if (card.HasTag("Defend"))
                {
                    fastenPerPoint += playProbability * card.BlockEffectCount * card.BlockValuePerBlock;
                }
            }

            if (card.StarCost > 0)
            {
                starsSpent += playProbability * card.StarCost;
                starSpendEvents += playProbability;
            }

            if (card.StarGain > 0)
            {
                starsGained += playProbability * card.StarGain;
                starGainEvents += playProbability;
            }

            expectedGeneratedCards += playProbability * card.Actions
                .Where(action => action.Kind is "createCard" or "createCardChoices" or "transformCard")
                .Sum(action => Math.Max(0d, (double)(action.Amount ?? 1m)));

            if (!IsSovereignBlade(card))
            {
                continue;
            }

            bladeCount++;
            double baseDamage = card.BaseDamage > 0d ? card.BaseDamage : card.DamageValue;
            double targetMultiplier = hasSeekingEdge ? card.AoeDamageMultiplier : 1d;
            parryPerPoint += playProbability * card.BlockValuePerBlock;
            seekingEdgePerPoint += playProbability * baseDamage * Math.Max(0d, targetMultiplier - 1d) * card.DamageUnitValue;
            swordSagePerPoint += playProbability * baseDamage * targetMultiplier * card.DamageUnitValue;
            sovereignBladeDirectValue += playProbability * Math.Max(0d, card.IntrinsicValue);
        }

        FutureTurnOpportunityProfile profile = new(
            cardCount,
            expectedDraws,
            cardsPlayed,
            attacks,
            attackDirectValue,
            skills,
            starsSpent,
            starSpendEvents,
            starsGained,
            starGainEvents,
            energySpent,
            cardsPlayed <= 0d ? 0d : energyCostTotal / cardsPlayed,
            cardCount == 0 ? 0d : averageValueTotal / cardCount,
            double.IsPositiveInfinity(lowestValue) ? 0d : lowestValue,
            generatedLibrary.GeneratedCardValue,
            generatedLibrary.AttackStrengthValuePerPoint,
            expectedGeneratedCards,
            strengthPerPoint,
            dexterityPerPoint,
            fastenPerPoint,
            parryPerPoint,
            seekingEdgePerPoint,
            swordSagePerPoint,
            sovereignBladeDirectValue,
            Math.Max(1, Math.Min(3, bladeCount)));
        state.CacheFutureTurnOpportunityProfile(
            state.NextTurnEnergy,
            handDrawBonus,
            hasSeekingEdge,
            profile);
        return profile;
    }

    private static GeneratedLibraryContinuationStats BuildGeneratedLibraryContinuationStats(
        IReadOnlyList<SimulationCard> library)
    {
        double[] topValues = new double[5];
        int topCount = 0;
        double attackStrengthTotal = 0d;
        int attackCount = 0;
        foreach (SimulationCard card in library)
        {
            if (!card.IsPlayable || card.IsPower)
            {
                continue;
            }

            double value = Math.Max(0d, Math.Max(card.IntrinsicValue, card.StaticEstimatedValue));
            int insert = 0;
            while (insert < topCount && topValues[insert] >= value)
            {
                insert++;
            }

            if (insert < topValues.Length)
            {
                int last = Math.Min(topCount, topValues.Length - 1);
                for (int index = last; index > insert; index--)
                {
                    topValues[index] = topValues[index - 1];
                }

                topValues[insert] = value;
                topCount = Math.Min(topValues.Length, topCount + 1);
            }

            if (card.IsAttack)
            {
                attackStrengthTotal += Math.Max(1d, card.DamageModifierMultiplier) * card.DamageUnitValue;
                attackCount++;
            }
        }

        double generatedValue = topCount == 0 ? 0d : topValues.Take(topCount).Average();
        return new GeneratedLibraryContinuationStats(
            generatedValue,
            attackCount == 0 ? 0d : attackStrengthTotal / attackCount);
    }

    private static PowerEventResult ResolveEnergySpentPowers(SimulationState state, int amount)
    {
        // P8: fires per energy-spending play; plain foreach + continue avoids the Where iterator alloc.
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind != ActivePowerKind.Orbit)
            {
                continue;
            }

            power.Counter += amount;
            int triggers = power.Counter / 4;
            if (triggers <= 0)
            {
                continue;
            }

            int energy = (int)power.Amount * triggers;
            state.Energy += energy;
            if (state.TrackAttributionSources)
            {
                state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                    power.SourceModelId,
                    power.SourceTypeName,
                    energy));
            }
            power.Counter %= 4;
        }

        return PowerEventResult.Empty;
    }

    private static PowerEventResult ResolveAfterCardPlayedPowers(
        SimulationState state,
        DeckCardInstance playedInstance,
        FastRandom rng,
        DeckSimulationOptions options)
    {
        SimulationCard playedCard = playedInstance.Card;
        // P8: fires per card play; the resolution/credit lists stay empty unless a Calamity/Panache
        // power actually produces value, so allocate them lazily. All per-power side effects
        // (counters, MutableStrengthSources) and the CardsPlayedThisTurn++ are preserved exactly.
        List<PowerResolution>? resolutions = null;
        List<CardValueCreditEvent>? credits = null;
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind == ActivePowerKind.VoidForm)
            {
                power.Counter++;
                continue;
            }

            if (power.Kind == ActivePowerKind.Calamity && playedCard.IsAttack)
            {
                PowerEventResult generated = GenerateCardsToHandFromGeneratedPool(
                    state,
                    options,
                    CalamityGeneratedPoolId(state),
                    (int)power.Amount,
                    distinct: false,
                    upgradeGenerated: false);
                (resolutions ??= []).AddRange(generated.PowerResolutions);
                (credits ??= []).AddRange(generated.ValueCredits);

                continue;
            }

            if (power.Kind == ActivePowerKind.Monologue)
            {
                if (power.SourceInstanceId != playedInstance.InstanceId && power.Amount > 0d)
                {
                    state.MutableStrengthSources.Add(new ResourceSourceCredit(
                        power.SourceModelId,
                        power.SourceTypeName,
                        power.Amount));
                    power.Counter += (int)Math.Round(power.Amount, MidpointRounding.AwayFromZero);
                }

                continue;
            }

            if (power.Kind != ActivePowerKind.Panache)
            {
                continue;
            }

            if (power.SourceInstanceId == playedInstance.InstanceId)
            {
                continue;
            }

            power.Counter++;
            if (power.Counter < 5)
            {
                continue;
            }

            double value = power.Amount * power.SourceCard.DamageUnitValue * power.SourceCard.AoeDamageMultiplier;
            (resolutions ??= []).Add(new PowerResolution(power.SourceModelId, power.SourceTypeName, value));
            power.Counter = 0;
        }

        state.CardsPlayedThisTurn++;
        return resolutions is null ? PowerEventResult.Empty : new PowerEventResult(resolutions, credits ?? []);
    }

    private static void ResolveCardDrawnPowers(SimulationState state)
    {
        // P8: fires per card drawn (frequent); plain foreach + continue avoids the Where iterator alloc.
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind != ActivePowerKind.Automation)
            {
                continue;
            }

            power.Counter--;
            if (power.Counter > 0)
            {
                continue;
            }

            int energy = (int)power.Amount;
            state.Energy += energy;
            if (state.TrackAttributionSources)
            {
                state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                    power.SourceModelId,
                    power.SourceTypeName,
                    energy));
            }
            power.Counter += 10;
        }
    }

    private static void GainStarsFromPower(
        SimulationState state,
        int amount,
        ActivePower power,
        List<PowerResolution> resolutions,
        List<CardValueCreditEvent> credits)
    {
        if (amount <= 0)
        {
            return;
        }

        state.Stars += amount;
        IReadOnlyList<PowerResolution> starGainedResolutions = DispatchPowerEvent(
            state,
            new SimulationEvent(SimulationEventKind.StarGained, amount));
        resolutions.AddRange(starGainedResolutions);
        if (state.TrackAttributionSources)
        {
            ResourceSourceCredit source = new(power.SourceModelId, power.SourceTypeName, amount);
            state.StarSources.Add(source);
            credits.AddRange(StarTriggerCredits([source], starGainedResolutions.Sum(resolution => resolution.Value)));
        }
    }

    private static void ExhaustLowestValueCardsFromHand(SimulationState state, int count)
    {
        if (count <= 0 || state.Hand.Count == 0)
        {
            return;
        }

        IReadOnlyList<DeckCardInstance> selected = state.Hand
            .OrderBy(card => CardObjectChoiceScore(card))
            .ThenBy(card => card.InstanceId)
            .Take(count)
            .ToArray();
        if (selected.Count > 0)
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }

        foreach (DeckCardInstance card in selected)
        {
            state.Hand.Remove(card);
            state.ExhaustPile.Add(card);
        }
    }

    private sealed record CardObjectActionResult(
        SimulationCard? TransformedSource,
        double DecisionValueAdjustment)
    {
        public static CardObjectActionResult Empty { get; } = new(null, 0d);
    }

    private sealed record CardObjectSearchContext(
        int Run,
        int Turn,
        int ActionsPlayed,
        int Seed);

    private sealed record CardObjectContinuation(
        double CurrentTurnValue,
        double NextTurnValue)
    {
        public double TotalValue => CurrentTurnValue + NextTurnValue;
    }

    private static CardObjectActionResult ResolveCardObjectActions(
        SimulationState state,
        DeckCardInstance source,
        FastRandom rng,
        DeckSimulationOptions options,
        List<CardMoveChoiceEvent>? moveChoices = null,
        List<CardTransformChoiceEvent>? transformChoices = null,
        CardObjectSearchContext? searchContext = null)
    {
        SimulationCard? transformedSource = null;
        double decisionValueAdjustment = 0d;
        CardObjectDecisionProfile? decisionProfile = CardBehaviorCatalog.ForCard(source.Card).CardObjectDecision;
        foreach (CardActionFact action in source.Card.Actions)
        {
            if (action.Kind == "moveCardBetweenPiles")
            {
                if (IsAnointedRareDrawToHand(source.Card, action))
                {
                    ResolveAnointedRareDrawToHand(state, rng);
                    continue;
                }

                decisionValueAdjustment += decisionProfile is null
                    ? ResolveMoveCardBetweenPiles(state, action)
                    : ResolveProfiledMoveCardBetweenPiles(
                        state,
                        source,
                        action,
                        decisionProfile,
                        options,
                        moveChoices,
                        searchContext);
            }
            else if (action.Kind == "transformCard")
            {
                CardObjectActionResult transformResult = decisionProfile is null
                    ? new CardObjectActionResult(
                        ResolveTransformCard(state, source, action, options, transformChoices),
                        0d)
                    : ResolveProfiledTransformCard(
                        state,
                        source,
                        action,
                        decisionProfile,
                        options,
                        transformChoices,
                        searchContext);
                transformedSource = transformResult.TransformedSource ?? transformedSource;
                decisionValueAdjustment += transformResult.DecisionValueAdjustment;
            }
        }

        if (CardBehaviorCatalog.Has(source.Card, CardBehaviorKind.PuritySelectiveExhaust))
        {
            ResolvePurityExhaust(state, source.Card);
        }

        return new CardObjectActionResult(transformedSource, decisionValueAdjustment);
    }

    private static bool IsAnointedRareDrawToHand(SimulationCard source, CardActionFact action)
    {
        if (!IsAnointed(source)
            || action.Source != "CardPileCmd.Add"
            || action.Parameter is null)
        {
            return false;
        }

        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        return GetParameter(parameters, "from") is null
            && string.Equals(GetParameter(parameters, "to"), "Hand", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnointed(SimulationCard card)
    {
        return CardBehaviorCatalog.Has(card, CardBehaviorKind.AnointedRareDrawToHand);
    }

    private static void ResolveAnointedRareDrawToHand(SimulationState state, FastRandom rng)
    {
        int count = Math.Max(0, state.MaxHandSize - state.Hand.Count);
        if (count == 0 || state.DrawPile.Count == 0)
        {
            return;
        }

        List<DeckCardInstance> eligible = state.DrawPile
            .Where(instance => string.Equals(instance.Card.Rarity, "Rare", StringComparison.OrdinalIgnoreCase))
            .ToList();
        for (int index = 0; index < count && eligible.Count > 0; index++)
        {
            int selectedIndex = rng.Next(eligible.Count);
            DeckCardInstance selected = eligible[selectedIndex];
            eligible.RemoveAt(selectedIndex);
            state.DrawPile.Remove(selected);
            state.Hand.Add(selected);
            ArmGuaranteedSearchAdmission([selected]);
        }
    }

    // Purity: choose up to Cards (3, upgraded 5) hand cards and Exhaust them. Simplified selection:
    // only exhaust genuinely low-value fodder - basic Strike/Defend (including upgraded) and any
    // attack whose StaticEstimatedValue < 15 - so it never culls a good card. The deck-thinning
    // payoff is measured by play-delta dEV.
    private static void ResolvePurityExhaust(SimulationState state, SimulationCard purity)
    {
        int limit = purity.UpgradeLevel > 0 ? 5 : 3;
        if (limit <= 0 || state.Hand.Count == 0)
        {
            return;
        }

        List<DeckCardInstance> eligible = state.Hand
            .Where(card => IsPurityExhaustEligible(card.Card))
            .OrderBy(card => CardObjectChoiceScore(card))
            .ThenBy(card => card.InstanceId)
            .Take(limit)
            .ToList();
        if (eligible.Count > 0)
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }

        foreach (DeckCardInstance card in eligible)
        {
            state.Hand.Remove(card);
            state.ExhaustPile.Add(card);
        }
    }

    private static bool IsPurityExhaustEligible(SimulationCard card)
    {
        if (CardBehaviorCatalog.Has(card, CardBehaviorKind.PurityAlwaysExhaustible))
        {
            return true;
        }

        return card.IsAttack && card.StaticEstimatedValue < 15d;
    }

    private static IReadOnlyList<DeckCardInstance> SelectBestCardsToDraw(
        IReadOnlyList<DeckCardInstance> pile,
        int count)
    {
        return pile
            .OrderByDescending(instance => CardObjectChoiceScore(instance))
            .ThenBy(instance => instance.InstanceId)
            .Take(count)
            .ToArray();
    }

    private static double ResolveMoveCardBetweenPiles(SimulationState state, CardActionFact action)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        string? fromPileName = GetParameter(parameters, "from");
        string? toPileName = GetParameter(parameters, "to") ?? GetParameter(parameters, "pile");
        if (fromPileName is null || toPileName is null)
        {
            return 0d;
        }

        SimulationCardPile? fromPile = TryGetPile(state, fromPileName);
        SimulationCardPile? toPile = TryGetPile(state, toPileName);
        if (fromPile is null || toPile is null || fromPile.Count == 0)
        {
            return 0d;
        }

        int count = Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero));
        if (count == 0)
        {
            return 0d;
        }

        bool preferHighValue = IsBeneficialDestination(toPileName);
        // Retrieval into Hand/Draw, including CosmicIndifference fetching a discard card to draw top,
        // is usually payoff for a later draw/play. Rank by the card-object search score instead of
        // current-turn immediate value, so temporary current resources do not pick the wrong card.
        IReadOnlyList<DeckCardInstance> selected = preferHighValue
            ? SelectBestCardsToDraw(fromPile, count)
            : SelectCardObjects(fromPile, count, preferHighValue: false);
        if (selected.Count > 0)
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }

        foreach (DeckCardInstance selectedCard in selected)
        {
            fromPile.Remove(selectedCard);
        }

        AddCardsToPile(state, toPile, selected, GetParameter(parameters, "position"));
        return 0d;
    }

    private static double ResolveProfiledMoveCardBetweenPiles(
        SimulationState state,
        DeckCardInstance source,
        CardActionFact action,
        CardObjectDecisionProfile profile,
        DeckSimulationOptions options,
        List<CardMoveChoiceEvent>? moveChoices,
        CardObjectSearchContext? searchContext)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        string? fromPileName = GetParameter(parameters, "from");
        string? toPileName = GetParameter(parameters, "to") ?? GetParameter(parameters, "pile");
        if (fromPileName is null || toPileName is null)
        {
            return 0d;
        }

        SimulationCardPile? fromPile = TryGetPile(state, fromPileName);
        SimulationCardPile? toPile = TryGetPile(state, toPileName);
        if (fromPile is null || toPile is null || fromPile.Count == 0)
        {
            return 0d;
        }

        int count = Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero));
        if (count == 0)
        {
            return 0d;
        }

        if (!IsBeneficialDestination(toPileName) || count != 1)
        {
            return ResolveMoveCardBetweenPiles(state, action);
        }

        int turn = searchContext?.Turn ?? 1;
        int width = Math.Max(1, profile.TargetBranchWidth);
        bool fromHand = string.Equals(NormalizePileName(fromPileName), "Hand", StringComparison.Ordinal);
        DeckCardInstance[] diagnosticCandidates = moveChoices is null
            ? []
            : fromPile.Select(card => card.Clone()).ToArray();
        DeckCardInstance[] shortlisted = fromPile
            .OrderByDescending(instance => MoveCardObjectCandidateScore(
                instance,
                state,
                options,
                turn,
                fromHand))
            .ThenBy(instance => instance.InstanceId)
            .Take(width)
            .ToArray();
        if (shortlisted.Length == 0)
        {
            return 0d;
        }

        if (searchContext is null || options.CardObjectLookaheadTurns <= 0)
        {
            AddMoveDiagnostics(
                moveChoices,
                source.Card,
                diagnosticCandidates,
                shortlisted[0].InstanceId,
                fromPileName,
                toPileName,
                state,
                options,
                turn,
                fromHand);
            MoveCardObjectByInstanceId(
                state,
                fromPileName,
                toPileName,
                shortlisted[0].InstanceId,
                GetParameter(parameters, "position"));
            return 0d;
        }

        bool includeNextTurn = profile.Horizon == CardObjectDecisionHorizon.ThroughNextTurn
            && searchContext.Turn < options.Turns;
        int continuationSeed = DeriveSeed(searchContext.Seed, source.InstanceId, 73);
        CardObjectContinuation baseline = EvaluateCardObjectContinuation(
            state,
            source,
            options,
            searchContext,
            continuationSeed,
            includeNextTurn);
        SimulationState? bestState = null;
        CardObjectContinuation? bestContinuation = null;
        int? bestCandidateInstanceId = null;
        foreach (DeckCardInstance candidate in shortlisted)
        {
            SimulationState candidateState = state.Clone();
            MoveCardObjectByInstanceId(
                candidateState,
                fromPileName,
                toPileName,
                candidate.InstanceId,
                GetParameter(parameters, "position"));
            CardObjectContinuation continuation = EvaluateCardObjectContinuation(
                candidateState,
                source,
                options,
                searchContext,
                continuationSeed,
                includeNextTurn);
            if (bestContinuation is null || continuation.TotalValue > bestContinuation.TotalValue)
            {
                bestState = candidateState;
                bestContinuation = continuation;
                bestCandidateInstanceId = candidate.InstanceId;
            }
        }

        if (bestState is null || bestContinuation is null || bestCandidateInstanceId is null)
        {
            return 0d;
        }

        AddMoveDiagnostics(
            moveChoices,
            source.Card,
            diagnosticCandidates,
            bestCandidateInstanceId.Value,
            fromPileName,
            toPileName,
            state,
            options,
            turn,
            fromHand);
        state.CopyFrom(bestState);
        return includeNextTurn
            ? bestContinuation.NextTurnValue - baseline.NextTurnValue
            : 0d;
    }

    private static double MoveCardObjectCandidateScore(
        DeckCardInstance instance,
        SimulationState state,
        DeckSimulationOptions options,
        int turn,
        bool fromHand)
    {
        return CardContinuationValue(instance, state, options, turn)
            - CurrentHandOpportunityCost(instance, state, options, fromHand);
    }

    private static void AddMoveDiagnostics(
        List<CardMoveChoiceEvent>? moveChoices,
        SimulationCard source,
        IReadOnlyList<DeckCardInstance> candidates,
        int selectedInstanceId,
        string fromPileName,
        string toPileName,
        SimulationState state,
        DeckSimulationOptions options,
        int turn,
        bool fromHand)
    {
        if (moveChoices is null)
        {
            return;
        }

        string fromPile = NormalizePileName(fromPileName);
        string toPile = NormalizePileName(toPileName);
        foreach (DeckCardInstance candidate in candidates)
        {
            moveChoices.Add(new CardMoveChoiceEvent(
                source.ReportModelId,
                source.ReportTypeName,
                candidate.Card.ReportModelId,
                candidate.Card.ReportTypeName,
                fromPile,
                toPile,
                candidate.InstanceId == selectedInstanceId,
                MoveCardObjectCandidateScore(candidate, state, options, turn, fromHand)));
        }
    }

    private static double CurrentHandOpportunityCost(
        DeckCardInstance instance,
        SimulationState state,
        DeckSimulationOptions options,
        bool fromHand)
    {
        if (!fromHand
            || !CanPlay(
                instance.Card,
                state,
                options,
                instance.InstanceId,
                instance.BonusDrawCostReduction,
                instance.CostOverrideThisCombat,
                instance.BonusUntilPlayedCostReduction,
                instance.FreeThisTurn))
        {
            return 0d;
        }

        return EstimateCardDecisionValue(
            instance.Card,
            state,
            ResourceReferenceValuesForTurns(options.Turns),
            includeDynamicSetup: false);
    }

    private static void MoveCardObjectByInstanceId(
        SimulationState state,
        string fromPileName,
        string toPileName,
        int instanceId,
        string? position)
    {
        SimulationCardPile? fromPile = TryGetPile(state, fromPileName);
        SimulationCardPile? toPile = TryGetPile(state, toPileName);
        DeckCardInstance? card = fromPile?.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (fromPile is null || toPile is null || card is null)
        {
            return;
        }

        state.InvalidateFutureTurnOpportunityProfile();
        fromPile.Remove(card);
        AddCardsToPile(state, toPile, [card], position);
    }

    private static CardObjectActionResult ResolveProfiledTransformCard(
        SimulationState state,
        DeckCardInstance source,
        CardActionFact action,
        CardObjectDecisionProfile profile,
        DeckSimulationOptions options,
        List<CardTransformChoiceEvent>? transformChoices,
        CardObjectSearchContext? searchContext)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        SimulationCard replacement = ResolveTransformReplacement(parameters, options, source.Card);
        string? fromPileName = GetParameter(parameters, "from");
        if (fromPileName is null)
        {
            return new CardObjectActionResult(replacement, 0d);
        }

        SimulationCardPile? fromPile = TryGetPile(state, fromPileName);
        if (fromPile is null || fromPile.Count == 0)
        {
            return CardObjectActionResult.Empty;
        }

        DeckCardInstance[] diagnosticCandidates = transformChoices is null
            ? []
            : fromPile.Select(card => card.Clone()).ToArray();

        int count = TransformCount(source.Card, action, fromPile.Count);
        if (count == 0)
        {
            return CardObjectActionResult.Empty;
        }

        int turn = searchContext?.Turn ?? 1;
        CardTransformBehavior behavior = CardBehaviorCatalog.ForCard(source.Card).Transform
            ?? new CardTransformBehavior();
        double replacementScore = CardContinuationValue(replacement, state, options, turn);
        List<DeckCardInstance> eligible = fromPile
            .Where(card => IsEligibleTransformTarget(
                card,
                behavior,
                CardContinuationValue(card, state, options, turn),
                replacementScore,
                state,
                options,
                turn))
            .ToList();
        count = Math.Min(count, eligible.Count);
        if (count == 0)
        {
            AddTransformDiagnostics(
                transformChoices,
                source.Card,
                diagnosticCandidates,
                replacement,
                [],
                state,
                options,
                turn);
            return CardObjectActionResult.Empty;
        }

        IReadOnlyList<IReadOnlyList<DeckCardInstance>> plans = BuildTransformTargetPlans(
            eligible,
            count,
            behavior,
            state,
            options,
            turn,
            profile.TargetBranchWidth);
        if (plans.Count == 0)
        {
            return CardObjectActionResult.Empty;
        }

        IReadOnlyList<DeckCardInstance> selectedPlan = plans[0];
        SimulationState? selectedState = null;
        CardObjectContinuation? selectedContinuation = null;
        CardObjectContinuation? baselineContinuation = null;
        bool canPreview = searchContext is not null && options.CardObjectLookaheadTurns > 0;
        bool includeNextTurn = canPreview
            && profile.Horizon == CardObjectDecisionHorizon.ThroughNextTurn
            && searchContext!.Turn < options.Turns;
        if (canPreview)
        {
            int continuationSeed = DeriveSeed(searchContext!.Seed, source.InstanceId, 97);
            baselineContinuation = EvaluateCardObjectContinuation(
                state,
                source,
                options,
                searchContext,
                continuationSeed,
                includeNextTurn);
            foreach (IReadOnlyList<DeckCardInstance> plan in plans)
            {
                SimulationState candidateState = state.Clone();
                ApplyTransformPlan(candidateState, fromPileName, plan, replacement);
                CardObjectContinuation continuation = EvaluateCardObjectContinuation(
                    candidateState,
                    source,
                    options,
                    searchContext,
                    continuationSeed,
                    includeNextTurn);
                if (selectedContinuation is null || continuation.TotalValue > selectedContinuation.TotalValue)
                {
                    selectedPlan = plan;
                    selectedState = candidateState;
                    selectedContinuation = continuation;
                }
            }
        }

        if (selectedState is null)
        {
            ApplyTransformPlan(state, fromPileName, selectedPlan, replacement);
        }
        else
        {
            state.CopyFrom(selectedState);
        }

        AddTransformDiagnostics(
            transformChoices,
            source.Card,
            diagnosticCandidates,
            replacement,
            selectedPlan,
            state,
            options,
            turn);
        double adjustment = includeNextTurn && selectedContinuation is not null && baselineContinuation is not null
            ? selectedContinuation.NextTurnValue - baselineContinuation.NextTurnValue
            : 0d;
        return new CardObjectActionResult(null, adjustment);
    }

    private static IReadOnlyList<IReadOnlyList<DeckCardInstance>> BuildTransformTargetPlans(
        IReadOnlyList<DeckCardInstance> eligible,
        int count,
        CardTransformBehavior behavior,
        SimulationState state,
        DeckSimulationOptions options,
        int turn,
        int targetBranchWidth)
    {
        IOrderedEnumerable<DeckCardInstance> ordered = behavior.SelectionMode == CardTransformSelectionMode.DisposableFodder
            ? eligible
                .OrderBy(TransformFodderPriority)
                .ThenBy(card => CardContinuationValue(card, state, options, turn))
                .ThenBy(card => card.InstanceId)
            : eligible
                .OrderBy(card => CardContinuationValue(card, state, options, turn))
                .ThenBy(card => card.InstanceId);
        DeckCardInstance[] sorted = ordered.ToArray();
        if (behavior.CountMode == CardTransformCountMode.AllAvailable)
        {
            return IsEligibleTransformTargetPlan(sorted, behavior, state, options, turn)
                ? [sorted]
                : [];
        }

        int width = Math.Max(1, targetBranchWidth);
        if (behavior.SelectionMode != CardTransformSelectionMode.DisposableFodder)
        {
            DeckCardInstance[] pool = sorted.Take(Math.Min(sorted.Length, count + width - 1)).ToArray();
            return EnumerateCardObjectCombinations(pool, count)
                .Where(plan => IsEligibleTransformTargetPlan(plan, behavior, state, options, turn))
                .OrderBy(plan => plan.Sum(card => CardContinuationValue(card, state, options, turn)))
                .ThenBy(plan => string.Join(",", plan.Select(card => card.InstanceId)))
                .Take(width)
                .Cast<IReadOnlyList<DeckCardInstance>>()
                .ToArray();
        }

        int boundaryPriority = TransformFodderPriority(sorted[count - 1]);
        DeckCardInstance[] mandatory = sorted
            .Where(card => TransformFodderPriority(card) < boundaryPriority)
            .ToArray();
        int remaining = count - mandatory.Length;
        IEnumerable<DeckCardInstance> boundaryCards = sorted
            .Where(card => TransformFodderPriority(card) == boundaryPriority);
        DeckCardInstance[] variablePool = behavior.TargetConstraints.Count > 0
            ? boundaryCards.ToArray()
            : boundaryCards.Take(Math.Min(sorted.Length, remaining + width - 1)).ToArray();
        return EnumerateCardObjectCombinations(variablePool, remaining)
            .Select(plan => (IReadOnlyList<DeckCardInstance>)[.. mandatory, .. plan])
            .Where(plan => IsEligibleTransformTargetPlan(plan, behavior, state, options, turn))
            .OrderBy(plan => plan.Sum(card => CardContinuationValue(card, state, options, turn)))
            .ThenBy(plan => string.Join(",", plan.Select(card => card.InstanceId)))
            .Take(width)
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<DeckCardInstance>> EnumerateCardObjectCombinations(
        IReadOnlyList<DeckCardInstance> cards,
        int count)
    {
        List<IReadOnlyList<DeckCardInstance>> results = [];
        List<DeckCardInstance> current = [];
        Add(startIndex: 0);
        return results;

        void Add(int startIndex)
        {
            if (current.Count == count)
            {
                results.Add(current.ToArray());
                return;
            }

            int needed = count - current.Count;
            for (int index = startIndex; index <= cards.Count - needed; index++)
            {
                current.Add(cards[index]);
                Add(index + 1);
                current.RemoveAt(current.Count - 1);
            }
        }
    }

    private static void ApplyTransformPlan(
        SimulationState state,
        string fromPileName,
        IReadOnlyList<DeckCardInstance> plan,
        SimulationCard replacement)
    {
        SimulationCardPile? pile = TryGetPile(state, fromPileName);
        if (pile is null)
        {
            return;
        }

        HashSet<int> selectedIds = plan.Select(card => card.InstanceId).ToHashSet();
        bool changed = false;
        foreach (DeckCardInstance candidate in pile)
        {
            if (selectedIds.Contains(candidate.InstanceId))
            {
                candidate.Card = replacement;
                changed = true;
            }
        }

        if (changed)
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }
    }

    private static void AddTransformDiagnostics(
        List<CardTransformChoiceEvent>? transformChoices,
        SimulationCard source,
        IReadOnlyList<DeckCardInstance> candidates,
        SimulationCard replacement,
        IReadOnlyList<DeckCardInstance> selected,
        SimulationState state,
        DeckSimulationOptions options,
        int turn)
    {
        if (transformChoices is null)
        {
            return;
        }

        HashSet<int> selectedIds = selected.Select(card => card.InstanceId).ToHashSet();
        double replacementScore = CardContinuationValue(replacement, state, options, turn);
        foreach (DeckCardInstance candidate in candidates)
        {
            transformChoices.Add(new CardTransformChoiceEvent(
                source.ReportModelId,
                source.ReportTypeName,
                candidate.Card.ReportModelId,
                candidate.Card.ReportTypeName,
                replacement.ReportModelId,
                replacement.ReportTypeName,
                selectedIds.Contains(candidate.InstanceId),
                CardContinuationValue(candidate, state, options, turn),
                replacementScore));
        }
    }

    private static CardObjectContinuation EvaluateCardObjectContinuation(
        SimulationState candidateState,
        DeckCardInstance source,
        DeckSimulationOptions options,
        CardObjectSearchContext context,
        int seed,
        bool includeNextTurn)
    {
        SimulationState previewState = candidateState.Clone();
        int attackSkillPlaysBeforePlay = previewState.AttacksPlayedThisTurn + previewState.SkillsPlayedThisTurn;
        MovePlayedCardToResultPile(
            previewState,
            source.Clone(),
            source.Card,
            transformedPlayedCard: null,
            attackSkillPlaysBeforePlay);
        if (source.Card.IsAttack)
        {
            previewState.AttacksPlayedThisTurn++;
        }
        else if (IsSkillCard(source.Card))
        {
            previewState.SkillsPlayedThisTurn++;
        }

        previewState.CardsPlayedThisCombat++;
        int maxPreviewPlays = Math.Max(
            context.ActionsPlayed + 1,
            context.ActionsPlayed + 1 + Math.Max(0, options.CardObjectLookaheadCardsPlayed));
        DeckSimulationOptions previewOptions = options with
        {
            CardObjectLookaheadTurns = 0,
            MaxBranchingCards = Math.Max(1, options.CardObjectLookaheadBranchingCards),
            MaxFullyBranchedCardsPlayedPerTurn = Math.Min(
                options.MaxFullyBranchedCardsPlayedPerTurn,
                2),
            MaxCardsPlayedPerTurn = Math.Min(options.MaxCardsPlayedPerTurn, maxPreviewPlays),
            StateValue = null,
            SearchPolicyCollector = null,
            SearchPolicyMetadata = null,
            CollectAttribution = false,
            CollectSearchPlayTrace = false,
            CollectCardObjectDiagnostics = false
        };
        SearchResult suffix = Search(
            previewState,
            previewOptions,
            new FiniteHorizonContext(previewOptions.Turns, context.Turn),
            context.Run,
            context.Turn,
            context.ActionsPlayed + 1,
            1,
            seed,
            useFiniteHorizonLeafValue: false);
        SimulationState endState = suffix.State.Clone();
        PowerEventResult turnEnd = ResolveTurnEndPowers(endState);
        double currentTurnValue = suffix.DecisionValue + turnEnd.Value;
        if (!includeNextTurn || context.Turn >= previewOptions.Turns)
        {
            return new CardObjectContinuation(currentTurnValue, 0d);
        }

        endState.LastTurnCardsPlayed = suffix.CardsPlayed + 1;
        FinishTurn(endState);
        endState.CurrentTurnEnergySources.Clear();
        FastRandom rng = new(DeriveSeed(seed, context.Turn, 131));
        TurnTrialSummary nextTurn = PlayTurn(
            endState,
            previewOptions,
            rng,
            context.Run,
            context.Turn + 1);
        return new CardObjectContinuation(currentTurnValue, nextTurn.Value);
    }

    private static SimulationCard? ResolveTransformCard(
        SimulationState state,
        DeckCardInstance source,
        CardActionFact action,
        DeckSimulationOptions options,
        List<CardTransformChoiceEvent>? transformChoices)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        SimulationCard replacement = ResolveTransformReplacement(parameters, options, source.Card);
        string? fromPileName = GetParameter(parameters, "from");
        if (fromPileName is null)
        {
            return replacement;
        }

        SimulationCardPile? fromPile = TryGetPile(state, fromPileName);
        if (fromPile is null || fromPile.Count == 0)
        {
            return null;
        }

        int count = TransformCount(source.Card, action, fromPile.Count);
        if (count == 0)
        {
            return null;
        }

        IReadOnlyList<DeckCardInstance> selected = SelectTransformTargets(source.Card, fromPile, replacement, count);
        if (transformChoices is not null)
        {
            HashSet<int> selectedIds = selected.Select(card => card.InstanceId).ToHashSet();
            double replacementScore = CardObjectChoiceScore(replacement);
            foreach (DeckCardInstance candidate in fromPile)
            {
                transformChoices.Add(new CardTransformChoiceEvent(
                    source.Card.ReportModelId,
                    source.Card.ReportTypeName,
                    candidate.Card.ReportModelId,
                    candidate.Card.ReportTypeName,
                    replacement.ReportModelId,
                    replacement.ReportTypeName,
                    selectedIds.Contains(candidate.InstanceId),
                    CardObjectChoiceScore(candidate),
                    replacementScore));
            }
        }

        foreach (DeckCardInstance selectedCard in selected)
        {
            selectedCard.Card = replacement;
        }

        state.InvalidateFutureTurnOpportunityProfile();

        return null;
    }

    private static void TransformLowestValueCardsFromGeneratedPool(
        SimulationState state,
        DeckSimulationOptions options,
        FastRandom rng,
        string poolId,
        int count,
        bool upgradeGenerated)
    {
        if (count <= 0 || state.Hand.Count == 0)
        {
            return;
        }

        List<SimulationCard> candidates = ResolveGeneratedPoolCandidates(options, poolId, upgradeGenerated);
        state.InvalidateFutureTurnOpportunityProfile();
        for (int i = 0; i < count && state.Hand.Count > 0; i++)
        {
            DeckCardInstance? selected = SelectCardObjects(state.Hand, 1, preferHighValue: false).FirstOrDefault();
            if (selected is null)
            {
                return;
            }

            selected.Card = candidates[rng.Next(candidates.Count)];
        }
    }

    private static SimulationCard ResolveTransformReplacement(
        IReadOnlyDictionary<string, string> parameters,
        DeckSimulationOptions options,
        SimulationCard sourceCard)
    {
        string? target = TransformTarget(sourceCard, GetParameter(parameters, "card"));
        if (!string.IsNullOrWhiteSpace(target)
            && !string.Equals(target, "SIM.TRANSFORMED_CARD", StringComparison.OrdinalIgnoreCase)
            && TryFindSimulationCard(
                options.CardLibrary,
                UpgradeTargetName(target, sourceCard.UpgradeLevel),
                out SimulationCard? upgradedReplacement))
        {
            return upgradedReplacement!;
        }

        if (!string.IsNullOrWhiteSpace(target)
            && !string.Equals(target, "SIM.TRANSFORMED_CARD", StringComparison.OrdinalIgnoreCase)
            && TryFindSimulationCard(options.CardLibrary, target, out SimulationCard? replacement))
        {
            return replacement!;
        }

        return CreateGenericTransformedCard();
    }

    private static string? TransformTarget(SimulationCard sourceCard, string? parsedTarget)
    {
        if (!string.Equals(parsedTarget, "SIM.TRANSFORMED_CARD", StringComparison.OrdinalIgnoreCase))
        {
            return parsedTarget;
        }

        return CardBehaviorCatalog.ForCard(sourceCard).Transform?.GenericTargetOverride ?? parsedTarget;
    }

    private static int TransformCount(SimulationCard sourceCard, CardActionFact action, int available)
    {
        if (available <= 0)
        {
            return 0;
        }

        CardTransformBehavior? behavior = CardBehaviorCatalog.ForCard(sourceCard).Transform;
        if (behavior?.CountMode == CardTransformCountMode.AllAvailable)
        {
            return available;
        }

        int count = Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero));
        return Math.Min(count, available);
    }

    private static IReadOnlyList<DeckCardInstance> SelectTransformTargets(
        SimulationCard sourceCard,
        IReadOnlyList<DeckCardInstance> cards,
        SimulationCard replacement,
        int count)
    {
        CardTransformBehavior? behavior = CardBehaviorCatalog.ForCard(sourceCard).Transform;
        if (behavior?.SelectionMode != CardTransformSelectionMode.DisposableFodder)
        {
            IReadOnlyList<DeckCardInstance> selected = SelectCardObjects(cards, count, preferHighValue: false);
            if (behavior?.RequireReplacementImprovement != true)
            {
                return selected;
            }

            double replacementScore = CardObjectChoiceScore(replacement);
            return selected
                .Where(card => CardObjectChoiceScore(card) < replacementScore)
                .ToArray();
        }

        double disposableReplacementScore = CardObjectChoiceScore(replacement);
        IEnumerable<DeckCardInstance> eligible = cards
            .Where(card => IsEligibleTransformTarget(
                card,
                behavior,
                CardObjectChoiceScore(card),
                disposableReplacementScore));

        return eligible
            .OrderBy(TransformFodderPriority)
            .ThenBy(CardObjectChoiceScore)
            .ThenBy(card => card.InstanceId)
            .Take(count)
            .ToArray();
    }

    private static int TransformFodderPriority(DeckCardInstance card)
    {
        if (string.Equals(card.Card.CardType, "Status", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (CardBehaviorCatalog.Has(card.Card, CardBehaviorKind.PreferredTransformFodder)
            && !HasEnchantment(card.Card, "TEZCATARAS_EMBER"))
        {
            return 1;
        }

        return CardBehaviorCatalog.Has(card.Card, CardBehaviorKind.SecondaryTransformFodder)
            ? 2
            : 3;
    }

    private static bool IsEligibleTransformTarget(
        DeckCardInstance candidate,
        CardTransformBehavior behavior,
        double candidateScore,
        double replacementScore,
        SimulationState? state = null,
        DeckSimulationOptions? options = null,
        int turn = 1)
    {
        if (behavior.SelectionMode == CardTransformSelectionMode.DisposableFodder
            && candidate.Card.Ethereal)
        {
            return false;
        }

        if (behavior.RequireImprovingFallbackFodder
            && TransformFodderPriority(candidate) >= 3
            && candidateScore >= replacementScore)
        {
            return false;
        }

        if (behavior.RequireReplacementImprovement && candidateScore >= replacementScore)
        {
            return false;
        }

        if (behavior.TargetConstraints.Count > 0
            && (state is null
                || options is null
                || behavior.TargetConstraints.Any(constraint => ProtectsTransformTarget(
                    constraint,
                    candidate,
                    state,
                    options,
                    turn))))
        {
            return false;
        }

        return true;
    }

    private static bool ProtectsTransformTarget(
        CardTransformTargetConstraint constraint,
        DeckCardInstance candidate,
        SimulationState state,
        DeckSimulationOptions options,
        int turn)
    {
        return constraint switch
        {
            PreserveResourceBalanceConstraint resourceBalance =>
                WouldBreakResourceBalance([candidate], state, resourceBalance),
            PreserveReusableEffectCoverageConstraint effectCoverage =>
                WouldRemoveRequiredReusableEffectCoverage([candidate], state, effectCoverage),
            ProtectCardTypeOutsideFutureTurnWindowConstraint cardTypeWindow =>
                string.Equals(candidate.Card.CardType, cardTypeWindow.CardType, StringComparison.OrdinalIgnoreCase)
                && options.Turns - turn != cardTypeWindow.EligibleFutureTurns,
            AlwaysProtectTransformTargetsConstraint alwaysProtect =>
                MatchesAnyBaseTypeName(candidate.Card, alwaysProtect.TargetBaseTypeNames),
            _ => throw new InvalidOperationException(
                $"Unsupported transform target constraint '{constraint.GetType().Name}'.")
        };
    }

    private static bool IsEligibleTransformTargetPlan(
        IReadOnlyList<DeckCardInstance> plan,
        CardTransformBehavior behavior,
        SimulationState state,
        DeckSimulationOptions options,
        int turn)
    {
        foreach (CardTransformTargetConstraint constraint in behavior.TargetConstraints)
        {
            bool protectedPlan = constraint switch
            {
                PreserveResourceBalanceConstraint resourceBalance =>
                    WouldBreakResourceBalance(plan, state, resourceBalance),
                PreserveReusableEffectCoverageConstraint effectCoverage =>
                    WouldRemoveRequiredReusableEffectCoverage(plan, state, effectCoverage),
                _ => plan.Any(candidate => ProtectsTransformTarget(
                    constraint,
                    candidate,
                    state,
                    options,
                    turn))
            };
            if (protectedPlan)
            {
                return false;
            }
        }

        return true;
    }

    private static bool WouldBreakResourceBalance(
        IReadOnlyList<DeckCardInstance> candidates,
        SimulationState state,
        PreserveResourceBalanceConstraint constraint)
    {
        int removedResourceGain = candidates.Sum(candidate =>
            StaticResourceGain(candidate.Card, constraint.Resource));
        if (removedResourceGain <= 0)
        {
            return false;
        }

        int totalResourceGain = 0;
        int totalResourceCost = 0;
        foreach (DeckCardInstance card in NonExhaustCards(state))
        {
            totalResourceGain += StaticResourceGain(card.Card, constraint.Resource);
            totalResourceCost += StaticResourceCost(card.Card, constraint.Resource);
        }

        int remainingResourceGain = totalResourceGain - removedResourceGain;
        int remainingResourceCost = totalResourceCost - candidates.Sum(candidate =>
            StaticResourceCost(candidate.Card, constraint.Resource));
        return remainingResourceCost
            > remainingResourceGain + Math.Max(0, constraint.Reserve);
    }

    private static int StaticResourceGain(SimulationCard card, CardResourceKind resource)
    {
        return resource switch
        {
            CardResourceKind.Stars => Math.Max(0, card.StarGain) + Math.Max(0, card.StarNextTurn),
            CardResourceKind.Energy => Math.Max(0, card.EnergyGain) + Math.Max(0, card.EnergyNextTurn),
            _ => throw new InvalidOperationException($"Unsupported card resource '{resource}'.")
        };
    }

    private static int StaticResourceCost(SimulationCard card, CardResourceKind resource)
    {
        return resource switch
        {
            CardResourceKind.Stars => Math.Max(0, card.StarCost),
            CardResourceKind.Energy => Math.Max(0, card.EnergyCost),
            _ => throw new InvalidOperationException($"Unsupported card resource '{resource}'.")
        };
    }

    private static bool WouldRemoveRequiredReusableEffectCoverage(
        IReadOnlyList<DeckCardInstance> candidates,
        SimulationState state,
        PreserveReusableEffectCoverageConstraint constraint)
    {
        if (!candidates.Any(candidate =>
                MatchesAnyBaseTypeName(candidate.Card, constraint.TargetBaseTypeNames)))
        {
            return false;
        }

        HashSet<int> removedInstanceIds = candidates
            .Select(candidate => candidate.InstanceId)
            .ToHashSet();

        foreach (string requiredActionKind in constraint.RequiredActionKinds)
        {
            bool covered = NonExhaustCards(state).Any(card =>
                !removedInstanceIds.Contains(card.InstanceId)
                && !HasEffectiveExhaust(card.Card)
                && !card.Card.Ethereal
                && card.Card.Actions.Any(action =>
                    string.Equals(action.Kind, requiredActionKind, StringComparison.Ordinal)
                    && (action.Amount ?? 0m) > 0m));
            if (!covered)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyBaseTypeName(
        SimulationCard card,
        IReadOnlyList<string> baseTypeNames)
    {
        string baseTypeName = CardBehaviorCatalog.BaseTypeName(card.TypeName);
        return baseTypeNames.Contains(baseTypeName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryFindSimulationCard(
        IReadOnlyList<SimulationCard> cards,
        string target,
        out SimulationCard? card)
    {
        card = cards.FirstOrDefault(candidate =>
            string.Equals(candidate.TypeName, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ModelId, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.FullTypeName, target, StringComparison.OrdinalIgnoreCase)
            || candidate.FullTypeName.EndsWith("." + target, StringComparison.OrdinalIgnoreCase));
        return card is not null;
    }

    // Fully re-executes a card's OnPlay effects for a FREE play (no energy/star cost; the card is
    // not one being drawn/paid from hand). Used by auto-play (BeatDown/Catastrophe/DecisionsDecisions)
    // and replay (HiddenGem). This runs the REAL play path - damage/block via PlayValue (so
    // conditional scaling like LunarBlast/Radiate is recomputed against current state), star gain and
    // its StarGained triggers, energy gain, next-turn resources, block-next-turn, Vulnerable, Forge,
    // draw, generated cards, nested auto-play, power installs, and after-card-played powers - instead
    // of merely multiplying a precomputed value. It does NOT process the instance's own replay grant
    // loop (that is the caller's job, so replays never fan out exponentially). Depth-guarded so nested
    // auto-play chains stay bounded and recursion-safe.
    private static FreePlayResult ResolveFreeCardPlay(
        SimulationState state,
        DeckCardInstance instance,
        FastRandom rng,
        DeckSimulationOptions options,
        int depth,
        bool allowSelfReplays = true)
    {
        bool collect = options.CollectAttribution;
        RecordActivePowerExposures(state, options);
        List<CardMoveChoiceEvent>? moveChoices = options.CollectCardObjectDiagnostics ? [] : null;
        List<CardTransformChoiceEvent>? transformChoices = options.CollectCardObjectDiagnostics ? [] : null;
        SimulationCard playedCard = instance.Card;
        int playId = state.NextPlayEventId++;
        int attackSkillPlaysBeforePlay = state.AttacksPlayedThisTurn + state.SkillsPlayedThisTurn;
        PowerEventResult beforeCardPlayedResult = ResolveBeforeCardPlayedPowers(state);
        PlayValueResult playValue = PlayValue(instance, state, collect);

        state.Energy += playedCard.EnergyGain;
        if (playedCard.EnergyGain > 0 && state.TrackAttributionSources)
        {
            state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                SourceModelId(playedCard), SourceTypeName(playedCard), playedCard.EnergyGain));
        }

        state.Stars += playedCard.StarGain;
        state.StarsGainedThisTurn += playedCard.StarGain;
        IReadOnlyList<ResourceSourceCredit> starGainSources = playedCard.StarGain > 0
            && state.TrackAttributionSources
            ? [new ResourceSourceCredit(SourceModelId(playedCard), SourceTypeName(playedCard), playedCard.StarGain)]
            : [];
        if (starGainSources.Count > 0)
        {
            state.StarSources.AddRange(starGainSources);
        }

        IReadOnlyList<PowerResolution> starGainedResolutions = playedCard.StarGain > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, playedCard.StarGain, instance))
            : [];
        state.NextTurnEnergy += playedCard.EnergyNextTurn;
        if (playedCard.EnergyNextTurn > 0 && state.TrackAttributionSources)
        {
            state.NextTurnEnergySources.Add(new ResourceSourceCredit(
                SourceModelId(playedCard), SourceTypeName(playedCard), playedCard.EnergyNextTurn));
        }

        state.NextTurnStars += playedCard.StarNextTurn;
        if (playedCard.StarNextTurn > 0 && state.TrackAttributionSources)
        {
            state.NextTurnStarSources.Add(new ResourceSourceCredit(
                SourceModelId(playedCard), SourceTypeName(playedCard), playedCard.StarNextTurn));
        }

        state.NextTurnDraw += playedCard.DrawNextTurn;
        if (playedCard.BlockNextTurn > 0)
        {
            state.NextTurnBlock += playedCard.BlockNextTurn;
            double delayedValue = playedCard.BlockNextTurn * playedCard.BlockValuePerBlock;
            state.NextTurnBlockDecisionValue += delayedValue;
            if (state.TrackAttributionSources)
            {
                state.NextTurnBlockCredits.Add(new DelayedValueCredit(
                    SourceModelId(playedCard), SourceTypeName(playedCard), delayedValue));
            }
        }

        ApplyEnemyVulnerable(state, playedCard);
        ResolveBeforeForgeCardActions(state, playedCard, options);
        int forgeAmount = playedCard.Forge + DynamicForgeAmount(playedCard, state);
        PowerEventResult forgeResult = ApplyForge(state, forgeAmount, instance, playId);
        int drawCount = playedCard.DrawsToHandFull
            ? Math.Max(0, state.MaxHandSize - state.Hand.Count)
            : playedCard.Draw;
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        ResolveCardObjectActions(state, instance, rng, options, moveChoices, transformChoices);
        PowerEventResult generatedCardResult = ResolveGeneratedCardActions(state, instance, options);

        double nestedValue = 0d;
        List<CardValueCreditEvent>? nestedCredits = collect ? [] : null;
        FreePlayResult nestedAutoPlay = ResolveAutoPlayActions(state, instance, rng, options, depth);
        nestedValue += nestedAutoPlay.Value;
        nestedCredits?.AddRange(nestedAutoPlay.Credits);
        moveChoices?.AddRange(nestedAutoPlay.MoveChoices);
        transformChoices?.AddRange(nestedAutoPlay.TransformChoices);
        EnchantmentPlayResult enchantmentResult = ResolveOnPlayEnchantment(state, instance, rng, options);
        nestedValue += enchantmentResult.Value;
        nestedCredits?.AddRange(enchantmentResult.ValueCredits);
        ResolveReplayGrant(state, playedCard, rng);
        FreePlayResult replayResult = ResolvePostPlayReplays(
            state,
            instance,
            rng,
            options,
            depth,
            allowSelfReplays);
        nestedValue += replayResult.Value;
        nestedCredits?.AddRange(replayResult.Credits);
        moveChoices?.AddRange(replayResult.MoveChoices);
        transformChoices?.AddRange(replayResult.TransformChoices);
        ResolveAfterCardPlayedEnchantment(instance);

        InstallPower(state, instance);
        PowerEventResult afterCardPlayedResult = ResolveAfterCardPlayedPowers(state, instance, rng, options);

        if (playedCard.IsAttack)
        {
            state.AttacksPlayedThisTurn++;
        }
        else if (string.Equals(playedCard.CardType, "Skill", StringComparison.OrdinalIgnoreCase))
        {
            state.SkillsPlayedThisTurn++;
        }

        if (playedCard.EndsTurn)
        {
            state.TurnEnded = true;
        }

        state.CardsPlayedThisCombat++;

        double powerValue = 0d;
        AddPowerResolutionValues(ref powerValue, beforeCardPlayedResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, starGainedResolutions);
        AddPowerResolutionValues(ref powerValue, forgeResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, drawResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, generatedCardResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, enchantmentResult.PowerResolutions);
        AddPowerResolutionValues(ref powerValue, afterCardPlayedResult.PowerResolutions);
        double value = playValue.Value + powerValue + nestedValue;

        IReadOnlyList<CardValueCreditEvent> credits = [];
        if (collect)
        {
            IReadOnlyList<PowerResolution> powerResolutions =
            [
                .. beforeCardPlayedResult.PowerResolutions,
                .. starGainedResolutions,
                .. forgeResult.PowerResolutions,
                .. drawResult.PowerResolutions,
                .. generatedCardResult.PowerResolutions,
                .. enchantmentResult.PowerResolutions,
                .. afterCardPlayedResult.PowerResolutions
            ];
            credits =
            [
                .. BuildValueCredits(
                    instance,
                    playValue.DirectValue,
                    [
                        .. playValue.ValueCredits,
                        .. PowerCredits(powerResolutions),
                        .. beforeCardPlayedResult.ValueCredits,
                        .. forgeResult.ValueCredits,
                        .. drawResult.ValueCredits,
                        .. generatedCardResult.ValueCredits,
                        .. enchantmentResult.ValueCredits,
                        .. afterCardPlayedResult.ValueCredits
                    ],
                    StarTriggerCredits(starGainSources, starGainedResolutions.Sum(resolution => resolution.Value))),
                .. (nestedCredits ?? (IReadOnlyList<CardValueCreditEvent>)[])
            ];
        }

        return new FreePlayResult(
            value,
            credits,
            attackSkillPlaysBeforePlay,
            MoveChoiceEvents: moveChoices,
            TransformChoiceEvents: transformChoices);
    }

    // Executes a played card's CardCmd.AutoPlay effect: select cards from the descriptor's source
    // pile (per filter + selection mode), remove them from the pile, and PLAY EACH ONE through the
    // real free-play path (ResolveFreeCardPlay) so their star gain, draw, conditional scaling, and
    // powers actually resolve - then send them to the played-card result pile. Their value flows into deck EV
    // (which is what play-delta measures), credited to the auto-played card, not to the trigger,
    // which is exactly why auto-play cards are play-delta. Depth-guarded to stay recursion-safe.
    private static FreePlayResult ResolveAutoPlayActions(
        SimulationState state,
        DeckCardInstance sourceInstance,
        FastRandom rng,
        DeckSimulationOptions options,
        int depth)
    {
        AutoPlayEffect? effect = sourceInstance.Card.AutoPlay;
        if (effect is null || effect.Count <= 0 || depth + 1 > MaxNestedPlayDepth)
        {
            return FreePlayResult.Empty;
        }

        SimulationCardPile? pile = TryGetPile(state, effect.SourcePile);
        if (pile is null || pile.Count == 0)
        {
            return FreePlayResult.Empty;
        }

        List<DeckCardInstance> candidates = pile
            .Where(instance => MatchesAutoPlayFilter(instance.Card, effect))
            .ToList();
        if (candidates.Count == 0)
        {
            return FreePlayResult.Empty;
        }

        bool collect = options.CollectAttribution;
        double total = 0d;
        List<CardValueCreditEvent>? credits = collect ? [] : null;
        List<CardMoveChoiceEvent>? moveChoices = options.CollectCardObjectDiagnostics ? [] : null;
        List<CardTransformChoiceEvent>? transformChoices = options.CollectCardObjectDiagnostics ? [] : null;

        if (effect.RepeatSameCard)
        {
            // DecisionsDecisions: choose one hand card (best playable Skill) and play THAT card Count times.
            DeckCardInstance chosen = candidates
                .OrderByDescending(instance => CardObjectChoiceScore(instance))
                .ThenBy(instance => instance.InstanceId)
                .First();
            pile.Remove(chosen);
            int attackSkillPlaysBeforePlay = state.AttacksPlayedThisTurn + state.SkillsPlayedThisTurn;
            for (int play = 0; play < effect.Count; play++)
            {
                FreePlayResult result = ResolveFreeCardPlay(state, chosen, rng, options, depth + 1);
                total += result.Value;
                credits?.AddRange(result.Credits);
                moveChoices?.AddRange(result.MoveChoices);
                transformChoices?.AddRange(result.TransformChoices);
                attackSkillPlaysBeforePlay = result.AttackSkillPlaysBeforePlay;
            }

            MovePlayedCardToResultPile(
                state,
                chosen,
                chosen.Card,
                transformedPlayedCard: null,
                attackSkillPlaysBeforePlay: attackSkillPlaysBeforePlay);
            return new FreePlayResult(
                total,
                credits ?? (IReadOnlyList<CardValueCreditEvent>)[],
                MoveChoiceEvents: moveChoices,
                TransformChoiceEvents: transformChoices);
        }

        // BeatDown / Catastrophe: auto-play up to Count distinct cards sampled at random from the pile.
        List<DeckCardInstance> selected = candidates
            .OrderBy(_ => rng.Next())
            .ThenBy(instance => instance.InstanceId)
            .Take(effect.Count)
            .ToList();
        foreach (DeckCardInstance instance in selected)
        {
            pile.Remove(instance);
            FreePlayResult result = ResolveFreeCardPlay(state, instance, rng, options, depth + 1);
            total += result.Value;
            credits?.AddRange(result.Credits);
            moveChoices?.AddRange(result.MoveChoices);
            transformChoices?.AddRange(result.TransformChoices);
            MovePlayedCardToResultPile(
                state,
                instance,
                instance.Card,
                transformedPlayedCard: null,
                attackSkillPlaysBeforePlay: result.AttackSkillPlaysBeforePlay);
        }

        return new FreePlayResult(
            total,
            credits ?? (IReadOnlyList<CardValueCreditEvent>)[],
            MoveChoiceEvents: moveChoices,
            TransformChoiceEvents: transformChoices);
    }

    private static EnchantmentPlayResult ResolveOnPlayEnchantment(
        SimulationState state,
        DeckCardInstance instance,
        FastRandom rng,
        DeckSimulationOptions options)
    {
        SimulationCard card = instance.Card;
        if (!IsRuntimeSupportedEnchantment(card))
        {
            return EnchantmentPlayResult.Empty;
        }

        List<PowerResolution> powerResolutions = [];
        List<CardValueCreditEvent> valueCredits = [];
        int cardsDrawn = 0;
        int energyGained = 0;
        if (HasEnchantment(card, "SWIFT") && !instance.EnchantmentDisabled)
        {
            DrawResult drawResult = DrawCards(state, EnchantmentAmount(instance), rng, allowShuffle: true, options);
            cardsDrawn += drawResult.CardsDrawn;
            powerResolutions.AddRange(drawResult.PowerResolutions);
            valueCredits.AddRange(drawResult.ValueCredits);
            instance.EnchantmentDisabled = true;
        }

        if (HasEnchantment(card, "SOWN") && !instance.EnchantmentDisabled)
        {
            int amount = EnchantmentAmount(instance);
            state.Energy += amount;
            if (state.TrackAttributionSources)
            {
                state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                    SourceModelId(card),
                    SourceTypeName(card),
                    amount));
            }
            energyGained += amount;
            instance.EnchantmentDisabled = true;
        }

        if (HasEnchantment(card, "MOMENTUM"))
        {
            instance.EnchantmentBonusDamage += EnchantmentAmount(instance);
        }

        return new EnchantmentPlayResult(cardsDrawn, energyGained, powerResolutions, valueCredits);
    }

    private static FreePlayResult ResolvePostPlayReplays(
        SimulationState state,
        DeckCardInstance instance,
        FastRandom rng,
        DeckSimulationOptions options,
        int depth,
        bool allowSelfReplays = true)
    {
        if (!allowSelfReplays || depth + 1 > MaxNestedPlayDepth)
        {
            return FreePlayResult.Empty;
        }

        int replayCount = instance.BonusReplayCount + EnchantmentReplayCount(instance);
        if (replayCount <= 0)
        {
            return FreePlayResult.Empty;
        }

        bool collect = options.CollectAttribution;
        double total = 0d;
        List<CardValueCreditEvent>? credits = collect ? [] : null;
        List<CardMoveChoiceEvent>? moveChoices = options.CollectCardObjectDiagnostics ? [] : null;
        List<CardTransformChoiceEvent>? transformChoices = options.CollectCardObjectDiagnostics ? [] : null;
        for (int replay = 0; replay < replayCount; replay++)
        {
            FreePlayResult replayResult = ResolveFreeCardPlay(
                state,
                instance,
                rng,
                options,
                depth + 1,
                allowSelfReplays: false);
            total += replayResult.Value;
            credits?.AddRange(replayResult.Credits);
            moveChoices?.AddRange(replayResult.MoveChoices);
            transformChoices?.AddRange(replayResult.TransformChoices);
        }

        if (HasEnchantment(instance.Card, "GLAM"))
        {
            instance.EnchantmentDisabled = true;
        }

        return new FreePlayResult(
            total,
            credits ?? (IReadOnlyList<CardValueCreditEvent>)[],
            MoveChoiceEvents: moveChoices,
            TransformChoiceEvents: transformChoices);
    }

    private static int EnchantmentReplayCount(DeckCardInstance instance)
    {
        if (HasEnchantment(instance.Card, "SPIRAL"))
        {
            return 1;
        }

        return HasEnchantment(instance.Card, "GLAM") && !instance.EnchantmentDisabled ? 1 : 0;
    }

    private static void ResolveAfterCardPlayedEnchantment(DeckCardInstance instance)
    {
        if (HasEnchantment(instance.Card, "GOOPY"))
        {
            instance.EnchantmentAmount++;
        }

        if (HasEnchantment(instance.Card, "VIGOROUS"))
        {
            instance.EnchantmentDisabled = true;
        }
    }

    // HiddenGem: enchants a RANDOM eligible draw-pile card with ReplayGrant extra replays. This is a
    // real state mutation (not a value estimate): the chosen instance's BonusReplayCount is raised, and
    // its extra plays are realized in PlayCard only if it is actually drawn and played later. HiddenGem's
    // own value therefore comes out of the play-delta dEV, exactly like draw/create cards. Eligible =
    // playable, non-power, not already enchanted (mirrors the game's Unplayable/status/curse exclusion
    // and GetEnchantedReplayCount() < 1 filter).
    private static void ResolveReplayGrant(SimulationState state, SimulationCard card, FastRandom rng)
    {
        if (card.ReplayGrant <= 0 || state.DrawPile.Count == 0)
        {
            return;
        }

        List<DeckCardInstance> eligible = state.DrawPile
            .Where(instance => instance.Card.IsPlayable && !instance.Card.IsPower && instance.BonusReplayCount == 0)
            .ToList();
        if (eligible.Count == 0)
        {
            return;
        }

        DeckCardInstance target = eligible[rng.Next(eligible.Count)];
        target.BonusReplayCount += card.ReplayGrant;
    }

    private static bool MatchesAutoPlayFilter(SimulationCard card, AutoPlayEffect effect)
    {
        if (effect.ExcludeUnplayable && !card.IsPlayable)
        {
            return false;
        }

        return effect.CardTypeFilter switch
        {
            "Attack" => card.IsAttack,
            "Skill" => string.Equals(card.CardType, "Skill", StringComparison.OrdinalIgnoreCase),
            "Power" => card.IsPower,
            _ => true
        };
    }

    private static IReadOnlyList<DeckCardInstance> SelectCardObjects(
        IReadOnlyList<DeckCardInstance> cards,
        int count,
        bool preferHighValue)
    {
        IOrderedEnumerable<DeckCardInstance> ordered = preferHighValue
            ? cards
                .OrderByDescending(card => CardObjectChoiceScore(card))
                .ThenBy(card => card.InstanceId)
            : cards
                .OrderBy(card => CardObjectChoiceScore(card))
                .ThenBy(card => card.InstanceId);
        return ordered.Take(count).ToArray();
    }

    private static double CardObjectChoiceScore(SimulationCard card)
    {
        return CardSearchScore(card);
    }

    // Instance-aware value used when the simulator chooses WHICH copy to transform / exhaust / move.
    // Per-instance enchants (HiddenGem Replay) raise the effective value of that specific copy: a
    // Replay-2 Defend plays 3x, so it is worth ~3x its block and must not be picked as the "lowest
    // value" card to sacrifice - even against an upgraded Defend+ with no replay. Scale the model
    // score by the replay multiplier so keep/sacrifice decisions rank instances, not just card models.
    private static double CardObjectChoiceScore(DeckCardInstance instance)
    {
        return CardObjectChoiceScore(instance.Card) * (1 + instance.BonusReplayCount);
    }

    private static double CardContinuationValue(
        DeckCardInstance instance,
        SimulationState state,
        DeckSimulationOptions options,
        int turn)
    {
        return CardContinuationValue(instance.Card, state, options, turn)
            * (1 + instance.BonusReplayCount);
    }

    private static double CardContinuationValue(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options,
        int turn)
    {
        double ordinaryValue = EstimateCardDecisionValue(
            card,
            state,
            ResourceReferenceValuesForTurns(options.Turns),
            includeDynamicSetup: false);
        GeneratedChoiceContinuationBehavior? generatedChoice =
            CardBehaviorCatalog.ForCard(card).GeneratedChoiceContinuation;
        if (generatedChoice is null
            || turn > options.Turns
            || !CanReachGeneratedChoiceCost(state, generatedChoice.RequiredStars)
            || !options.GeneratedCardPools.Pools.ContainsKey(generatedChoice.PoolId))
        {
            return ordinaryValue;
        }

        List<SimulationCard> candidates = ResolveGeneratedPoolCandidates(
            options,
            generatedChoice.PoolId,
            upgradeGenerated: card.UpgradeLevel > 0);
        if (candidates.Count == 0)
        {
            return ordinaryValue;
        }

        double generatedValue = ExpectedBestGeneratedChoiceValue(
            candidates,
            generatedChoice.ChoiceCount,
            state,
            options);
        return Math.Max(ordinaryValue, generatedValue);
    }

    private static bool CanReachGeneratedChoiceCost(SimulationState state, int requiredStars)
    {
        if (requiredStars <= 0 || state.Stars >= requiredStars)
        {
            return true;
        }

        int reachableStars = state.Stars + state.NextTurnStars;
        foreach (DeckCardInstance instance in NonExhaustCards(state))
        {
            reachableStars += Math.Max(0, instance.Card.StarGain);
            reachableStars += Math.Max(0, instance.Card.StarNextTurn);
            if (reachableStars >= requiredStars)
            {
                return true;
            }
        }

        return false;
    }

    private static double ExpectedBestGeneratedChoiceValue(
        IReadOnlyList<SimulationCard> candidates,
        int choiceCount,
        SimulationState state,
        DeckSimulationOptions options)
    {
        double[] values = candidates
            .Select(card => EstimateCardDecisionValue(
                card,
                state,
                ResourceReferenceValuesForTurns(options.Turns),
                includeDynamicSetup: false))
            .Order()
            .ToArray();
        int sampleSize = Math.Min(Math.Max(1, choiceCount), values.Length);
        double denominator = CombinationCount(values.Length, sampleSize);
        if (denominator <= 0d)
        {
            return values[^1];
        }

        double expected = 0d;
        for (int index = sampleSize - 1; index < values.Length; index++)
        {
            double probability = CombinationCount(index, sampleSize - 1) / denominator;
            expected += values[index] * probability;
        }

        return expected;
    }

    private static double CombinationCount(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return 0d;
        }

        k = Math.Min(k, n - k);
        double result = 1d;
        for (int index = 1; index <= k; index++)
        {
            result *= (double)(n - k + index) / index;
        }

        return result;
    }

    private static bool IsBeneficialDestination(string pileName)
    {
        string normalized = NormalizePileName(pileName);
        return normalized is "Hand" or "Draw";
    }

    private static void AddCardsToPile(
        SimulationState state,
        SimulationCardPile pile,
        IReadOnlyList<DeckCardInstance> cards,
        string? position)
    {
        IReadOnlyList<DeckCardInstance> cardsToAdd = ReferenceEquals(pile, state.Hand)
            ? cards.Take(Math.Max(0, state.MaxHandSize - state.Hand.Count)).ToArray()
            : cards;
        if (ReferenceEquals(pile, state.Hand))
        {
            ArmGuaranteedSearchAdmission(cardsToAdd);
        }
        if (string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase))
        {
            foreach (DeckCardInstance card in cardsToAdd.Reverse())
            {
                pile.Insert(0, card);
            }

            return;
        }

        pile.AddRange(cardsToAdd);
    }

    private static SimulationCardPile? TryGetPile(SimulationState state, string pileName)
    {
        return NormalizePileName(pileName) switch
        {
            "Draw" => state.DrawPile,
            "Hand" => state.Hand,
            "Discard" => state.DiscardPile,
            "Exhaust" => state.ExhaustPile,
            _ => null
        };
    }

    private static string NormalizePileName(string pileName)
    {
        return pileName switch
        {
            "DrawPile" => "Draw",
            "DiscardPile" => "Discard",
            "ExhaustPile" => "Exhaust",
            _ => pileName
        };
    }

    private static string? GetParameter(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyActionParameters =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // P5: action parameter strings ("from:Discard;to:Draw;position:Top") repeat across every play of
    // a card; parse each distinct string once and reuse the immutable result (callers only read it).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> ActionParameterCache =
        new(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> ParseActionParameters(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return EmptyActionParameters;
        }

        return ActionParameterCache.GetOrAdd(parameter, ParseActionParametersUncached);
    }

    private static IReadOnlyDictionary<string, string> ParseActionParametersUncached(string parameter)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in parameter.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            values[part[..separator]] = part[(separator + 1)..];
        }

        return values;
    }

    private static void InstallPower(SimulationState state, DeckCardInstance source)
    {
        string sourceModelId = SourceModelId(source.Card);
        string sourceTypeName = SourceTypeName(source.Card);

        void AddActivePower(ActivePowerKind kind, double powerAmount, double secondaryAmount = 0d, int counter = 0)
        {
            state.ActivePowers.Add(new ActivePower(
                sourceModelId,
                sourceTypeName,
                kind,
                source.Card,
                powerAmount,
                secondaryAmount,
                counter,
                sourceInstanceId: source.InstanceId));
        }

        foreach (CardActionFact action in source.Card.Actions.Where(action => action.Kind == "power"))
        {
            string? key = PowerKey(action.Parameter);
            double amount = (double)(action.Amount ?? 0m);
            switch (key)
            {
                case "Strength":
                    state.MutableStrengthSources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    break;
                case "Dexterity":
                    state.MutableDexteritySources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    break;
                case "Fasten":
                    state.MutableFastenSources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    break;
                case "Frail":
                    state.PlayerFrail += Math.Max(0, (int)Math.Round(amount, MidpointRounding.AwayFromZero));
                    break;
                case "Arsenal":
                    AddActivePower(ActivePowerKind.Arsenal, amount);
                    break;
                case "Automation":
                    AddActivePower(ActivePowerKind.Automation, amount, counter: 10);
                    break;
                case "Calamity":
                    AddActivePower(ActivePowerKind.Calamity, amount);
                    break;
                case "Conqueror":
                    AddActivePower(ActivePowerKind.Conqueror, amount);
                    break;
                case "Entropy":
                    AddActivePower(ActivePowerKind.Entropy, amount);
                    break;
                case "Furnace":
                    AddActivePower(ActivePowerKind.Furnace, amount);
                    break;
                case "Genesis":
                    AddActivePower(ActivePowerKind.Genesis, amount);
                    break;
                case "Mayhem":
                    AddActivePower(ActivePowerKind.Mayhem, amount);
                    break;
                case "Monologue":
                    AddActivePower(ActivePowerKind.Monologue, amount);
                    break;
                case "Nostalgia":
                    AddActivePower(ActivePowerKind.Nostalgia, amount);
                    break;
                case "Orbit":
                    AddActivePower(ActivePowerKind.Orbit, amount);
                    break;
                case "PaleBlueDot":
                    AddActivePower(ActivePowerKind.PaleBlueDot, amount);
                    break;
                case "Panache":
                    AddActivePower(ActivePowerKind.Panache, amount);
                    break;
                case "Parry":
                    state.MutableParrySources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    break;
                case "PillarOfCreation":
                    AddActivePower(ActivePowerKind.PillarOfCreation, amount);
                    break;
                case "Plating":
                    AddActivePower(ActivePowerKind.Plating, amount);
                    break;
                case "PrepTime":
                    AddActivePower(ActivePowerKind.PrepTime, amount);
                    break;
                case "RollingBoulder":
                    AddActivePower(ActivePowerKind.RollingBoulder, amount, secondaryAmount: 5d);
                    break;
                case "RetainHand":
                    AddActivePower(ActivePowerKind.RetainHand, amount);
                    break;
                case "SeekingEdge":
                    if (state.SeekingEdgeSources.Count == 0)
                    {
                        state.MutableSeekingEdgeSources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    }
                    break;
                case "SpectrumShift":
                    AddActivePower(ActivePowerKind.SpectrumShift, amount);
                    break;
                case "Stratagem":
                    AddActivePower(ActivePowerKind.Stratagem, amount);
                    break;
                case "SwordSage":
                    state.MutableSwordSageSources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    break;
                case "TheSealedThrone":
                    AddActivePower(ActivePowerKind.TheSealedThrone, amount);
                    break;
                case "TheBomb":
                    AddActivePower(
                        ActivePowerKind.TheBomb,
                        TheBombDamage(source.Card),
                        counter: Math.Max(1, (int)Math.Round(amount, MidpointRounding.AwayFromZero)));
                    break;
                case "Thorns":
                    AddActivePower(ActivePowerKind.Thorns, amount);
                    break;
                case "Tyranny":
                    AddActivePower(ActivePowerKind.Tyranny, amount);
                    break;
                case "Vigor":
                    state.VigorSources.Add(new ResourceSourceCredit(sourceModelId, sourceTypeName, amount));
                    break;
                case "VoidForm":
                    AddActivePower(ActivePowerKind.VoidForm, amount);
                    break;
            }
        }

        ISimulationPowerBehavior? behavior = CreatePowerBehavior(source.Card);
        if (behavior is null)
        {
            return;
        }

        state.ActivePowers.Add(ActivePower.Persistent(
            sourceModelId,
            sourceTypeName,
            source.Card,
            behavior));
    }

    private static ISimulationPowerBehavior? CreatePowerBehavior(SimulationCard card)
    {
        CardActionFact? childOfTheStars = card.Actions.FirstOrDefault(action =>
            action.Kind == "persistentPowerTrigger"
            && action.Parameter == "AfterStarsSpent:gainBlockPerStarSpent");
        if (childOfTheStars is not null)
        {
            return new ChildOfTheStarsBehavior((double)(childOfTheStars.Amount ?? 0m), card.BlockValuePerBlock);
        }

        IReadOnlyList<CardActionFact> blackHoleTriggers = card.Actions
            .Where(action =>
                action.Kind == "persistentPowerTrigger"
                && action.Parameter is
                    "AfterCardPlayed:damageAllEnemiesOnStarSpent"
                    or "AfterStarsGained:damageAllEnemiesOnStarGained")
            .ToArray();
        if (blackHoleTriggers.Count > 0)
        {
            double damage = blackHoleTriggers.Select(action => (double)(action.Amount ?? 0m)).DefaultIfEmpty(0d).Max();
            bool triggersOnStarSpent = blackHoleTriggers.Any(action =>
                action.Parameter == "AfterCardPlayed:damageAllEnemiesOnStarSpent");
            bool triggersOnStarGained = blackHoleTriggers.Any(action =>
                action.Parameter == "AfterStarsGained:damageAllEnemiesOnStarGained");
            return new BlackHoleBehavior(
                damage,
                card.DamageUnitValue,
                card.AoeDamageMultiplier,
                triggersOnStarSpent,
                triggersOnStarGained);
        }

        return null;
    }

    private static string? PowerKey(string? parameter)
    {
        const string prefix = "power:";
        if (parameter is null || !parameter.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string key = parameter[prefix.Length..];
        int separator = key.IndexOf(';', StringComparison.Ordinal);
        return separator >= 0 ? key[..separator] : key;
    }

    private static bool IsPowerCard(SimulationCard card)
    {
        return card.IsPower;
    }

    private static void ResolveBeforeForgeCardActions(
        SimulationState state,
        SimulationCard playedCard,
        DeckSimulationOptions options)
    {
        if (CardBehaviorCatalog.Has(playedCard, CardBehaviorKind.RetrieveSovereignBladesBeforeForge))
        {
            MoveSovereignBladesToHand(state, options.MaxHandSize);
        }
    }

    private static int DynamicForgeAmount(SimulationCard playedCard, SimulationState state)
    {
        if (!CardBehaviorCatalog.Has(playedCard, CardBehaviorKind.DynamicForgeFromAttacksPlayed)
            || state.AttacksPlayedThisTurn <= 0)
        {
            return 0;
        }

        return (int)Math.Round(playedCard.BaseDamage * state.AttacksPlayedThisTurn, MidpointRounding.AwayFromZero);
    }

    private static double TheBombDamage(SimulationCard card)
    {
        return CardBehaviorCatalog.Has(card, CardBehaviorKind.UpgradedBombDamage) && card.UpgradeLevel > 0
            ? 50d
            : 40d;
    }

    private static PowerEventResult ResolveGeneratedCardActions(
        SimulationState state,
        DeckCardInstance source,
        DeckSimulationOptions options)
    {
        return CardBehaviorCatalog.ForCard(source.Card).GeneratedCards switch
        {
            GeneratedCardBehavior.CollisionCourse => GenerateNamedCardsToHand(state, options, "Debris", 1, upgradeGenerated: false),
            GeneratedCardBehavior.CrashLanding => GenerateNamedCardsToHand(
                state,
                options,
                "Debris",
                Math.Max(0, options.MaxHandSize - state.Hand.Count),
                upgradeGenerated: false),
            GeneratedCardBehavior.BundleOfJoy => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                "bundleOfJoy.colorless",
                3 + source.Card.UpgradeLevel,
                distinct: true,
                upgradeGenerated: false),
            GeneratedCardBehavior.ManifestAuthority => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                "manifestAuthority.colorless",
                1,
                distinct: true,
                upgradeGenerated: source.Card.UpgradeLevel > 0),
            GeneratedCardBehavior.Quasar => GenerateBestCardFromGeneratedChoices(
                state,
                options,
                "quasar.colorless",
                3,
                upgradeGenerated: source.Card.UpgradeLevel > 0),
            GeneratedCardBehavior.JackOfAllTrades => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                "jackOfAllTrades.colorless",
                1 + source.Card.UpgradeLevel,
                distinct: true,
                upgradeGenerated: false),
            GeneratedCardBehavior.Discovery => GenerateBestCardFromGeneratedChoices(
                state,
                options,
                "discovery.regent",
                3,
                upgradeGenerated: false,
                freeThisTurn: true),
            GeneratedCardBehavior.Jackpot => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                "jackpot.regent.zeroCost",
                3,
                distinct: true,
                upgradeGenerated: source.Card.UpgradeLevel > 0),
            GeneratedCardBehavior.HeirloomHammer => CopyBestColorlessCardToHand(state),
            GeneratedCardBehavior.Splash => GenerateBestCardFromGeneratedChoices(
                state,
                options,
                "splash.otherHeroes.attack",
                3,
                upgradeGenerated: source.Card.UpgradeLevel > 0,
                freeThisTurn: true),
            _ => ResolveExplicitGeneratedCardActions(state, source, options)
        };
    }

    private static PowerEventResult ResolveExplicitGeneratedCardActions(
        SimulationState state,
        DeckCardInstance source,
        DeckSimulationOptions options)
    {
        // P8: this is the default arm reached for every played card, and the overwhelming majority
        // have no createCard action. Use a plain foreach (no Where iterator) and allocate the result
        // lists + dedupe set only once a real createCard-to-hand target is found. Output-identical:
        // the dedupe set is still consulted for every valid target in encounter order.
        List<PowerResolution>? resolutions = null;
        List<CardValueCreditEvent>? credits = null;
        HashSet<string>? generatedTargets = null;
        foreach (CardActionFact action in source.Card.Actions)
        {
            if (action.Kind != "createCard")
            {
                continue;
            }

            IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
            string? target = GetParameter(parameters, "card");
            string? pile = GetParameter(parameters, "pile");
            if (string.IsNullOrWhiteSpace(target) || !string.Equals(pile, "Hand", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            generatedTargets ??= new(StringComparer.OrdinalIgnoreCase);
            if (!generatedTargets.Add(target))
            {
                continue;
            }

            PowerEventResult result = GenerateNamedCardsToHand(
                state,
                options,
                target,
                Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero)),
                upgradeGenerated: false);
            (resolutions ??= []).AddRange(result.PowerResolutions);
            (credits ??= []).AddRange(result.ValueCredits);
        }

        return resolutions is null ? PowerEventResult.Empty : new PowerEventResult(resolutions, credits ?? []);
    }

    private static PowerEventResult GenerateCardsToHandFromGeneratedPool(
        SimulationState state,
        DeckSimulationOptions options,
        string poolId,
        int count,
        bool distinct,
        bool upgradeGenerated)
    {
        if (count <= 0)
        {
            return PowerEventResult.Empty;
        }

        List<SimulationCard> candidates = ResolveGeneratedPoolCandidates(options, poolId, upgradeGenerated);
        if (candidates.Count == 0)
        {
            return PowerEventResult.Empty;
        }

        options.ActiveSearchTurnProfile?.RecordGeneratedPool(poolId, count);

        List<PowerResolution>? resolutions = null;
        List<CardValueCreditEvent>? credits = null;
        if (distinct)
        {
            // The game implements GetDistinctForCombat by shuffling the complete eligible pool,
            // then taking the requested prefix. Preserve both its distinctness and RNG-consumption
            // semantics: one call advances CombatCardGeneration by pool.Count - 1, even for count 1.
            SimulationCard[] candidateBuffer = ArrayPool<SimulationCard>.Shared.Rent(candidates.Count);
            try
            {
                candidates.CopyTo(candidateBuffer, 0);
                Shuffle(candidateBuffer, candidates.Count, ref state.CombatCardGenerationRandom);
                int selectedCount = Math.Min(count, candidates.Count);
                for (int index = 0; index < selectedCount; index++)
                {
                    AddGeneratedCardResult(
                        state,
                        candidateBuffer[index],
                        ref resolutions,
                        ref credits);
                }
            }
            finally
            {
                Array.Clear(candidateBuffer, 0, candidates.Count);
                ArrayPool<SimulationCard>.Shared.Return(candidateBuffer);
            }
        }
        else
        {
            // The game's GetForCombat samples once per result and allows duplicates.
            for (int i = 0; i < count; i++)
            {
                AddGeneratedCardResult(
                    state,
                    candidates[state.CombatCardGenerationRandom.Next(candidates.Count)],
                    ref resolutions,
                    ref credits);
            }
        }

        return resolutions is null && credits is null
            ? PowerEventResult.Empty
            : new PowerEventResult(resolutions ?? [], credits ?? []);
    }

    private static PowerEventResult GenerateBestCardFromGeneratedChoices(
        SimulationState state,
        DeckSimulationOptions options,
        string poolId,
        int choiceCount,
        bool upgradeGenerated,
        bool freeThisTurn = false)
    {
        List<SimulationCard> candidates = ResolveGeneratedPoolCandidates(options, poolId, upgradeGenerated);
        if (candidates.Count == 0 || choiceCount <= 0)
        {
            return PowerEventResult.Empty;
        }

        options.ActiveSearchTurnProfile?.RecordGeneratedPool(poolId, 1);

        // Splash, Quasar, Discovery and their peers all call GetDistinctForCombat in the game, so
        // their candidate screens consume the shared generation stream identically. Reuse a pooled
        // buffer instead of allocating a complete List plus OrderBy's sort structures per branch.
        SimulationCard[] candidateBuffer = ArrayPool<SimulationCard>.Shared.Rent(candidates.Count);
        try
        {
            candidates.CopyTo(candidateBuffer, 0);
            Shuffle(candidateBuffer, candidates.Count, ref state.CombatCardGenerationRandom);
            int selectedCount = Math.Min(choiceCount, candidates.Count);
            SimulationCard best = candidateBuffer[0];
            double bestScore = CardSearchScore(best);
            for (int index = 1; index < selectedCount; index++)
            {
                SimulationCard candidate = candidateBuffer[index];
                double score = CardSearchScore(candidate);
                if (score > bestScore
                    || (score == bestScore
                        && string.CompareOrdinal(candidate.TypeName, best.TypeName) < 0))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return AddGeneratedCardToHand(state, best, freeThisTurn);
        }
        finally
        {
            Array.Clear(candidateBuffer, 0, candidates.Count);
            ArrayPool<SimulationCard>.Shared.Return(candidateBuffer);
        }
    }

    private static void AddGeneratedCardResult(
        SimulationState state,
        SimulationCard card,
        ref List<PowerResolution>? resolutions,
        ref List<CardValueCreditEvent>? credits)
    {
        PowerEventResult result = AddGeneratedCardToHand(state, card);
        if (result.PowerResolutions.Count > 0)
        {
            (resolutions ??= []).AddRange(result.PowerResolutions);
        }

        if (result.ValueCredits.Count > 0)
        {
            (credits ??= []).AddRange(result.ValueCredits);
        }
    }

    private static PowerEventResult GenerateNamedCardsToHand(
        SimulationState state,
        DeckSimulationOptions options,
        string typeName,
        int count,
        bool upgradeGenerated)
    {
        if (count <= 0)
        {
            return PowerEventResult.Empty;
        }

        SimulationCard card = ResolveGeneratedCard(options, typeName, upgradeGenerated);
        options.ActiveSearchTurnProfile?.RecordGeneratedPool($"named:{typeName}", count);
        List<PowerResolution>? resolutions = null;
        List<CardValueCreditEvent>? credits = null;
        for (int i = 0; i < count; i++)
        {
            AddGeneratedCardResult(state, card, ref resolutions, ref credits);
            if (state.Hand.Count >= state.MaxHandSize)
            {
                break;
            }
        }

        return resolutions is null && credits is null
            ? PowerEventResult.Empty
            : new PowerEventResult(resolutions ?? [], credits ?? []);
    }

    // Resolved generated-card pool candidates, cached per (library, poolId, upgradeGenerated). The
    // resolution depends only on those and the returned list is read-only for callers, so it is built
    // once per library and reused. Without this cache every generation event - Quasar/Discovery/Jackpot
    // plays, and (worse) the per-turn Calamity/SpectrumShift/etc. powers living in the base decks -
    // re-scanned the whole card library for each of the pool's 18-78 typeNames (O(poolSize x library)),
    // which dominated run time once the pools were expanded to the full simulatable set.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<SimulationCard>, System.Collections.Concurrent.ConcurrentDictionary<string, List<SimulationCard>>> GeneratedPoolCandidateCache = new();

    private static List<SimulationCard> ResolveGeneratedPoolCandidates(
        DeckSimulationOptions options,
        string poolId,
        bool upgradeGenerated)
    {
        System.Collections.Concurrent.ConcurrentDictionary<string, List<SimulationCard>> byKey =
            GeneratedPoolCandidateCache.GetValue(options.CardLibrary, static _ => new(StringComparer.Ordinal));
        string key = upgradeGenerated ? poolId + "#u" : poolId;
        return byKey.GetOrAdd(key, _ => options.GeneratedCardPools
            .RequirePool(poolId)
            .Select(typeName => ResolveGeneratedCard(options, typeName, upgradeGenerated))
            .ToList());
    }

    private static SimulationCard ResolveGeneratedCard(
        DeckSimulationOptions options,
        string typeName,
        bool upgradeGenerated)
    {
        if (upgradeGenerated
            && TryFindSimulationCard(options.CardLibrary, UpgradeTargetName(typeName, 1), out SimulationCard? upgradedCard))
        {
            return upgradedCard!;
        }

        if (TryFindSimulationCard(options.CardLibrary, typeName, out SimulationCard? card))
        {
            return card!;
        }

        if (string.Equals(typeName, "Debris", StringComparison.OrdinalIgnoreCase))
        {
            return CreateGeneratedDebris();
        }

        throw new InvalidOperationException($"Generated card '{typeName}' is missing from the simulation card library.");
    }

    private static PowerEventResult AddGeneratedCardToHand(
        SimulationState state,
        SimulationCard card,
        bool freeThisTurn = false)
    {
        if (state.Hand.Count >= state.MaxHandSize)
        {
            return PowerEventResult.Empty;
        }

        DeckCardInstance generated = new(state.NextGeneratedInstanceId++, card)
        {
            FreeThisTurn = freeThisTurn
        };
        state.InvalidateFutureTurnOpportunityProfile();
        state.Hand.Add(generated);
        state.GeneratedCardsCreated++;
        return ResolveGeneratedCardPowers(state, card);
    }

    private static PowerEventResult CopyBestColorlessCardToHand(SimulationState state)
    {
        DeckCardInstance? selected = state.Hand
            .Where(card => card.Card.Pools.Contains("Colorless", StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(card => CardObjectChoiceScore(card))
            .ThenBy(card => card.InstanceId)
            .FirstOrDefault();
        return selected is null
            ? PowerEventResult.Empty
            : AddGeneratedCardToHand(state, selected.Card);
    }

    private static void MoveSovereignBladesToHand(SimulationState state, int maxHandSize)
    {
        foreach (SimulationCardPile pile in new[] { state.DrawPile, state.DiscardPile, state.ExhaustPile })
        {
            IReadOnlyList<DeckCardInstance> blades = pile
                .Where(card => IsSovereignBlade(card.Card))
                .Take(Math.Max(0, maxHandSize - state.Hand.Count))
                .ToArray();
            if (ReferenceEquals(pile, state.ExhaustPile) && blades.Count > 0)
            {
                state.InvalidateFutureTurnOpportunityProfile();
            }

            foreach (DeckCardInstance blade in blades)
            {
                pile.Remove(blade);
                state.Hand.Add(blade);
                ArmGuaranteedSearchAdmission([blade]);
            }

            if (state.Hand.Count >= maxHandSize)
            {
                return;
            }
        }
    }

    private static string CalamityGeneratedPoolId(SimulationState state)
    {
        string character = string.IsNullOrWhiteSpace(state.CharacterPoolName)
            ? "regent"
            : state.CharacterPoolName.ToLowerInvariant();
        return $"calamity.{character}.attack";
    }

    private static PowerEventResult ResolveGeneratedCardPowers(SimulationState state, SimulationCard generatedCard)
    {
        List<PowerResolution>? resolutions = null;
        foreach (ActivePower power in state.ActivePowers)
        {
            switch (power.Kind)
            {
                case ActivePowerKind.Arsenal:
                    state.MutableStrengthSources.Add(new ResourceSourceCredit(
                        power.SourceModelId,
                        power.SourceTypeName,
                        power.Amount));
                    break;
                case ActivePowerKind.PillarOfCreation:
                    (resolutions ??= []).Add(new PowerResolution(
                        power.SourceModelId,
                        power.SourceTypeName,
                        power.Amount * power.SourceCard.BlockValuePerBlock));
                    break;
            }
        }

        return resolutions is null
            ? PowerEventResult.Empty
            : new PowerEventResult(resolutions, []);
    }

    private static PowerEventResult ApplyForge(SimulationState state, int amount, DeckCardInstance source, int sourcePlayId)
    {
        if (amount <= 0)
        {
            return PowerEventResult.Empty;
        }

        PowerEventResult generatedResult = PowerEventResult.Empty;
        IReadOnlyList<DeckCardInstance> unexhaustedBlades = NonExhaustCards(state)
            .Where(card => IsSovereignBlade(card.Card))
            .ToArray();
        if (unexhaustedBlades.Count == 0)
        {
            generatedResult = AddGeneratedCardToHand(
                state,
                CreateGeneratedSovereignBlade(
                    source.Card.DamageUnitValue,
                    source.Card.BlockValuePerBlock,
                    source.Card.AoeDamageMultiplier));
        }

        ForgeSourceCredit sourceCredit = new(
            SourceModelId(source.Card),
            SourceTypeName(source.Card),
            sourcePlayId,
            amount);
        ApplyForgeCredit(state, amount, sourceCredit);
        return generatedResult;
    }

    private static PowerEventResult ApplyForgeFromPower(SimulationState state, int amount, ActivePower source)
    {
        if (amount <= 0)
        {
            return PowerEventResult.Empty;
        }

        PowerEventResult generatedResult = PowerEventResult.Empty;
        IReadOnlyList<DeckCardInstance> unexhaustedBlades = NonExhaustCards(state)
            .Where(card => IsSovereignBlade(card.Card))
            .ToArray();
        if (unexhaustedBlades.Count == 0)
        {
            generatedResult = AddGeneratedCardToHand(
                state,
                CreateGeneratedSovereignBlade(
                    source.SourceCard.DamageUnitValue,
                    source.SourceCard.BlockValuePerBlock,
                    source.SourceCard.AoeDamageMultiplier));
        }

        ForgeSourceCredit sourceCredit = new(
            source.SourceModelId,
            source.SourceTypeName,
            -1,
            amount);
        ApplyForgeCredit(state, amount, sourceCredit);
        return generatedResult;
    }

    private static void ApplyForgeCredit(SimulationState state, int amount, ForgeSourceCredit sourceCredit)
    {
        bool changed = false;
        foreach (DeckCardInstance blade in AllCards(state).Where(card => IsSovereignBlade(card.Card)))
        {
            changed = true;
            blade.Card = blade.Card with
            {
                IntrinsicValue = blade.Card.IntrinsicValue + amount,
                StaticEstimatedValue = blade.Card.StaticEstimatedValue + amount,
                DamageValue = blade.Card.DamageValue + amount,
                BaseDamage = blade.Card.BaseDamage + amount
            };
            blade.AddForgeCredit(sourceCredit);
        }

        if (changed)
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }
    }

    private static IEnumerable<DeckCardInstance> AllCards(SimulationState state)
    {
        return state.DrawPile
            .Concat(state.Hand)
            .Concat(state.DiscardPile)
            .Concat(state.ExhaustPile);
    }

    // P4: cards in the active piles (draw/hand/discard), i.e. everything except the exhaust pile.
    // Equivalent to AllCards(state).Where(c => c is not in ExhaustPile) - instance ids are unique so
    // a non-exhaust card can never share an id with an exhaust card - but without the per-card
    // O(exhaust) rescan the old code did.
    private static IEnumerable<DeckCardInstance> NonExhaustCards(SimulationState state)
    {
        foreach (DeckCardInstance card in state.DrawPile)
        {
            yield return card;
        }

        foreach (DeckCardInstance card in state.Hand)
        {
            yield return card;
        }

        foreach (DeckCardInstance card in state.DiscardPile)
        {
            yield return card;
        }
    }

    private static bool IsSovereignBlade(SimulationCard card)
    {
        return CardBehaviorCatalog.Has(card, CardBehaviorKind.SovereignBlade);
    }

    private static SimulationCard CreateGeneratedSovereignBlade(
        double damageUnitValue,
        double blockValuePerBlock,
        double aoeDamageMultiplier)
    {
        return new SimulationCard
        {
            ModelId = "GENERATED.SOVEREIGN_BLADE",
            TypeName = "SovereignBlade",
            FullTypeName = "MegaCrit.Sts2.Core.Models.Cards.SovereignBlade",
            Cost = 2,
            CardType = "Attack",
            Rarity = "Token",
            TargetType = "AnyEnemy",
            Layer = 1,
            StaticEstimatedValue = 10d,
            IntrinsicValue = 10d,
            DamageValue = 10d,
            BaseDamage = 10d,
            DamageModifierMultiplier = 1d,
            DamageUnitValue = damageUnitValue,
            EnergyCost = 2,
            BlockValuePerBlock = blockValuePerBlock,
            AoeDamageMultiplier = aoeDamageMultiplier,
            Retain = true,
            Confidence = 0.75,
            Warnings = ["Generated by simplified Forge simulation."]
        };
    }

    private static SimulationCard CreateGeneratedDebris()
    {
        return new SimulationCard
        {
            ModelId = "CARD.DEBRIS",
            TypeName = "Debris",
            FullTypeName = "MegaCrit.Sts2.Core.Models.Cards.Debris",
            Cost = 1,
            CardType = "Status",
            Rarity = "Status",
            TargetType = "None",
            Layer = 1,
            StaticEstimatedValue = 0d,
            IntrinsicValue = 0d,
            BeamSetupValue = 12.5d,
            DamageUnitValue = 1d,
            EnergyCost = 1,
            Exhausts = true,
            Confidence = 0.75,
            Warnings = ["Generated Debris fallback: 1-cost, 0-value, exhausts when played."]
        };
    }

    private static SimulationCard CreateGenericTransformedCard()
    {
        return new SimulationCard
        {
            ModelId = "SIM.TRANSFORMED_CARD",
            TypeName = "SimTransformedCard",
            FullTypeName = "CardValueOverlay.Modeling.Simulation.SimTransformedCard",
            Cost = 1,
            CardType = "Attack",
            Rarity = "Simulation",
            TargetType = "AnyEnemy",
            Layer = 1,
            StaticEstimatedValue = 11d,
            IntrinsicValue = 11d,
            DamageValue = 11d,
            DamageUnitValue = 1d,
            EnergyCost = 1,
            Confidence = 0.5,
            Warnings = ["Generic transform placeholder: 1-cost card worth 11 damage-equivalent value."]
        };
    }

    private static string UpgradeTargetName(string typeName, int upgradeLevel)
    {
        if (upgradeLevel <= 0 || typeName.Contains('+', StringComparison.Ordinal))
        {
            return typeName;
        }

        return $"{typeName}+{upgradeLevel}";
    }

    private static bool HasXCostDamage(SimulationCard card)
    {
        return GetCardDerived(card).HasXCostDamage;
    }

    private static bool IsXCostDamageAction(CardActionFact action)
    {
        return action.Kind == "xCostDamage"
            && string.Equals(action.Parameter, "energyX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPowerAction(SimulationCard card, string powerKey)
    {
        return card.Actions.Any(action =>
            action.Kind == "power"
            && string.Equals(PowerKey(action.Parameter), powerKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHeavenlyDrill(SimulationCard card)
    {
        return CardBehaviorCatalog.Has(card, CardBehaviorKind.HeavenlyDrillMinimumEnergy);
    }

    private static bool ReturnsPlayedCardToDrawTop(SimulationCard card)
    {
        // P6: called for EVERY played card to decide placement; avoid the LINQ Where allocation and
        // (via P5) the per-play string split. Plain scan over the card's (few) actions.
        foreach (CardActionFact action in card.Actions)
        {
            if (action.Kind is not ("moveCardBetweenPiles" or "selfReturn"))
            {
                continue;
            }

            if (!string.Equals(action.Source, "CardPileCmd.Add", StringComparison.Ordinal))
            {
                continue;
            }

            IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
            if (string.Equals(GetParameter(parameters, "to"), "Draw", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetParameter(parameters, "position"), "Top", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void MovePlayedCardToResultPile(
        SimulationState state,
        DeckCardInstance instance,
        SimulationCard playedCard,
        SimulationCard? transformedPlayedCard,
        int attackSkillPlaysBeforePlay)
    {
        instance.BonusUntilPlayedCostReduction = 0;
        if (transformedPlayedCard is not null
            || HasEffectiveExhaust(playedCard)
            || IsPowerCard(playedCard))
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }

        if (transformedPlayedCard is not null)
        {
            instance.Card = transformedPlayedCard;
            state.DiscardPile.Add(instance);
        }
        else if (HasEffectiveExhaust(playedCard) || IsPowerCard(playedCard))
        {
            state.ExhaustPile.Add(instance);
        }
        else if (ReturnsPlayedCardToDrawTop(playedCard)
            || ShouldNostalgiaReturnPlayedCardToDrawTop(state, playedCard, attackSkillPlaysBeforePlay))
        {
            state.DrawPile.Insert(0, instance);
        }
        else
        {
            state.DiscardPile.Add(instance);
        }
    }

    private static bool ShouldNostalgiaReturnPlayedCardToDrawTop(
        SimulationState state,
        SimulationCard playedCard,
        int attackSkillPlaysBeforePlay)
    {
        if (!playedCard.IsAttack && !IsSkillCard(playedCard))
        {
            return false;
        }

        double amount = 0d;
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind == ActivePowerKind.Nostalgia)
            {
                amount += power.Amount;
            }
        }

        return amount > attackSkillPlaysBeforePlay;
    }

    private static bool IsSkillCard(SimulationCard card)
    {
        return string.Equals(card.CardType, "Skill", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanPlay(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions? options = null,
        int instanceId = -1,
        int drawCostReduction = 0,
        int? costOverrideThisCombat = null,
        int untilPlayedCostReduction = 0,
        bool freeThisTurn = false,
        int turn = 1)
    {
        if (instanceId >= 0
            && options?.BlockedPlayInstanceIds.Count > 0
            && options.BlockedPlayInstanceIds.Contains(instanceId))
        {
            return false;
        }

        if (options?.BlockedPlayModelIds.Count > 0
            && options.BlockedPlayModelIds.Any(blocked => MatchesReportedModelId(card.ReportModelId, blocked)))
        {
            return false;
        }

        if (options is not null
            && !HasRequiredTransformTarget(card, state, options, instanceId, turn))
        {
            return false;
        }

        if (IsHeavenlyDrill(card) && state.Energy < 4)
        {
            return false;
        }

        return card.IsPlayable
            && EffectiveEnergyCost(
                card,
                state,
                drawCostReduction,
                freeThisTurn,
                costOverrideThisCombat,
                untilPlayedCostReduction) <= state.Energy
            && EffectiveStarCost(card, state) <= EffectiveAvailableStarsForPlay(state);
    }

    private static bool HasRequiredTransformTarget(
        SimulationCard sourceCard,
        SimulationState state,
        DeckSimulationOptions options,
        int sourceInstanceId,
        int turn)
    {
        CardTransformBehavior? behavior = CardBehaviorCatalog.ForCard(sourceCard).Transform;
        if (behavior?.RequireTargetToPlay != true)
        {
            return true;
        }

        if (options.Turns - turn < behavior.MinimumFutureTurnsToPlay)
        {
            return false;
        }

        foreach (CardActionFact action in sourceCard.Actions)
        {
            if (!string.Equals(action.Kind, "transformCard", StringComparison.Ordinal))
            {
                continue;
            }

            IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
            string? fromPileName = GetParameter(parameters, "from");
            SimulationCardPile? fromPile = fromPileName is null
                ? null
                : TryGetPile(state, fromPileName);
            if (fromPile is null)
            {
                continue;
            }

            int requiredCount = behavior.RequireFullTransformCountToPlay
                ? Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero))
                : TransformCount(sourceCard, action, fromPile.Count);
            if (requiredCount <= 0)
            {
                continue;
            }

            SimulationCard replacement = ResolveTransformReplacement(parameters, options, sourceCard);
            double replacementScore = CardContinuationValue(replacement, state, options, turn);
            List<DeckCardInstance> eligible = [];
            foreach (DeckCardInstance candidate in fromPile)
            {
                if (candidate.InstanceId == sourceInstanceId)
                {
                    continue;
                }

                double candidateScore = CardContinuationValue(candidate, state, options, turn);
                if (IsEligibleTransformTarget(
                        candidate,
                        behavior,
                        candidateScore,
                        replacementScore,
                        state,
                        options,
                        turn))
                {
                    eligible.Add(candidate);
                }
            }

            if (eligible.Count < requiredCount)
            {
                continue;
            }

            int targetBranchWidth = CardBehaviorCatalog.ForCard(sourceCard)
                .CardObjectDecision?.TargetBranchWidth ?? 1;
            if (BuildTransformTargetPlans(
                    eligible,
                    requiredCount,
                    behavior,
                    state,
                    options,
                    turn,
                    targetBranchWidth).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int EffectiveEnergyCost(
        SimulationCard card,
        SimulationState state,
        int drawCostReduction = 0,
        bool freeThisTurn = false,
        int? costOverrideThisCombat = null,
        int untilPlayedCostReduction = 0)
    {
        if (freeThisTurn)
        {
            return 0;
        }

        if (HasXCostDamage(card))
        {
            return Math.Max(0, state.Energy);
        }

        if (IsVoidFormFreeCard(state))
        {
            return 0;
        }

        int baseCost = HasEnchantment(card, "TEZCATARAS_EMBER")
            ? 0
            : costOverrideThisCombat ?? card.EnergyCost;
        // KinglyKick and SlumberingEssence lower this instance's cost (never below 0).
        return Math.Max(0, baseCost - drawCostReduction - untilPlayedCostReduction);
    }

    private static int EffectiveEnergyCost(DeckCardInstance card, SimulationState state)
    {
        return EffectiveEnergyCost(
            card.Card,
            state,
            card.BonusDrawCostReduction,
            card.FreeThisTurn,
            card.CostOverrideThisCombat,
            card.BonusUntilPlayedCostReduction);
    }

    private static int EffectiveStarCost(SimulationCard card, SimulationState state)
    {
        if (IsVoidFormFreeCard(state))
        {
            return 0;
        }

        return card.StarCost;
    }

    private static int EffectiveAvailableStarsForPlay(SimulationState state)
    {
        // P8: reached from CanPlay for every hand card at every search node; avoid the LINQ
        // Where(...).Sum(...) iterator allocation (fires even when ActivePowers is empty).
        int bonus = 0;
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind == ActivePowerKind.TheSealedThrone)
            {
                bonus += (int)power.Amount;
            }
        }

        return state.Stars + bonus;
    }

    private static bool IsVoidFormFreeCard(SimulationState state)
    {
        // P8: reached from EffectiveEnergyCost/EffectiveStarCost via CanPlay per hand card per node.
        double freeCards = 0d;
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind == ActivePowerKind.VoidForm)
            {
                freeCards += power.Amount;
            }
        }

        return freeCards > 0d && state.CardsPlayedThisTurn < freeCards;
    }

    private static double CardSearchScore(SimulationCard card)
    {
        return Math.Max(
                card.StaticEstimatedValue,
                card.IntrinsicValue + ExplicitResourceReferenceValue(card, MidlineResourceReferenceValues))
            + BeamSetupDecisionValue(card)
            + (card.Exhausts ? 0.01d : 0d)
            + (card.Retain ? 0.005d : 0d);
    }

    private static double CardSearchScore(SimulationCard card, SimulationState state)
    {
        return EstimateSearchScore(card, state, MidlineResourceReferenceValues);
    }

    private static double CardSearchScore(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EstimateSearchScore(card, state, ResourceReferenceValuesForTurns(options.Turns));
    }

    private static double CardSearchScore(DeckCardInstance card, SimulationState state)
    {
        return EstimateSearchScore(card, state, MidlineResourceReferenceValues, options: null);
    }

    private static double CardSearchScore(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EstimateSearchScore(card, state, ResourceReferenceValuesForTurns(options.Turns), options);
    }

    private static double CardSearchScore(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon)
    {
        return EstimateSearchScore(
            card,
            state,
            ResourceReferenceValuesForTurns(horizon.RemainingTurns),
            options,
            horizon.HasFutureTurn);
    }

    private static double EstimateSearchScore(
        DeckCardInstance instance,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues,
        DeckSimulationOptions? options,
        bool includeNextTurn = true)
    {
        return EstimateSearchScore(instance.Card, state, resourceReferenceValues, includeNextTurn)
            + EnchantmentBeamSetupDecisionValue(instance, state, resourceReferenceValues, options);
    }

    private static double EstimateSearchScore(
        SimulationCard card,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues,
        bool includeNextTurn = true)
    {
        double immediateValue = EstimateImmediateSearchValue(card, state, XCostEnergy(card, state));
        double resourceAndNextTurnValue = EstimateResourceAndNextTurnSearchValue(
            card,
            resourceReferenceValues,
            includeNextTurn);
        return immediateValue
            + resourceAndNextTurnValue
            + BeamSetupDecisionValue(card, state, resourceReferenceValues)
            + SearchTieBreak(card);
    }

    private static double EstimateImmediateSearchValue(SimulationCard card, SimulationState state, int energyForXCost)
    {
        double xCostDamageValue = XCostDamageValue(card, state, energyForXCost);
        double scalingDamageValue = DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: false);
        double directDamageValue = card.DamageValue + scalingDamageValue + xCostDamageValue;
        double directValue = card.IntrinsicValue
            + scalingDamageValue
            + xCostDamageValue
            + VulnerableBonus(directDamageValue, state)
            + ReflectApproximationValue(card)
            + StrengthLossDefenseValue(card)
            + HpLossPenaltyValue(card)
            + FrailBlockPenaltyValue(card, state);

        double modifierValue = EstimateSovereignBladePowerValue(card, state);
        double damageModifierMultiplier = EffectiveDamageModifierMultiplier(card, state)
            + XCostDamageModifierMultiplier(card, state, energyForXCost);
        if (card.IsAttack && damageModifierMultiplier > 0d)
        {
            modifierValue += (SumSources(state.StrengthSources) + SumSources(state.VigorSources))
                * damageModifierMultiplier
                * card.DamageUnitValue;
        }

        if (card.BaseBlock > 0d && card.BlockEffectCount > 0)
        {
            double blockModifierValue = SumSources(state.DexteritySources) * card.BlockEffectCount * card.BlockValuePerBlock;
            if (card.HasTag("Defend"))
            {
                blockModifierValue += SumSources(state.FastenSources) * card.BlockEffectCount * card.BlockValuePerBlock;
            }

            modifierValue += blockModifierValue;
        }

        if (IsSovereignBlade(card) && HasActivePower(state, ActivePowerKind.Conqueror))
        {
            modifierValue += directValue + modifierValue;
        }

        int starCost = EffectiveStarCost(card, state);
        double starTriggerValue = 0d;
        if (starCost > 0)
        {
            starTriggerValue += DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarSpent, starCost))
                .Sum(resolution => resolution.Value);
        }

        if (card.StarGain > 0)
        {
            starTriggerValue += DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, card.StarGain))
                .Sum(resolution => resolution.Value);
        }

        return directValue + modifierValue + starTriggerValue + EstimateForgeSearchValue(card, state);
    }

    private static double EstimateSovereignBladePowerValue(SimulationCard card, SimulationState state)
    {
        if (!IsSovereignBlade(card))
        {
            return 0d;
        }

        double baseDamage = card.BaseDamage > 0d ? card.BaseDamage : card.DamageValue;
        double targetMultiplier = SovereignBladeTargetMultiplier(card, state);
        double value = 0d;
        if (targetMultiplier > 1d)
        {
            value += SumSources(state.SeekingEdgeSources) * baseDamage * (targetMultiplier - 1d) * card.DamageUnitValue;
        }

        value += SumSources(state.SwordSageSources) * baseDamage * targetMultiplier * card.DamageUnitValue;
        value += SumSources(state.ParrySources) * card.BlockValuePerBlock;
        return value;
    }

    private static double EstimateForgeSearchValue(SimulationCard card, SimulationState state)
    {
        int forgeAmount = card.Forge + DynamicForgeAmount(card, state);
        if (forgeAmount <= 0)
        {
            return 0d;
        }

        int activeBladeCount = 0;
        foreach (DeckCardInstance instance in NonExhaustCards(state))
        {
            if (IsSovereignBlade(instance.Card))
            {
                activeBladeCount++;
            }
        }
        int valuedBladeCount = activeBladeCount == 0 ? 1 : Math.Min(3, activeBladeCount);
        return forgeAmount * valuedBladeCount;
    }

    private static double EstimateResourceAndNextTurnSearchValue(
        SimulationCard card,
        ExplicitResourceReferenceValues resourceReferenceValues,
        bool includeNextTurn = true)
    {
        return ExplicitResourceReferenceValue(card, resourceReferenceValues, includeNextTurn)
            + (includeNextTurn ? card.BlockNextTurn * card.BlockValuePerBlock : 0d);
    }

    private static double EstimateAveragePlayableCardValue(SimulationState state)
    {
        SimulationCard[] candidates = AllCards(state)
            .Select(instance => instance.Card)
            .Where(card => card.IsPlayable)
            .ToArray();
        if (candidates.Length == 0)
        {
            return 0d;
        }

        return candidates
            .Select(card => Math.Max(0d, Math.Max(card.IntrinsicValue, card.StaticEstimatedValue)))
            .OrderDescending()
            .Take(Math.Min(5, candidates.Length))
            .DefaultIfEmpty(0d)
            .Average();
    }

    [Flags]
    private enum DynamicSetupSlot
    {
        Beam = 1,
        Play = 2
    }

    private sealed record DynamicSetupRule(
        string Key,
        DynamicSetupSlot Slots,
        Func<SimulationCard, bool> AppliesTo,
        Func<SimulationCard, SimulationState, ExplicitResourceReferenceValues, double> Score);

    private static readonly IReadOnlyList<DynamicSetupRule> DynamicSetupRules =
    [
        new(
            CardBehaviorCatalog.AnointedRareDrawAverageDecisionValue,
            DynamicSetupSlot.Beam | DynamicSetupSlot.Play,
            IsAnointed,
            static (_, state, resourceReferenceValues) =>
                EstimateAverageRareDrawPileDecisionValue(state, resourceReferenceValues))
    ];

    private static double BeamSetupDecisionValue(SimulationCard card)
    {
        return card.BeamSetupValue;
    }

    private static double BeamSetupDecisionValue(
        SimulationCard card,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        return BeamSetupDecisionValue(card)
            + DynamicSetupDecisionValue(
                card,
                state,
                resourceReferenceValues,
                DynamicSetupSlot.Beam,
                includeDynamicSetup: true);
    }

    // Enchantment Beam setup is instance-aware and affects candidate ordering only. It must never
    // mutate state or be added to PlayCard's realized/decision value. Explicit zero cases are kept in
    // the switch so every runtime-supported enchantment has an auditable policy instead of silently
    // falling through when the supported list grows.
    private static double EnchantmentBeamSetupDecisionValue(
        DeckCardInstance instance,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EnchantmentBeamSetupDecisionValue(
            instance,
            state,
            ResourceReferenceValuesForTurns(options.Turns),
            options);
    }

    private static double EnchantmentBeamSetupDecisionValue(
        DeckCardInstance instance,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues,
        DeckSimulationOptions? options)
    {
        SimulationEnchantment? enchantment = instance.Card.Enchantment;
        if (enchantment is null || !enchantment.IsRuntimeSupported)
        {
            return 0d;
        }

        if (!EnchantmentBeamRules.TryGetValue(enchantment.Key, out EnchantmentBeamRule rule))
        {
            return 0d;
        }

        return rule switch
        {
            EnchantmentBeamRule.Immediate => EnchantmentImmediateMarginalValue(instance, state),
            EnchantmentBeamRule.GlamReplay => instance.EnchantmentDisabled
                ? 0d
                : EstimateFreeReplayBeamValue(instance, state, resourceReferenceValues),
            EnchantmentBeamRule.SlumberingCost => SlumberingEssenceBeamValue(instance, state, resourceReferenceValues),
            EnchantmentBeamRule.SownEnergy => instance.EnchantmentDisabled
                ? 0d
                : EnchantmentAmount(instance) * resourceReferenceValues.Energy,
            EnchantmentBeamRule.SpiralReplay => EstimateFreeReplayBeamValue(instance, state, resourceReferenceValues),
            EnchantmentBeamRule.SwiftDraw => instance.EnchantmentDisabled
                ? 0d
                : EffectiveEnchantmentDrawCount(instance, state, options) * resourceReferenceValues.Draw,
            EnchantmentBeamRule.TezcatarasEmber => EnchantmentImmediateMarginalValue(instance, state)
                + TezcatarasEmberCostBeamValue(instance, state, resourceReferenceValues),
            _ => 0d
        };
    }

    private enum EnchantmentBeamRule
    {
        Zero,
        Immediate,
        GlamReplay,
        SlumberingCost,
        SownEnergy,
        SpiralReplay,
        SwiftDraw,
        TezcatarasEmber
    }

    private static readonly IReadOnlyDictionary<string, EnchantmentBeamRule> EnchantmentBeamRules =
        new Dictionary<string, EnchantmentBeamRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADROIT"] = EnchantmentBeamRule.Immediate,
            ["CLONE"] = EnchantmentBeamRule.Zero,
            ["CORRUPTED"] = EnchantmentBeamRule.Immediate,
            ["DEPRECATED_ENCHANTMENT"] = EnchantmentBeamRule.Zero,
            ["GLAM"] = EnchantmentBeamRule.GlamReplay,
            ["GOOPY"] = EnchantmentBeamRule.Immediate,
            ["IMBUED"] = EnchantmentBeamRule.Zero,
            ["INKY"] = EnchantmentBeamRule.Immediate,
            ["INSTINCT"] = EnchantmentBeamRule.Immediate,
            ["MOMENTUM"] = EnchantmentBeamRule.Immediate,
            ["NIMBLE"] = EnchantmentBeamRule.Immediate,
            ["PERFECT_FIT"] = EnchantmentBeamRule.Zero,
            ["ROYALLY_APPROVED"] = EnchantmentBeamRule.Zero,
            ["SHARP"] = EnchantmentBeamRule.Immediate,
            // Slither rerolls its cost every draw. The sampled cost still controls legality and energy
            // spending, but intentionally receives no extra Beam prior.
            ["SLITHER"] = EnchantmentBeamRule.Zero,
            ["SLUMBERING_ESSENCE"] = EnchantmentBeamRule.SlumberingCost,
            ["SOULS_POWER"] = EnchantmentBeamRule.Zero,
            ["SOWN"] = EnchantmentBeamRule.SownEnergy,
            ["SPIRAL"] = EnchantmentBeamRule.SpiralReplay,
            ["STEADY"] = EnchantmentBeamRule.Zero,
            ["SWIFT"] = EnchantmentBeamRule.SwiftDraw,
            ["TEZCATARAS_EMBER"] = EnchantmentBeamRule.TezcatarasEmber,
            ["VIGOROUS"] = EnchantmentBeamRule.Immediate
        };

    private static double EnchantmentImmediateMarginalValue(
        DeckCardInstance instance,
        SimulationState state)
    {
        SimulationCard card = instance.Card;
        double xCostDamageValue = XCostDamageValue(card, state);
        double scalingDamageValue = DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: true);
        double drawScalingDamageValue = instance.BonusDrawDamage * card.DamageUnitValue;
        double baseDamageValue = card.DamageValue
            + scalingDamageValue
            + xCostDamageValue
            + drawScalingDamageValue;
        double additiveDamageValue = EnchantmentDamageAdditiveValue(instance);
        double damageMultiplier = EnchantmentDamageMultiplier(card);
        double enchantedDamageValue = (baseDamageValue + additiveDamageValue) * damageMultiplier;
        double damageDelta = enchantedDamageValue - baseDamageValue;
        double vulnerableDelta = VulnerableBonus(enchantedDamageValue, state)
            - VulnerableBonus(baseDamageValue, state);

        return damageDelta
            + vulnerableDelta
            + EnchantmentBlockAdditiveValue(instance)
            + EnchantmentOnPlayBlockValue(card)
            + EnchantmentDebuffValue(card)
            + EnchantmentHpLossPenaltyValue(card);
    }

    private static double EstimateFreeReplayBeamValue(
        DeckCardInstance instance,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        SimulationCard card = instance.Card;
        return EstimateImmediateSearchValue(card, state, XCostEnergy(card, state))
            + EstimateResourceAndNextTurnSearchValue(card, resourceReferenceValues);
    }

    private static double SlumberingEssenceBeamValue(
        DeckCardInstance instance,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        if (instance.BonusUntilPlayedCostReduction <= 0)
        {
            return 0d;
        }

        int costWithoutEnchantment = EffectiveEnergyCost(
            instance.Card,
            state,
            instance.BonusDrawCostReduction,
            instance.FreeThisTurn,
            instance.CostOverrideThisCombat,
            untilPlayedCostReduction: 0);
        int currentCost = EffectiveEnergyCost(
            instance.Card,
            state,
            instance.BonusDrawCostReduction,
            instance.FreeThisTurn,
            instance.CostOverrideThisCombat,
            instance.BonusUntilPlayedCostReduction);
        return Math.Max(0, costWithoutEnchantment - currentCost) * resourceReferenceValues.Energy;
    }

    private static int EffectiveEnchantmentDrawCount(
        DeckCardInstance instance,
        SimulationState state,
        DeckSimulationOptions? options)
    {
        int maxHandSize = options?.MaxHandSize ?? state.MaxHandSize;
        int handSlotsAfterPlay = Math.Max(0, maxHandSize - state.Hand.Count + 1);
        int availableCards = state.DrawPile.Count + state.DiscardPile.Count;
        return Math.Min(EnchantmentAmount(instance), Math.Min(handSlotsAfterPlay, availableCards));
    }

    private static double TezcatarasEmberCostBeamValue(
        DeckCardInstance instance,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        if (IsVoidFormFreeCard(state) || instance.FreeThisTurn)
        {
            return 0d;
        }

        int costWithoutEnchantment = Math.Max(
            0,
            (instance.CostOverrideThisCombat ?? instance.Card.EnergyCost)
                - instance.BonusDrawCostReduction
                - instance.BonusUntilPlayedCostReduction);
        return costWithoutEnchantment * resourceReferenceValues.Energy;
    }

    private static double PlaySetupDecisionValue(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options,
        FiniteHorizonContext horizon)
    {
        return PlaySetupDecisionValue(
            card,
            state,
            ResourceReferenceValuesForTurns(horizon.RemainingTurns),
            includeDynamicSetup: true);
    }

    private static double PlaySetupDecisionValue(
        SimulationCard card,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues,
        bool includeDynamicSetup)
    {
        // Generic Power timing is enforced by the deterministic play prelude. Keep its legacy setup
        // contribution at zero so ordinary route scoring and finite-horizon continuation do not add
        // a second, conflicting always-play prior.
        double setup = card.IsPower ? 0d : card.PlaySetupValue;

        if (!card.IsPower)
        {
            setup += DynamicForgeAmount(card, state) * 2d;
        }

        setup += DynamicSetupDecisionValue(
            card,
            state,
            resourceReferenceValues,
            DynamicSetupSlot.Play,
            includeDynamicSetup);

        return setup;
    }

    private static double DynamicSetupDecisionValue(
        SimulationCard card,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues,
        DynamicSetupSlot slot,
        bool includeDynamicSetup)
    {
        if (!includeDynamicSetup)
        {
            return 0d;
        }

        double value = 0d;
        foreach (DynamicSetupRule rule in DynamicSetupRules)
        {
            if ((rule.Slots & slot) == 0 || !DynamicSetupAppliesTo(card, rule))
            {
                continue;
            }

            value += rule.Score(card, state, resourceReferenceValues);
        }

        return value;
    }

    private static bool DynamicSetupAppliesTo(SimulationCard card, DynamicSetupRule rule)
    {
        return DynamicSetupsForCard(card).Any(setup => string.Equals(setup.Key, rule.Key, StringComparison.Ordinal))
            || rule.AppliesTo(card);
    }

    private static double EstimateAverageRareDrawPileDecisionValue(
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        double total = 0d;
        int count = 0;
        foreach (DeckCardInstance instance in state.DrawPile)
        {
            if (!string.Equals(instance.Card.Rarity, "Rare", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += EstimateCardDecisionValue(
                instance.Card,
                state,
                resourceReferenceValues,
                includeDynamicSetup: false);
            count++;
        }

        return count == 0 ? 0d : total / count;
    }

    private static double EstimateCardDecisionValue(
        SimulationCard card,
        SimulationState state,
        ExplicitResourceReferenceValues resourceReferenceValues,
        bool includeDynamicSetup)
    {
        return EstimateImmediateSearchValue(card, state, XCostEnergy(card, state))
            + EstimateResourceAndNextTurnSearchValue(card, resourceReferenceValues)
            + PlaySetupDecisionValue(card, state, resourceReferenceValues, includeDynamicSetup);
    }

    private static ExplicitResourceReferenceValues ResourceReferenceValuesForTurns(int turns)
    {
        if (turns <= 4)
        {
            return ShortlineResourceReferenceValues;
        }

        if (turns <= 8)
        {
            return MidlineResourceReferenceValues;
        }

        return LonglineResourceReferenceValues;
    }

    private static double ExplicitResourceReferenceValue(
        SimulationCard card,
        ExplicitResourceReferenceValues values,
        bool includeNextTurn = true)
    {
        double immediateValue =
            (card.Draw * values.Draw)
            + (card.EnergyGain * values.Energy)
            + (card.StarGain * values.Star);
        double nextTurnValue =
            (card.DrawNextTurn * values.Draw)
            + (card.EnergyNextTurn * values.Energy)
            + (card.StarNextTurn * values.Star);
        return immediateValue
            + (includeNextTurn ? nextTurnValue * NextTurnExplicitResourceReferenceMultiplier : 0d);
    }

    private static bool HasActivePower(SimulationState state, ActivePowerKind kind)
    {
        // P8: the Any(...) lambda captures `kind`, allocating a closure + delegate per call; a plain
        // loop is allocation-free and output-identical.
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind == kind && power.Amount > 0d)
            {
                return true;
            }
        }

        return false;
    }

    private static double SearchTieBreak(SimulationCard card)
    {
        return (card.Exhausts ? 0.003d : 0d)
            + (card.Retain ? 0.002d : 0d)
            - (card.EnergyCost * 0.0001d)
            - (card.StarCost * 0.00005d);
    }

    private static bool IsBetter(SearchResult candidate, SearchResult best)
    {
        int decisionComparison = CompareSearchValues(candidate.DecisionValue, best.DecisionValue);
        if (decisionComparison != 0)
        {
            return decisionComparison > 0;
        }

        int realizedComparison = CompareSearchValues(candidate.Value, best.Value);
        if (realizedComparison != 0)
        {
            return realizedComparison > 0;
        }

        if (candidate.CardsPlayed != best.CardsPlayed)
        {
            return candidate.CardsPlayed < best.CardsPlayed;
        }

        return candidate.EnergySpent + candidate.StarSpent < best.EnergySpent + best.StarSpent;
    }

    private static int CompareSearchValues(double left, double right)
    {
        if (left == right)
        {
            return 0;
        }

        if (!double.IsFinite(left) || !double.IsFinite(right))
        {
            return left > right ? 1 : -1;
        }

        double difference = left - right;
        if (Math.Abs(difference) <= SearchValueComparisonTolerance)
        {
            return 0;
        }

        return difference > 0d ? 1 : -1;
    }

    private static DrawResult DrawCards(
        SimulationState state,
        int count,
        FastRandom? rng,
        bool allowShuffle,
        DeckSimulationOptions options)
    {
        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            if (state.Hand.Count >= options.MaxHandSize)
            {
                break;
            }

            if (allowShuffle && rng is not null)
            {
                ShuffleDrawPileIfNecessary(state, rng);
            }

            if (state.DrawPile.Count == 0)
            {
                break;
            }

            DeckCardInstance card = state.DrawPile[0];
            state.DrawPile.RemoveAt(0);
            state.Hand.Add(card);
            if (state.TrackedStartingInstanceIds?.Contains(card.InstanceId) == true
                && state.TrackedDrawModelId is not null
                && MatchesModelId(card.Card, state.TrackedDrawModelId))
            {
                state.TrackedDrawCount++;
            }
            ArmGuaranteedSearchAdmission([card]);
            drawn++;
            if (card.Card.DamageIncreasePerDraw != 0d)
            {
                // KinglyPunch: drawing it permanently raises its damage for the rest of the combat.
                card.BonusDrawDamage += card.Card.DamageIncreasePerDraw;
            }

            if (card.Card.CostReductionPerDraw != 0)
            {
                // KinglyKick: drawing it permanently lowers its energy cost for the rest of the combat.
                card.BonusDrawCostReduction += card.Card.CostReductionPerDraw;
            }

            if (HasEnchantment(card.Card, "SLITHER"))
            {
                card.CostOverrideThisCombat = rng?.Next(4) ?? 0;
            }

            if (card.Card.EnergyLossPerDraw != 0)
            {
                // Void: drawing it immediately drains player energy (applies to state, not the instance).
                state.Energy = Math.Max(0, state.Energy - card.Card.EnergyLossPerDraw);
            }

            ResolveCardDrawnPowers(state);
        }

        return new DrawResult(drawn, [], []);
    }

    private static void ShuffleDrawPileIfNecessary(SimulationState state, FastRandom rng)
    {
        if (state.DrawPile.Count > 0 || state.DiscardPile.Count == 0)
        {
            return;
        }

        state.DrawPile.AddRange(state.DiscardPile);
        state.DiscardPile.Clear();
        state.ShuffleCycle++;
        if (state.CounterfactualStableShuffle)
        {
            StableShuffle(state.DrawPile, state.RunSeed, state.ShuffleCycle);
        }
        else
        {
            Shuffle(state.DrawPile, rng);
            state.DrawPile.InvalidateSearchStateHash();
        }
        MovePerfectFitCardsToTop(state.DrawPile);
        ResolveShufflePowers(state);
    }

    private static void MovePerfectFitCardsToTop(SimulationCardPile drawPile)
    {
        IReadOnlyList<DeckCardInstance> perfectFit = drawPile
            .Where(card => HasEnchantment(card.Card, "PERFECT_FIT"))
            .OrderBy(card => card.InstanceId)
            .ToArray();
        if (perfectFit.Count == 0)
        {
            return;
        }

        drawPile.RemoveAll(card => HasEnchantment(card.Card, "PERFECT_FIT"));
        drawPile.InsertRange(0, perfectFit);
    }

    private static void ResolveShufflePowers(SimulationState state)
    {
        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.Stratagem))
        {
            IReadOnlyList<DeckCardInstance> selected = SelectCardObjects(
                state.DrawPile,
                (int)power.Amount,
                preferHighValue: true);
            foreach (DeckCardInstance card in selected)
            {
                state.DrawPile.Remove(card);
            }

            AddCardsToPile(state, state.Hand, selected, "Hand");
        }
    }

    private static int DeriveSeed(int seed, int actionsPlayed, int instanceId)
    {
        unchecked
        {
            int hash = seed == 0 ? 17 : seed;
            hash = (hash * 397) ^ actionsPlayed;
            hash = (hash * 397) ^ instanceId;
            return hash & 0x7fffffff;
        }
    }

    private static int CombatCardGenerationSeed(int runSeed)
    {
        // Separate named stream, mirroring RunRngSet.CombatCardGeneration. This need not reproduce
        // MegaRandom's exact sequence; it preserves the gameplay-significant stream separation and
        // counter progression inside each Monte Carlo run.
        return DeriveSeed(runSeed, 0x434347, 0); // "CCG"
    }

    private static void FinishTurn(SimulationState state)
    {
        List<DeckCardInstance> retained = [];
        bool exhaustedCard = false;
        bool retainHand = state.ActivePowers.Any(power =>
            power.Kind == ActivePowerKind.RetainHand
            && power.Amount > 0d);
        int frailAppliedNextTurn = state.Hand.Sum(card => TurnEndFrailAmount(card.Card));
        foreach (DeckCardInstance card in state.Hand)
        {
            if (HasEnchantment(card.Card, "SLUMBERING_ESSENCE"))
            {
                card.BonusUntilPlayedCostReduction++;
            }

            if (card.Card.Ethereal)
            {
                exhaustedCard = true;
                state.ExhaustPile.Add(card);
            }
            else if (retainHand || HasEffectiveRetain(card.Card))
            {
                retained.Add(card);
            }
            else
            {
                state.DiscardPile.Add(card);
            }
        }

        state.Hand.Clear();
        state.Hand.AddRange(retained);
        if (exhaustedCard)
        {
            state.InvalidateFutureTurnOpportunityProfile();
        }

        ClearFreeThisTurn(state.DrawPile);
        ClearFreeThisTurn(state.Hand);
        ClearFreeThisTurn(state.DiscardPile);
        ClearFreeThisTurn(state.ExhaustPile);
        state.PlayerFrail = Math.Max(0, state.PlayerFrail - 1) + frailAppliedNextTurn;
        ExpireEndOfTurnTemporaryPowers(state);
    }

    private static void ClearFreeThisTurn(IEnumerable<DeckCardInstance> cards)
    {
        foreach (DeckCardInstance card in cards)
        {
            card.FreeThisTurn = false;
        }
    }

    private static int TurnEndFrailAmount(SimulationCard card)
    {
        if (!CardBehaviorCatalog.Has(card, CardBehaviorKind.TurnEndFrail))
        {
            return 0;
        }

        double amount = card.Actions
            .Where(action => action.Kind == "power")
            .Where(action => string.Equals(PowerKey(action.Parameter), "Frail", StringComparison.OrdinalIgnoreCase))
            .Sum(action => (double)(action.Amount ?? 0m));
        return Math.Max(0, (int)Math.Round(amount, MidpointRounding.AwayFromZero));
    }

    private static void ExpireEndOfTurnTemporaryPowers(SimulationState state)
    {
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind is ActivePowerKind.Conqueror or ActivePowerKind.RetainHand)
            {
                power.Amount -= 1d;
            }

            if (power.Kind == ActivePowerKind.Monologue && power.Counter > 0)
            {
                RemovePowerModifierSource(
                    state.MutableStrengthSources,
                    power.SourceModelId,
                    power.SourceTypeName,
                    power.Counter);
                power.Counter = 0;
            }
        }

        state.ActivePowers.RemoveAll(power =>
            (power.Kind == ActivePowerKind.Conqueror || power.Kind == ActivePowerKind.RetainHand)
            && power.Amount <= 0d);
        state.ActivePowers.RemoveAll(power => power.Kind == ActivePowerKind.Monologue);
    }

    private static void RemovePowerModifierSource(
        List<ResourceSourceCredit> sources,
        string sourceModelId,
        string sourceTypeName,
        double amount)
    {
        double remaining = amount;
        for (int index = sources.Count - 1; index >= 0 && remaining > 0d; index--)
        {
            ResourceSourceCredit source = sources[index];
            if (!string.Equals(source.SourceModelId, sourceModelId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.SourceTypeName, sourceTypeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double consumed = Math.Min(remaining, source.Amount);
            remaining -= consumed;
            double nextAmount = source.Amount - consumed;
            if (nextAmount <= 0d)
            {
                sources.RemoveAt(index);
            }
            else
            {
                sources[index] = source with { Amount = nextAmount };
            }
        }
    }

    private static TurnSimulationSummary BuildTurnSummary(
        int turn,
        IReadOnlyList<TurnTrialSummary> samples,
        double bucketSize)
    {
        IReadOnlyList<double> values = samples.Select(sample => sample.Value).Order().ToArray();
        double mean = values.Average();
        double variance = values.Count == 0
            ? 0d
            : values.Average(value => (value - mean) * (value - mean));

        return new TurnSimulationSummary(
            turn,
            Round(mean),
            Round(variance),
            Round(Sqrt(variance)),
            Percentile(values, 0.10d),
            Percentile(values, 0.25d),
            Percentile(values, 0.50d),
            Percentile(values, 0.75d),
            Percentile(values, 0.90d),
            Round(samples.Average(sample => (double)sample.CardsDrawn)),
            Round(samples.Average(sample => (double)sample.CardsPlayed)),
            Round(samples.Average(sample => (double)sample.EnergySpent)),
            Round(samples.Average(sample => (double)sample.EnergyGained)),
            Round(samples.Average(sample => (double)sample.EnergyWasted)),
            Round(samples.Average(sample => (double)sample.StarSpent)),
            Round(samples.Average(sample => (double)sample.StarGained)),
            Round(samples.Average(sample => (double)sample.StarsWasted)),
            Round(samples.Average(sample => sample.UnplayedIntrinsicValue)),
            BuildPmf(values, bucketSize));
    }

    private static IReadOnlyList<ProbabilityBucket> BuildPmf(IReadOnlyList<double> values, double bucketSize)
    {
        if (bucketSize <= 0d)
        {
            bucketSize = 1d;
        }

        return values
            .GroupBy(value => Round(Math.Round(value / bucketSize, MidpointRounding.AwayFromZero) * bucketSize))
            .OrderBy(group => group.Key)
            .Select(group => new ProbabilityBucket(
                group.Key,
                group.Count(),
                Round((double)group.Count() / values.Count)))
            .ToArray();
    }

    private static IReadOnlyList<TurnCovariance> BuildCovariances(
        double[,] turnValues,
        IReadOnlyList<TurnSimulationSummary> turnSummaries)
    {
        int runs = turnValues.GetLength(0);
        int turns = turnValues.GetLength(1);
        double[] means = turnSummaries.Select(summary => (double)summary.ExpectedValue).ToArray();
        List<TurnCovariance> covariances = [];
        for (int first = 0; first < turns; first++)
        {
            for (int second = first + 1; second < turns; second++)
            {
                double productMean = 0;
                for (int run = 0; run < runs; run++)
                {
                    productMean += turnValues[run, first] * turnValues[run, second];
                }

                productMean /= runs;
                covariances.Add(new TurnCovariance(
                    first + 1,
                    second + 1,
                    Round((double)(productMean - (means[first] * means[second])))));
            }
        }

        return covariances;
    }

    private static decimal RoundTotalVariance(
        IReadOnlyList<TurnSimulationSummary> turnSummaries,
        IReadOnlyList<TurnCovariance> covariances)
    {
        decimal variance = turnSummaries.Sum(turn => turn.Variance)
            + (2m * covariances.Sum(covariance => covariance.Covariance));
        return Round((double)Math.Max(0m, variance));
    }

    private static decimal Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0m;
        }

        double rawIndex = percentile * (sortedValues.Count - 1);
        int left = (int)Math.Floor(rawIndex);
        int right = (int)Math.Ceiling(rawIndex);
        if (left == right)
        {
            return Round(sortedValues[left]);
        }

        double ratio = rawIndex - left;
        return Round(sortedValues[left] + ((sortedValues[right] - sortedValues[left]) * ratio));
    }

    private static IReadOnlyList<string> BuildWarnings(
        IReadOnlyList<SimulationCard> deck,
        int ignoredStartingSovereignBlades)
    {
        List<string> warnings = [];
        if (ignoredStartingSovereignBlades > 0)
        {
            warnings.Add("Starting Sovereign Blade token cards were ignored. Sovereign Blade is generated into hand by the first Forge card instead of starting in the deck.");
        }

        if (deck.Any(card => card.StarCost > 0 || card.StarGain > 0 || card.StarNextTurn > 0))
        {
            warnings.Add("Star effects come from parsed CanonicalStarCost, StarsVar, and StarNextTurnPower terms.");
        }

        if (deck.Any(card => card.Forge > 0))
        {
            warnings.Add("Forge simulation uses a simplified Sovereign Blade model: Forge creates a retained 2-cost blade if none is unexhausted, adds damage to all blade copies, and credits realized Forge value to the source card when a blade is played.");
        }

        if (deck.Any(card => card.Draw > 0))
        {
            warnings.Add("Sampled-lookahead policy can see cards drawn within a sampled trial, so draw chains may be optimistic until expectation-based play search is added.");
        }

        if (deck.Any(card => card.Vulnerable > 0))
        {
            warnings.Add("Vulnerable is simulated as a dynamic enemy state: attacks gain floor(damage value * 50%) while Vulnerable is active, and one stack decays at the start of each player turn.");
        }

        if (deck.Any(card => card.Actions.Any(action => action.Kind == "persistentPowerTrigger")))
        {
            warnings.Add("Supported persistent powers are installed after the source Power card is played, remain active across turns, and credit realized trigger value back to the source card.");
        }

        if (deck.Any(card => card.Actions.Any(action => action.Kind == "transformCard")))
        {
            warnings.Add("Transform simulation replaces selected low-value card objects with the parsed TransformTo target when present in the card library; random or unresolved transforms use a generic 1-cost 11-value attack placeholder.");
        }

        if (deck.Any(card => card.Enchantment is not null))
        {
            warnings.Add("Card enchantments are tracked as per-instance card identities; supported enchantments are simulated by the runtime model.");
        }

        if (deck.Any(card => card.Enchantment is { IsRuntimeSupported: false }))
        {
            warnings.Add("Unsupported card enchantments are preserved in card identity but simulated as no-op effects.");
        }

        return warnings;
    }

    private static void Validate(IReadOnlyList<SimulationCard> deck, DeckSimulationOptions options)
    {
        if (deck.Count == 0)
        {
            throw new InvalidOperationException("Deck simulation requires at least one card.");
        }

        if (options.Turns <= 0)
        {
            throw new InvalidOperationException("Simulation turns must be positive.");
        }

        if (options.Runs <= 0)
        {
            throw new InvalidOperationException("Simulation runs must be positive.");
        }

        if (options.HandSize <= 0)
        {
            throw new InvalidOperationException("Simulation hand size must be positive.");
        }

        if (options.MaxHandSize <= 0)
        {
            throw new InvalidOperationException("Simulation max hand size must be positive.");
        }


        if (options.MaxCardsPlayedPerTurn <= 0)
        {
            throw new InvalidOperationException("Simulation max cards played must be positive.");
        }

        if (options.MaxDeterministicPlayChain < 0)
        {
            throw new InvalidOperationException("Simulation deterministic play chain cap cannot be negative.");
        }

        if (options.MaxSearchNodesPerTurn <= 0)
        {
            throw new InvalidOperationException("Simulation search node budget must be positive.");
        }

        if (options.TranspositionCapacityPerTurn < 0)
        {
            throw new InvalidOperationException("Simulation transposition capacity cannot be negative.");
        }
    }

    private static void Shuffle<T>(IList<T> items, FastRandom rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static void Shuffle<T>(IList<T> items, ref FastRandomState rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static void Shuffle<T>(T[] items, int count, ref FastRandomState rng)
    {
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static void StableShuffle(SimulationCardPile items, int runSeed, int shuffleCycle)
    {
        items.Sort((left, right) =>
        {
            ulong leftPriority = StableShufflePriority(runSeed, shuffleCycle, left.InstanceId);
            ulong rightPriority = StableShufflePriority(runSeed, shuffleCycle, right.InstanceId);
            int priorityComparison = leftPriority.CompareTo(rightPriority);
            return priorityComparison != 0
                ? priorityComparison
                : left.InstanceId.CompareTo(right.InstanceId);
        });
    }

    private static ulong StableShufflePriority(int runSeed, int shuffleCycle, int instanceId)
    {
        ulong value = unchecked((uint)runSeed);
        value ^= unchecked((ulong)(uint)shuffleCycle) * 0x9E3779B97F4A7C15UL;
        value ^= unchecked((ulong)(uint)instanceId) * 0xBF58476D1CE4E5B9UL;
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static void MoveInnateCardsToTop(SimulationCardPile drawPile)
    {
        IReadOnlyList<DeckCardInstance> innate = drawPile
            .Where(card => HasEffectiveInnate(card.Card))
            .OrderBy(card => card.InstanceId)
            .ToArray();
        if (innate.Count == 0)
        {
            return;
        }

        drawPile.RemoveAll(card => HasEffectiveInnate(card.Card));
        drawPile.InsertRange(0, innate);
    }

    private static void MoveStartAtBottomCardsToBottom(SimulationCardPile drawPile)
    {
        IReadOnlyList<DeckCardInstance> bottomCards = drawPile
            .Where(card => HasEnchantment(card.Card, "IMBUED"))
            .OrderBy(card => card.InstanceId)
            .ToArray();
        if (bottomCards.Count == 0)
        {
            return;
        }

        drawPile.RemoveAll(card => HasEnchantment(card.Card, "IMBUED"));
        drawPile.AddRange(bottomCards);
    }

    // P9: SimulationState.Create runs this once per run (Runs = up to a few thousand per simulation),
    // but the result depends only on the deck, which is a stable reference for the whole simulation
    // (NormalizeStartingDeck produces one array reused across every run). Memoize per deck reference so
    // the SelectMany/GroupBy/OrderBy chain runs once instead of per run. Weak keys collect with the
    // per-simulation deck array; output-identical (pure function of the deck).
    private sealed record CharacterPoolNameHolder(string? Value);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<SimulationCard>, CharacterPoolNameHolder> CharacterPoolNameCache = new();

    private static string? ResolveCharacterPoolName(IReadOnlyList<SimulationCard> deck)
    {
        return CharacterPoolNameCache
            .GetValue(deck, static d => new CharacterPoolNameHolder(InferCharacterPoolName(d)))
            .Value;
    }

    private static string? InferCharacterPoolName(IReadOnlyList<SimulationCard> deck)
    {
        return deck
            .SelectMany(card => card.Pools)
            .Where(pool => !string.Equals(pool, "Colorless", StringComparison.OrdinalIgnoreCase))
            .GroupBy(pool => pool, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static double Sqrt(double value)
    {
        return (double)Math.Sqrt((double)value);
    }

    private static decimal Round(double value)
    {
        return (decimal)Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    // These packed, active-length lists keep List<T>'s direct read/enumeration fast path while
    // owning component fingerprints. Mutations are funneled through hidden List methods; card and
    // Power field changes invalidate their owning component through the back-reference.
    private sealed class SimulationCardPile : List<DeckCardInstance>
    {
        private ulong searchStateHash;
        private bool searchStateHashDirty = true;

        public ulong SearchStateHash
        {
            get
            {
                if (!searchStateHashDirty)
                {
                    return searchStateHash;
                }

                ulong hash = 14695981039346656037UL;
                hash ^= CountHash(Count);
                for (int index = 0; index < Count; index++)
                {
                    hash ^= ItemHash(this[index].SearchStateHash, index);
                }

                searchStateHash = hash;
                searchStateHashDirty = false;
                return hash;
            }
        }

        public new void Add(DeckCardInstance item)
        {
            int index = Count;
            if (!searchStateHashDirty)
            {
                searchStateHash ^= CountHash(index);
                searchStateHash ^= CountHash(index + 1);
                searchStateHash ^= ItemHash(item.SearchStateHash, index);
            }

            base.Add(item);
            item.AttachToPile(this);
        }

        public new void AddRange(IEnumerable<DeckCardInstance> collection)
        {
            DeckCardInstance[] added = collection as DeckCardInstance[] ?? collection.ToArray();
            if (added.Length == 0)
            {
                return;
            }

            foreach (DeckCardInstance item in added)
            {
                Add(item);
            }
        }

        public new void Clear()
        {
            foreach (DeckCardInstance item in this)
            {
                item.DetachFromPile(this);
            }

            if (Count > 0)
            {
                base.Clear();
                searchStateHashDirty = true;
            }
        }

        public new void Insert(int index, DeckCardInstance item)
        {
            base.Insert(index, item);
            item.AttachToPile(this);
            searchStateHashDirty = true;
        }

        public new void InsertRange(int index, IEnumerable<DeckCardInstance> collection)
        {
            DeckCardInstance[] inserted = collection as DeckCardInstance[] ?? collection.ToArray();
            if (inserted.Length == 0)
            {
                return;
            }

            base.InsertRange(index, inserted);
            foreach (DeckCardInstance item in inserted)
            {
                item.AttachToPile(this);
            }

            searchStateHashDirty = true;
        }

        public new bool Remove(DeckCardInstance item)
        {
            if (!base.Remove(item))
            {
                return false;
            }

            item.DetachFromPile(this);
            searchStateHashDirty = true;
            return true;
        }

        public new int RemoveAll(Predicate<DeckCardInstance> match)
        {
            int removed = 0;
            for (int index = Count - 1; index >= 0; index--)
            {
                if (!match(this[index]))
                {
                    continue;
                }

                RemoveAt(index);
                removed++;
            }

            return removed;
        }

        public new void RemoveAt(int index)
        {
            DeckCardInstance item = this[index];
            base.RemoveAt(index);
            item.DetachFromPile(this);
            searchStateHashDirty = true;
        }

        public new void RemoveRange(int index, int count)
        {
            if (count == 0)
            {
                return;
            }

            for (int itemIndex = index; itemIndex < index + count; itemIndex++)
            {
                this[itemIndex].DetachFromPile(this);
            }

            base.RemoveRange(index, count);
            searchStateHashDirty = true;
        }

        public new void Sort(Comparison<DeckCardInstance> comparison)
        {
            base.Sort(comparison);
            searchStateHashDirty = true;
        }

        public void CopyFrom(SimulationCardPile source)
        {
            if (ReferenceEquals(this, source))
            {
                return;
            }

            int sharedCount = Math.Min(Count, source.Count);
            for (int index = 0; index < sharedCount; index++)
            {
                this[index].CopyFrom(source[index]);
            }

            if (Count > source.Count)
            {
                RemoveRange(source.Count, Count - source.Count);
            }
            else
            {
                Capacity = Math.Max(Capacity, source.Count);
                for (int index = Count; index < source.Count; index++)
                {
                    Add(source[index].Clone());
                }
            }

            searchStateHash = source.searchStateHash;
            searchStateHashDirty = source.searchStateHashDirty;
        }

        internal void InvalidateSearchStateHash()
        {
            searchStateHashDirty = true;
        }

        internal void CardSearchStateChanged(DeckCardInstance card, ulong previousHash)
        {
            if (searchStateHashDirty)
            {
                return;
            }

            int index = IndexOf(card);
            if (index < 0)
            {
                searchStateHashDirty = true;
                return;
            }

            searchStateHash ^= ItemHash(previousHash, index);
            searchStateHash ^= ItemHash(card.SearchStateHash, index);
        }

        private static ulong CountHash(int count)
        {
            ulong hash = 0x9E3779B97F4A7C15UL;
            AddExactSearchHash(ref hash, count);
            return hash;
        }

        private static ulong ItemHash(ulong itemHash, int index)
        {
            ulong hash = 0xBF58476D1CE4E5B9UL;
            AddExactSearchHash(ref hash, index);
            AddExactSearchHash(ref hash, unchecked((long)itemHash));
            return hash;
        }
    }

    private sealed class ActivePowerCollection : List<ActivePower>
    {
        private ulong searchStateHash;
        private bool searchStateHashDirty = true;

        public ulong SearchStateHash
        {
            get
            {
                if (!searchStateHashDirty)
                {
                    return searchStateHash;
                }

                ulong hash = 14695981039346656037UL;
                hash ^= CountHash(Count);
                for (int index = 0; index < Count; index++)
                {
                    hash ^= ItemHash(this[index].SearchStateHash, index);
                }

                searchStateHash = hash;
                searchStateHashDirty = false;
                return hash;
            }
        }

        public new void Add(ActivePower item)
        {
            int index = Count;
            if (!searchStateHashDirty)
            {
                searchStateHash ^= CountHash(index);
                searchStateHash ^= CountHash(index + 1);
                searchStateHash ^= ItemHash(item.SearchStateHash, index);
            }

            base.Add(item);
            item.AttachToCollection(this);
        }

        public new int RemoveAll(Predicate<ActivePower> match)
        {
            int removed = 0;
            for (int index = Count - 1; index >= 0; index--)
            {
                if (!match(this[index]))
                {
                    continue;
                }

                ActivePower item = this[index];
                base.RemoveAt(index);
                item.DetachFromCollection(this);
                removed++;
            }

            if (removed > 0)
            {
                searchStateHashDirty = true;
            }

            return removed;
        }

        public void CopyFrom(ActivePowerCollection source)
        {
            if (ReferenceEquals(this, source))
            {
                return;
            }

            int sharedCount = Math.Min(Count, source.Count);
            for (int index = 0; index < sharedCount; index++)
            {
                this[index].CopyFrom(source[index]);
            }

            if (Count > source.Count)
            {
                for (int index = Count - 1; index >= source.Count; index--)
                {
                    ActivePower item = this[index];
                    base.RemoveAt(index);
                    item.DetachFromCollection(this);
                }
            }
            else
            {
                for (int index = Count; index < source.Count; index++)
                {
                    Add(source[index].Clone());
                }
            }

            searchStateHash = source.searchStateHash;
            searchStateHashDirty = source.searchStateHashDirty;
        }

        internal void InvalidateSearchStateHash()
        {
            searchStateHashDirty = true;
        }

        internal void PowerSearchStateChanged(ActivePower power, ulong previousHash)
        {
            if (searchStateHashDirty)
            {
                return;
            }

            int index = IndexOf(power);
            if (index < 0)
            {
                searchStateHashDirty = true;
                return;
            }

            searchStateHash ^= ItemHash(previousHash, index);
            searchStateHash ^= ItemHash(power.SearchStateHash, index);
        }

        private static ulong CountHash(int count)
        {
            ulong hash = 0x94D049BB133111EBUL;
            AddExactSearchHash(ref hash, count);
            return hash;
        }

        private static ulong ItemHash(ulong itemHash, int index)
        {
            ulong hash = 0xD6E8FEB86659FD93UL;
            AddExactSearchHash(ref hash, index);
            AddExactSearchHash(ref hash, unchecked((long)itemHash));
            return hash;
        }
    }

    private sealed class SimulationState
    {
        public SimulationCardPile DrawPile { get; } = new();

        public SimulationCardPile Hand { get; } = new();

        public SimulationCardPile DiscardPile { get; } = new();

        public SimulationCardPile ExhaustPile { get; } = new();

        public ActivePowerCollection ActivePowers { get; } = new();

        public List<ResourceSourceCredit> CurrentTurnEnergySources { get; } = [];

        public List<ResourceSourceCredit> NextTurnEnergySources { get; } = [];

        public List<ResourceSourceCredit> NextTurnStarSources { get; } = [];

        public List<ResourceSourceCredit> StarSources { get; } = [];

        public List<DelayedValueCredit> NextTurnBlockCredits { get; } = [];

        // P1: these power-modifier source lists are empty in the vast majority of states (only
        // populated when a matching power is installed), so allocating a fresh List per Clone is
        // pure waste. Read path returns a shared empty list (no allocation); MutableX lazily
        // allocates the backing list on first write. Compiler-enforced: writers must use MutableX.
        private static readonly IReadOnlyList<ResourceSourceCredit> EmptyCredits = [];

        private List<ResourceSourceCredit>? _strengthSources;
        public IReadOnlyList<ResourceSourceCredit> StrengthSources => _strengthSources ?? EmptyCredits;
        public List<ResourceSourceCredit> MutableStrengthSources => _strengthSources ??= [];

        private List<ResourceSourceCredit>? _dexteritySources;
        public IReadOnlyList<ResourceSourceCredit> DexteritySources => _dexteritySources ?? EmptyCredits;
        public List<ResourceSourceCredit> MutableDexteritySources => _dexteritySources ??= [];

        private List<ResourceSourceCredit>? _fastenSources;
        public IReadOnlyList<ResourceSourceCredit> FastenSources => _fastenSources ?? EmptyCredits;
        public List<ResourceSourceCredit> MutableFastenSources => _fastenSources ??= [];

        private List<ResourceSourceCredit>? _parrySources;
        public IReadOnlyList<ResourceSourceCredit> ParrySources => _parrySources ?? EmptyCredits;
        public List<ResourceSourceCredit> MutableParrySources => _parrySources ??= [];

        private List<ResourceSourceCredit>? _seekingEdgeSources;
        public IReadOnlyList<ResourceSourceCredit> SeekingEdgeSources => _seekingEdgeSources ?? EmptyCredits;
        public List<ResourceSourceCredit> MutableSeekingEdgeSources => _seekingEdgeSources ??= [];

        private List<ResourceSourceCredit>? _swordSageSources;
        public IReadOnlyList<ResourceSourceCredit> SwordSageSources => _swordSageSources ?? EmptyCredits;
        public List<ResourceSourceCredit> MutableSwordSageSources => _swordSageSources ??= [];

        // The finite-horizon leaf evaluator visits the same non-exhaust card set at most search
        // nodes. Normal plays only move a card from Hand to Discard, so cloned descendants can
        // reuse this immutable opportunity profile. Structural card changes explicitly invalidate
        // it; the small dynamic inputs are part of the cache key.
        private FutureTurnOpportunityProfile? _futureTurnOpportunityProfile;
        private int _futureTurnOpportunityNextTurnEnergy;
        private int _futureTurnOpportunityHandDrawBonus;
        private bool _futureTurnOpportunityHasSeekingEdge;

        public List<ResourceSourceCredit> VigorSources { get; } = [];

        public List<ResourceSourceCredit> EnemyVulnerableSources { get; } = [];

        public string? CharacterPoolName { get; set; }

        public int Energy { get; set; }

        public int Stars { get; set; }

        public int BaseStarsRemaining { get; set; }

        public int NextTurnEnergy { get; set; }

        public int NextTurnStars { get; set; }

        public int NextTurnDraw { get; set; }

        public int NextTurnBlock { get; set; }

        public double NextTurnBlockDecisionValue { get; set; }

        public int NextGeneratedInstanceId { get; set; }

        public int NextPlayEventId { get; set; }

        public int EnemyVulnerable { get; set; }

        public int PlayerFrail { get; set; }

        public int GeneratedCardsCreated { get; set; }

        public int LastTurnCardsPlayed { get; set; }

        public int CardsPlayedThisTurn { get; set; }

        public int CardsPlayedThisCombat { get; set; }

        public int AttacksPlayedThisTurn { get; set; }

        public int SkillsPlayedThisTurn { get; set; }

        public int StarsGainedThisTurn { get; set; }

        public int MaxHandSize { get; set; }

        public bool TurnEnded { get; set; }

        public int RunSeed { get; set; }

        public int ShuffleCycle { get; set; }

        public bool CounterfactualStableShuffle { get; set; }

        public bool TrackAttributionSources { get; set; }

        public FastRandomState CombatCardGenerationRandom;

        public string? TrackedDrawModelId { get; set; }

        public IReadOnlySet<int>? TrackedStartingInstanceIds { get; set; }

        public int TrackedDrawCount { get; set; }

        public bool TryGetFutureTurnOpportunityProfile(
            int nextTurnEnergy,
            int handDrawBonus,
            bool hasSeekingEdge,
            out FutureTurnOpportunityProfile profile)
        {
            if (_futureTurnOpportunityProfile is not null
                && _futureTurnOpportunityNextTurnEnergy == nextTurnEnergy
                && _futureTurnOpportunityHandDrawBonus == handDrawBonus
                && _futureTurnOpportunityHasSeekingEdge == hasSeekingEdge)
            {
                profile = _futureTurnOpportunityProfile;
                return true;
            }

            profile = null!;
            return false;
        }

        public void CacheFutureTurnOpportunityProfile(
            int nextTurnEnergy,
            int handDrawBonus,
            bool hasSeekingEdge,
            FutureTurnOpportunityProfile profile)
        {
            _futureTurnOpportunityNextTurnEnergy = nextTurnEnergy;
            _futureTurnOpportunityHandDrawBonus = handDrawBonus;
            _futureTurnOpportunityHasSeekingEdge = hasSeekingEdge;
            _futureTurnOpportunityProfile = profile;
        }

        public void InvalidateFutureTurnOpportunityProfile()
        {
            _futureTurnOpportunityProfile = null;
        }

        public static SimulationState Create(
            IReadOnlyList<SimulationCard> deck,
            FastRandom rng,
            DeckSimulationOptions options,
            int runSeed)
        {
            IReadOnlyList<int> startingIds = options.StartingInstanceIds.Count == deck.Count
                ? options.StartingInstanceIds
                : Enumerable.Range(0, deck.Count).ToArray();
            SimulationState state = new()
            {
                Stars = options.BaseStars,
                BaseStarsRemaining = options.BaseStars,
                CharacterPoolName = ResolveCharacterPoolName(deck),
                MaxHandSize = options.MaxHandSize,
                RunSeed = runSeed,
                CounterfactualStableShuffle = options.CounterfactualStableShuffle,
                TrackAttributionSources = options.CollectAttribution,
                CombatCardGenerationRandom = new FastRandomState(CombatCardGenerationSeed(runSeed)),
                TrackedDrawModelId = options.TrackedDrawModelId,
                TrackedStartingInstanceIds = options.TrackedStartingInstanceIds
            };
            for (int i = 0; i < deck.Count; i++)
            {
                state.DrawPile.Add(new DeckCardInstance(startingIds[i], deck[i]));
            }

            state.NextGeneratedInstanceId = startingIds.Count == 0 ? 0 : startingIds.Max() + 1;
            if (state.CounterfactualStableShuffle)
            {
                StableShuffle(state.DrawPile, runSeed, shuffleCycle: 0);
            }
            else
            {
                Shuffle(state.DrawPile, rng);
                state.DrawPile.InvalidateSearchStateHash();
            }
            MoveInnateCardsToTop(state.DrawPile);
            MoveStartAtBottomCardsToBottom(state.DrawPile);
            return state;
        }

        public SimulationState Clone()
        {
            SimulationState clone = new()
            {
                Energy = Energy,
                Stars = Stars,
                BaseStarsRemaining = BaseStarsRemaining,
                NextTurnEnergy = NextTurnEnergy,
                NextTurnStars = NextTurnStars,
                NextTurnDraw = NextTurnDraw,
                NextTurnBlock = NextTurnBlock,
                NextTurnBlockDecisionValue = NextTurnBlockDecisionValue,
                NextGeneratedInstanceId = NextGeneratedInstanceId,
                NextPlayEventId = NextPlayEventId,
                EnemyVulnerable = EnemyVulnerable,
                PlayerFrail = PlayerFrail,
                GeneratedCardsCreated = GeneratedCardsCreated,
                LastTurnCardsPlayed = LastTurnCardsPlayed,
                CardsPlayedThisTurn = CardsPlayedThisTurn,
                CardsPlayedThisCombat = CardsPlayedThisCombat,
                AttacksPlayedThisTurn = AttacksPlayedThisTurn,
                SkillsPlayedThisTurn = SkillsPlayedThisTurn,
                StarsGainedThisTurn = StarsGainedThisTurn,
                MaxHandSize = MaxHandSize,
                TurnEnded = TurnEnded,
                CharacterPoolName = CharacterPoolName,
                RunSeed = RunSeed,
                ShuffleCycle = ShuffleCycle,
                CounterfactualStableShuffle = CounterfactualStableShuffle,
                TrackAttributionSources = TrackAttributionSources,
                CombatCardGenerationRandom = CombatCardGenerationRandom,
                TrackedDrawModelId = TrackedDrawModelId,
                TrackedStartingInstanceIds = TrackedStartingInstanceIds,
                TrackedDrawCount = TrackedDrawCount
            };
            clone.DrawPile.CopyFrom(DrawPile);
            clone.Hand.CopyFrom(Hand);
            clone.DiscardPile.CopyFrom(DiscardPile);
            clone.ExhaustPile.CopyFrom(ExhaustPile);
            clone.ActivePowers.CopyFrom(ActivePowers);
            clone.CurrentTurnEnergySources.AddRange(CurrentTurnEnergySources);
            clone.NextTurnEnergySources.AddRange(NextTurnEnergySources);
            clone.NextTurnStarSources.AddRange(NextTurnStarSources);
            clone.StarSources.AddRange(StarSources);
            clone.NextTurnBlockCredits.AddRange(NextTurnBlockCredits);
            if (_strengthSources is { Count: > 0 }) { clone._strengthSources = [.. _strengthSources]; }
            if (_dexteritySources is { Count: > 0 }) { clone._dexteritySources = [.. _dexteritySources]; }
            if (_fastenSources is { Count: > 0 }) { clone._fastenSources = [.. _fastenSources]; }
            if (_parrySources is { Count: > 0 }) { clone._parrySources = [.. _parrySources]; }
            if (_seekingEdgeSources is { Count: > 0 }) { clone._seekingEdgeSources = [.. _seekingEdgeSources]; }
            if (_swordSageSources is { Count: > 0 }) { clone._swordSageSources = [.. _swordSageSources]; }
            clone._futureTurnOpportunityProfile = _futureTurnOpportunityProfile;
            clone._futureTurnOpportunityNextTurnEnergy = _futureTurnOpportunityNextTurnEnergy;
            clone._futureTurnOpportunityHandDrawBonus = _futureTurnOpportunityHandDrawBonus;
            clone._futureTurnOpportunityHasSeekingEdge = _futureTurnOpportunityHasSeekingEdge;
            clone.VigorSources.AddRange(VigorSources);
            clone.EnemyVulnerableSources.AddRange(EnemyVulnerableSources);
            return clone;
        }

        public void CopyFrom(SimulationState state)
        {
            if (ReferenceEquals(this, state))
            {
                return;
            }

            DrawPile.CopyFrom(state.DrawPile);
            Hand.CopyFrom(state.Hand);
            DiscardPile.CopyFrom(state.DiscardPile);
            ExhaustPile.CopyFrom(state.ExhaustPile);
            ActivePowers.CopyFrom(state.ActivePowers);
            CurrentTurnEnergySources.Clear();
            CurrentTurnEnergySources.AddRange(state.CurrentTurnEnergySources);
            NextTurnEnergySources.Clear();
            NextTurnEnergySources.AddRange(state.NextTurnEnergySources);
            NextTurnStarSources.Clear();
            NextTurnStarSources.AddRange(state.NextTurnStarSources);
            StarSources.Clear();
            StarSources.AddRange(state.StarSources);
            NextTurnBlockCredits.Clear();
            NextTurnBlockCredits.AddRange(state.NextTurnBlockCredits);
            CopyOptionalCredits(ref _strengthSources, state._strengthSources);
            CopyOptionalCredits(ref _dexteritySources, state._dexteritySources);
            CopyOptionalCredits(ref _fastenSources, state._fastenSources);
            CopyOptionalCredits(ref _parrySources, state._parrySources);
            CopyOptionalCredits(ref _seekingEdgeSources, state._seekingEdgeSources);
            CopyOptionalCredits(ref _swordSageSources, state._swordSageSources);
            _futureTurnOpportunityProfile = state._futureTurnOpportunityProfile;
            _futureTurnOpportunityNextTurnEnergy = state._futureTurnOpportunityNextTurnEnergy;
            _futureTurnOpportunityHandDrawBonus = state._futureTurnOpportunityHandDrawBonus;
            _futureTurnOpportunityHasSeekingEdge = state._futureTurnOpportunityHasSeekingEdge;
            VigorSources.Clear();
            VigorSources.AddRange(state.VigorSources);
            EnemyVulnerableSources.Clear();
            EnemyVulnerableSources.AddRange(state.EnemyVulnerableSources);
            CharacterPoolName = state.CharacterPoolName;
            Energy = state.Energy;
            Stars = state.Stars;
            BaseStarsRemaining = state.BaseStarsRemaining;
            NextTurnEnergy = state.NextTurnEnergy;
            NextTurnStars = state.NextTurnStars;
            NextTurnDraw = state.NextTurnDraw;
            NextTurnBlock = state.NextTurnBlock;
            NextTurnBlockDecisionValue = state.NextTurnBlockDecisionValue;
            NextGeneratedInstanceId = state.NextGeneratedInstanceId;
            NextPlayEventId = state.NextPlayEventId;
            EnemyVulnerable = state.EnemyVulnerable;
            PlayerFrail = state.PlayerFrail;
            GeneratedCardsCreated = state.GeneratedCardsCreated;
            LastTurnCardsPlayed = state.LastTurnCardsPlayed;
            CardsPlayedThisTurn = state.CardsPlayedThisTurn;
            CardsPlayedThisCombat = state.CardsPlayedThisCombat;
            AttacksPlayedThisTurn = state.AttacksPlayedThisTurn;
            SkillsPlayedThisTurn = state.SkillsPlayedThisTurn;
            StarsGainedThisTurn = state.StarsGainedThisTurn;
            MaxHandSize = state.MaxHandSize;
            TurnEnded = state.TurnEnded;
            RunSeed = state.RunSeed;
            ShuffleCycle = state.ShuffleCycle;
            CounterfactualStableShuffle = state.CounterfactualStableShuffle;
            TrackAttributionSources = state.TrackAttributionSources;
            CombatCardGenerationRandom = state.CombatCardGenerationRandom;
            TrackedDrawModelId = state.TrackedDrawModelId;
            TrackedStartingInstanceIds = state.TrackedStartingInstanceIds;
            TrackedDrawCount = state.TrackedDrawCount;
        }

        private static void CopyOptionalCredits(
            ref List<ResourceSourceCredit>? destination,
            IReadOnlyList<ResourceSourceCredit>? source)
        {
            if (source is not { Count: > 0 })
            {
                destination?.Clear();
                return;
            }

            destination ??= new List<ResourceSourceCredit>(source.Count);
            destination.Clear();
            destination.AddRange(source);
        }
    }

    private struct CardInstanceMutableState
    {
        public int BonusReplayCount;
        public double BonusDrawDamage;
        public int BonusDrawCostReduction;
        public int BonusUntilPlayedCostReduction;
        public int? CostOverrideThisCombat;
        public int EnchantmentAmount;
        public bool EnchantmentDisabled;
        public double EnchantmentBonusDamage;
        public bool FreeThisTurn;
        public bool PendingGuaranteedSearchAdmission;
    }

    private sealed class DeckCardInstance(int instanceId, SimulationCard card)
    {
        // Lazily allocated: the vast majority of card instances never accrue Forge credits,
        // so cloning them across the search tree should not allocate an empty list each time.
        private List<ForgeSourceCredit>? forgeCredits;

        private int _instanceId = instanceId;
        private CardInstanceMutableState mutableState = new()
        {
            EnchantmentAmount = card.Enchantment is null ? 0 : Math.Max(1, card.Enchantment.Amount),
            PendingGuaranteedSearchAdmission = UsesGuaranteedSearchAdmission(card)
        };
        private ulong _searchStateHash;
        private bool _searchStateHashDirty = true;
        private SimulationCardPile? owningPile;

        public int InstanceId
        {
            get => _instanceId;
            private set
            {
                _instanceId = value;
                InvalidateSearchStateHash();
            }
        }

        private SimulationCard _card = card;
        private ulong _stableSearchIdentityHash = SearchCardIdentityCache.GetValue(
            card,
            static value => StableCardSearchIdentity.Create(value)).Hash;

        public SimulationCard Card
        {
            get => _card;
            set
            {
                _card = value;
                _stableSearchIdentityHash = SearchCardIdentityCache.GetValue(
                    value,
                    static cardValue => StableCardSearchIdentity.Create(cardValue)).Hash;
                InvalidateSearchStateHash();
            }
        }

        public ulong SearchStateHash
        {
            get
            {
                if (!_searchStateHashDirty)
                {
                    return _searchStateHash;
                }

                ulong hash = 14695981039346656037UL;
                AddExactSearchHash(ref hash, _instanceId);
                AddExactSearchHash(ref hash, unchecked((long)_stableSearchIdentityHash));
                AddExactSearchHash(ref hash, mutableState.BonusReplayCount);
                AddExactSearchHash(ref hash, BitConverter.DoubleToInt64Bits(mutableState.BonusDrawDamage));
                AddExactSearchHash(ref hash, mutableState.BonusDrawCostReduction);
                AddExactSearchHash(ref hash, mutableState.BonusUntilPlayedCostReduction);
                AddExactSearchHash(ref hash, mutableState.CostOverrideThisCombat ?? int.MinValue);
                AddExactSearchHash(ref hash, mutableState.EnchantmentAmount);
                AddExactSearchHash(ref hash, mutableState.EnchantmentDisabled ? 1 : 0);
                AddExactSearchHash(ref hash, BitConverter.DoubleToInt64Bits(mutableState.EnchantmentBonusDamage));
                AddExactSearchHash(ref hash, mutableState.FreeThisTurn ? 1 : 0);
                AddExactSearchHash(ref hash, mutableState.PendingGuaranteedSearchAdmission ? 1 : 0);
                _searchStateHash = hash;
                _searchStateHashDirty = false;
                return hash;
            }
        }

        // Extra replays enchanted onto THIS instance (HiddenGem). Permanent for the combat: the
        // card replays its play this many extra times every time it is played. Persists across
        // discard/reshuffle because it lives on the instance, not the shared card model.
        public int BonusReplayCount
        {
            get => mutableState.BonusReplayCount;
            set
            {
                mutableState.BonusReplayCount = value;
                InvalidateSearchStateHash();
            }
        }

        // KinglyPunch: raw damage THIS instance has permanently gained from being drawn
        // (AfterCardDrawn adds Card.DamageIncreasePerDraw each draw). Persists across discard/reshuffle
        // because it lives on the instance, not the shared card model.
        public double BonusDrawDamage
        {
            get => mutableState.BonusDrawDamage;
            set
            {
                mutableState.BonusDrawDamage = value;
                InvalidateSearchStateHash();
            }
        }

        // KinglyKick: energy cost THIS instance has permanently lost from being drawn this combat
        // (AfterCardDrawn adds Card.CostReductionPerDraw each draw). Lives on the instance like above.
        public int BonusDrawCostReduction
        {
            get => mutableState.BonusDrawCostReduction;
            set
            {
                mutableState.BonusDrawCostReduction = value;
                InvalidateSearchStateHash();
            }
        }

        public int BonusUntilPlayedCostReduction
        {
            get => mutableState.BonusUntilPlayedCostReduction;
            set
            {
                mutableState.BonusUntilPlayedCostReduction = value;
                InvalidateSearchStateHash();
            }
        }

        public int? CostOverrideThisCombat
        {
            get => mutableState.CostOverrideThisCombat;
            set
            {
                mutableState.CostOverrideThisCombat = value;
                InvalidateSearchStateHash();
            }
        }

        public int EnchantmentAmount
        {
            get => mutableState.EnchantmentAmount;
            set
            {
                mutableState.EnchantmentAmount = value;
                InvalidateSearchStateHash();
            }
        }

        public bool EnchantmentDisabled
        {
            get => mutableState.EnchantmentDisabled;
            set
            {
                mutableState.EnchantmentDisabled = value;
                InvalidateSearchStateHash();
            }
        }

        public double EnchantmentBonusDamage
        {
            get => mutableState.EnchantmentBonusDamage;
            set
            {
                mutableState.EnchantmentBonusDamage = value;
                InvalidateSearchStateHash();
            }
        }

        // Discovery creates a concrete hand instance that is free only for the current turn.
        public bool FreeThisTurn
        {
            get => mutableState.FreeThisTurn;
            set
            {
                mutableState.FreeThisTurn = value;
                InvalidateSearchStateHash();
            }
        }

        public bool PendingGuaranteedSearchAdmission
        {
            get => mutableState.PendingGuaranteedSearchAdmission;
            set
            {
                mutableState.PendingGuaranteedSearchAdmission = value;
                InvalidateSearchStateHash();
            }
        }

        public IReadOnlyList<ForgeSourceCredit> ForgeCredits => forgeCredits ?? (IReadOnlyList<ForgeSourceCredit>)[];

        public void AddForgeCredit(ForgeSourceCredit credit)
        {
            (forgeCredits ??= []).Add(credit);
        }

        public void AttachToPile(SimulationCardPile pile)
        {
            owningPile = pile;
        }

        public void DetachFromPile(SimulationCardPile pile)
        {
            if (ReferenceEquals(owningPile, pile))
            {
                owningPile = null;
            }
        }

        public DeckCardInstance Clone()
        {
            DeckCardInstance clone = new(_instanceId, _card);
            clone.CopyFrom(this);
            return clone;
        }

        public void CopyFrom(DeckCardInstance source)
        {
            _instanceId = source._instanceId;
            _card = source._card;
            _stableSearchIdentityHash = source._stableSearchIdentityHash;
            mutableState = source.mutableState;
            _searchStateHash = source._searchStateHash;
            _searchStateHashDirty = source._searchStateHashDirty;
            if (source.forgeCredits is { Count: > 0 })
            {
                forgeCredits ??= new List<ForgeSourceCredit>(source.forgeCredits.Count);
                forgeCredits.Clear();
                forgeCredits.AddRange(source.forgeCredits);
            }
            else
            {
                forgeCredits?.Clear();
            }
        }

        private void InvalidateSearchStateHash()
        {
            if (_searchStateHashDirty)
            {
                return;
            }

            ulong previousHash = _searchStateHash;
            _searchStateHashDirty = true;
            owningPile?.CardSearchStateChanged(this, previousHash);
        }
    }

    private readonly record struct FreePlayResult(
        double Value,
        IReadOnlyList<CardValueCreditEvent> Credits,
        int AttackSkillPlaysBeforePlay = 0,
        int CardsPlayed = 0,
        IReadOnlyList<CardMoveChoiceEvent>? MoveChoiceEvents = null,
        IReadOnlyList<CardTransformChoiceEvent>? TransformChoiceEvents = null)
    {
        public static FreePlayResult Empty { get; } = new(0d, []);

        public IReadOnlyList<CardMoveChoiceEvent> MoveChoices => MoveChoiceEvents ?? [];

        public IReadOnlyList<CardTransformChoiceEvent> TransformChoices => TransformChoiceEvents ?? [];
    }

    private sealed record ForgeSourceCredit(
        string SourceModelId,
        string SourceTypeName,
        int SourcePlayId,
        double Amount);

    private sealed record ResourceSourceCredit(
        string SourceModelId,
        string SourceTypeName,
        double Amount);

    private sealed record DelayedValueCredit(
        string SourceModelId,
        string SourceTypeName,
        double Value);

    private sealed record FutureTurnOpportunityProfile(
        double ActiveCardCount,
        double ExpectedDraws,
        double ExpectedCardsPlayed,
        double ExpectedAttacksPlayed,
        double ExpectedAttackDirectValue,
        double ExpectedSkillsPlayed,
        double ExpectedStarsSpent,
        double ExpectedStarSpendEvents,
        double ExpectedStarsGained,
        double ExpectedStarGainEvents,
        double ExpectedEnergySpent,
        double AverageEnergyCost,
        double AveragePlayableCardValue,
        double LowestPlayableCardValue,
        double GeneratedCardValue,
        double GeneratedAttackStrengthValuePerPoint,
        double ExpectedGeneratedCards,
        double StrengthValuePerPoint,
        double DexterityValuePerPoint,
        double FastenValuePerPoint,
        double ParryValuePerPoint,
        double SeekingEdgeValuePerPoint,
        double SwordSageValuePerPoint,
        double SovereignBladeDirectValue,
        int ValuedSovereignBladeCount);

    private sealed record GeneratedLibraryContinuationStats(
        double GeneratedCardValue,
        double AttackStrengthValuePerPoint);

    private enum SimulationEventKind
    {
        StarSpent,
        StarGained
    }

    private sealed record SimulationEvent(
        SimulationEventKind Kind,
        int Amount,
        DeckCardInstance? Source = null);

    private enum ActivePowerKind
    {
        Persistent,
        Arsenal,
        Automation,
        Calamity,
        Conqueror,
        Entropy,
        Furnace,
        Genesis,
        Mayhem,
        Monologue,
        Nostalgia,
        Orbit,
        PaleBlueDot,
        Panache,
        PillarOfCreation,
        Plating,
        PrepTime,
        RetainHand,
        RollingBoulder,
        SpectrumShift,
        Stratagem,
        TheSealedThrone,
        TheBomb,
        Thorns,
        Tyranny,
        VoidForm
    }

    private struct ActivePowerMutableState
    {
        public double Amount;
        public int Counter;
    }

    private sealed class ActivePower(
        string sourceModelId,
        string sourceTypeName,
        ActivePowerKind kind,
        SimulationCard sourceCard,
        double amount,
        double secondaryAmount = 0d,
        int counter = 0,
        int sourceInstanceId = -1,
        ISimulationPowerBehavior? behavior = null)
    {
        private ActivePowerMutableState mutableState = new()
        {
            Amount = amount,
            Counter = counter
        };
        private ulong _searchStateHash;
        private bool _searchStateHashDirty = true;
        private ActivePowerCollection? owningCollection;

        public string SourceModelId { get; private set; } = sourceModelId;

        public string SourceTypeName { get; private set; } = sourceTypeName;

        public ActivePowerKind Kind { get; private set; } = kind;

        public SimulationCard SourceCard { get; private set; } = sourceCard;

        public double Amount
        {
            get => mutableState.Amount;
            set
            {
                mutableState.Amount = value;
                InvalidateSearchStateHash();
            }
        }

        public double SecondaryAmount { get; private set; } = secondaryAmount;

        public int Counter
        {
            get => mutableState.Counter;
            set
            {
                mutableState.Counter = value;
                InvalidateSearchStateHash();
            }
        }

        public int SourceInstanceId { get; private set; } = sourceInstanceId;

        public ISimulationPowerBehavior? Behavior { get; private set; } = behavior;

        public ulong SearchStateHash
        {
            get
            {
                if (!_searchStateHashDirty)
                {
                    return _searchStateHash;
                }

                ulong hash = 14695981039346656037UL;
                AddExactSearchHash(ref hash, (int)Kind);
                AddExactSearchHash(ref hash, unchecked((long)StableSearchStringHash(SourceModelId)));
                AddExactSearchHash(ref hash, BitConverter.DoubleToInt64Bits(mutableState.Amount));
                AddExactSearchHash(ref hash, BitConverter.DoubleToInt64Bits(SecondaryAmount));
                AddExactSearchHash(ref hash, mutableState.Counter);
                AddExactSearchHash(ref hash, SourceInstanceId);
                _searchStateHash = hash;
                _searchStateHashDirty = false;
                return hash;
            }
        }

        public static ActivePower Persistent(
            string sourceModelId,
            string sourceTypeName,
            SimulationCard sourceCard,
            ISimulationPowerBehavior behavior)
        {
            return new ActivePower(sourceModelId, sourceTypeName, ActivePowerKind.Persistent, sourceCard, 0d, behavior: behavior);
        }

        public ActivePower Clone()
        {
            ActivePower clone = new(
                SourceModelId,
                SourceTypeName,
                Kind,
                SourceCard,
                Amount,
                SecondaryAmount,
                Counter,
                SourceInstanceId,
                Behavior);
            clone._searchStateHash = _searchStateHash;
            clone._searchStateHashDirty = _searchStateHashDirty;
            return clone;
        }

        public void AttachToCollection(ActivePowerCollection collection)
        {
            owningCollection = collection;
        }

        public void DetachFromCollection(ActivePowerCollection collection)
        {
            if (ReferenceEquals(owningCollection, collection))
            {
                owningCollection = null;
            }
        }

        public void CopyFrom(ActivePower source)
        {
            SourceModelId = source.SourceModelId;
            SourceTypeName = source.SourceTypeName;
            Kind = source.Kind;
            SourceCard = source.SourceCard;
            mutableState = source.mutableState;
            SecondaryAmount = source.SecondaryAmount;
            SourceInstanceId = source.SourceInstanceId;
            Behavior = source.Behavior;
            _searchStateHash = source._searchStateHash;
            _searchStateHashDirty = source._searchStateHashDirty;
        }

        private void InvalidateSearchStateHash()
        {
            if (_searchStateHashDirty)
            {
                return;
            }

            ulong previousHash = _searchStateHash;
            _searchStateHashDirty = true;
            owningCollection?.PowerSearchStateChanged(this, previousHash);
        }
    }

    private interface ISimulationPowerBehavior
    {
        IReadOnlyList<PowerResolution> Resolve(SimulationEvent simulationEvent, ActivePower source);

        double EstimateFutureTurnValue(FutureTurnOpportunityProfile profile, ActivePower source);
    }

    private sealed class ChildOfTheStarsBehavior(double blockPerStarSpent, double blockValuePerBlock) : ISimulationPowerBehavior
    {
        public IReadOnlyList<PowerResolution> Resolve(SimulationEvent simulationEvent, ActivePower source)
        {
            if (simulationEvent.Kind != SimulationEventKind.StarSpent || simulationEvent.Amount <= 0)
            {
                return [];
            }

            double value = simulationEvent.Amount * blockPerStarSpent * blockValuePerBlock;
            return [new PowerResolution(source.SourceModelId, source.SourceTypeName, value)];
        }

        public double EstimateFutureTurnValue(FutureTurnOpportunityProfile profile, ActivePower source)
        {
            return profile.ExpectedStarsSpent * blockPerStarSpent * blockValuePerBlock;
        }
    }

    private sealed class BlackHoleBehavior(
        double damage,
        double damageUnitValue,
        double aoeDamageMultiplier,
        bool triggersOnStarSpent,
        bool triggersOnStarGained) : ISimulationPowerBehavior
    {
        public IReadOnlyList<PowerResolution> Resolve(SimulationEvent simulationEvent, ActivePower source)
        {
            if (simulationEvent.Amount <= 0)
            {
                return [];
            }

            if ((simulationEvent.Kind == SimulationEventKind.StarSpent && !triggersOnStarSpent)
                || (simulationEvent.Kind == SimulationEventKind.StarGained && !triggersOnStarGained)
                || simulationEvent.Kind is not (SimulationEventKind.StarSpent or SimulationEventKind.StarGained))
            {
                return [];
            }

            double value = damage * damageUnitValue * aoeDamageMultiplier;
            return [new PowerResolution(source.SourceModelId, source.SourceTypeName, value)];
        }

        public double EstimateFutureTurnValue(FutureTurnOpportunityProfile profile, ActivePower source)
        {
            double expectedTriggers =
                (triggersOnStarSpent ? profile.ExpectedStarSpendEvents : 0d)
                + (triggersOnStarGained ? profile.ExpectedStarGainEvents : 0d);
            return expectedTriggers * damage * damageUnitValue * aoeDamageMultiplier;
        }
    }

    private sealed record PowerResolution(
        string SourceModelId,
        string SourceTypeName,
        double Value);

    private sealed record PowerEventResult(
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits,
        double AdditionalValue = 0d)
    {
        public static PowerEventResult Empty { get; } = new([], []);

        public double Value => AdditionalValue + PowerResolutions.Sum(resolution => resolution.Value);
    }

    private sealed record DrawResult(
        int CardsDrawn,
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits)
    {
        public double Value => PowerResolutions.Sum(resolution => resolution.Value);
    }

    private sealed record EnchantmentPlayResult(
        int CardsDrawn,
        int EnergyGained,
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits)
    {
        public static EnchantmentPlayResult Empty { get; } = new(0, 0, [], []);

        public double Value => PowerResolutions.Sum(resolution => resolution.Value);
    }

    private sealed record PlayValueResult(
        double DirectValue,
        double Value,
        IReadOnlyList<CardValueCreditEvent> ValueCredits);

    private sealed record CardValueCreditEvent(
        string ModelId,
        string TypeName,
        double DirectValue,
        double ForgeRealizedValue,
        double PowerRealizedValue,
        double EnergyRealizedValue,
        double StarRealizedValue,
        bool CountsAsDirectPlay);

    private sealed record PlayEvent(
        int InstanceId,
        SimulationCard Card,
        double Value,
        double DecisionValue,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        IReadOnlyList<CardValueCreditEvent> ValueCredits,
        IReadOnlyList<CardMoveChoiceEvent> MoveChoices,
        IReadOnlyList<CardTransformChoiceEvent> TransformChoices);

    private readonly record struct PlayOutcome(
        double Value,
        double DecisionValue,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        PlayEvent? Event);

    private sealed record CardMoveChoiceEvent(
        string SourceModelId,
        string SourceTypeName,
        string CandidateModelId,
        string CandidateTypeName,
        string FromPile,
        string ToPile,
        bool WasMoved,
        double CandidateScore);

    private sealed record CardTransformChoiceEvent(
        string SourceModelId,
        string SourceTypeName,
        string CandidateModelId,
        string CandidateTypeName,
        string ReplacementModelId,
        string ReplacementTypeName,
        bool WasTransformed,
        double CandidateScore,
        double ReplacementScore);

    private readonly record struct SearchResult(
        SimulationState State,
        double Value,
        double DecisionValue,
        int CardsPlayed,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        PlayTraceNode? PlayTrace);

    private sealed class PlayTraceNode(PlayEvent play, PlayTraceNode? next)
    {
        public PlayEvent Play { get; } = play;

        public PlayTraceNode? Next { get; set; } = next;
    }

    private struct SearchPrefix
    {
        private double value;
        private double decisionValue;
        private int cardsPlayed;
        private int cardsDrawn;
        private int energySpent;
        private int energyGained;
        private int starSpent;
        private int starGained;
        private PlayTraceNode? head;
        private PlayTraceNode? tail;

        public void Append(PlayOutcome play, SearchBranchDiagnosticsCollector? diagnostics)
        {
            value += play.Value;
            decisionValue += play.DecisionValue;
            cardsPlayed++;
            cardsDrawn += play.CardsDrawn;
            energySpent += play.EnergySpent;
            energyGained += play.EnergyGained;
            starSpent += play.StarSpent;
            starGained += play.StarGained;
            if (play.Event is null)
            {
                return;
            }

            PlayTraceNode node = new(play.Event, null);
            diagnostics?.RecordPlayTraceNode();
            if (tail is null)
            {
                head = node;
            }
            else
            {
                tail.Next = node;
            }

            tail = node;
        }

        public SearchResult Apply(SearchResult suffix)
        {
            if (cardsPlayed == 0)
            {
                return suffix;
            }

            if (tail is not null)
            {
                tail.Next = suffix.PlayTrace;
            }

            return new SearchResult(
                suffix.State,
                value + suffix.Value,
                decisionValue + suffix.DecisionValue,
                cardsPlayed + suffix.CardsPlayed,
                cardsDrawn + suffix.CardsDrawn,
                energySpent + suffix.EnergySpent,
                energyGained + suffix.EnergyGained,
                starSpent + suffix.StarSpent,
                starGained + suffix.StarGained,
                head ?? suffix.PlayTrace);
        }
    }

    private sealed class SearchSession(DeckSimulationOptions options)
    {
        private readonly DeckCardInstance[][] playableBuffers = CreateBuffers<DeckCardInstance>(
            options.MaxCardsPlayedPerTurn + 1,
            Math.Max(1, options.MaxHandSize));
        private readonly DeckCardInstance[][] policyBuffers = CreateBuffers<DeckCardInstance>(
            options.MaxCardsPlayedPerTurn + 1,
            Math.Max(1, options.MaxHandSize));
        private readonly SearchCandidate[][] searchCandidateBuffers = CreateBuffers<SearchCandidate>(
            options.MaxCardsPlayedPerTurn + 1,
            Math.Max(2, options.MaxBranchingCards + 1));
        private readonly SimulationState[] branchStateBuffers = CreateStateBuffers(
            options.MaxCardsPlayedPerTurn + 1);
        private readonly SimulationState[] bestStateBuffers = CreateStateBuffers(
            options.MaxCardsPlayedPerTurn + 1);
        private readonly SimulationState[] greedyTailStopStateBuffers = CreateStateBuffers(
            options.MaxCardsPlayedPerTurn + 1);
        private readonly Dictionary<SearchTranspositionKey, SearchPolicyCacheEntry> transpositions = [];
        private readonly SearchWorkBudget workBudget = options.ActiveSearchWorkBudget
            ?? new SearchWorkBudget(options.MaxSearchNodesPerTurn);
        private readonly SearchTurnProfile? slowTailProfile = options.ActiveSearchTurnProfile;
        private readonly ulong[] loopHashes = new ulong[options.MaxCardsPlayedPerTurn + 1];
        private readonly int[] loopEnergies = new int[options.MaxCardsPlayedPerTurn + 1];
        private readonly int[] loopStars = new int[options.MaxCardsPlayedPerTurn + 1];
        private readonly bool[] loopStatesRecorded = new bool[options.MaxCardsPlayedPerTurn + 1];

        public DeckCardInstance[] PlayableBuffer(int resolvedPlays, int requiredWidth) =>
            EnsureWidth(
                playableBuffers,
                Math.Min(resolvedPlays, playableBuffers.Length - 1),
                requiredWidth);

        public DeckCardInstance[] PolicyBuffer(int resolvedPlays, int requiredWidth) =>
            EnsureWidth(
                policyBuffers,
                Math.Min(resolvedPlays, policyBuffers.Length - 1),
                requiredWidth);

        public SearchCandidate[] SearchCandidateBuffer(int resolvedPlays) =>
            searchCandidateBuffers[Math.Min(resolvedPlays, searchCandidateBuffers.Length - 1)];

        public bool EnterNode(SearchBranchDiagnosticsCollector? diagnostics)
        {
            bool fallback = workBudget.EnterNode();
            diagnostics?.RecordSearchNode(fallback);
            slowTailProfile?.RecordSearchNode(fallback);
            return fallback;
        }

        public bool UsesTranspositions(DeckSimulationOptions searchOptions) =>
            CanUseTranspositions(searchOptions);

        public bool TryFindLoop(
            int resolvedPlays,
            ulong hash,
            int energy,
            int stars,
            out int priorEnergy,
            out int priorStars)
        {
            int limit = Math.Min(resolvedPlays, loopHashes.Length);
            for (int index = limit - 1; index >= 0; index--)
            {
                if (!loopStatesRecorded[index] || loopHashes[index] != hash)
                {
                    continue;
                }

                priorEnergy = loopEnergies[index];
                priorStars = loopStars[index];
                return true;
            }

            priorEnergy = energy;
            priorStars = stars;
            return false;
        }

        public void RecordLoopState(int resolvedPlays, ulong hash, int energy, int stars)
        {
            int index = Math.Min(resolvedPlays, loopHashes.Length - 1);
            loopHashes[index] = hash;
            loopEnergies[index] = energy;
            loopStars[index] = stars;
            loopStatesRecorded[index] = true;
        }

        public SimulationState CloneState(
            SimulationState state,
            int resolvedPlays,
            SearchBranchDiagnosticsCollector? diagnostics)
        {
            diagnostics?.RecordStateClone();
            slowTailProfile?.RecordStateClone();
            SimulationState destination = branchStateBuffers[
                Math.Min(resolvedPlays, branchStateBuffers.Length - 1)];
            destination.CopyFrom(state);
            return destination;
        }

        public SimulationState CaptureBestState(SimulationState state, int resolvedPlays)
        {
            SimulationState destination = bestStateBuffers[
                Math.Min(resolvedPlays, bestStateBuffers.Length - 1)];
            destination.CopyFrom(state);
            return destination;
        }

        public SimulationState CaptureGreedyTailStopState(
            SimulationState state,
            int resolvedPlays,
            SearchBranchDiagnosticsCollector? diagnostics)
        {
            diagnostics?.RecordStateClone();
            slowTailProfile?.RecordStateClone();
            SimulationState destination = greedyTailStopStateBuffers[
                Math.Min(resolvedPlays, greedyTailStopStateBuffers.Length - 1)];
            destination.CopyFrom(state);
            return destination;
        }

        public bool TryGetTransposition(
            SearchTranspositionKey key,
            DeckSimulationOptions searchOptions,
            SearchBranchDiagnosticsCollector? diagnostics,
            out SearchPolicyCacheEntry entry)
        {
            if (!CanUseTranspositions(searchOptions))
            {
                entry = default;
                return false;
            }

            bool hit = transpositions.TryGetValue(key, out entry);
            diagnostics?.RecordTranspositionLookup(hit);
            return hit;
        }

        public void StoreTransposition(
            SearchTranspositionKey key,
            SearchPolicyCacheEntry entry,
            DeckSimulationOptions searchOptions,
            SearchBranchDiagnosticsCollector? diagnostics)
        {
            if (!CanUseTranspositions(searchOptions)
                || transpositions.Count >= searchOptions.TranspositionCapacityPerTurn
                || !transpositions.TryAdd(key, entry))
            {
                return;
            }

            diagnostics?.RecordTranspositionStore();
        }

        private static bool CanUseTranspositions(DeckSimulationOptions searchOptions)
        {
            return searchOptions.TranspositionCapacityPerTurn > 0
                && !searchOptions.CollectSearchPlayTrace
                && searchOptions.SearchPolicyCollector is null;
        }

        private static T[][] CreateBuffers<T>(int depth, int width)
        {
            T[][] buffers = new T[Math.Max(1, depth)][];
            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index] = new T[width];
            }

            return buffers;
        }

        private static T[] EnsureWidth<T>(T[][] buffers, int depth, int requiredWidth)
        {
            T[] buffer = buffers[depth];
            if (buffer.Length >= requiredWidth)
            {
                return buffer;
            }

            int width = Math.Max(requiredWidth, buffer.Length * 2);
            buffer = new T[width];
            buffers[depth] = buffer;
            return buffer;
        }

        private static SimulationState[] CreateStateBuffers(int depth)
        {
            SimulationState[] buffers = new SimulationState[Math.Max(1, depth)];
            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index] = new SimulationState();
            }

            return buffers;
        }
    }

    private readonly record struct ForcedPlayPolicyResult(
        DeckCardInstance? ForcedCard,
        CardCandidateSet OrdinaryCandidates,
        DeckCardInstance? RequiredOrdinaryCandidate = null,
        bool MustPlay = false);

    private readonly struct CardCandidateSet(
        DeckCardInstance[] cards,
        int count) : IReadOnlyList<DeckCardInstance>
    {
        public static CardCandidateSet Empty { get; } = new([], 0);

        public int Count { get; } = count;

        public DeckCardInstance this[int index] => index >= 0 && index < Count
            ? cards[index]
            : throw new ArgumentOutOfRangeException(nameof(index));

        public static CardCandidateSet Single(DeckCardInstance[] buffer, DeckCardInstance card)
        {
            buffer[0] = card;
            return new CardCandidateSet(buffer, 1);
        }

        public IEnumerator<DeckCardInstance> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
            {
                yield return cards[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly record struct SearchStateFingerprint(ulong LoopHash, ulong ExactHash);

    private readonly record struct SearchTranspositionKey(
        ulong LoopHash,
        ulong ExactHash,
        int Seed,
        int ResolvedPlays,
        int FullBranchDecisions,
        int Turn,
        int FutureTurns,
        bool UseFiniteHorizonLeafValue,
        bool WorkBudgetFallback);

    private readonly record struct SearchPolicyCacheEntry(bool Stop, int InstanceId);

    private sealed class StableCardSearchIdentity(ulong hash)
    {
        public ulong Hash { get; } = hash;

        public static StableCardSearchIdentity Create(SimulationCard card)
        {
            ulong value = StableSearchStringHash(card.ReportModelId);
            AddExactSearchHash(ref value, unchecked((long)StableSearchStringHash(card.TypeName)));
            AddExactSearchHash(ref value, card.UpgradeLevel);
            AddExactSearchHash(ref value, card.EnergyCost);
            AddExactSearchHash(ref value, card.StarCost);
            AddExactSearchHash(ref value, card.Cost ?? int.MinValue);
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.IntrinsicValue));
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.DamageValue));
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.BaseDamage));
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.BaseBlock));
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.BlockValuePerBlock));
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.BeamSetupValue));
            AddExactSearchHash(ref value, BitConverter.DoubleToInt64Bits(card.PlaySetupValue));
            AddExactSearchHash(ref value, card.Draw);
            AddExactSearchHash(ref value, card.DrawsToHandFull ? 1 : 0);
            AddExactSearchHash(ref value, card.DrawNextTurn);
            AddExactSearchHash(ref value, card.BlockNextTurn);
            AddExactSearchHash(ref value, card.EnergyGain);
            AddExactSearchHash(ref value, card.EnergyNextTurn);
            AddExactSearchHash(ref value, card.StarGain);
            AddExactSearchHash(ref value, card.StarNextTurn);
            AddExactSearchHash(ref value, card.Forge);
            AddExactSearchHash(ref value, card.ReplayGrant);
            AddExactSearchHash(ref value, card.Vulnerable);
            AddExactSearchHash(ref value, card.Exhausts ? 1 : 0);
            AddExactSearchHash(ref value, card.EndsTurn ? 1 : 0);
            AddExactSearchHash(ref value, card.Unplayable ? 1 : 0);
            AddExactSearchHash(ref value, (int)card.SearchAdmission);
            AddExactSearchHash(ref value, card.PowerPlayPriority);
            return new StableCardSearchIdentity(value);
        }
    }

    private readonly record struct SearchCandidate(
        DeckCardInstance Card,
        double Score);

    private readonly record struct GreedyTailStep(
        PlayOutcome Play,
        SimulationState? StopState,
        double StopDecisionValue,
        string CardTypeName,
        int ResolvedPlayDepth,
        long ProfileStartNodes,
        long ProfileStartedAt);

    private readonly record struct SearchCandidateSet(
        SearchCandidate[] Candidates,
        int Count)
    {
        public static SearchCandidateSet Empty { get; } = new([], 0);
    }

    private sealed record TurnTrialSummary(
        int Turn,
        double Value,
        int CardsDrawn,
        int CardsPlayed,
        int EnergySpent,
        int EnergyGained,
        int EnergyWasted,
        int StarSpent,
        int StarGained,
        int StarsWasted,
        double UnplayedIntrinsicValue,
        IReadOnlyList<PlayEvent> PlayedCards,
        IReadOnlyList<CardValueCreditEvent> ValueCredits);

    private sealed class CardPlayAccumulator(string modelId, string typeName)
    {
        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int PlayCount { get; set; }

        public double TotalValue { get; set; }

        public int TotalPositionInTurn { get; set; }

        public int MinimumPositionInTurn { get; set; } = int.MaxValue;

        public int MaximumPositionInTurn { get; set; }
    }

    private sealed class CardMoveChoiceAccumulator(
        string sourceModelId,
        string sourceTypeName,
        string candidateModelId,
        string candidateTypeName,
        string fromPile,
        string toPile)
    {
        public string SourceModelId { get; } = sourceModelId;
        public string SourceTypeName { get; } = sourceTypeName;
        public string CandidateModelId { get; } = candidateModelId;
        public string CandidateTypeName { get; } = candidateTypeName;
        public string FromPile { get; } = fromPile;
        public string ToPile { get; } = toPile;
        public int CandidateSeenCount { get; set; }
        public int MoveCount { get; set; }
        public double CandidateScoreSum { get; set; }
        public double MovedCandidateScoreSum { get; set; }
        public double RetainedCandidateScoreSum { get; set; }
        public double MinimumCandidateScore { get; set; } = double.PositiveInfinity;
        public double MaximumCandidateScore { get; set; } = double.NegativeInfinity;
    }

    private sealed class CardTransformChoiceAccumulator(
        string sourceModelId,
        string sourceTypeName,
        string candidateModelId,
        string candidateTypeName,
        string replacementModelId,
        string replacementTypeName)
    {
        public string SourceModelId { get; } = sourceModelId;
        public string SourceTypeName { get; } = sourceTypeName;
        public string CandidateModelId { get; } = candidateModelId;
        public string CandidateTypeName { get; } = candidateTypeName;
        public string ReplacementModelId { get; } = replacementModelId;
        public string ReplacementTypeName { get; } = replacementTypeName;
        public int CandidateSeenCount { get; set; }
        public int TransformCount { get; set; }
        public double CandidateScoreSum { get; set; }
        public double TransformedCandidateScoreSum { get; set; }
        public double RetainedCandidateScoreSum { get; set; }
        public double MinimumCandidateScore { get; set; } = double.PositiveInfinity;
        public double MaximumCandidateScore { get; set; } = double.NegativeInfinity;
        public double ReplacementScoreSum { get; set; }
    }

    private sealed record ExplicitResourceReferenceValues(
        double Draw,
        double Energy,
        double Star);

    private sealed class CardPlayTurnAccumulator(int turn, string modelId, string typeName)
    {
        public int Turn { get; } = turn;

        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int PlayCount { get; set; }

        public double TotalValue { get; set; }
    }

    private sealed class CardValueCreditAccumulator(string modelId, string typeName)
    {
        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int DirectPlayCount { get; set; }

        public double DirectValue { get; set; }

        public double ForgeRealizedValue { get; set; }

        public double PowerRealizedValue { get; set; }

        public double EnergyRealizedValue { get; set; }

        public double StarRealizedValue { get; set; }

        public double TotalCreditedValue => DirectValue + ForgeRealizedValue + PowerRealizedValue + EnergyRealizedValue + StarRealizedValue;
    }

    private sealed class CardValueCreditTurnAccumulator(int turn, string modelId, string typeName)
    {
        public int Turn { get; } = turn;

        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int DirectPlayCount { get; set; }

        public double DirectValue { get; set; }

        public double ForgeRealizedValue { get; set; }

        public double PowerRealizedValue { get; set; }

        public double EnergyRealizedValue { get; set; }

        public double StarRealizedValue { get; set; }

        public double TotalCreditedValue => DirectValue + ForgeRealizedValue + PowerRealizedValue + EnergyRealizedValue + StarRealizedValue;
    }

    private sealed class ExpectedValueLocalSums(int turns, int startingInstanceCount)
    {
        public double[] TurnValueSums { get; } = new double[turns];

        public int[,] InstancePlayCounts { get; } = new int[turns, startingInstanceCount];
    }

    private sealed class TrackedCardLocalSums(int turns)
    {
        public double[] TurnValueSums { get; } = new double[turns];

        public int[] DrawCounts { get; } = new int[turns];

        public int[] PlayCounts { get; } = new int[turns];

        public int[] DirectPlayCounts { get; } = new int[turns];

        public double[] DirectValueSums { get; } = new double[turns];

        public double[] ForgeValueSums { get; } = new double[turns];

        public double[] PowerValueSums { get; } = new double[turns];

        public double[] EnergyValueSums { get; } = new double[turns];

        public double[] StarValueSums { get; } = new double[turns];
    }
}
