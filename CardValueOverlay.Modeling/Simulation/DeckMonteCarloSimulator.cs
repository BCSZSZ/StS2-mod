using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class DeckMonteCarloSimulator
{
    private const double NextTurnExplicitResourceReferenceMultiplier = 0.75d;

    // Bounds nested free plays (auto-play chains / replays that themselves auto-play) so the play
    // path stays recursion-safe. A card at this depth resolves its own effects but triggers no
    // further nested auto-play.
    private const int MaxNestedPlayDepth = 3;

    // Default forward horizon (in turns) for the teacher route-value Q. The teacher forces a
    // candidate card, then rolls the game forward this many turns at the teacher beam width, so
    // engine/persistent-power payoff is realized in the label instead of relying on a setup prior.
    private const int DefaultTeacherForwardTurns = 4;

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

    public IReadOnlyList<decimal> SimulateExpectedTurnValues(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        IReadOnlyList<SimulationCard> simulationDeck = NormalizeStartingDeck(deck);
        Validate(simulationDeck, options);

        // Expected-value sampling never reads attribution; skip building credit events entirely.
        options = options with { CollectAttribution = false };
        double[] turnValueSums = new double[options.Turns];
        FastRandom seedRng = new(options.Seed);
        int[] runSeeds = Enumerable.Range(0, options.Runs)
            .Select(_ => seedRng.Next())
            .ToArray();
        int runDegreeOfParallelism = Math.Max(1, options.RunDegreeOfParallelism);
        if (runDegreeOfParallelism <= 1)
        {
            for (int run = 0; run < options.Runs; run++)
            {
                FastRandom rng = new(runSeeds[run]);
                SimulationState state = SimulationState.Create(simulationDeck, rng, options);
                for (int turn = 1; turn <= options.Turns; turn++)
                {
                    TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                    turnValueSums[turn - 1] += summary.Value;
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
                () => new double[options.Turns],
                (run, _, localSums) =>
                {
                    FastRandom rng = new(runSeeds[run]);
                    SimulationState state = SimulationState.Create(simulationDeck, rng, options);
                    for (int turn = 1; turn <= options.Turns; turn++)
                    {
                        TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                        localSums[turn - 1] += summary.Value;
                    }

                    return localSums;
                },
                localSums =>
                {
                    lock (sumLock)
                    {
                        for (int turn = 0; turn < localSums.Length; turn++)
                        {
                            turnValueSums[turn] += localSums[turn];
                        }
                    }
                });
        }

        return turnValueSums
            .Select(sum => Round(sum / options.Runs))
            .ToArray();
    }

    public TrackedCardSimulationReport SimulateTrackedCard(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options,
        string trackedModelId,
        bool collectCredits)
    {
        IReadOnlyList<SimulationCard> simulationDeck = NormalizeStartingDeck(deck);
        Validate(simulationDeck, options);

        options = options with { CollectAttribution = collectCredits };
        double[] turnValueSums = new double[options.Turns];
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
            int[] localPlayCounts,
            int[] localDirectPlayCounts,
            double[] localDirectValueSums,
            double[] localForgeValueSums,
            double[] localPowerValueSums,
            double[] localEnergyValueSums,
            double[] localStarValueSums)
        {
            localTurnValueSums[turnIndex] += summary.Value;
            foreach (PlayEvent played in summary.PlayedCards)
            {
                if (string.Equals(played.Card.ModelId, trackedModelId, StringComparison.OrdinalIgnoreCase))
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
                if (!string.Equals(credit.ModelId, trackedModelId, StringComparison.OrdinalIgnoreCase))
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
                SimulationState state = SimulationState.Create(simulationDeck, rng, options);
                for (int turn = 1; turn <= options.Turns; turn++)
                {
                    TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                    AddTurn(
                        turn - 1,
                        summary,
                        turnValueSums,
                        playCounts,
                        directPlayCounts,
                        directValueSums,
                        forgeValueSums,
                        powerValueSums,
                        energyValueSums,
                        starValueSums);
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
                () => new TrackedCardLocalSums(options.Turns),
                (run, _, local) =>
                {
                    FastRandom rng = new(runSeeds[run]);
                    SimulationState state = SimulationState.Create(simulationDeck, rng, options);
                    for (int turn = 1; turn <= options.Turns; turn++)
                    {
                        TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
                        AddTurn(
                            turn - 1,
                            summary,
                            local.TurnValueSums,
                            local.PlayCounts,
                            local.DirectPlayCounts,
                            local.DirectValueSums,
                            local.ForgeValueSums,
                            local.PowerValueSums,
                            local.EnergyValueSums,
                            local.StarValueSums);
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
            SimulationState state = SimulationState.Create(simulationDeck, rng, options);
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
                SimulateRun);
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

                foreach (PlayEvent played in summary.PlayedCards)
                {
                    SimulationCard card = played.Card;
                    if (!cardPlayAccumulators.TryGetValue(card.ModelId, out CardPlayAccumulator? accumulator))
                    {
                        accumulator = new CardPlayAccumulator(card.ModelId, card.TypeName);
                        cardPlayAccumulators.Add(card.ModelId, accumulator);
                    }

                    accumulator.PlayCount++;
                    accumulator.TotalValue += played.Value;

                    (int Turn, string ModelId) turnKey = (turn, card.ModelId);
                    if (!cardPlayByTurnAccumulators.TryGetValue(turnKey, out CardPlayTurnAccumulator? turnAccumulator))
                    {
                        turnAccumulator = new CardPlayTurnAccumulator(turn, card.ModelId, card.TypeName);
                        cardPlayByTurnAccumulators.Add(turnKey, turnAccumulator);
                    }

                    turnAccumulator.PlayCount++;
                    turnAccumulator.TotalValue += played.Value;
                }

                foreach (CardValueCreditEvent credit in summary.ValueCredits)
                {
                    if (trackedCreditModelId is not null
                        && !string.Equals(credit.ModelId, trackedCreditModelId, StringComparison.OrdinalIgnoreCase))
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
                item.PlayCount == 0 ? 0m : Round(item.TotalValue / item.PlayCount)))
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

    private static TurnTrialSummary PlayTurn(
        SimulationState state,
        DeckSimulationOptions options,
        FastRandom rng,
        int run,
        int turn)
    {
        bool collect = options.CollectAttribution;
        state.TurnEnded = false;
        state.CardsPlayedThisTurn = 0;
        state.AttacksPlayedThisTurn = 0;
        state.SkillsPlayedThisTurn = 0;
        int queuedEnergy = state.NextTurnEnergy;
        int queuedStars = state.NextTurnStars;
        int queuedDraw = state.NextTurnDraw;
        state.CurrentTurnEnergySources.Clear();
        state.CurrentTurnEnergySources.AddRange(state.NextTurnEnergySources);
        state.NextTurnEnergySources.Clear();
        IReadOnlyList<ResourceSourceCredit> queuedStarSources = state.NextTurnStarSources.ToArray();
        state.NextTurnStarSources.Clear();
        double delayedBlockValue = state.NextTurnBlockCredits.Sum(credit => credit.Value);
        IReadOnlyList<CardValueCreditEvent> delayedBlockCredits = collect
            ? DelayedDirectCredits(state.NextTurnBlockCredits)
            : [];
        state.NextTurnEnergy = 0;
        state.NextTurnStars = 0;
        state.NextTurnDraw = 0;
        state.NextTurnBlock = 0;
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

        state.StarSources.AddRange(queuedStarSources);

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
        int drawCount = options.HandSize + queuedDraw + HandDrawBonus(state);
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        PowerEventResult playerTurnStartResult = ResolveAfterPlayerTurnStartPowers(state, options, rng);
        SearchResult result = Search(state.Clone(), options, run, turn, actionsPlayed: 0, rng.Next());
        state.CopyFrom(result.State);

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
                .. PowerCredits(drawResult.PowerResolutions),
                .. drawResult.ValueCredits,
                .. PowerCredits(playerTurnStartResult.PowerResolutions),
                .. playerTurnStartResult.ValueCredits,
                .. result.PlayedCards.SelectMany(card => card.ValueCredits),
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

        return new TurnTrialSummary(
            turn,
            turnStartValue
                + turnStartPowerResult.Value
                + beforeDrawResult.Value
                + drawResult.Value
                + playerTurnStartResult.Value
                + result.Value
                + turnEndPowerResult.Value,
            drawResult.CardsDrawn + result.CardsDrawn,
            result.CardsPlayed,
            result.EnergySpent,
            result.EnergyGained,
            energyWasted,
            result.StarSpent,
            result.StarGained,
            starsWasted,
            unplayedIntrinsicValue,
            result.PlayedCards,
            valueCredits);
    }

    private static SearchResult Search(SimulationState state, DeckSimulationOptions options, int run, int turn, int actionsPlayed, int seed)
    {
        SearchResult best = new(state, 0d, 0d, 0, 0, 0, 0, 0, 0, []);
        if (state.TurnEnded || actionsPlayed >= options.MaxCardsPlayedPerTurn)
        {
            return best;
        }

        IReadOnlyList<DeckCardInstance> legalPlayableCards = SelectPlayableCards(state, options);
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
                actionsPlayed,
                seed);
            if (group is not null)
            {
                collector.TryAdd(group);
            }
        }

        IReadOnlyList<DeckCardInstance> playableCards = SelectTopPlayableCards(state, options, legalPlayableCards);

        foreach (DeckCardInstance card in playableCards)
        {
            SimulationState next = state.Clone();
            DeckCardInstance nextCard = FindHandCard(next, card.InstanceId);
            FastRandom branchRng = new(DeriveSeed(seed, actionsPlayed, card.InstanceId));
            PlayEvent play = PlayCard(next, nextCard, branchRng, options);
            SearchResult suffix = Search(next, options, run, turn, actionsPlayed + 1, branchRng.Next());
            List<PlayEvent> playedCards = [play, .. suffix.PlayedCards];
            SearchResult candidate = new(
                suffix.State,
                play.Value + suffix.Value,
                play.DecisionValue + suffix.DecisionValue,
                1 + suffix.CardsPlayed,
                play.CardsDrawn + suffix.CardsDrawn,
                play.EnergySpent + suffix.EnergySpent,
                play.EnergyGained + suffix.EnergyGained,
                play.StarSpent + suffix.StarSpent,
                play.StarGained + suffix.StarGained,
                playedCards);

            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IReadOnlyList<DeckCardInstance> SelectPlayableCards(
        SimulationState state,
        DeckSimulationOptions options)
    {
        // P3: runs at every search node; build the playable list with a plain loop instead of
        // Where(...).ToArray() to avoid the closure + enumerator + array churn.
        List<DeckCardInstance> playable = [];
        foreach (DeckCardInstance card in state.Hand)
        {
            if (CanPlay(card.Card, state, options))
            {
                playable.Add(card);
            }
        }

        return playable;
    }

    private static IReadOnlyList<DeckCardInstance> SelectTopPlayableCards(
        SimulationState state,
        DeckSimulationOptions options,
        IReadOnlyList<DeckCardInstance> legalPlayableCards)
    {
        int limit = Math.Min(Math.Max(0, options.MaxBranchingCards), legalPlayableCards.Count);
        if (limit == 0)
        {
            return [];
        }

        DeckCardInstance[] selectedCards = new DeckCardInstance[limit];
        double[] selectedScores = new double[limit];
        int selectedCount = 0;
        foreach (DeckCardInstance card in legalPlayableCards)
        {
            double score = ScoreSearchCard(card, state, options);
            int insertIndex = 0;
            while (insertIndex < selectedCount
                && (selectedScores[insertIndex] > score
                    || (selectedScores[insertIndex] == score
                        && selectedCards[insertIndex].InstanceId < card.InstanceId)))
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
                selectedCards[index] = selectedCards[index - 1];
                selectedScores[index] = selectedScores[index - 1];
            }

            selectedCards[insertIndex] = card;
            selectedScores[insertIndex] = score;
            if (selectedCount < limit)
            {
                selectedCount++;
            }
        }

        DeckCardInstance[] result = new DeckCardInstance[selectedCount];
        Array.Copy(selectedCards, result, selectedCount);
        return result;
    }

    private static double ScoreSearchCard(DeckCardInstance card, SimulationState state, DeckSimulationOptions options)
    {
        if (options.SearchCardScorer is { } scorer)
        {
            // A learned scorer is expected to have captured cross-card effects from features itself.
            return scorer.Score(BuildSearchCardScoringContext(card.Card, state, options));
        }

        // Heuristic beam: add cross-card synergy bonuses so a narrow beam keeps enabler cards
        // (e.g. skills that pump a skills-scaling attack in hand) ranked ahead of alternatives.
        return CardSearchScore(card, state, options) + SearchSynergyBonus(card.Card, state, options);
    }

    // Cross-card synergy framework for the heuristic play-search. Each hook inspects the card being
    // scored plus the full state (crucially, OTHER cards in hand) and returns a search-score bonus
    // capturing coupling that the per-card heuristic cannot see on its own. These bonuses bias ONLY
    // the search beam/ordering — they are never added to realized value (PlayCard/PlayValue) — so an
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
                || !CanPlay(payoff, state, options))
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
                DefaultTeacherForwardTurns);
        metadata = metadata with
        {
            TeacherMaxBranchingCards = Math.Max(1, metadata.TeacherMaxBranchingCards),
            TeacherMaxCardsPlayedPerTurn = Math.Max(1, metadata.TeacherMaxCardsPlayedPerTurn),
            TeacherForwardTurns = Math.Max(1, metadata.TeacherForwardTurns)
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
                seed);
            unranked.Add(new SearchPolicyActionSample(
                card.Card.ModelId,
                card.Card.TypeName,
                card.InstanceId,
                BuildActionFeatures(card.Card, state, options),
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
            CollectAttribution = false
        };
        int forwardTurns = Math.Max(1, metadata.TeacherForwardTurns);

        // Common random numbers: every candidate card at this decision node shares one RNG stream
        // (seeded by the decision, not the card), so the score difference reflects the forced first
        // play, not draw luck.
        SimulationState next = state.Clone();
        DeckCardInstance nextCard = FindHandCard(next, firstCard.InstanceId);
        FastRandom rng = new(DeriveSeed(seed, turn, actionsPlayed));

        // Force this card as the next play, then finish the current turn's play phase at the teacher
        // beam width. Rank by REALIZED value (not DecisionValue): no setup-priority / resource-prior
        // in the label. Search a CLONE so its base-case result never aliases `next` (a self-CopyFrom
        // would wipe the piles); this mirrors PlayTurn's Search(state.Clone()) pattern.
        PlayEvent play = PlayCard(next, nextCard, rng, teacherOptions);
        SearchResult suffix = Search(next.Clone(), teacherOptions, run, turn, actionsPlayed + 1, rng.Next());
        next.CopyFrom(suffix.State);
        double total = play.Value + suffix.Value;

        // Roll the game forward so persistent-power / engine payoff is realized in the label. This is
        // why VoidForm / Calamity score high without a hand-tuned setup prior: forcing them first
        // genuinely raises the total value over the forward horizon.
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
        SimulationCard card,
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

        return new SearchCardScoringContext(card.ModelId, card.TypeName, features);
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
        AddFeature(features, "context.playableHandCount", state.Hand.Count(card => CanPlay(card.Card, state, options)));
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
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal);
        AddFeature(features, "card.energyCost", card.EnergyCost);
        AddFeature(features, "card.effectiveEnergyCost", EffectiveEnergyCost(card, state));
        AddFeature(features, "card.starCost", card.StarCost);
        AddFeature(features, "card.effectiveStarCost", EffectiveStarCost(card, state));
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
        AddFeature(features, "card.setupPriorityValue", card.SetupPriorityValue);
        AddFeature(features, "card.effectiveSetupPriorityValue", card.EffectiveSetupPriorityValue);
        AddFeature(features, "card.upgradeLevel", card.UpgradeLevel);
        AddFeature(features, "card.layer", card.Layer);
        AddFeature(features, "card.isPlayable", card.IsPlayable);
        AddFeature(features, "card.canPlay", CanPlay(card, state, options));
        AddFeature(features, "card.isAttack", card.IsAttack);
        AddFeature(features, "card.isPower", card.IsPower);
        AddFeature(features, "card.exhausts", card.Exhausts);
        AddFeature(features, "card.ethereal", card.Ethereal);
        AddFeature(features, "card.retain", card.Retain);
        AddFeature(features, "card.innate", card.Innate);
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

        return features;
    }

    // P3: called many times per candidate per search node (strength/dex/vigor/… modifiers). Plain
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

    private static PlayEvent PlayCard(SimulationState state, DeckCardInstance card, FastRandom rng, DeckSimulationOptions options)
    {
        bool collect = options.CollectAttribution;
        state.Hand.Remove(card);
        SimulationCard playedCard = card.Card;
        int playId = state.NextPlayEventId++;
        int energyCost = EffectiveEnergyCost(playedCard, state);
        int starCost = EffectiveStarCost(playedCard, state);
        PowerEventResult beforeCardPlayedResult = ResolveBeforeCardPlayedPowers(state);
        PlayValueResult playValue = PlayValue(playedCard, state, collect, card.BonusDrawDamage);
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
        if (playedCard.EnergyGain > 0)
        {
            state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                playedCard.ModelId,
                playedCard.TypeName,
                playedCard.EnergyGain));
        }

        state.Stars += playedCard.StarGain;
        state.StarsGainedThisTurn += playedCard.StarGain;
        IReadOnlyList<ResourceSourceCredit> starGainSources = playedCard.StarGain > 0
            ? [new ResourceSourceCredit(playedCard.ModelId, playedCard.TypeName, playedCard.StarGain)]
            : [];
        if (playedCard.StarGain > 0)
        {
            state.StarSources.AddRange(starGainSources);
        }

        IReadOnlyList<PowerResolution> starGainedResolutions = playedCard.StarGain > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, playedCard.StarGain, card))
            : [];
        state.NextTurnEnergy += playedCard.EnergyNextTurn;
        if (playedCard.EnergyNextTurn > 0)
        {
            state.NextTurnEnergySources.Add(new ResourceSourceCredit(
                playedCard.ModelId,
                playedCard.TypeName,
                playedCard.EnergyNextTurn));
        }

        state.NextTurnStars += playedCard.StarNextTurn;
        if (playedCard.StarNextTurn > 0)
        {
            state.NextTurnStarSources.Add(new ResourceSourceCredit(
                playedCard.ModelId,
                playedCard.TypeName,
                playedCard.StarNextTurn));
        }

        state.NextTurnDraw += playedCard.DrawNextTurn;
        if (playedCard.BlockNextTurn > 0)
        {
            state.NextTurnBlock += playedCard.BlockNextTurn;
            state.NextTurnBlockCredits.Add(new DelayedValueCredit(
                playedCard.ModelId,
                playedCard.TypeName,
                playedCard.BlockNextTurn * playedCard.BlockValuePerBlock));
        }

        ApplyEnemyVulnerable(state, playedCard);
        ResolveBeforeForgeCardActions(state, playedCard, options);
        int forgeAmount = playedCard.Forge + DynamicForgeAmount(playedCard, state);
        PowerEventResult forgeResult = ApplyForge(state, forgeAmount, card, playId);
        int drawCount = playedCard.DrawsToHandFull
            ? Math.Max(0, state.MaxHandSize - state.Hand.Count)
            : playedCard.Draw;
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        SimulationCard? transformedPlayedCard = ResolveCardObjectActions(state, card, options);
        PowerEventResult generatedCardResult = ResolveGeneratedCardActions(state, card, rng, options);
        FreePlayResult autoPlay = ResolveAutoPlayActions(state, card, rng, options, depth: 0);
        double autoPlayValue = autoPlay.Value;
        // HiddenGem: enchant a random draw-pile card, then realize any replays already enchanted onto
        // THIS instance by fully RE-PLAYING it through the real OnPlay path (recomputes damage/scaling,
        // re-gains stars, re-draws, re-triggers powers) instead of multiplying a precomputed value.
        ResolveReplayGrant(state, playedCard, rng);
        double bonusReplayValue = 0d;
        List<CardValueCreditEvent>? bonusReplayCredits = collect ? [] : null;
        for (int replay = 0; replay < card.BonusReplayCount; replay++)
        {
            FreePlayResult replayResult = ResolveFreeCardPlay(state, card, rng, options, depth: 1);
            bonusReplayValue += replayResult.Value;
            bonusReplayCredits?.AddRange(replayResult.Credits);
        }

        InstallPower(state, card);
        PowerEventResult afterCardPlayedResult = ResolveAfterCardPlayedPowers(state, card, rng, options);
        IReadOnlyList<PowerResolution> powerResolutions =
        [
            .. beforeCardPlayedResult.PowerResolutions,
            .. starSpentResolutions,
            .. starGainedResolutions,
            .. energySpentResult.PowerResolutions,
            .. forgeResult.PowerResolutions,
            .. drawResult.PowerResolutions,
            .. generatedCardResult.PowerResolutions,
            .. afterCardPlayedResult.PowerResolutions
        ];
        double powerValue = powerResolutions.Sum(resolution => resolution.Value);
        double value = playValue.Value + powerValue + autoPlayValue + bonusReplayValue;
        double decisionValue = value
            + SetupPriorityDecisionValue(playedCard)
            + ExplicitResourceReferenceValue(playedCard, ResourceReferenceValuesForTurns(options.Turns));
        IReadOnlyList<CardValueCreditEvent> valueCredits;
        if (collect)
        {
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
                        .. afterCardPlayedResult.ValueCredits
                    ],
                    starCredits),
                .. autoPlay.Credits,
                .. (bonusReplayCredits ?? (IReadOnlyList<CardValueCreditEvent>)[])
            ];
        }
        else
        {
            valueCredits = [];
        }

        if (transformedPlayedCard is not null)
        {
            card.Card = transformedPlayedCard;
            state.DiscardPile.Add(card);
        }
        else if (playedCard.Exhausts || IsPowerCard(playedCard))
        {
            state.ExhaustPile.Add(card);
        }
        else if (ReturnsPlayedCardToDrawTop(playedCard))
        {
            state.DrawPile.Insert(0, card);
        }
        else
        {
            state.DiscardPile.Add(card);
        }

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
        return new PlayEvent(
            playedCard,
            value,
            decisionValue,
            drawResult.CardsDrawn,
            energyCost,
            playedCard.EnergyGain,
            starCost,
            playedCard.StarGain,
            valueCredits);
    }

    private static PlayValueResult PlayValue(
        SimulationCard card,
        SimulationState state,
        bool collectCredits,
        double bonusDrawDamage = 0d)
    {
        double xCostDamageValue = XCostDamageValue(card, state);
        double scalingDamageValue = DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: true);
        // KinglyPunch: damage accrued from prior draws adds to this single-target hit's value.
        double drawScalingDamageValue = bonusDrawDamage * card.DamageUnitValue;
        double directDamageValue = card.DamageValue + scalingDamageValue + xCostDamageValue + drawScalingDamageValue;
        double vulnerableBonus = VulnerableBonus(directDamageValue, state);
        double directValue = card.IntrinsicValue
            + scalingDamageValue
            + xCostDamageValue
            + drawScalingDamageValue
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
    // fields), yet the play-search re-evaluates them for every candidate card at every search node —
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
            card.Card.ModelId,
            card.Card.TypeName,
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
        if (attributableAmount <= 0d)
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
        state.EnemyVulnerableSources.Add(new ResourceSourceCredit(
            sourceCard.ModelId,
            sourceCard.TypeName,
            sourceCard.Vulnerable));
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

        List<PowerResolution> resolutions = [];
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Behavior is not null)
            {
                resolutions.AddRange(power.Behavior.Resolve(simulationEvent, power));
            }
        }

        return resolutions;
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
        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.SpectrumShift))
        {
            PowerEventResult result = GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                rng,
                "spectrumShift.colorless",
                (int)power.Amount,
                distinct: true,
                upgradeGenerated: false);
            resolutions.AddRange(result.PowerResolutions);
            credits.AddRange(result.ValueCredits);
        }

        return new PowerEventResult(resolutions, credits);
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
        List<PowerResolution> resolutions = [];
        foreach (ActivePower power in state.ActivePowers)
        {
            switch (power.Kind)
            {
                case ActivePowerKind.Plating:
                    if (power.Amount > 0d)
                    {
                        resolutions.Add(new PowerResolution(
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
                    resolutions.Add(new PowerResolution(
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
                        resolutions.Add(new PowerResolution(
                            power.SourceModelId,
                            power.SourceTypeName,
                            power.Amount * power.SourceCard.AoeDamageMultiplier * power.SourceCard.DamageUnitValue));
                        power.Counter = 0;
                    }
                    break;
            }
        }

        state.ActivePowers.RemoveAll(power => power.Kind == ActivePowerKind.TheBomb && power.Counter <= 0);
        return new PowerEventResult(resolutions, []);
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
            state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                power.SourceModelId,
                power.SourceTypeName,
                energy));
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
                    rng,
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
            state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                power.SourceModelId,
                power.SourceTypeName,
                energy));
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

        ResourceSourceCredit source = new(power.SourceModelId, power.SourceTypeName, amount);
        state.Stars += amount;
        state.StarSources.Add(source);
        IReadOnlyList<PowerResolution> starGainedResolutions = DispatchPowerEvent(
            state,
            new SimulationEvent(SimulationEventKind.StarGained, amount));
        resolutions.AddRange(starGainedResolutions);
        credits.AddRange(StarTriggerCredits([source], starGainedResolutions.Sum(resolution => resolution.Value)));
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
        foreach (DeckCardInstance card in selected)
        {
            state.Hand.Remove(card);
            state.ExhaustPile.Add(card);
        }
    }

    private static SimulationCard? ResolveCardObjectActions(
        SimulationState state,
        DeckCardInstance source,
        DeckSimulationOptions options)
    {
        SimulationCard? transformedSource = null;
        foreach (CardActionFact action in source.Card.Actions)
        {
            if (action.Kind == "moveCardBetweenPiles")
            {
                ResolveMoveCardBetweenPiles(state, action);
            }
            else if (action.Kind == "transformCard")
            {
                transformedSource = ResolveTransformCard(state, source, action, options) ?? transformedSource;
            }
        }

        if (BaseTypeName(source.Card) == "Purity")
        {
            ResolvePurityExhaust(state, source.Card);
        }

        return transformedSource;
    }

    // Purity: choose up to Cards (3, upgraded 5) hand cards and Exhaust them. Simplified selection:
    // only exhaust genuinely low-value fodder — basic Strike/Defend (including upgraded) and any
    // attack whose StaticEstimatedValue < 15 — so it never culls a good card. The deck-thinning
    // payoff is measured by play-delta ΔEV.
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
        foreach (DeckCardInstance card in eligible)
        {
            state.Hand.Remove(card);
            state.ExhaustPile.Add(card);
        }
    }

    private static bool IsPurityExhaustEligible(SimulationCard card)
    {
        string baseTypeName = BaseTypeName(card);
        if (baseTypeName is "StrikeRegent" or "DefendRegent")
        {
            return true;
        }

        return card.IsAttack && card.StaticEstimatedValue < 15d;
    }

    // Context-aware retrieval selection (Approach A): rank candidate cards by their projected play
    // value in the CURRENT board state (EstimateImmediateSearchValue captures strength/vulnerable/
    // SovereignBlade synergy, star/energy triggers, forge, etc.), so a fetch grabs the card that is
    // actually best to draw and play next — not just the highest static score.
    private static IReadOnlyList<DeckCardInstance> SelectBestCardsToDraw(
        SimulationState state,
        IReadOnlyList<DeckCardInstance> pile,
        int count)
    {
        return pile
            .OrderByDescending(instance => EstimateImmediateSearchValue(instance.Card, state, XCostEnergy(instance.Card, state)))
            .ThenByDescending(instance => CardObjectChoiceScore(instance))
            .ThenBy(instance => instance.InstanceId)
            .Take(count)
            .ToArray();
    }

    private static void ResolveMoveCardBetweenPiles(SimulationState state, CardActionFact action)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        string? fromPileName = GetParameter(parameters, "from");
        string? toPileName = GetParameter(parameters, "to") ?? GetParameter(parameters, "pile");
        if (fromPileName is null || toPileName is null)
        {
            return;
        }

        List<DeckCardInstance>? fromPile = TryGetPile(state, fromPileName);
        List<DeckCardInstance>? toPile = TryGetPile(state, toPileName);
        if (fromPile is null || toPile is null || fromPile.Count == 0)
        {
            return;
        }

        int count = Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero));
        if (count == 0)
        {
            return;
        }

        bool preferHighValue = IsBeneficialDestination(toPileName);
        // Approach A: retrieving cards INTO a beneficial pile (Hand/Draw, e.g. CosmicIndifference
        // fetching from the discard onto the draw top) should grab the card that is most valuable to
        // PLAY given the current board state — buffs, synergies, available resources — a context-aware
        // proxy for "the card you'd fetch to use next", rather than a static model score. This raises
        // the realized (ΔEV) value of retrieval effects. Non-beneficial moves keep the static
        // lowest-value pick (choosing which card to send away).
        IReadOnlyList<DeckCardInstance> selected = preferHighValue
            ? SelectBestCardsToDraw(state, fromPile, count)
            : SelectCardObjects(fromPile, count, preferHighValue: false);
        foreach (DeckCardInstance selectedCard in selected)
        {
            fromPile.Remove(selectedCard);
        }

        AddCardsToPile(state, toPile, selected, GetParameter(parameters, "position"));
    }

    private static SimulationCard? ResolveTransformCard(
        SimulationState state,
        DeckCardInstance source,
        CardActionFact action,
        DeckSimulationOptions options)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        SimulationCard replacement = ResolveTransformReplacement(parameters, options, source.Card);
        string? fromPileName = GetParameter(parameters, "from");
        if (fromPileName is null)
        {
            return replacement;
        }

        List<DeckCardInstance>? fromPile = TryGetPile(state, fromPileName);
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
        foreach (DeckCardInstance selectedCard in selected)
        {
            selectedCard.Card = replacement;
        }

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

        return BaseTypeName(sourceCard) switch
        {
            "Begone" => "MinionStrike",
            "Guards" => "MinionSacrifice",
            _ => parsedTarget
        };
    }

    private static int TransformCount(SimulationCard sourceCard, CardActionFact action, int available)
    {
        if (available <= 0)
        {
            return 0;
        }

        if (BaseTypeName(sourceCard) == "Guards")
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
        IReadOnlyList<DeckCardInstance> selected = SelectCardObjects(cards, count, preferHighValue: false);
        if (BaseTypeName(sourceCard) != "Guards")
        {
            return selected;
        }

        double replacementScore = CardObjectChoiceScore(replacement);
        return selected
            .Where(card => CardObjectChoiceScore(card) < replacementScore)
            .ToArray();
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
    // and replay (HiddenGem). This runs the REAL play path — damage/block via PlayValue (so
    // conditional scaling like LunarBlast/Radiate is recomputed against current state), star gain and
    // its StarGained triggers, energy gain, next-turn resources, block-next-turn, Vulnerable, Forge,
    // draw, generated cards, nested auto-play, power installs, and after-card-played powers — instead
    // of merely multiplying a precomputed value. It does NOT process the instance's own replay grant
    // loop (that is the caller's job, so replays never fan out exponentially). Depth-guarded so nested
    // auto-play chains stay bounded and recursion-safe.
    private static FreePlayResult ResolveFreeCardPlay(
        SimulationState state,
        DeckCardInstance instance,
        FastRandom rng,
        DeckSimulationOptions options,
        int depth)
    {
        bool collect = options.CollectAttribution;
        SimulationCard playedCard = instance.Card;
        int playId = state.NextPlayEventId++;
        PowerEventResult beforeCardPlayedResult = ResolveBeforeCardPlayedPowers(state);
        PlayValueResult playValue = PlayValue(playedCard, state, collect, instance.BonusDrawDamage);

        state.Energy += playedCard.EnergyGain;
        if (playedCard.EnergyGain > 0)
        {
            state.CurrentTurnEnergySources.Add(new ResourceSourceCredit(
                playedCard.ModelId, playedCard.TypeName, playedCard.EnergyGain));
        }

        state.Stars += playedCard.StarGain;
        state.StarsGainedThisTurn += playedCard.StarGain;
        IReadOnlyList<ResourceSourceCredit> starGainSources = playedCard.StarGain > 0
            ? [new ResourceSourceCredit(playedCard.ModelId, playedCard.TypeName, playedCard.StarGain)]
            : [];
        if (playedCard.StarGain > 0)
        {
            state.StarSources.AddRange(starGainSources);
        }

        IReadOnlyList<PowerResolution> starGainedResolutions = playedCard.StarGain > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, playedCard.StarGain, instance))
            : [];
        state.NextTurnEnergy += playedCard.EnergyNextTurn;
        if (playedCard.EnergyNextTurn > 0)
        {
            state.NextTurnEnergySources.Add(new ResourceSourceCredit(
                playedCard.ModelId, playedCard.TypeName, playedCard.EnergyNextTurn));
        }

        state.NextTurnStars += playedCard.StarNextTurn;
        if (playedCard.StarNextTurn > 0)
        {
            state.NextTurnStarSources.Add(new ResourceSourceCredit(
                playedCard.ModelId, playedCard.TypeName, playedCard.StarNextTurn));
        }

        state.NextTurnDraw += playedCard.DrawNextTurn;
        if (playedCard.BlockNextTurn > 0)
        {
            state.NextTurnBlock += playedCard.BlockNextTurn;
            state.NextTurnBlockCredits.Add(new DelayedValueCredit(
                playedCard.ModelId, playedCard.TypeName, playedCard.BlockNextTurn * playedCard.BlockValuePerBlock));
        }

        ApplyEnemyVulnerable(state, playedCard);
        ResolveBeforeForgeCardActions(state, playedCard, options);
        int forgeAmount = playedCard.Forge + DynamicForgeAmount(playedCard, state);
        PowerEventResult forgeResult = ApplyForge(state, forgeAmount, instance, playId);
        int drawCount = playedCard.DrawsToHandFull
            ? Math.Max(0, state.MaxHandSize - state.Hand.Count)
            : playedCard.Draw;
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        ResolveCardObjectActions(state, instance, options);
        PowerEventResult generatedCardResult = ResolveGeneratedCardActions(state, instance, rng, options);

        double nestedValue = 0d;
        List<CardValueCreditEvent>? nestedCredits = collect ? [] : null;
        FreePlayResult nestedAutoPlay = ResolveAutoPlayActions(state, instance, rng, options, depth);
        nestedValue += nestedAutoPlay.Value;
        nestedCredits?.AddRange(nestedAutoPlay.Credits);
        ResolveReplayGrant(state, playedCard, rng);

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

        IReadOnlyList<PowerResolution> powerResolutions =
        [
            .. beforeCardPlayedResult.PowerResolutions,
            .. starGainedResolutions,
            .. forgeResult.PowerResolutions,
            .. drawResult.PowerResolutions,
            .. generatedCardResult.PowerResolutions,
            .. afterCardPlayedResult.PowerResolutions
        ];
        double powerValue = powerResolutions.Sum(resolution => resolution.Value);
        double value = playValue.Value + powerValue + nestedValue;

        IReadOnlyList<CardValueCreditEvent> credits = [];
        if (collect)
        {
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
                        .. afterCardPlayedResult.ValueCredits
                    ],
                    StarTriggerCredits(starGainSources, starGainedResolutions.Sum(resolution => resolution.Value))),
                .. (nestedCredits ?? (IReadOnlyList<CardValueCreditEvent>)[])
            ];
        }

        return new FreePlayResult(value, credits);
    }

    // Executes a played card's CardCmd.AutoPlay effect: select cards from the descriptor's source
    // pile (per filter + selection mode), remove them from the pile, and PLAY EACH ONE through the
    // real free-play path (ResolveFreeCardPlay) so their star gain, draw, conditional scaling, and
    // powers actually resolve — then send them to the discard pile. Their value flows into deck EV
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

        List<DeckCardInstance>? pile = TryGetPile(state, effect.SourcePile);
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

        if (effect.RepeatSameCard)
        {
            // DecisionsDecisions: choose one hand card (best playable Skill) and play THAT card Count times.
            DeckCardInstance chosen = candidates
                .OrderByDescending(instance => CardObjectChoiceScore(instance))
                .ThenBy(instance => instance.InstanceId)
                .First();
            pile.Remove(chosen);
            for (int play = 0; play < effect.Count; play++)
            {
                FreePlayResult result = ResolveFreeCardPlay(state, chosen, rng, options, depth + 1);
                total += result.Value;
                credits?.AddRange(result.Credits);
            }

            state.DiscardPile.Add(chosen);
            return new FreePlayResult(total, credits ?? (IReadOnlyList<CardValueCreditEvent>)[]);
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
            state.DiscardPile.Add(instance);
        }

        return new FreePlayResult(total, credits ?? (IReadOnlyList<CardValueCreditEvent>)[]);
    }

    // HiddenGem: enchants a RANDOM eligible draw-pile card with ReplayGrant extra replays. This is a
    // real state mutation (not a value estimate): the chosen instance's BonusReplayCount is raised, and
    // its extra plays are realized in PlayCard only if it is actually drawn and played later. HiddenGem's
    // own value therefore comes out of the play-delta ΔEV, exactly like draw/create cards. Eligible =
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
    // value" card to sacrifice — even against an upgraded Defend+ with no replay. Scale the model
    // score by the replay multiplier so keep/sacrifice decisions rank instances, not just card models.
    private static double CardObjectChoiceScore(DeckCardInstance instance)
    {
        return CardObjectChoiceScore(instance.Card) * (1 + instance.BonusReplayCount);
    }

    private static bool IsBeneficialDestination(string pileName)
    {
        string normalized = NormalizePileName(pileName);
        return normalized is "Hand" or "Draw";
    }

    private static void AddCardsToPile(
        SimulationState state,
        List<DeckCardInstance> pile,
        IReadOnlyList<DeckCardInstance> cards,
        string? position)
    {
        IReadOnlyList<DeckCardInstance> cardsToAdd = ReferenceEquals(pile, state.Hand)
            ? cards.Take(Math.Max(0, state.MaxHandSize - state.Hand.Count)).ToArray()
            : cards;
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

    private static List<DeckCardInstance>? TryGetPile(SimulationState state, string pileName)
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
        void AddActivePower(ActivePowerKind kind, double powerAmount, double secondaryAmount = 0d, int counter = 0)
        {
            state.ActivePowers.Add(new ActivePower(
                source.Card.ModelId,
                source.Card.TypeName,
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
                    state.MutableStrengthSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
                    break;
                case "Dexterity":
                    state.MutableDexteritySources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
                    break;
                case "Fasten":
                    state.MutableFastenSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
                case "Monologue":
                    AddActivePower(ActivePowerKind.Monologue, amount);
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
                    state.MutableParrySources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
                        state.MutableSeekingEdgeSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
                    }
                    break;
                case "SpectrumShift":
                    AddActivePower(ActivePowerKind.SpectrumShift, amount);
                    break;
                case "Stratagem":
                    AddActivePower(ActivePowerKind.Stratagem, amount);
                    break;
                case "SwordSage":
                    state.MutableSwordSageSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
                    state.VigorSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
            source.Card.ModelId,
            source.Card.TypeName,
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
        if (BaseTypeName(playedCard) == "SummonForth")
        {
            MoveSovereignBladesToHand(state, options.MaxHandSize);
        }
    }

    private static int DynamicForgeAmount(SimulationCard playedCard, SimulationState state)
    {
        if (BaseTypeName(playedCard) != "BeatIntoShape" || state.AttacksPlayedThisTurn <= 0)
        {
            return 0;
        }

        return (int)Math.Round(playedCard.BaseDamage * state.AttacksPlayedThisTurn, MidpointRounding.AwayFromZero);
    }

    private static double TheBombDamage(SimulationCard card)
    {
        return BaseTypeName(card) == "TheBomb" && card.UpgradeLevel > 0
            ? 50d
            : 40d;
    }

    private static PowerEventResult ResolveGeneratedCardActions(
        SimulationState state,
        DeckCardInstance source,
        FastRandom rng,
        DeckSimulationOptions options)
    {
        return BaseTypeName(source.Card) switch
        {
            "CollisionCourse" => GenerateNamedCardsToHand(state, options, "Debris", 1, upgradeGenerated: false),
            "CrashLanding" => GenerateNamedCardsToHand(
                state,
                options,
                "Debris",
                Math.Max(0, options.MaxHandSize - state.Hand.Count),
                upgradeGenerated: false),
            "BundleOfJoy" => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                rng,
                "bundleOfJoy.colorless",
                3 + source.Card.UpgradeLevel,
                distinct: true,
                upgradeGenerated: false),
            "ManifestAuthority" => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                rng,
                "manifestAuthority.colorless",
                1,
                distinct: true,
                upgradeGenerated: source.Card.UpgradeLevel > 0),
            "Quasar" => GenerateBestCardFromGeneratedChoices(
                state,
                options,
                rng,
                "quasar.colorless",
                3,
                upgradeGenerated: source.Card.UpgradeLevel > 0),
            "JackOfAllTrades" => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                rng,
                "jackOfAllTrades.colorless",
                1 + source.Card.UpgradeLevel,
                distinct: true,
                upgradeGenerated: false),
            "Discovery" => GenerateBestCardFromGeneratedChoices(
                state,
                options,
                rng,
                "discovery.regent",
                3,
                upgradeGenerated: false),
            "Jackpot" => GenerateCardsToHandFromGeneratedPool(
                state,
                options,
                rng,
                "jackpot.regent.zeroCost",
                3,
                distinct: true,
                upgradeGenerated: source.Card.UpgradeLevel > 0),
            "HeirloomHammer" => CopyBestColorlessCardToHand(state),
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
        FastRandom rng,
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

        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
        HashSet<string> selectedModelIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            List<SimulationCard> pool = distinct
                ? candidates.Where(card => !selectedModelIds.Contains(card.ModelId)).ToList()
                : candidates;
            if (pool.Count == 0)
            {
                break;
            }

            SimulationCard selected = pool[rng.Next(pool.Count)];
            selectedModelIds.Add(selected.ModelId);
            PowerEventResult generatedResult = AddGeneratedCardToHand(state, selected);
            resolutions.AddRange(generatedResult.PowerResolutions);
            credits.AddRange(generatedResult.ValueCredits);
        }

        return new PowerEventResult(resolutions, credits);
    }

    private static PowerEventResult GenerateBestCardFromGeneratedChoices(
        SimulationState state,
        DeckSimulationOptions options,
        FastRandom rng,
        string poolId,
        int choiceCount,
        bool upgradeGenerated)
    {
        List<SimulationCard> candidates = ResolveGeneratedPoolCandidates(options, poolId, upgradeGenerated);
        List<SimulationCard> choices = [];
        HashSet<string> selectedModelIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < choiceCount && selectedModelIds.Count < candidates.Count; i++)
        {
            List<SimulationCard> remaining = candidates
                .Where(card => !selectedModelIds.Contains(card.ModelId))
                .ToList();
            if (remaining.Count == 0)
            {
                break;
            }

            SimulationCard selected = remaining[rng.Next(remaining.Count)];
            selectedModelIds.Add(selected.ModelId);
            choices.Add(selected);
        }

        SimulationCard? best = choices
            .OrderByDescending(CardSearchScore)
            .ThenBy(card => card.TypeName, StringComparer.Ordinal)
            .FirstOrDefault();
        return best is null
            ? PowerEventResult.Empty
            : AddGeneratedCardToHand(state, best);
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
        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
        for (int i = 0; i < count; i++)
        {
            PowerEventResult result = AddGeneratedCardToHand(state, card);
            resolutions.AddRange(result.PowerResolutions);
            credits.AddRange(result.ValueCredits);
            if (state.Hand.Count >= state.MaxHandSize)
            {
                break;
            }
        }

        return new PowerEventResult(resolutions, credits);
    }

    // Resolved generated-card pool candidates, cached per (library, poolId, upgradeGenerated). The
    // resolution depends only on those and the returned list is read-only for callers, so it is built
    // once per library and reused. Without this cache every generation event — Quasar/Discovery/Jackpot
    // plays, and (worse) the per-turn Calamity/SpectrumShift/etc. powers living in the base decks —
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

    private static PowerEventResult AddGeneratedCardToHand(SimulationState state, SimulationCard card)
    {
        if (state.Hand.Count >= state.MaxHandSize)
        {
            return PowerEventResult.Empty;
        }

        DeckCardInstance generated = new(state.NextGeneratedInstanceId++, card);
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
        foreach (List<DeckCardInstance> pile in new[] { state.DrawPile, state.DiscardPile, state.ExhaustPile })
        {
            IReadOnlyList<DeckCardInstance> blades = pile
                .Where(card => IsSovereignBlade(card.Card))
                .Take(Math.Max(0, maxHandSize - state.Hand.Count))
                .ToArray();
            foreach (DeckCardInstance blade in blades)
            {
                pile.Remove(blade);
                state.Hand.Add(blade);
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
        List<PowerResolution> resolutions = [];
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
                    resolutions.Add(new PowerResolution(
                        power.SourceModelId,
                        power.SourceTypeName,
                        power.Amount * power.SourceCard.BlockValuePerBlock));
                    break;
            }
        }

        return new PowerEventResult(resolutions, []);
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
            source.Card.ModelId,
            source.Card.TypeName,
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
        foreach (DeckCardInstance blade in AllCards(state).Where(card => IsSovereignBlade(card.Card)))
        {
            blade.Card = blade.Card with
            {
                IntrinsicValue = blade.Card.IntrinsicValue + amount,
                StaticEstimatedValue = blade.Card.StaticEstimatedValue + amount,
                DamageValue = blade.Card.DamageValue + amount,
                BaseDamage = blade.Card.BaseDamage + amount
            };
            blade.AddForgeCredit(sourceCredit);
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
    // Equivalent to AllCards(state).Where(c => c is not in ExhaustPile) — instance ids are unique so
    // a non-exhaust card can never share an id with an exhaust card — but without the per-card
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
        return string.Equals(card.ModelId, "CARD.SOVEREIGN_BLADE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.ModelId, "GENERATED.SOVEREIGN_BLADE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.TypeName, "SovereignBlade", StringComparison.OrdinalIgnoreCase);
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

    private static string BaseTypeName(SimulationCard card)
    {
        int upgradeSeparator = card.TypeName.IndexOf('+', StringComparison.Ordinal);
        return upgradeSeparator < 0 ? card.TypeName : card.TypeName[..upgradeSeparator];
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
        return string.Equals(BaseTypeName(card), "HeavenlyDrill", StringComparison.OrdinalIgnoreCase);
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

    private static bool CanPlay(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions? options = null)
    {
        if (options?.BlockedPlayModelIds.Count > 0
            && options.BlockedPlayModelIds.Contains(card.ModelId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsHeavenlyDrill(card) && state.Energy < 4)
        {
            return false;
        }

        return card.IsPlayable
            && EffectiveEnergyCost(card, state) <= state.Energy
            && EffectiveStarCost(card, state) <= EffectiveAvailableStarsForPlay(state);
    }

    private static int EffectiveEnergyCost(SimulationCard card, SimulationState state)
    {
        if (HasXCostDamage(card))
        {
            return Math.Max(0, state.Energy);
        }

        if (IsVoidFormFreeCard(state))
        {
            return 0;
        }

        return card.EnergyCost;
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
            + SetupPriorityDecisionValue(card)
            + (card.Exhausts ? 0.01d : 0d)
            + (card.Retain ? 0.005d : 0d);
    }

    private static double CardSearchScore(SimulationCard card, SimulationState state)
    {
        return EstimateSearchScore(card, state, excludedInstanceId: null, MidlineResourceReferenceValues);
    }

    private static double CardSearchScore(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EstimateSearchScore(card, state, excludedInstanceId: null, ResourceReferenceValuesForTurns(options.Turns));
    }

    private static double CardSearchScore(DeckCardInstance card, SimulationState state)
    {
        return EstimateSearchScore(card.Card, state, card.InstanceId, MidlineResourceReferenceValues);
    }

    private static double CardSearchScore(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EstimateSearchScore(card.Card, state, card.InstanceId, ResourceReferenceValuesForTurns(options.Turns));
    }

    private static double EstimateSearchScore(
        SimulationCard card,
        SimulationState state,
        int? excludedInstanceId,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        double immediateValue = EstimateImmediateSearchValue(card, state, XCostEnergy(card, state));
        double continuationValue = EstimateContinuationValueAfterPlaying(card, state, excludedInstanceId);
        double delayedValue = EstimateDelayedSearchValue(card, state, immediateValue, resourceReferenceValues);
        return immediateValue
            + continuationValue
            + delayedValue
            + SetupPriorityDecisionValue(card)
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

    private static double EstimateContinuationValueAfterPlaying(
        SimulationCard playedCard,
        SimulationState state,
        int? excludedInstanceId)
    {
        int energy = Math.Max(0, state.Energy - EffectiveEnergyCost(playedCard, state) + playedCard.EnergyGain);
        int stars = Math.Max(0, EffectiveAvailableStarsForPlay(state) - EffectiveStarCost(playedCard, state) + playedCard.StarGain);
        int remainingHandCount = Math.Max(0, state.Hand.Count - 1);
        int drawLimit = Math.Min(playedCard.Draw, Math.Max(0, state.MaxHandSize - remainingHandCount));
        List<SimulationCard> candidates = [];
        bool skippedPlayedCard = excludedInstanceId.HasValue;
        foreach (DeckCardInstance handCard in state.Hand)
        {
            if (excludedInstanceId.HasValue && handCard.InstanceId == excludedInstanceId.Value)
            {
                continue;
            }

            if (!excludedInstanceId.HasValue && !skippedPlayedCard && ReferenceEquals(handCard.Card, playedCard))
            {
                skippedPlayedCard = true;
                continue;
            }

            candidates.Add(handCard.Card);
        }

        candidates.AddRange(state.DrawPile
            .Take(drawLimit)
            .Select(instance => instance.Card));
        return EstimateGreedyContinuationValue(candidates, state, energy, stars, maxCards: 3);
    }

    private static double EstimateGreedyContinuationValue(
        IReadOnlyList<SimulationCard> candidates,
        SimulationState state,
        int energy,
        int stars,
        int maxCards)
    {
        // P7: EstimateImmediateSearchValue is energy-independent for non-X-cost cards (XCostDamageValue
        // returns 0 without an X-cost damage action). Precompute those once and reuse across the
        // up-to-maxCards greedy picks instead of recomputing them each pick; only X-cost cards are
        // re-evaluated as the projected energy drops. Output-identical to the previous loop.
        int n = candidates.Count;
        bool[] used = new bool[n];
        bool[] hasXCost = new bool[n];
        double[] nonXCostImmediate = new double[n];
        for (int i = 0; i < n; i++)
        {
            hasXCost[i] = HasXCostDamage(candidates[i]);
            if (!hasXCost[i])
            {
                nonXCostImmediate[i] = EstimateImmediateSearchValue(candidates[i], state, energy);
            }
        }

        double value = 0d;
        for (int play = 0; play < maxCards; play++)
        {
            int bestIndex = -1;
            double bestValue = 0d;
            double bestImmediate = 0d;
            int bestEnergyCost = 0;
            int bestStarCost = 0;
            for (int i = 0; i < n; i++)
            {
                if (used[i])
                {
                    continue;
                }

                SimulationCard candidate = candidates[i];
                if (!CanPlayWithResources(candidate, energy, stars))
                {
                    continue;
                }

                int energyCost = SearchEnergyCost(candidate, energy);
                int starCost = candidate.StarCost;
                double candidateValue = hasXCost[i]
                    ? EstimateImmediateSearchValue(candidate, state, energy)
                    : nonXCostImmediate[i];
                double efficiencyAdjustedValue = candidateValue / (1d + (energyCost * 0.15d) + (starCost * 0.05d));
                if (efficiencyAdjustedValue > bestValue)
                {
                    bestIndex = i;
                    bestValue = efficiencyAdjustedValue;
                    bestImmediate = candidateValue;
                    bestEnergyCost = energyCost;
                    bestStarCost = starCost;
                }
            }

            if (bestIndex < 0 || bestValue <= 0d)
            {
                break;
            }

            value += bestImmediate;
            energy = Math.Max(0, energy - bestEnergyCost + candidates[bestIndex].EnergyGain);
            stars = Math.Max(0, stars - bestStarCost + candidates[bestIndex].StarGain);
            used[bestIndex] = true;
        }

        return value * 0.85d;
    }

    private static bool CanPlayWithResources(SimulationCard card, int energy, int stars)
    {
        if (!card.IsPlayable)
        {
            return false;
        }

        if (IsHeavenlyDrill(card) && energy < 4)
        {
            return false;
        }

        return SearchEnergyCost(card, energy) <= energy
            && card.StarCost <= stars;
    }

    private static int SearchEnergyCost(SimulationCard card, int availableEnergy)
    {
        return HasXCostDamage(card)
            ? Math.Max(0, availableEnergy)
            : card.EnergyCost;
    }

    private static double EstimateDelayedSearchValue(
        SimulationCard card,
        SimulationState state,
        double immediateValue,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        double residualStaticValue = Math.Max(
            0d,
            card.StaticEstimatedValue
                - Math.Max(0d, immediateValue)
                - ExplicitResourceReferenceValue(card, MidlineResourceReferenceValues));
        return residualStaticValue
            + ExplicitResourceReferenceValue(card, resourceReferenceValues)
            + (card.BlockNextTurn * card.BlockValuePerBlock);
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

    private static double SetupPriorityDecisionValue(SimulationCard card)
    {
        return SimulationCard.SetupPriorityForCardType(card.CardType, card.SetupPriorityValue);
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
        ExplicitResourceReferenceValues values)
    {
        double immediateValue =
            (card.Draw * values.Draw)
            + (card.EnergyGain * values.Energy)
            + (card.StarGain * values.Star);
        double nextTurnValue =
            (card.DrawNextTurn * values.Draw)
            + (card.EnergyNextTurn * values.Energy)
            + (card.StarNextTurn * values.Star);
        return immediateValue + (nextTurnValue * NextTurnExplicitResourceReferenceMultiplier);
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
        if (candidate.DecisionValue != best.DecisionValue)
        {
            return candidate.DecisionValue > best.DecisionValue;
        }

        if (candidate.Value != best.Value)
        {
            return candidate.Value > best.Value;
        }

        if (candidate.CardsPlayed != best.CardsPlayed)
        {
            return candidate.CardsPlayed > best.CardsPlayed;
        }

        return candidate.EnergySpent + candidate.StarSpent > best.EnergySpent + best.StarSpent;
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

            if (state.DrawPile.Count == 0 && state.DiscardPile.Count > 0 && allowShuffle && rng is not null)
            {
                state.DrawPile.AddRange(state.DiscardPile);
                state.DiscardPile.Clear();
                Shuffle(state.DrawPile, rng);
                ResolveShufflePowers(state);
            }

            if (state.DrawPile.Count == 0)
            {
                break;
            }

            DeckCardInstance card = state.DrawPile[0];
            state.DrawPile.RemoveAt(0);
            state.Hand.Add(card);
            drawn++;
            if (card.Card.DamageIncreasePerDraw != 0d)
            {
                // KinglyPunch: drawing it permanently raises its damage for the rest of the combat.
                card.BonusDrawDamage += card.Card.DamageIncreasePerDraw;
            }

            ResolveCardDrawnPowers(state);
        }

        return new DrawResult(drawn, [], []);
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

    private static void FinishTurn(SimulationState state)
    {
        List<DeckCardInstance> retained = [];
        bool retainHand = state.ActivePowers.Any(power =>
            power.Kind == ActivePowerKind.RetainHand
            && power.Amount > 0d);
        int frailAppliedNextTurn = state.Hand.Sum(card => TurnEndFrailAmount(card.Card));
        foreach (DeckCardInstance card in state.Hand)
        {
            if (card.Card.Ethereal)
            {
                state.ExhaustPile.Add(card);
            }
            else if (retainHand || card.Card.Retain)
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
        state.PlayerFrail = Math.Max(0, state.PlayerFrail - 1) + frailAppliedNextTurn;
        ExpireEndOfTurnTemporaryPowers(state);
    }

    private static int TurnEndFrailAmount(SimulationCard card)
    {
        if (BaseTypeName(card) != "Shame")
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
    }

    private static void Shuffle<T>(IList<T> items, FastRandom rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static void MoveInnateCardsToTop(List<DeckCardInstance> drawPile)
    {
        IReadOnlyList<DeckCardInstance> innate = drawPile
            .Where(card => card.Card.Innate)
            .OrderBy(card => card.InstanceId)
            .ToArray();
        if (innate.Count == 0)
        {
            return;
        }

        drawPile.RemoveAll(card => card.Card.Innate);
        drawPile.InsertRange(0, innate);
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

    private sealed class SimulationState
    {
        public List<DeckCardInstance> DrawPile { get; } = [];

        public List<DeckCardInstance> Hand { get; } = [];

        public List<DeckCardInstance> DiscardPile { get; } = [];

        public List<DeckCardInstance> ExhaustPile { get; } = [];

        public List<ActivePower> ActivePowers { get; } = [];

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

        public static SimulationState Create(IReadOnlyList<SimulationCard> deck, FastRandom rng, DeckSimulationOptions options)
        {
            SimulationState state = new()
            {
                Stars = options.BaseStars,
                BaseStarsRemaining = options.BaseStars,
                CharacterPoolName = ResolveCharacterPoolName(deck),
                MaxHandSize = options.MaxHandSize
            };
            for (int i = 0; i < deck.Count; i++)
            {
                state.DrawPile.Add(new DeckCardInstance(i, deck[i]));
            }

            state.NextGeneratedInstanceId = deck.Count;
            Shuffle(state.DrawPile, rng);
            MoveInnateCardsToTop(state.DrawPile);
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
                CharacterPoolName = CharacterPoolName
            };
            clone.DrawPile.Capacity = DrawPile.Count;
            clone.Hand.Capacity = Hand.Count;
            clone.DiscardPile.Capacity = DiscardPile.Count;
            clone.ExhaustPile.Capacity = ExhaustPile.Count;
            foreach (DeckCardInstance card in DrawPile) { clone.DrawPile.Add(card.Clone()); }
            foreach (DeckCardInstance card in Hand) { clone.Hand.Add(card.Clone()); }
            foreach (DeckCardInstance card in DiscardPile) { clone.DiscardPile.Add(card.Clone()); }
            foreach (DeckCardInstance card in ExhaustPile) { clone.ExhaustPile.Add(card.Clone()); }
            clone.ActivePowers.AddRange(ActivePowers.Select(power => power.Clone()));
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
            clone.VigorSources.AddRange(VigorSources);
            clone.EnemyVulnerableSources.AddRange(EnemyVulnerableSources);
            return clone;
        }

        public void CopyFrom(SimulationState state)
        {
            DrawPile.Clear();
            DrawPile.AddRange(state.DrawPile.Select(card => card.Clone()));
            Hand.Clear();
            Hand.AddRange(state.Hand.Select(card => card.Clone()));
            DiscardPile.Clear();
            DiscardPile.AddRange(state.DiscardPile.Select(card => card.Clone()));
            ExhaustPile.Clear();
            ExhaustPile.AddRange(state.ExhaustPile.Select(card => card.Clone()));
            ActivePowers.Clear();
            ActivePowers.AddRange(state.ActivePowers.Select(power => power.Clone()));
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
            _strengthSources = state._strengthSources is { Count: > 0 } ? [.. state._strengthSources] : null;
            _dexteritySources = state._dexteritySources is { Count: > 0 } ? [.. state._dexteritySources] : null;
            _fastenSources = state._fastenSources is { Count: > 0 } ? [.. state._fastenSources] : null;
            _parrySources = state._parrySources is { Count: > 0 } ? [.. state._parrySources] : null;
            _seekingEdgeSources = state._seekingEdgeSources is { Count: > 0 } ? [.. state._seekingEdgeSources] : null;
            _swordSageSources = state._swordSageSources is { Count: > 0 } ? [.. state._swordSageSources] : null;
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
        }
    }

    private sealed class DeckCardInstance(int instanceId, SimulationCard card)
    {
        // Lazily allocated: the vast majority of card instances never accrue Forge credits,
        // so cloning them across the search tree should not allocate an empty list each time.
        private List<ForgeSourceCredit>? forgeCredits;

        public int InstanceId { get; } = instanceId;

        public SimulationCard Card { get; set; } = card;

        // Extra replays enchanted onto THIS instance (HiddenGem). Permanent for the combat: the
        // card replays its play this many extra times every time it is played. Persists across
        // discard/reshuffle because it lives on the instance, not the shared card model.
        public int BonusReplayCount { get; set; }

        // KinglyPunch: raw damage THIS instance has permanently gained from being drawn
        // (AfterCardDrawn adds Card.DamageIncreasePerDraw each draw). Persists across discard/reshuffle
        // because it lives on the instance, not the shared card model.
        public double BonusDrawDamage { get; set; }

        public IReadOnlyList<ForgeSourceCredit> ForgeCredits => forgeCredits ?? (IReadOnlyList<ForgeSourceCredit>)[];

        public void AddForgeCredit(ForgeSourceCredit credit)
        {
            (forgeCredits ??= []).Add(credit);
        }

        public DeckCardInstance Clone()
        {
            DeckCardInstance clone = new(InstanceId, Card)
            {
                BonusReplayCount = BonusReplayCount,
                BonusDrawDamage = BonusDrawDamage
            };
            if (forgeCredits is { Count: > 0 })
            {
                clone.forgeCredits = [.. forgeCredits];
            }

            return clone;
        }
    }

    private readonly record struct FreePlayResult(double Value, IReadOnlyList<CardValueCreditEvent> Credits)
    {
        public static FreePlayResult Empty { get; } = new(0d, []);
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
        Monologue,
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
        public string SourceModelId { get; } = sourceModelId;

        public string SourceTypeName { get; } = sourceTypeName;

        public ActivePowerKind Kind { get; } = kind;

        public SimulationCard SourceCard { get; } = sourceCard;

        public double Amount { get; set; } = amount;

        public double SecondaryAmount { get; } = secondaryAmount;

        public int Counter { get; set; } = counter;

        public int SourceInstanceId { get; } = sourceInstanceId;

        public ISimulationPowerBehavior? Behavior { get; } = behavior;

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
            return new ActivePower(
                SourceModelId,
                SourceTypeName,
                Kind,
                SourceCard,
                Amount,
                SecondaryAmount,
                Counter,
                SourceInstanceId,
                Behavior);
        }
    }

    private interface ISimulationPowerBehavior
    {
        IReadOnlyList<PowerResolution> Resolve(SimulationEvent simulationEvent, ActivePower source);
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
    }

    private sealed record PowerResolution(
        string SourceModelId,
        string SourceTypeName,
        double Value);

    private sealed record PowerEventResult(
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits)
    {
        public static PowerEventResult Empty { get; } = new([], []);

        public double Value => PowerResolutions.Sum(resolution => resolution.Value);
    }

    private sealed record DrawResult(
        int CardsDrawn,
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits)
    {
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
        SimulationCard Card,
        double Value,
        double DecisionValue,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        IReadOnlyList<CardValueCreditEvent> ValueCredits);

    private sealed record SearchResult(
        SimulationState State,
        double Value,
        double DecisionValue,
        int CardsPlayed,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        IReadOnlyList<PlayEvent> PlayedCards);

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

    private sealed class TrackedCardLocalSums(int turns)
    {
        public double[] TurnValueSums { get; } = new double[turns];

        public int[] PlayCounts { get; } = new int[turns];

        public int[] DirectPlayCounts { get; } = new int[turns];

        public double[] DirectValueSums { get; } = new double[turns];

        public double[] ForgeValueSums { get; } = new double[turns];

        public double[] PowerValueSums { get; } = new double[turns];

        public double[] EnergyValueSums { get; } = new double[turns];

        public double[] StarValueSums { get; } = new double[turns];
    }
}
