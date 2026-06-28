using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class DeckMonteCarloSimulator
{
    private const decimal NextTurnExplicitResourceReferenceMultiplier = 0.75m;

    private static readonly ExplicitResourceReferenceValues ShortlineResourceReferenceValues = new(
        Draw: 5.1m,
        Energy: 8.8m,
        Star: 2.7m);

    private static readonly ExplicitResourceReferenceValues MidlineResourceReferenceValues = new(
        Draw: 5.2m,
        Energy: 10.0m,
        Star: 5.3m);

    private static readonly ExplicitResourceReferenceValues LonglineResourceReferenceValues = new(
        Draw: 5.1m,
        Energy: 11.2m,
        Star: 6.3m);

    public IReadOnlyList<decimal> SimulateExpectedTurnValues(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        IReadOnlyList<SimulationCard> simulationDeck = NormalizeStartingDeck(deck);
        Validate(simulationDeck, options);

        decimal[] turnValueSums = new decimal[options.Turns];
        Random seedRng = new(options.Seed);
        int[] runSeeds = Enumerable.Range(0, options.Runs)
            .Select(_ => seedRng.Next())
            .ToArray();
        int runDegreeOfParallelism = Math.Max(1, options.RunDegreeOfParallelism);
        if (runDegreeOfParallelism <= 1)
        {
            for (int run = 0; run < options.Runs; run++)
            {
                Random rng = new(runSeeds[run]);
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
                () => new decimal[options.Turns],
                (run, _, localSums) =>
                {
                    Random rng = new(runSeeds[run]);
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

    public DeckSimulationReport Simulate(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        IReadOnlyList<SimulationCard> simulationDeck = NormalizeStartingDeck(deck);
        int ignoredStartingSovereignBlades = deck.Count - simulationDeck.Count;
        Validate(simulationDeck, options);

        double[,] turnValues = new double[options.Runs, options.Turns];
        List<TurnTrialSummary>[] turnSamples = Enumerable.Range(0, options.Turns)
            .Select(_ => new List<TurnTrialSummary>(options.Runs))
            .ToArray();
        Dictionary<string, CardPlayAccumulator> cardPlayAccumulators = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int Turn, string ModelId), CardPlayTurnAccumulator> cardPlayByTurnAccumulators = [];
        Dictionary<string, CardValueCreditAccumulator> cardValueCreditAccumulators = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int Turn, string ModelId), CardValueCreditTurnAccumulator> cardValueCreditByTurnAccumulators = [];
        Random seedRng = new(options.Seed);

        for (int run = 0; run < options.Runs; run++)
        {
            Random rng = new(seedRng.Next());
            SimulationState state = SimulationState.Create(simulationDeck, rng, options);
            for (int turn = 1; turn <= options.Turns; turn++)
            {
                TurnTrialSummary summary = PlayTurn(state, options, rng, run, turn);
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
                    AddCredit(cardValueCreditAccumulators, credit);
                    AddTurnCredit(cardValueCreditByTurnAccumulators, turn, credit);
                }
            }
        }

        IReadOnlyList<TurnSimulationSummary> turnSummaries = turnSamples
            .Select((samples, index) => BuildTurnSummary(index + 1, samples, options.PmfBucketSize))
            .ToArray();
        IReadOnlyList<TurnCovariance> covariances = BuildCovariances(turnValues, turnSummaries);
        IReadOnlyList<CardPlaySummary> playedCards = cardPlayAccumulators.Values
            .OrderByDescending(item => item.PlayCount)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(item => new CardPlaySummary(
                item.ModelId,
                item.TypeName,
                item.PlayCount,
                Round((decimal)item.PlayCount / options.Runs),
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
                Round((decimal)item.PlayCount / options.Runs),
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
        decimal totalExpectedValue = Round(turnSummaries.Sum(turn => turn.ExpectedValue));
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
        Random rng,
        int run,
        int turn)
    {
        state.TurnEnded = false;
        state.CardsPlayedThisTurn = 0;
        state.AttacksPlayedThisTurn = 0;
        int queuedEnergy = state.NextTurnEnergy;
        int queuedStars = state.NextTurnStars;
        int queuedDraw = state.NextTurnDraw;
        state.CurrentTurnEnergySources.Clear();
        state.CurrentTurnEnergySources.AddRange(state.NextTurnEnergySources);
        state.NextTurnEnergySources.Clear();
        IReadOnlyList<ResourceSourceCredit> queuedStarSources = state.NextTurnStarSources.ToArray();
        state.NextTurnStarSources.Clear();
        IReadOnlyList<CardValueCreditEvent> delayedBlockCredits = DelayedDirectCredits(state.NextTurnBlockCredits);
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
        ExpireEnemyVulnerable(state);
        IReadOnlyList<PowerResolution> turnStartResolutions = queuedStars > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, queuedStars))
            : [];
        IReadOnlyList<CardValueCreditEvent> turnStartStarCredits = StarTriggerCredits(
            queuedStarSources,
            turnStartResolutions.Sum(resolution => resolution.Value));
        decimal turnStartValue = turnStartResolutions.Sum(resolution => resolution.Value)
            + delayedBlockCredits.Sum(credit => credit.DirectValue);
        IReadOnlyList<CardValueCreditEvent> turnStartCredits =
            [.. PowerCredits(turnStartResolutions), .. turnStartStarCredits, .. delayedBlockCredits];
        PowerEventResult turnStartPowerResult = ResolveTurnStartPowers(state);

        PowerEventResult beforeDrawResult = ResolveBeforeHandDrawPowers(state, options, rng);
        int drawCount = options.HandSize + queuedDraw + HandDrawBonus(state);
        DrawResult drawResult = DrawCards(state, drawCount, rng, allowShuffle: true, options);
        PowerEventResult playerTurnStartResult = ResolveAfterPlayerTurnStartPowers(state, options, rng);
        SearchResult result = Search(state.Clone(), options, run, turn, actionsPlayed: 0, rng.Next());
        state.CopyFrom(result.State);

        decimal unplayedIntrinsicValue = state.Hand
            .Where(card => card.Card.IsPlayable && card.Card.IntrinsicValue > 0m)
            .Sum(card => card.Card.IntrinsicValue);
        int energyWasted = Math.Max(0, state.Energy);
        int starsWasted = Math.Max(0, state.Stars);
        PowerEventResult turnEndPowerResult = ResolveTurnEndPowers(state);
        FinishTurn(state);
        state.LastTurnCardsPlayed = result.CardsPlayed;
        decimal resourceAttributionValue = turnStartResolutions.Sum(resolution => resolution.Value)
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
        state.CurrentTurnEnergySources.Clear();
        IReadOnlyList<CardValueCreditEvent> valueCredits =
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
        SearchResult best = new(state, 0m, 0m, 0, 0, 0, 0, 0, 0, []);
        if (state.TurnEnded || actionsPlayed >= options.MaxCardsPlayedPerTurn)
        {
            return best;
        }

        IReadOnlyList<DeckCardInstance> legalPlayableCards = SelectPlayableCards(state);
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
            Random branchRng = new(DeriveSeed(seed, actionsPlayed, card.InstanceId));
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

    private static IReadOnlyList<DeckCardInstance> SelectPlayableCards(SimulationState state)
    {
        return state.Hand
            .Where(card => CanPlay(card.Card, state))
            .ToArray();
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
        decimal[] selectedScores = new decimal[limit];
        int selectedCount = 0;
        foreach (DeckCardInstance card in legalPlayableCards)
        {
            decimal score = ScoreSearchCard(card, state, options);
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

    private static decimal ScoreSearchCard(DeckCardInstance card, SimulationState state, DeckSimulationOptions options)
    {
        return options.SearchCardScorer?.Score(BuildSearchCardScoringContext(card.Card, state, options))
            ?? CardSearchScore(card, state, options);
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
                Math.Max(1, options.MaxCardsPlayedPerTurn));
        metadata = metadata with
        {
            TeacherMaxBranchingCards = Math.Max(1, metadata.TeacherMaxBranchingCards),
            TeacherMaxCardsPlayedPerTurn = Math.Max(1, metadata.TeacherMaxCardsPlayedPerTurn)
        };

        List<SearchPolicyActionSample> unranked = [];
        foreach (DeckCardInstance card in legalPlayableCards)
        {
            decimal teacherRouteValue = TeacherRouteDecisionValue(
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
            BuildContextFeatures(state),
            actions,
            ranked[0].Index,
            metadata);
    }

    private static decimal TeacherRouteDecisionValue(
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
            SearchPolicyMetadata = null
        };
        SimulationState next = state.Clone();
        DeckCardInstance nextCard = FindHandCard(next, firstCard.InstanceId);
        Random branchRng = new(DeriveSeed(seed, actionsPlayed, firstCard.InstanceId));
        PlayEvent play = PlayCard(next, nextCard, branchRng, teacherOptions);
        SearchResult suffix = Search(next, teacherOptions, run, turn, actionsPlayed + 1, branchRng.Next());
        return play.DecisionValue + suffix.DecisionValue;
    }

    private static SearchCardScoringContext BuildSearchCardScoringContext(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, double> feature in BuildContextFeatures(state))
        {
            features[feature.Key] = feature.Value;
        }

        foreach (KeyValuePair<string, double> feature in BuildActionFeatures(card, state, options))
        {
            features[feature.Key] = feature.Value;
        }

        return new SearchCardScoringContext(card.ModelId, card.TypeName, features);
    }

    private static IReadOnlyDictionary<string, double> BuildContextFeatures(SimulationState state)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal);
        AddFeature(features, "context.energy", state.Energy);
        AddFeature(features, "context.stars", state.Stars);
        AddFeature(features, "context.baseStarsRemaining", state.BaseStarsRemaining);
        AddFeature(features, "context.handCount", state.Hand.Count);
        AddFeature(features, "context.playableHandCount", state.Hand.Count(card => CanPlay(card.Card, state)));
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
        AddFeature(features, "card.canPlay", CanPlay(card, state));
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
            AddFeature(features, $"card.action.{key}.amount", group.Sum(action => action.Amount ?? 0m));
        }

        return features;
    }

    private static decimal SumSources(IEnumerable<ResourceSourceCredit> sources)
    {
        return sources.Sum(source => source.Amount);
    }

    private static void AddFeature(IDictionary<string, double> features, string name, bool value)
    {
        features[name] = value ? 1d : 0d;
    }

    private static void AddFeature(IDictionary<string, double> features, string name, int value)
    {
        features[name] = value;
    }

    private static void AddFeature(IDictionary<string, double> features, string name, decimal value)
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

    private static PlayEvent PlayCard(SimulationState state, DeckCardInstance card, Random rng, DeckSimulationOptions options)
    {
        state.Hand.Remove(card);
        SimulationCard playedCard = card.Card;
        int playId = state.NextPlayEventId++;
        int energyCost = EffectiveEnergyCost(playedCard, state);
        int starCost = EffectiveStarCost(playedCard, state);
        PowerEventResult beforeCardPlayedResult = ResolveBeforeCardPlayedPowers(state);
        PlayValueResult playValue = PlayValue(playedCard, state);
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
        DrawResult drawResult = DrawCards(state, playedCard.Draw, rng, allowShuffle: true, options);
        SimulationCard? transformedPlayedCard = ResolveCardObjectActions(state, card, options);
        PowerEventResult generatedCardResult = ResolveGeneratedCardActions(state, card, rng, options);
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
        decimal powerValue = powerResolutions.Sum(resolution => resolution.Value);
        decimal value = playValue.Value + powerValue;
        decimal decisionValue = value
            + SetupPriorityDecisionValue(playedCard)
            + ExplicitResourceReferenceValue(playedCard, ResourceReferenceValuesForTurns(options.Turns));
        IReadOnlyList<CardValueCreditEvent> starCredits =
        [
            .. StarSpendCredits(
                consumedStarSources,
                playValue.Value + starSpentResolutions.Sum(resolution => resolution.Value),
                starCost),
            .. StarTriggerCredits(starGainSources, starGainedResolutions.Sum(resolution => resolution.Value))
        ];
        IReadOnlyList<CardValueCreditEvent> valueCredits = BuildValueCredits(
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
            starCredits);

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

    private static PlayValueResult PlayValue(SimulationCard card, SimulationState state)
    {
        decimal xCostDamageValue = XCostDamageValue(card, state);
        decimal scalingDamageValue = DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: true);
        decimal directDamageValue = card.DamageValue + scalingDamageValue + xCostDamageValue;
        decimal vulnerableBonus = VulnerableBonus(directDamageValue, state);
        decimal directValue = card.IntrinsicValue
            + scalingDamageValue
            + xCostDamageValue
            + ReflectApproximationValue(card)
            + StrengthLossDefenseValue(card)
            + HpLossPenaltyValue(card)
            + FrailBlockPenaltyValue(card, state);
        List<CardValueCreditEvent> credits = [];
        decimal modifierValue = AddSovereignBladePowerCredits(credits, card, state);
        decimal creditedVulnerableBonus = AddVulnerableSourceCredits(credits, state, vulnerableBonus);
        if (vulnerableBonus > 0m && creditedVulnerableBonus == 0m)
        {
            directValue += vulnerableBonus;
        }

        modifierValue += creditedVulnerableBonus;

        decimal damageModifierMultiplier = EffectiveDamageModifierMultiplier(card, state)
            + XCostDamageModifierMultiplier(card, state);
        if (card.IsAttack && damageModifierMultiplier > 0m)
        {
            int beforeCount = credits.Count;
            AddPowerModifierCredits(credits, state.StrengthSources, damageModifierMultiplier * card.DamageUnitValue);
            AddPowerModifierCredits(credits, state.VigorSources, damageModifierMultiplier * card.DamageUnitValue);
            modifierValue += credits.Sum(credit => credit.PowerRealizedValue);
            modifierValue -= credits.Take(beforeCount).Sum(credit => credit.PowerRealizedValue);
            state.VigorSources.Clear();
        }

        if (IsSovereignBlade(card))
        {
            modifierValue += AddConquerorPowerCredits(credits, state, directValue + modifierValue);
        }

        if (card.BaseBlock > 0m && card.BlockEffectCount > 0)
        {
            int count = card.BlockEffectCount;
            int beforeCount = credits.Count;
            AddPowerModifierCredits(credits, state.DexteritySources, count * card.BlockValuePerBlock);
            if (card.HasTag("Defend"))
            {
                AddPowerModifierCredits(credits, state.FastenSources, count * card.BlockValuePerBlock);
            }

            modifierValue += credits.Skip(beforeCount).Sum(credit => credit.PowerRealizedValue);
        }

        return new PlayValueResult(
            directValue,
            directValue + modifierValue,
            credits);
    }

    private static decimal AddSovereignBladePowerCredits(
        List<CardValueCreditEvent> credits,
        SimulationCard card,
        SimulationState state)
    {
        if (!IsSovereignBlade(card))
        {
            return 0m;
        }

        int beforeCount = credits.Count;
        decimal baseDamage = card.BaseDamage > 0m ? card.BaseDamage : card.DamageValue;
        decimal targetMultiplier = SovereignBladeTargetMultiplier(card, state);
        if (targetMultiplier > 1m)
        {
            AddPowerModifierCredits(credits, state.SeekingEdgeSources, baseDamage * (targetMultiplier - 1m) * card.DamageUnitValue);
        }

        AddPowerModifierCredits(credits, state.SwordSageSources, baseDamage * targetMultiplier * card.DamageUnitValue);
        AddPowerModifierCredits(credits, state.ParrySources, card.BlockValuePerBlock);
        return credits.Skip(beforeCount).Sum(credit => credit.PowerRealizedValue);
    }

    private static decimal EffectiveDamageModifierMultiplier(SimulationCard card, SimulationState state)
    {
        if (!IsSovereignBlade(card))
        {
            return card.DamageModifierMultiplier;
        }

        decimal replayCount = 1m + state.SwordSageSources.Sum(source => source.Amount);
        return SovereignBladeTargetMultiplier(card, state) * replayCount;
    }

    private static decimal AddConquerorPowerCredits(
        List<CardValueCreditEvent> credits,
        SimulationState state,
        decimal doubledValue)
    {
        if (doubledValue <= 0m)
        {
            return 0m;
        }

        ActivePower? conqueror = state.ActivePowers.FirstOrDefault(power =>
            power.Kind == ActivePowerKind.Conqueror
            && power.Amount > 0m);
        if (conqueror is null)
        {
            return 0m;
        }

        credits.Add(new CardValueCreditEvent(
            conqueror.SourceModelId,
            conqueror.SourceTypeName,
            0m,
            0m,
            doubledValue,
            0m,
            0m,
            CountsAsDirectPlay: false));
        return doubledValue;
    }

    private static decimal XCostDamageValue(SimulationCard card, SimulationState state)
    {
        int energy = XCostEnergy(card, state);
        return XCostDamageValue(card, state, energy);
    }

    private static decimal XCostDamageValue(SimulationCard card, SimulationState state, int energy)
    {
        if (!HasXCostDamage(card))
        {
            return 0m;
        }

        if (energy <= 0)
        {
            return 0m;
        }

        return card.Actions
            .Where(IsXCostDamageAction)
            .Sum(action => (action.Amount ?? 0m)
                * XCostHitCount(card, energy)
                * (action.HitCount ?? 1)
                * ActionTargetDamageMultiplier(card, action)
                * card.DamageUnitValue);
    }

    private static decimal XCostDamageModifierMultiplier(SimulationCard card, SimulationState state)
    {
        int energy = XCostEnergy(card, state);
        return XCostDamageModifierMultiplier(card, state, energy);
    }

    private static decimal XCostDamageModifierMultiplier(SimulationCard card, SimulationState state, int energy)
    {
        if (!HasXCostDamage(card))
        {
            return 0m;
        }

        if (energy <= 0)
        {
            return 0m;
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

    private static decimal DynamicScalingDamageValue(
        SimulationCard card,
        SimulationState state,
        bool includePlayedCardIfMissing)
    {
        if (card.ScalingDamageKind is null || card.ScalingDamagePerUnit <= 0m)
        {
            return 0m;
        }

        decimal multiplier = card.ScalingDamageKind switch
        {
            "starCostCardCount" => StarCostCardCount(state)
                + (includePlayedCardIfMissing && HasAnyStarCost(card) ? 1 : 0),
            "cardsPlayedThisCombat" => state.CardsPlayedThisCombat,
            "drawPileCount" => state.DrawPile.Count,
            "generatedCardsCreated" => state.GeneratedCardsCreated,
            _ => 0m
        };
        if (multiplier <= 0m)
        {
            return 0m;
        }

        return card.ScalingDamagePerUnit
            * multiplier
            * card.ScalingDamageTargetMultiplier
            * card.DamageUnitValue;
    }

    private static int StarCostCardCount(SimulationState state)
    {
        return AllCards(state).Count(instance => HasAnyStarCost(instance.Card));
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

    private static decimal ActionTargetDamageMultiplier(SimulationCard card, CardActionFact action)
    {
        return action.TargetType switch
        {
            "AllEnemies" => card.AoeDamageMultiplier,
            _ => 1m
        };
    }

    private static decimal ReflectApproximationValue(SimulationCard card)
    {
        return HasPowerAction(card, "Reflect")
            ? card.BaseBlock * card.DamageUnitValue
            : 0m;
    }

    private static decimal StrengthLossDefenseValue(SimulationCard card)
    {
        return card.Actions
            .Where(action => action.Kind == "power")
            .Where(action => PowerKey(action.Parameter) is "DyingStar" or "CrushUnder" or "DarkShackles")
            .Sum(action => (action.Amount ?? 0m) * 1.2m * card.DamageUnitValue);
    }

    private static decimal HpLossPenaltyValue(SimulationCard card)
    {
        return card.Actions
            .Where(action => action.Kind == "hpLoss")
            .Sum(action => -(action.Amount ?? 0m) * 1.5m);
    }

    private static decimal FrailBlockPenaltyValue(SimulationCard card, SimulationState state)
    {
        if (state.PlayerFrail <= 0)
        {
            return 0m;
        }

        decimal lostBlock = card.Actions
            .Where(action => action.Kind == "block")
            .Select(action => action.Amount ?? 0m)
            .Sum(amount => amount - Math.Floor(amount * 0.75m));
        if (lostBlock <= 0m && card.BaseBlock > 0m)
        {
            lostBlock = card.BaseBlock - Math.Floor(card.BaseBlock * 0.75m);
        }

        return -lostBlock * card.BlockValuePerBlock;
    }

    private static decimal SovereignBladeTargetMultiplier(SimulationCard card, SimulationState state)
    {
        return state.SeekingEdgeSources.Count > 0
            ? card.AoeDamageMultiplier
            : 1m;
    }

    private static void AddPowerModifierCredits(
        List<CardValueCreditEvent> credits,
        IReadOnlyList<ResourceSourceCredit> sources,
        decimal valuePerAmount)
    {
        foreach (ResourceSourceCredit source in sources)
        {
            decimal value = source.Amount * valuePerAmount;
            if (value == 0m)
            {
                continue;
            }

            credits.Add(new CardValueCreditEvent(
                source.SourceModelId,
                source.SourceTypeName,
                0m,
                0m,
                value,
                0m,
                0m,
                CountsAsDirectPlay: false));
        }
    }

    private static decimal VulnerableBonus(decimal damageValue, SimulationState state)
    {
        if (state.EnemyVulnerable <= 0 || damageValue <= 0m)
        {
            return 0m;
        }

        return Math.Floor(damageValue * 0.5m);
    }

    private static decimal AddVulnerableSourceCredits(
        List<CardValueCreditEvent> credits,
        SimulationState state,
        decimal vulnerableBonus)
    {
        if (vulnerableBonus <= 0m || state.EnemyVulnerableSources.Count == 0)
        {
            return 0m;
        }

        ResourceSourceCredit source = state.EnemyVulnerableSources.First(source => source.Amount > 0m);
        credits.Add(new CardValueCreditEvent(
            source.SourceModelId,
            source.SourceTypeName,
            0m,
            0m,
            vulnerableBonus,
            0m,
            0m,
            CountsAsDirectPlay: false));
        return vulnerableBonus;
    }

    private static IReadOnlyList<CardValueCreditEvent> BuildValueCredits(
        DeckCardInstance card,
        decimal directValue,
        IReadOnlyList<CardValueCreditEvent> powerCredits,
        IReadOnlyList<CardValueCreditEvent> starCredits)
    {
        List<CardValueCreditEvent> credits = [];
        decimal forgeRealizedValue = 0m;
        foreach (ForgeSourceCredit forgeCredit in card.ForgeCredits)
        {
            forgeRealizedValue += forgeCredit.Amount;
            credits.Add(new CardValueCreditEvent(
                forgeCredit.SourceModelId,
                forgeCredit.SourceTypeName,
                0m,
                forgeCredit.Amount,
                0m,
                0m,
                0m,
                CountsAsDirectPlay: false));
        }

        credits.Insert(0, new CardValueCreditEvent(
            card.Card.ModelId,
            card.Card.TypeName,
            directValue - forgeRealizedValue,
            0m,
            0m,
            0m,
            0m,
            CountsAsDirectPlay: true));
        credits.AddRange(powerCredits);
        credits.AddRange(starCredits);
        return credits;
    }

    private static IReadOnlyList<CardValueCreditEvent> PowerCredits(IReadOnlyList<PowerResolution> resolutions)
    {
        return resolutions
            .Where(resolution => resolution.Value != 0m)
            .Select(resolution => new CardValueCreditEvent(
                resolution.SourceModelId,
                resolution.SourceTypeName,
                0m,
                0m,
                resolution.Value,
                0m,
                0m,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> DelayedDirectCredits(IReadOnlyList<DelayedValueCredit> delayedCredits)
    {
        return delayedCredits
            .Where(credit => credit.Value != 0m)
            .Select(credit => new CardValueCreditEvent(
                credit.SourceModelId,
                credit.SourceTypeName,
                credit.Value,
                0m,
                0m,
                0m,
                0m,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> EnergyCredits(
        IReadOnlyList<ResourceSourceCredit> energySources,
        decimal turnPlayedValue,
        int baseEnergy,
        int actualEnergySpent)
    {
        decimal totalExtraEnergy = energySources.Sum(source => source.Amount);
        int extraEnergyNeeded = actualEnergySpent - baseEnergy;
        if (energySources.Count == 0
            || totalExtraEnergy <= 0m
            || extraEnergyNeeded <= 0
            || actualEnergySpent <= 0
            || turnPlayedValue <= 0m)
        {
            return [];
        }

        decimal usefulExtraEnergy = Math.Min(totalExtraEnergy, extraEnergyNeeded);
        decimal totalEnergyCredit = turnPlayedValue * usefulExtraEnergy / actualEnergySpent;
        return energySources
            .GroupBy(source => (source.SourceModelId, source.SourceTypeName))
            .Select(group => new CardValueCreditEvent(
                group.Key.SourceModelId,
                group.Key.SourceTypeName,
                0m,
                0m,
                0m,
                totalEnergyCredit * group.Sum(source => source.Amount) / totalExtraEnergy,
                0m,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> StarSpendCredits(
        IReadOnlyList<ResourceSourceCredit> consumedSources,
        decimal attributedValue,
        int totalStarsSpent)
    {
        if (consumedSources.Count == 0 || attributedValue <= 0m || totalStarsSpent <= 0)
        {
            return [];
        }

        return consumedSources
            .GroupBy(source => (source.SourceModelId, source.SourceTypeName))
            .Select(group => new CardValueCreditEvent(
                group.Key.SourceModelId,
                group.Key.SourceTypeName,
                0m,
                0m,
                0m,
                0m,
                attributedValue * group.Sum(source => source.Amount) / totalStarsSpent,
                CountsAsDirectPlay: false))
            .ToArray();
    }

    private static IReadOnlyList<CardValueCreditEvent> StarTriggerCredits(
        IReadOnlyList<ResourceSourceCredit> triggerSources,
        decimal triggeredValue)
    {
        decimal totalSourceStars = triggerSources.Sum(source => source.Amount);
        if (triggerSources.Count == 0 || triggeredValue <= 0m || totalSourceStars <= 0m)
        {
            return [];
        }

        return triggerSources
            .GroupBy(source => (source.SourceModelId, source.SourceTypeName))
            .Select(group => new CardValueCreditEvent(
                group.Key.SourceModelId,
                group.Key.SourceTypeName,
                0m,
                0m,
                0m,
                0m,
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
        decimal attributableAmount = amount - baseStarsConsumed;
        if (attributableAmount <= 0m)
        {
            return [];
        }

        return ConsumeSourceAmounts(state.StarSources, attributableAmount);
    }

    private static IReadOnlyList<ResourceSourceCredit> ConsumeSourceAmounts(
        List<ResourceSourceCredit> sources,
        decimal amount)
    {
        decimal available = sources.Sum(source => source.Amount);
        decimal consumedTotal = Math.Min(amount, available);
        if (consumedTotal <= 0m || available <= 0m)
        {
            return [];
        }

        List<ResourceSourceCredit> consumed = [];
        for (int i = 0; i < sources.Count; i++)
        {
            ResourceSourceCredit source = sources[i];
            decimal sourceConsumed = consumedTotal * source.Amount / available;
            if (sourceConsumed <= 0m)
            {
                continue;
            }

            consumed.Add(source with { Amount = sourceConsumed });
            sources[i] = source with { Amount = source.Amount - sourceConsumed };
        }

        sources.RemoveAll(source => source.Amount <= 0.000001m);
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
        ConsumeEnemyVulnerableDuration(state.EnemyVulnerableSources, 1m);
        if (state.EnemyVulnerable == 0)
        {
            state.EnemyVulnerableSources.Clear();
        }
    }

    private static void ConsumeEnemyVulnerableDuration(List<ResourceSourceCredit> sources, decimal amount)
    {
        decimal remaining = amount;
        while (remaining > 0m && sources.Count > 0)
        {
            ResourceSourceCredit source = sources[0];
            decimal consumed = Math.Min(remaining, source.Amount);
            remaining -= consumed;
            decimal nextAmount = source.Amount - consumed;
            if (nextAmount <= 0m)
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
                    if (power.Counter > 0 && power.Amount > 0m)
                    {
                        power.Amount -= 1m;
                    }
                    break;
                case ActivePowerKind.PrepTime:
                    state.VigorSources.Add(new ResourceSourceCredit(power.SourceModelId, power.SourceTypeName, power.Amount));
                    break;
                case ActivePowerKind.RollingBoulder:
                    if (power.Amount > 0m)
                    {
                        decimal value = power.Amount * power.SourceCard.DamageUnitValue * power.SourceCard.AoeDamageMultiplier;
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
        Random rng)
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
        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.TheSealedThrone))
        {
            GainStarsFromPower(state, (int)power.Amount, power, resolutions, credits);
        }

        return new PowerEventResult(resolutions, credits);
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
        Random rng)
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
                    if (power.Amount > 0m)
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
        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.Orbit))
        {
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
        Random rng,
        DeckSimulationOptions options)
    {
        SimulationCard playedCard = playedInstance.Card;
        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
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
                resolutions.AddRange(generated.PowerResolutions);
                credits.AddRange(generated.ValueCredits);

                continue;
            }

            if (power.Kind == ActivePowerKind.Monologue)
            {
                if (power.SourceInstanceId != playedInstance.InstanceId && power.Amount > 0m)
                {
                    state.StrengthSources.Add(new ResourceSourceCredit(
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

            decimal value = power.Amount * power.SourceCard.DamageUnitValue * power.SourceCard.AoeDamageMultiplier;
            resolutions.Add(new PowerResolution(power.SourceModelId, power.SourceTypeName, value));
            power.Counter = 0;
        }

        state.CardsPlayedThisTurn++;
        return new PowerEventResult(resolutions, credits);
    }

    private static void ResolveCardDrawnPowers(SimulationState state)
    {
        foreach (ActivePower power in state.ActivePowers.Where(power => power.Kind == ActivePowerKind.Automation))
        {
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
            .OrderBy(card => CardObjectChoiceScore(card.Card))
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

        return transformedSource;
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
        IReadOnlyList<DeckCardInstance> selected = SelectCardObjects(fromPile, count, preferHighValue);
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
        Random rng,
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

        decimal replacementScore = CardObjectChoiceScore(replacement);
        return selected
            .Where(card => CardObjectChoiceScore(card.Card) < replacementScore)
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

    private static IReadOnlyList<DeckCardInstance> SelectCardObjects(
        IReadOnlyList<DeckCardInstance> cards,
        int count,
        bool preferHighValue)
    {
        IOrderedEnumerable<DeckCardInstance> ordered = preferHighValue
            ? cards
                .OrderByDescending(card => CardObjectChoiceScore(card.Card))
                .ThenBy(card => card.InstanceId)
            : cards
                .OrderBy(card => CardObjectChoiceScore(card.Card))
                .ThenBy(card => card.InstanceId);
        return ordered.Take(count).ToArray();
    }

    private static decimal CardObjectChoiceScore(SimulationCard card)
    {
        return CardSearchScore(card);
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

    private static IReadOnlyDictionary<string, string> ParseActionParameters(string? parameter)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return values;
        }

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
        void AddActivePower(ActivePowerKind kind, decimal powerAmount, decimal secondaryAmount = 0m, int counter = 0)
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
            decimal amount = action.Amount ?? 0m;
            switch (key)
            {
                case "Strength":
                    state.StrengthSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
                    break;
                case "Dexterity":
                    state.DexteritySources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
                    break;
                case "Fasten":
                    state.FastenSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
                    state.ParrySources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
                    AddActivePower(ActivePowerKind.RollingBoulder, amount, secondaryAmount: 5m);
                    break;
                case "RetainHand":
                    AddActivePower(ActivePowerKind.RetainHand, amount);
                    break;
                case "SeekingEdge":
                    if (state.SeekingEdgeSources.Count == 0)
                    {
                        state.SeekingEdgeSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
                    }
                    break;
                case "SpectrumShift":
                    AddActivePower(ActivePowerKind.SpectrumShift, amount);
                    break;
                case "Stratagem":
                    AddActivePower(ActivePowerKind.Stratagem, amount);
                    break;
                case "SwordSage":
                    state.SwordSageSources.Add(new ResourceSourceCredit(source.Card.ModelId, source.Card.TypeName, amount));
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
                    state.TurnEnded = true;
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
            return new ChildOfTheStarsBehavior(childOfTheStars.Amount ?? 0m, card.BlockValuePerBlock);
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
            decimal damage = blackHoleTriggers.Select(action => action.Amount ?? 0m).DefaultIfEmpty(0m).Max();
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

    private static decimal TheBombDamage(SimulationCard card)
    {
        return BaseTypeName(card) == "TheBomb" && card.UpgradeLevel > 0
            ? 50m
            : 40m;
    }

    private static PowerEventResult ResolveGeneratedCardActions(
        SimulationState state,
        DeckCardInstance source,
        Random rng,
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
            "HeirloomHammer" => CopyBestColorlessCardToHand(state),
            _ => ResolveExplicitGeneratedCardActions(state, source, options)
        };
    }

    private static PowerEventResult ResolveExplicitGeneratedCardActions(
        SimulationState state,
        DeckCardInstance source,
        DeckSimulationOptions options)
    {
        List<PowerResolution> resolutions = [];
        List<CardValueCreditEvent> credits = [];
        HashSet<string> generatedTargets = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardActionFact action in source.Card.Actions.Where(action => action.Kind == "createCard"))
        {
            IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
            string? target = GetParameter(parameters, "card");
            string? pile = GetParameter(parameters, "pile");
            if (string.IsNullOrWhiteSpace(target) || !string.Equals(pile, "Hand", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
            resolutions.AddRange(result.PowerResolutions);
            credits.AddRange(result.ValueCredits);
        }

        return new PowerEventResult(resolutions, credits);
    }

    private static PowerEventResult GenerateCardsToHandFromGeneratedPool(
        SimulationState state,
        DeckSimulationOptions options,
        Random rng,
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
        Random rng,
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

    private static List<SimulationCard> ResolveGeneratedPoolCandidates(
        DeckSimulationOptions options,
        string poolId,
        bool upgradeGenerated)
    {
        return options.GeneratedCardPools
            .RequirePool(poolId)
            .Select(typeName => ResolveGeneratedCard(options, typeName, upgradeGenerated))
            .ToList();
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
            .OrderByDescending(card => CardObjectChoiceScore(card.Card))
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
                    state.StrengthSources.Add(new ResourceSourceCredit(
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
        IReadOnlyList<DeckCardInstance> unexhaustedBlades = AllCards(state)
            .Where(card => !state.ExhaustPile.Any(exhausted => exhausted.InstanceId == card.InstanceId))
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
        IReadOnlyList<DeckCardInstance> unexhaustedBlades = AllCards(state)
            .Where(card => !state.ExhaustPile.Any(exhausted => exhausted.InstanceId == card.InstanceId))
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
            blade.ForgeCredits.Add(sourceCredit);
        }
    }

    private static IEnumerable<DeckCardInstance> AllCards(SimulationState state)
    {
        return state.DrawPile
            .Concat(state.Hand)
            .Concat(state.DiscardPile)
            .Concat(state.ExhaustPile);
    }

    private static bool IsSovereignBlade(SimulationCard card)
    {
        return string.Equals(card.ModelId, "CARD.SOVEREIGN_BLADE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.ModelId, "GENERATED.SOVEREIGN_BLADE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.TypeName, "SovereignBlade", StringComparison.OrdinalIgnoreCase);
    }

    private static SimulationCard CreateGeneratedSovereignBlade(
        decimal damageUnitValue,
        decimal blockValuePerBlock,
        decimal aoeDamageMultiplier)
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
            StaticEstimatedValue = 10m,
            IntrinsicValue = 10m,
            DamageValue = 10m,
            BaseDamage = 10m,
            DamageModifierMultiplier = 1m,
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
            StaticEstimatedValue = 0m,
            IntrinsicValue = 0m,
            DamageUnitValue = 1m,
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
            StaticEstimatedValue = 11m,
            IntrinsicValue = 11m,
            DamageValue = 11m,
            DamageUnitValue = 1m,
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
        return card.Actions.Any(IsXCostDamageAction);
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
        foreach (CardActionFact action in card.Actions.Where(action => action.Kind == "moveCardBetweenPiles"))
        {
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

    private static bool CanPlay(SimulationCard card, SimulationState state)
    {
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
        return state.Stars + state.ActivePowers
            .Where(power => power.Kind == ActivePowerKind.TheSealedThrone)
            .Sum(power => (int)power.Amount);
    }

    private static bool IsVoidFormFreeCard(SimulationState state)
    {
        decimal freeCards = state.ActivePowers
            .Where(power => power.Kind == ActivePowerKind.VoidForm)
            .Sum(power => power.Amount);
        return freeCards > 0m && state.CardsPlayedThisTurn < freeCards;
    }

    private static decimal CardSearchScore(SimulationCard card)
    {
        return Math.Max(
                card.StaticEstimatedValue,
                card.IntrinsicValue + ExplicitResourceReferenceValue(card, MidlineResourceReferenceValues))
            + SetupPriorityDecisionValue(card)
            + (card.Exhausts ? 0.01m : 0m)
            + (card.Retain ? 0.005m : 0m);
    }

    private static decimal CardSearchScore(SimulationCard card, SimulationState state)
    {
        return EstimateSearchScore(card, state, excludedInstanceId: null, MidlineResourceReferenceValues);
    }

    private static decimal CardSearchScore(
        SimulationCard card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EstimateSearchScore(card, state, excludedInstanceId: null, ResourceReferenceValuesForTurns(options.Turns));
    }

    private static decimal CardSearchScore(DeckCardInstance card, SimulationState state)
    {
        return EstimateSearchScore(card.Card, state, card.InstanceId, MidlineResourceReferenceValues);
    }

    private static decimal CardSearchScore(
        DeckCardInstance card,
        SimulationState state,
        DeckSimulationOptions options)
    {
        return EstimateSearchScore(card.Card, state, card.InstanceId, ResourceReferenceValuesForTurns(options.Turns));
    }

    private static decimal EstimateSearchScore(
        SimulationCard card,
        SimulationState state,
        int? excludedInstanceId,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        decimal immediateValue = EstimateImmediateSearchValue(card, state, XCostEnergy(card, state));
        decimal continuationValue = EstimateContinuationValueAfterPlaying(card, state, excludedInstanceId);
        decimal delayedValue = EstimateDelayedSearchValue(card, state, immediateValue, resourceReferenceValues);
        return immediateValue
            + continuationValue
            + delayedValue
            + SetupPriorityDecisionValue(card)
            + SearchTieBreak(card);
    }

    private static decimal EstimateImmediateSearchValue(SimulationCard card, SimulationState state, int energyForXCost)
    {
        decimal xCostDamageValue = XCostDamageValue(card, state, energyForXCost);
        decimal scalingDamageValue = DynamicScalingDamageValue(card, state, includePlayedCardIfMissing: false);
        decimal directDamageValue = card.DamageValue + scalingDamageValue + xCostDamageValue;
        decimal directValue = card.IntrinsicValue
            + scalingDamageValue
            + xCostDamageValue
            + VulnerableBonus(directDamageValue, state)
            + ReflectApproximationValue(card)
            + StrengthLossDefenseValue(card)
            + HpLossPenaltyValue(card)
            + FrailBlockPenaltyValue(card, state);

        decimal modifierValue = EstimateSovereignBladePowerValue(card, state);
        decimal damageModifierMultiplier = EffectiveDamageModifierMultiplier(card, state)
            + XCostDamageModifierMultiplier(card, state, energyForXCost);
        if (card.IsAttack && damageModifierMultiplier > 0m)
        {
            modifierValue += (SumSources(state.StrengthSources) + SumSources(state.VigorSources))
                * damageModifierMultiplier
                * card.DamageUnitValue;
        }

        if (card.BaseBlock > 0m && card.BlockEffectCount > 0)
        {
            decimal blockModifierValue = SumSources(state.DexteritySources) * card.BlockEffectCount * card.BlockValuePerBlock;
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
        decimal starTriggerValue = 0m;
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

    private static decimal EstimateSovereignBladePowerValue(SimulationCard card, SimulationState state)
    {
        if (!IsSovereignBlade(card))
        {
            return 0m;
        }

        decimal baseDamage = card.BaseDamage > 0m ? card.BaseDamage : card.DamageValue;
        decimal targetMultiplier = SovereignBladeTargetMultiplier(card, state);
        decimal value = 0m;
        if (targetMultiplier > 1m)
        {
            value += SumSources(state.SeekingEdgeSources) * baseDamage * (targetMultiplier - 1m) * card.DamageUnitValue;
        }

        value += SumSources(state.SwordSageSources) * baseDamage * targetMultiplier * card.DamageUnitValue;
        value += SumSources(state.ParrySources) * card.BlockValuePerBlock;
        return value;
    }

    private static decimal EstimateForgeSearchValue(SimulationCard card, SimulationState state)
    {
        int forgeAmount = card.Forge + DynamicForgeAmount(card, state);
        if (forgeAmount <= 0)
        {
            return 0m;
        }

        int activeBladeCount = AllCards(state)
            .Count(instance => IsSovereignBlade(instance.Card) && !state.ExhaustPile.Any(exhausted => exhausted.InstanceId == instance.InstanceId));
        int valuedBladeCount = activeBladeCount == 0 ? 1 : Math.Min(3, activeBladeCount);
        return forgeAmount * valuedBladeCount;
    }

    private static decimal EstimateContinuationValueAfterPlaying(
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

    private static decimal EstimateGreedyContinuationValue(
        IReadOnlyList<SimulationCard> candidates,
        SimulationState state,
        int energy,
        int stars,
        int maxCards)
    {
        List<SimulationCard> remaining = candidates.ToList();
        decimal value = 0m;
        for (int play = 0; play < maxCards && remaining.Count > 0; play++)
        {
            SimulationCard? bestCard = null;
            decimal bestValue = 0m;
            int bestEnergyCost = 0;
            int bestStarCost = 0;
            foreach (SimulationCard candidate in remaining)
            {
                if (!CanPlayWithResources(candidate, energy, stars))
                {
                    continue;
                }

                int energyCost = SearchEnergyCost(candidate, energy);
                int starCost = candidate.StarCost;
                decimal candidateValue = EstimateImmediateSearchValue(candidate, state, energy);
                decimal efficiencyAdjustedValue = candidateValue / (1m + (energyCost * 0.15m) + (starCost * 0.05m));
                if (efficiencyAdjustedValue > bestValue)
                {
                    bestCard = candidate;
                    bestValue = efficiencyAdjustedValue;
                    bestEnergyCost = energyCost;
                    bestStarCost = starCost;
                }
            }

            if (bestCard is null || bestValue <= 0m)
            {
                break;
            }

            value += EstimateImmediateSearchValue(bestCard, state, energy);
            energy = Math.Max(0, energy - bestEnergyCost + bestCard.EnergyGain);
            stars = Math.Max(0, stars - bestStarCost + bestCard.StarGain);
            remaining.Remove(bestCard);
        }

        return value * 0.85m;
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

    private static decimal EstimateDelayedSearchValue(
        SimulationCard card,
        SimulationState state,
        decimal immediateValue,
        ExplicitResourceReferenceValues resourceReferenceValues)
    {
        decimal residualStaticValue = Math.Max(
            0m,
            card.StaticEstimatedValue
                - Math.Max(0m, immediateValue)
                - ExplicitResourceReferenceValue(card, MidlineResourceReferenceValues));
        return residualStaticValue
            + ExplicitResourceReferenceValue(card, resourceReferenceValues)
            + (card.BlockNextTurn * card.BlockValuePerBlock);
    }

    private static decimal EstimateAveragePlayableCardValue(SimulationState state)
    {
        SimulationCard[] candidates = AllCards(state)
            .Select(instance => instance.Card)
            .Where(card => card.IsPlayable)
            .ToArray();
        if (candidates.Length == 0)
        {
            return 0m;
        }

        return candidates
            .Select(card => Math.Max(0m, Math.Max(card.IntrinsicValue, card.StaticEstimatedValue)))
            .OrderDescending()
            .Take(Math.Min(5, candidates.Length))
            .DefaultIfEmpty(0m)
            .Average();
    }

    private static decimal SetupPriorityDecisionValue(SimulationCard card)
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

    private static decimal ExplicitResourceReferenceValue(
        SimulationCard card,
        ExplicitResourceReferenceValues values)
    {
        decimal immediateValue =
            (card.Draw * values.Draw)
            + (card.EnergyGain * values.Energy)
            + (card.StarGain * values.Star);
        decimal nextTurnValue =
            (card.DrawNextTurn * values.Draw)
            + (card.EnergyNextTurn * values.Energy)
            + (card.StarNextTurn * values.Star);
        return immediateValue + (nextTurnValue * NextTurnExplicitResourceReferenceMultiplier);
    }

    private static bool HasActivePower(SimulationState state, ActivePowerKind kind)
    {
        return state.ActivePowers.Any(power => power.Kind == kind && power.Amount > 0m);
    }

    private static decimal SearchTieBreak(SimulationCard card)
    {
        return (card.Exhausts ? 0.003m : 0m)
            + (card.Retain ? 0.002m : 0m)
            - (card.EnergyCost * 0.0001m)
            - (card.StarCost * 0.00005m);
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
        Random? rng,
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
            && power.Amount > 0m);
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

        decimal amount = card.Actions
            .Where(action => action.Kind == "power")
            .Where(action => string.Equals(PowerKey(action.Parameter), "Frail", StringComparison.OrdinalIgnoreCase))
            .Sum(action => action.Amount ?? 0m);
        return Math.Max(0, (int)Math.Round(amount, MidpointRounding.AwayFromZero));
    }

    private static void ExpireEndOfTurnTemporaryPowers(SimulationState state)
    {
        foreach (ActivePower power in state.ActivePowers)
        {
            if (power.Kind is ActivePowerKind.Conqueror or ActivePowerKind.RetainHand)
            {
                power.Amount -= 1m;
            }

            if (power.Kind == ActivePowerKind.Monologue && power.Counter > 0)
            {
                RemovePowerModifierSource(
                    state.StrengthSources,
                    power.SourceModelId,
                    power.SourceTypeName,
                    power.Counter);
                power.Counter = 0;
            }
        }

        state.ActivePowers.RemoveAll(power =>
            (power.Kind == ActivePowerKind.Conqueror || power.Kind == ActivePowerKind.RetainHand)
            && power.Amount <= 0m);
        state.ActivePowers.RemoveAll(power => power.Kind == ActivePowerKind.Monologue);
    }

    private static void RemovePowerModifierSource(
        List<ResourceSourceCredit> sources,
        string sourceModelId,
        string sourceTypeName,
        decimal amount)
    {
        decimal remaining = amount;
        for (int index = sources.Count - 1; index >= 0 && remaining > 0m; index--)
        {
            ResourceSourceCredit source = sources[index];
            if (!string.Equals(source.SourceModelId, sourceModelId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.SourceTypeName, sourceTypeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            decimal consumed = Math.Min(remaining, source.Amount);
            remaining -= consumed;
            decimal nextAmount = source.Amount - consumed;
            if (nextAmount <= 0m)
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
        decimal bucketSize)
    {
        IReadOnlyList<decimal> values = samples.Select(sample => sample.Value).Order().ToArray();
        decimal mean = values.Average();
        decimal variance = values.Count == 0
            ? 0m
            : values.Average(value => (value - mean) * (value - mean));

        return new TurnSimulationSummary(
            turn,
            Round(mean),
            Round(variance),
            Round(Sqrt(variance)),
            Percentile(values, 0.10m),
            Percentile(values, 0.25m),
            Percentile(values, 0.50m),
            Percentile(values, 0.75m),
            Percentile(values, 0.90m),
            Round(samples.Average(sample => (decimal)sample.CardsDrawn)),
            Round(samples.Average(sample => (decimal)sample.CardsPlayed)),
            Round(samples.Average(sample => (decimal)sample.EnergySpent)),
            Round(samples.Average(sample => (decimal)sample.EnergyGained)),
            Round(samples.Average(sample => (decimal)sample.EnergyWasted)),
            Round(samples.Average(sample => (decimal)sample.StarSpent)),
            Round(samples.Average(sample => (decimal)sample.StarGained)),
            Round(samples.Average(sample => (decimal)sample.StarsWasted)),
            Round(samples.Average(sample => sample.UnplayedIntrinsicValue)),
            BuildPmf(values, bucketSize));
    }

    private static IReadOnlyList<ProbabilityBucket> BuildPmf(IReadOnlyList<decimal> values, decimal bucketSize)
    {
        if (bucketSize <= 0m)
        {
            bucketSize = 1m;
        }

        return values
            .GroupBy(value => Round(Math.Round(value / bucketSize, MidpointRounding.AwayFromZero) * bucketSize))
            .OrderBy(group => group.Key)
            .Select(group => new ProbabilityBucket(
                group.Key,
                group.Count(),
                Round((decimal)group.Count() / values.Count)))
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
                    Round((decimal)(productMean - (means[first] * means[second])))));
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
        return Round(Math.Max(0m, variance));
    }

    private static decimal Percentile(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0m;
        }

        decimal rawIndex = percentile * (sortedValues.Count - 1);
        int left = (int)Math.Floor(rawIndex);
        int right = (int)Math.Ceiling(rawIndex);
        if (left == right)
        {
            return Round(sortedValues[left]);
        }

        decimal ratio = rawIndex - left;
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

    private static void Shuffle<T>(IList<T> items, Random rng)
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

    private static decimal Sqrt(decimal value)
    {
        return (decimal)Math.Sqrt((double)value);
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
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

        public List<ResourceSourceCredit> StrengthSources { get; } = [];

        public List<ResourceSourceCredit> DexteritySources { get; } = [];

        public List<ResourceSourceCredit> FastenSources { get; } = [];

        public List<ResourceSourceCredit> ParrySources { get; } = [];

        public List<ResourceSourceCredit> SeekingEdgeSources { get; } = [];

        public List<ResourceSourceCredit> SwordSageSources { get; } = [];

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

        public int MaxHandSize { get; set; }

        public bool TurnEnded { get; set; }

        public static SimulationState Create(IReadOnlyList<SimulationCard> deck, Random rng, DeckSimulationOptions options)
        {
            SimulationState state = new()
            {
                Stars = options.BaseStars,
                BaseStarsRemaining = options.BaseStars,
                CharacterPoolName = InferCharacterPoolName(deck),
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
                MaxHandSize = MaxHandSize,
                TurnEnded = TurnEnded,
                CharacterPoolName = CharacterPoolName
            };
            clone.DrawPile.AddRange(DrawPile.Select(card => card.Clone()));
            clone.Hand.AddRange(Hand.Select(card => card.Clone()));
            clone.DiscardPile.AddRange(DiscardPile.Select(card => card.Clone()));
            clone.ExhaustPile.AddRange(ExhaustPile.Select(card => card.Clone()));
            clone.ActivePowers.AddRange(ActivePowers.Select(power => power.Clone()));
            clone.CurrentTurnEnergySources.AddRange(CurrentTurnEnergySources);
            clone.NextTurnEnergySources.AddRange(NextTurnEnergySources);
            clone.NextTurnStarSources.AddRange(NextTurnStarSources);
            clone.StarSources.AddRange(StarSources);
            clone.NextTurnBlockCredits.AddRange(NextTurnBlockCredits);
            clone.StrengthSources.AddRange(StrengthSources);
            clone.DexteritySources.AddRange(DexteritySources);
            clone.FastenSources.AddRange(FastenSources);
            clone.ParrySources.AddRange(ParrySources);
            clone.SeekingEdgeSources.AddRange(SeekingEdgeSources);
            clone.SwordSageSources.AddRange(SwordSageSources);
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
            StrengthSources.Clear();
            StrengthSources.AddRange(state.StrengthSources);
            DexteritySources.Clear();
            DexteritySources.AddRange(state.DexteritySources);
            FastenSources.Clear();
            FastenSources.AddRange(state.FastenSources);
            ParrySources.Clear();
            ParrySources.AddRange(state.ParrySources);
            SeekingEdgeSources.Clear();
            SeekingEdgeSources.AddRange(state.SeekingEdgeSources);
            SwordSageSources.Clear();
            SwordSageSources.AddRange(state.SwordSageSources);
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
            MaxHandSize = state.MaxHandSize;
            TurnEnded = state.TurnEnded;
        }
    }

    private sealed class DeckCardInstance(int instanceId, SimulationCard card)
    {
        public int InstanceId { get; } = instanceId;

        public SimulationCard Card { get; set; } = card;

        public List<ForgeSourceCredit> ForgeCredits { get; } = [];

        public DeckCardInstance Clone()
        {
            DeckCardInstance clone = new(InstanceId, Card);
            clone.ForgeCredits.AddRange(ForgeCredits);
            return clone;
        }
    }

    private sealed record ForgeSourceCredit(
        string SourceModelId,
        string SourceTypeName,
        int SourcePlayId,
        decimal Amount);

    private sealed record ResourceSourceCredit(
        string SourceModelId,
        string SourceTypeName,
        decimal Amount);

    private sealed record DelayedValueCredit(
        string SourceModelId,
        string SourceTypeName,
        decimal Value);

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
        decimal amount,
        decimal secondaryAmount = 0m,
        int counter = 0,
        int sourceInstanceId = -1,
        ISimulationPowerBehavior? behavior = null)
    {
        public string SourceModelId { get; } = sourceModelId;

        public string SourceTypeName { get; } = sourceTypeName;

        public ActivePowerKind Kind { get; } = kind;

        public SimulationCard SourceCard { get; } = sourceCard;

        public decimal Amount { get; set; } = amount;

        public decimal SecondaryAmount { get; } = secondaryAmount;

        public int Counter { get; set; } = counter;

        public int SourceInstanceId { get; } = sourceInstanceId;

        public ISimulationPowerBehavior? Behavior { get; } = behavior;

        public static ActivePower Persistent(
            string sourceModelId,
            string sourceTypeName,
            SimulationCard sourceCard,
            ISimulationPowerBehavior behavior)
        {
            return new ActivePower(sourceModelId, sourceTypeName, ActivePowerKind.Persistent, sourceCard, 0m, behavior: behavior);
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

    private sealed class ChildOfTheStarsBehavior(decimal blockPerStarSpent, decimal blockValuePerBlock) : ISimulationPowerBehavior
    {
        public IReadOnlyList<PowerResolution> Resolve(SimulationEvent simulationEvent, ActivePower source)
        {
            if (simulationEvent.Kind != SimulationEventKind.StarSpent || simulationEvent.Amount <= 0)
            {
                return [];
            }

            decimal value = simulationEvent.Amount * blockPerStarSpent * blockValuePerBlock;
            return [new PowerResolution(source.SourceModelId, source.SourceTypeName, value)];
        }
    }

    private sealed class BlackHoleBehavior(
        decimal damage,
        decimal damageUnitValue,
        decimal aoeDamageMultiplier,
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

            decimal value = damage * damageUnitValue * aoeDamageMultiplier;
            return [new PowerResolution(source.SourceModelId, source.SourceTypeName, value)];
        }
    }

    private sealed record PowerResolution(
        string SourceModelId,
        string SourceTypeName,
        decimal Value);

    private sealed record PowerEventResult(
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits)
    {
        public static PowerEventResult Empty { get; } = new([], []);

        public decimal Value => PowerResolutions.Sum(resolution => resolution.Value);
    }

    private sealed record DrawResult(
        int CardsDrawn,
        IReadOnlyList<PowerResolution> PowerResolutions,
        IReadOnlyList<CardValueCreditEvent> ValueCredits)
    {
        public decimal Value => PowerResolutions.Sum(resolution => resolution.Value);
    }

    private sealed record PlayValueResult(
        decimal DirectValue,
        decimal Value,
        IReadOnlyList<CardValueCreditEvent> ValueCredits);

    private sealed record CardValueCreditEvent(
        string ModelId,
        string TypeName,
        decimal DirectValue,
        decimal ForgeRealizedValue,
        decimal PowerRealizedValue,
        decimal EnergyRealizedValue,
        decimal StarRealizedValue,
        bool CountsAsDirectPlay);

    private sealed record PlayEvent(
        SimulationCard Card,
        decimal Value,
        decimal DecisionValue,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        IReadOnlyList<CardValueCreditEvent> ValueCredits);

    private sealed record SearchResult(
        SimulationState State,
        decimal Value,
        decimal DecisionValue,
        int CardsPlayed,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        IReadOnlyList<PlayEvent> PlayedCards);

    private sealed record TurnTrialSummary(
        int Turn,
        decimal Value,
        int CardsDrawn,
        int CardsPlayed,
        int EnergySpent,
        int EnergyGained,
        int EnergyWasted,
        int StarSpent,
        int StarGained,
        int StarsWasted,
        decimal UnplayedIntrinsicValue,
        IReadOnlyList<PlayEvent> PlayedCards,
        IReadOnlyList<CardValueCreditEvent> ValueCredits);

    private sealed class CardPlayAccumulator(string modelId, string typeName)
    {
        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int PlayCount { get; set; }

        public decimal TotalValue { get; set; }
    }

    private sealed record ExplicitResourceReferenceValues(
        decimal Draw,
        decimal Energy,
        decimal Star);

    private sealed class CardPlayTurnAccumulator(int turn, string modelId, string typeName)
    {
        public int Turn { get; } = turn;

        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int PlayCount { get; set; }

        public decimal TotalValue { get; set; }
    }

    private sealed class CardValueCreditAccumulator(string modelId, string typeName)
    {
        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int DirectPlayCount { get; set; }

        public decimal DirectValue { get; set; }

        public decimal ForgeRealizedValue { get; set; }

        public decimal PowerRealizedValue { get; set; }

        public decimal EnergyRealizedValue { get; set; }

        public decimal StarRealizedValue { get; set; }

        public decimal TotalCreditedValue => DirectValue + ForgeRealizedValue + PowerRealizedValue + EnergyRealizedValue + StarRealizedValue;
    }

    private sealed class CardValueCreditTurnAccumulator(int turn, string modelId, string typeName)
    {
        public int Turn { get; } = turn;

        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int DirectPlayCount { get; set; }

        public decimal DirectValue { get; set; }

        public decimal ForgeRealizedValue { get; set; }

        public decimal PowerRealizedValue { get; set; }

        public decimal EnergyRealizedValue { get; set; }

        public decimal StarRealizedValue { get; set; }

        public decimal TotalCreditedValue => DirectValue + ForgeRealizedValue + PowerRealizedValue + EnergyRealizedValue + StarRealizedValue;
    }
}
