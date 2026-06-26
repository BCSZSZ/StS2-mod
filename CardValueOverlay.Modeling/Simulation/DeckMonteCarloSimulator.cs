using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class DeckMonteCarloSimulator
{
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
        Dictionary<string, CardValueCreditAccumulator> cardValueCreditAccumulators = new(StringComparer.OrdinalIgnoreCase);
        Random seedRng = new(options.Seed);

        for (int run = 0; run < options.Runs; run++)
        {
            Random rng = new(seedRng.Next());
            SimulationState state = SimulationState.Create(simulationDeck, rng);
            for (int turn = 1; turn <= options.Turns; turn++)
            {
                TurnTrialSummary summary = PlayTurn(state, options, rng, turn);
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
                }

                foreach (CardValueCreditEvent credit in summary.ValueCredits)
                {
                    AddCredit(cardValueCreditAccumulators, credit);
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
                Round(item.TotalCreditedValue),
                item.DirectPlayCount == 0 ? 0m : Round(item.DirectValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.ForgeRealizedValue / item.DirectPlayCount),
                item.DirectPlayCount == 0 ? 0m : Round(item.PowerRealizedValue / item.DirectPlayCount),
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
            cardValueCredits,
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
        int turn)
    {
        int queuedEnergy = state.NextTurnEnergy;
        int queuedStars = state.NextTurnStars;
        int queuedDraw = state.NextTurnDraw;
        state.NextTurnEnergy = 0;
        state.NextTurnStars = 0;
        state.NextTurnDraw = 0;
        state.Energy = options.BaseEnergy + queuedEnergy;
        state.Stars = (options.StarsPersistBetweenTurns ? state.Stars : options.BaseStars) + queuedStars;
        state.EnemyVulnerable = Math.Max(0, state.EnemyVulnerable - 1);
        IReadOnlyList<PowerResolution> turnStartResolutions = queuedStars > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, queuedStars))
            : [];
        decimal turnStartValue = turnStartResolutions.Sum(resolution => resolution.Value);
        IReadOnlyList<CardValueCreditEvent> turnStartCredits = PowerCredits(turnStartResolutions);

        int cardsDrawn = DrawCards(state, options.HandSize + queuedDraw, rng, allowShuffle: true);
        SearchResult result = Search(state.Clone(), options, actionsPlayed: 0, rng.Next());
        state.CopyFrom(result.State);

        decimal unplayedIntrinsicValue = state.Hand
            .Where(card => card.Card.IsPlayable && card.Card.IntrinsicValue > 0m)
            .Sum(card => card.Card.IntrinsicValue);
        int energyWasted = Math.Max(0, state.Energy);
        int starsWasted = Math.Max(0, state.Stars);
        FinishTurn(state);
        IReadOnlyList<CardValueCreditEvent> valueCredits = [.. turnStartCredits, .. result.PlayedCards.SelectMany(card => card.ValueCredits)];

        return new TurnTrialSummary(
            turn,
            turnStartValue + result.Value,
            cardsDrawn + result.CardsDrawn,
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

    private static SearchResult Search(SimulationState state, DeckSimulationOptions options, int actionsPlayed, int seed)
    {
        SearchResult best = new(state, 0m, 0m, 0, 0, 0, 0, 0, 0, []);
        if (actionsPlayed >= options.MaxCardsPlayedPerTurn)
        {
            return best;
        }

        IReadOnlyList<DeckCardInstance> playableCards = state.Hand
            .Where(card => CanPlay(card.Card, state))
            .OrderByDescending(card => CardSearchScore(card.Card))
            .ThenBy(card => card.InstanceId)
            .Take(options.MaxBranchingCards)
            .ToArray();

        foreach (DeckCardInstance card in playableCards)
        {
            SimulationState next = state.Clone();
            DeckCardInstance nextCard = next.Hand.Single(item => item.InstanceId == card.InstanceId);
            Random branchRng = new(DeriveSeed(seed, actionsPlayed, card.InstanceId));
            PlayEvent play = PlayCard(next, nextCard, branchRng, options);
            SearchResult suffix = Search(next, options, actionsPlayed + 1, branchRng.Next());
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

    private static PlayEvent PlayCard(SimulationState state, DeckCardInstance card, Random rng, DeckSimulationOptions options)
    {
        state.Hand.Remove(card);
        SimulationCard playedCard = card.Card;
        int playId = state.NextPlayEventId++;
        decimal directValue = PlayValue(playedCard, state);
        state.Energy -= playedCard.EnergyCost;
        state.Stars -= playedCard.StarCost;
        IReadOnlyList<PowerResolution> starSpentResolutions = playedCard.StarCost > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarSpent, playedCard.StarCost, card))
            : [];
        state.Energy += playedCard.EnergyGain;
        state.Stars += playedCard.StarGain;
        IReadOnlyList<PowerResolution> starGainedResolutions = playedCard.StarGain > 0
            ? DispatchPowerEvent(state, new SimulationEvent(SimulationEventKind.StarGained, playedCard.StarGain, card))
            : [];
        state.NextTurnEnergy += playedCard.EnergyNextTurn;
        state.NextTurnStars += playedCard.StarNextTurn;
        state.NextTurnDraw += playedCard.DrawNextTurn;
        state.EnemyVulnerable += playedCard.Vulnerable;
        ApplyForge(state, playedCard.Forge, card, playId);
        int cardsDrawn = DrawCards(state, playedCard.Draw, rng, allowShuffle: true);
        SimulationCard? transformedPlayedCard = ResolveCardObjectActions(state, card, options);
        InstallPower(state, card);
        IReadOnlyList<PowerResolution> powerResolutions = [.. starSpentResolutions, .. starGainedResolutions];
        decimal powerValue = powerResolutions.Sum(resolution => resolution.Value);
        decimal value = directValue + powerValue;
        decimal decisionValue = value + playedCard.SetupPriorityValue;
        IReadOnlyList<CardValueCreditEvent> valueCredits = BuildValueCredits(card, directValue, PowerCredits(powerResolutions));

        if (transformedPlayedCard is not null)
        {
            card.Card = transformedPlayedCard;
            state.DiscardPile.Add(card);
        }
        else if (playedCard.Exhausts || IsPowerCard(playedCard))
        {
            state.ExhaustPile.Add(card);
        }
        else
        {
            state.DiscardPile.Add(card);
        }

        return new PlayEvent(
            playedCard,
            value,
            decisionValue,
            cardsDrawn,
            playedCard.EnergyCost,
            playedCard.EnergyGain,
            playedCard.StarCost,
            playedCard.StarGain,
            valueCredits);
    }

    private static decimal PlayValue(SimulationCard card, SimulationState state)
    {
        return card.IntrinsicValue + VulnerableBonus(card, state);
    }

    private static decimal VulnerableBonus(SimulationCard card, SimulationState state)
    {
        if (state.EnemyVulnerable <= 0 || card.DamageValue <= 0m)
        {
            return 0m;
        }

        return Math.Floor(card.DamageValue * 0.5m);
    }

    private static IReadOnlyList<CardValueCreditEvent> BuildValueCredits(
        DeckCardInstance card,
        decimal directValue,
        IReadOnlyList<CardValueCreditEvent> powerCredits)
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
                CountsAsDirectPlay: false));
        }

        credits.Insert(0, new CardValueCreditEvent(
            card.Card.ModelId,
            card.Card.TypeName,
            directValue - forgeRealizedValue,
            0m,
            0m,
            CountsAsDirectPlay: true));
        credits.AddRange(powerCredits);
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
                CountsAsDirectPlay: false))
            .ToArray();
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
            resolutions.AddRange(power.Behavior.Resolve(simulationEvent, power));
        }

        return resolutions;
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

        AddCardsToPile(toPile, selected, GetParameter(parameters, "position"));
    }

    private static SimulationCard? ResolveTransformCard(
        SimulationState state,
        DeckCardInstance source,
        CardActionFact action,
        DeckSimulationOptions options)
    {
        IReadOnlyDictionary<string, string> parameters = ParseActionParameters(action.Parameter);
        SimulationCard replacement = ResolveTransformReplacement(parameters, options);
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

        int count = Math.Max(0, (int)Math.Round(action.Amount ?? 1m, MidpointRounding.AwayFromZero));
        if (count == 0)
        {
            return null;
        }

        foreach (DeckCardInstance selectedCard in SelectCardObjects(fromPile, count, preferHighValue: false))
        {
            selectedCard.Card = replacement;
        }

        return null;
    }

    private static SimulationCard ResolveTransformReplacement(
        IReadOnlyDictionary<string, string> parameters,
        DeckSimulationOptions options)
    {
        string? target = GetParameter(parameters, "card");
        if (!string.IsNullOrWhiteSpace(target)
            && !string.Equals(target, "SIM.TRANSFORMED_CARD", StringComparison.OrdinalIgnoreCase)
            && TryFindSimulationCard(options.CardLibrary, target, out SimulationCard? replacement))
        {
            return replacement!;
        }

        return CreateGenericTransformedCard();
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
        List<DeckCardInstance> pile,
        IReadOnlyList<DeckCardInstance> cards,
        string? position)
    {
        if (string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase))
        {
            foreach (DeckCardInstance card in cards.Reverse())
            {
                pile.Insert(0, card);
            }

            return;
        }

        pile.AddRange(cards);
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
        ISimulationPowerBehavior? behavior = CreatePowerBehavior(source.Card);
        if (behavior is null)
        {
            return;
        }

        state.ActivePowers.Add(new ActivePower(
            source.Card.ModelId,
            source.Card.TypeName,
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

    private static bool IsPowerCard(SimulationCard card)
    {
        return string.Equals(card.CardType, "Power", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyForge(SimulationState state, int amount, DeckCardInstance source, int sourcePlayId)
    {
        if (amount <= 0)
        {
            return;
        }

        IReadOnlyList<DeckCardInstance> unexhaustedBlades = AllCards(state)
            .Where(card => !state.ExhaustPile.Any(exhausted => exhausted.InstanceId == card.InstanceId))
            .Where(card => IsSovereignBlade(card.Card))
            .ToArray();
        if (unexhaustedBlades.Count == 0)
        {
            state.Hand.Add(new DeckCardInstance(
                state.NextGeneratedInstanceId++,
                CreateGeneratedSovereignBlade()));
        }

        ForgeSourceCredit sourceCredit = new(
            source.Card.ModelId,
            source.Card.TypeName,
            sourcePlayId,
            amount);
        foreach (DeckCardInstance blade in AllCards(state).Where(card => IsSovereignBlade(card.Card)))
        {
            blade.Card = blade.Card with
            {
                IntrinsicValue = blade.Card.IntrinsicValue + amount,
                StaticEstimatedValue = blade.Card.StaticEstimatedValue + amount,
                DamageValue = blade.Card.DamageValue + amount
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

    private static SimulationCard CreateGeneratedSovereignBlade()
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
            EnergyCost = 2,
            Retain = true,
            Confidence = 0.75,
            Warnings = ["Generated by simplified Forge simulation."]
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

    private static bool CanPlay(SimulationCard card, SimulationState state)
    {
        return card.IsPlayable
            && card.EnergyCost <= state.Energy
            && card.StarCost <= state.Stars;
    }

    private static decimal CardSearchScore(SimulationCard card)
    {
        return card.IntrinsicValue
            + card.SetupPriorityValue
            + (card.DamageValue * 0.01m)
            + (card.Draw * 0.25m)
            + (card.EnergyGain * 0.2m)
            + (card.StarGain * 0.2m)
            + (card.Forge * 0.2m)
            + (card.Vulnerable * 0.2m)
            + (card.DrawNextTurn * 0.05m)
            + (card.EnergyNextTurn * 0.05m)
            + (card.StarNextTurn * 0.05m)
            + (card.Actions.Any(action => action.Kind == "moveCardBetweenPiles") ? 0.5m : 0m)
            + (card.Actions.Any(action => action.Kind == "transformCard") ? 0.75m : 0m);
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

    private static int DrawCards(SimulationState state, int count, Random? rng, bool allowShuffle)
    {
        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            if (state.DrawPile.Count == 0 && state.DiscardPile.Count > 0 && allowShuffle && rng is not null)
            {
                state.DrawPile.AddRange(state.DiscardPile);
                state.DiscardPile.Clear();
                Shuffle(state.DrawPile, rng);
            }

            if (state.DrawPile.Count == 0)
            {
                break;
            }

            DeckCardInstance card = state.DrawPile[0];
            state.DrawPile.RemoveAt(0);
            state.Hand.Add(card);
            drawn++;
        }

        return drawn;
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
        foreach (DeckCardInstance card in state.Hand)
        {
            if (card.Card.Ethereal)
            {
                state.ExhaustPile.Add(card);
            }
            else if (card.Card.Retain)
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
    }

    private static void Shuffle<T>(IList<T> items, Random rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
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

        public int Energy { get; set; }

        public int Stars { get; set; }

        public int NextTurnEnergy { get; set; }

        public int NextTurnStars { get; set; }

        public int NextTurnDraw { get; set; }

        public int NextGeneratedInstanceId { get; set; }

        public int NextPlayEventId { get; set; }

        public int EnemyVulnerable { get; set; }

        public static SimulationState Create(IReadOnlyList<SimulationCard> deck, Random rng)
        {
            SimulationState state = new();
            for (int i = 0; i < deck.Count; i++)
            {
                state.DrawPile.Add(new DeckCardInstance(i, deck[i]));
            }

            state.NextGeneratedInstanceId = deck.Count;
            Shuffle(state.DrawPile, rng);
            return state;
        }

        public SimulationState Clone()
        {
            SimulationState clone = new()
            {
                Energy = Energy,
                Stars = Stars,
                NextTurnEnergy = NextTurnEnergy,
                NextTurnStars = NextTurnStars,
                NextTurnDraw = NextTurnDraw,
                NextGeneratedInstanceId = NextGeneratedInstanceId,
                NextPlayEventId = NextPlayEventId,
                EnemyVulnerable = EnemyVulnerable
            };
            clone.DrawPile.AddRange(DrawPile.Select(card => card.Clone()));
            clone.Hand.AddRange(Hand.Select(card => card.Clone()));
            clone.DiscardPile.AddRange(DiscardPile.Select(card => card.Clone()));
            clone.ExhaustPile.AddRange(ExhaustPile.Select(card => card.Clone()));
            clone.ActivePowers.AddRange(ActivePowers);
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
            ActivePowers.AddRange(state.ActivePowers);
            Energy = state.Energy;
            Stars = state.Stars;
            NextTurnEnergy = state.NextTurnEnergy;
            NextTurnStars = state.NextTurnStars;
            NextTurnDraw = state.NextTurnDraw;
            NextGeneratedInstanceId = state.NextGeneratedInstanceId;
            NextPlayEventId = state.NextPlayEventId;
            EnemyVulnerable = state.EnemyVulnerable;
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

    private enum SimulationEventKind
    {
        StarSpent,
        StarGained
    }

    private sealed record SimulationEvent(
        SimulationEventKind Kind,
        int Amount,
        DeckCardInstance? Source = null);

    private sealed record ActivePower(
        string SourceModelId,
        string SourceTypeName,
        ISimulationPowerBehavior Behavior);

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

    private sealed record CardValueCreditEvent(
        string ModelId,
        string TypeName,
        decimal DirectValue,
        decimal ForgeRealizedValue,
        decimal PowerRealizedValue,
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

    private sealed class CardValueCreditAccumulator(string modelId, string typeName)
    {
        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int DirectPlayCount { get; set; }

        public decimal DirectValue { get; set; }

        public decimal ForgeRealizedValue { get; set; }

        public decimal PowerRealizedValue { get; set; }

        public decimal TotalCreditedValue => DirectValue + ForgeRealizedValue + PowerRealizedValue;
    }
}
