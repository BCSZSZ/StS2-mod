namespace CardValueOverlay.Modeling.Simulation;

public sealed class DeckMonteCarloSimulator
{
    public DeckSimulationReport Simulate(
        IReadOnlyList<SimulationCard> deck,
        DeckSimulationOptions options)
    {
        Validate(deck, options);

        double[,] turnValues = new double[options.Runs, options.Turns];
        List<TurnTrialSummary>[] turnSamples = Enumerable.Range(0, options.Turns)
            .Select(_ => new List<TurnTrialSummary>(options.Runs))
            .ToArray();
        Dictionary<string, CardPlayAccumulator> cardPlayAccumulators = new(StringComparer.OrdinalIgnoreCase);
        Random seedRng = new(options.Seed);

        for (int run = 0; run < options.Runs; run++)
        {
            Random rng = new(seedRng.Next());
            SimulationState state = SimulationState.Create(deck, rng);
            for (int turn = 1; turn <= options.Turns; turn++)
            {
                TurnTrialSummary summary = PlayTurn(state, options, rng, turn);
                turnValues[run, turn - 1] = (double)summary.Value;
                turnSamples[turn - 1].Add(summary);

                foreach (SimulationCard card in summary.PlayedCards)
                {
                    if (!cardPlayAccumulators.TryGetValue(card.ModelId, out CardPlayAccumulator? accumulator))
                    {
                        accumulator = new CardPlayAccumulator(card.ModelId, card.TypeName);
                        cardPlayAccumulators.Add(card.ModelId, accumulator);
                    }

                    accumulator.PlayCount++;
                    accumulator.TotalIntrinsicValue += card.IntrinsicValue;
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
                item.PlayCount == 0 ? 0m : Round(item.TotalIntrinsicValue / item.PlayCount)))
            .ToArray();
        decimal totalExpectedValue = Round(turnSummaries.Sum(turn => turn.ExpectedValue));
        decimal totalVariance = RoundTotalVariance(turnSummaries, covariances);

        return new DeckSimulationReport(
            deck.Count,
            deck.Count(card => card.IsPlayable),
            options,
            totalExpectedValue,
            totalVariance,
            turnSummaries,
            covariances,
            playedCards,
            [],
            BuildWarnings(deck),
            "sampled-lookahead Monte Carlo deck simulator v1");
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

        int cardsDrawn = DrawCards(state, options.HandSize + queuedDraw, rng, allowShuffle: true);
        SearchResult result = Search(state.Clone(), options, actionsPlayed: 0);
        state.CopyFrom(result.State);

        decimal unplayedIntrinsicValue = state.Hand
            .Where(card => card.Card.IsPlayable && card.Card.IntrinsicValue > 0m)
            .Sum(card => card.Card.IntrinsicValue);
        int energyWasted = Math.Max(0, state.Energy);
        int starsWasted = Math.Max(0, state.Stars);
        FinishTurn(state);

        return new TurnTrialSummary(
            turn,
            result.Value,
            cardsDrawn + result.CardsDrawn,
            result.CardsPlayed,
            result.EnergySpent,
            result.EnergyGained,
            energyWasted,
            result.StarSpent,
            result.StarGained,
            starsWasted,
            unplayedIntrinsicValue,
            result.PlayedCards);
    }

    private static SearchResult Search(SimulationState state, DeckSimulationOptions options, int actionsPlayed)
    {
        SearchResult best = new(state, 0m, 0, 0, 0, 0, 0, 0, []);
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
            PlayEvent play = PlayCard(next, nextCard);
            SearchResult suffix = Search(next, options, actionsPlayed + 1);
            List<SimulationCard> playedCards = [play.Card, .. suffix.PlayedCards];
            SearchResult candidate = new(
                suffix.State,
                play.Value + suffix.Value,
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

    private static PlayEvent PlayCard(SimulationState state, DeckCardInstance card)
    {
        state.Hand.Remove(card);
        state.Energy -= card.Card.EnergyCost;
        state.Stars -= card.Card.StarCost;
        state.Energy += card.Card.EnergyGain;
        state.Stars += card.Card.StarGain;
        state.NextTurnEnergy += card.Card.EnergyNextTurn;
        state.NextTurnStars += card.Card.StarNextTurn;
        state.NextTurnDraw += card.Card.DrawNextTurn;
        ApplyForge(state, card.Card.Forge);
        int cardsDrawn = DrawCards(state, card.Card.Draw, rng: null, allowShuffle: false);

        if (card.Card.Exhausts)
        {
            state.ExhaustPile.Add(card);
        }
        else
        {
            state.DiscardPile.Add(card);
        }

        return new PlayEvent(
            card.Card,
            card.Card.IntrinsicValue,
            cardsDrawn,
            card.Card.EnergyCost,
            card.Card.EnergyGain,
            card.Card.StarCost,
            card.Card.StarGain);
    }

    private static void ApplyForge(SimulationState state, int amount)
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

        foreach (DeckCardInstance blade in AllCards(state).Where(card => IsSovereignBlade(card.Card)))
        {
            blade.Card = blade.Card with
            {
                IntrinsicValue = blade.Card.IntrinsicValue + amount,
                StaticEstimatedValue = blade.Card.StaticEstimatedValue + amount
            };
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
            EnergyCost = 2,
            Retain = true,
            Confidence = 0.75,
            Warnings = ["Generated by simplified Forge simulation."]
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
            + (card.Draw * 0.25m)
            + (card.EnergyGain * 0.2m)
            + (card.StarGain * 0.2m)
            + (card.DrawNextTurn * 0.05m)
            + (card.EnergyNextTurn * 0.05m)
            + (card.StarNextTurn * 0.05m);
    }

    private static bool IsBetter(SearchResult candidate, SearchResult best)
    {
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

    private static IReadOnlyList<string> BuildWarnings(IReadOnlyList<SimulationCard> deck)
    {
        List<string> warnings = [];
        if (deck.Any(card => card.StarCost > 0 || card.StarGain > 0 || card.StarNextTurn > 0))
        {
            warnings.Add("Star effects come from parsed CanonicalStarCost, StarsVar, and StarNextTurnPower terms.");
        }

        if (deck.Any(card => card.Forge > 0))
        {
            warnings.Add("Forge simulation uses a simplified Sovereign Blade model: Forge creates a retained 2-cost blade if none is unexhausted and adds damage to all blade copies.");
        }

        if (deck.Any(card => card.Draw > 0))
        {
            warnings.Add("Sampled-lookahead policy can see cards drawn within a sampled trial, so draw chains may be optimistic until expectation-based play search is added.");
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

        public int Energy { get; set; }

        public int Stars { get; set; }

        public int NextTurnEnergy { get; set; }

        public int NextTurnStars { get; set; }

        public int NextTurnDraw { get; set; }

        public int NextGeneratedInstanceId { get; set; }

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
                NextGeneratedInstanceId = NextGeneratedInstanceId
            };
            clone.DrawPile.AddRange(DrawPile.Select(card => card.Clone()));
            clone.Hand.AddRange(Hand.Select(card => card.Clone()));
            clone.DiscardPile.AddRange(DiscardPile.Select(card => card.Clone()));
            clone.ExhaustPile.AddRange(ExhaustPile.Select(card => card.Clone()));
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
            Energy = state.Energy;
            Stars = state.Stars;
            NextTurnEnergy = state.NextTurnEnergy;
            NextTurnStars = state.NextTurnStars;
            NextTurnDraw = state.NextTurnDraw;
            NextGeneratedInstanceId = state.NextGeneratedInstanceId;
        }
    }

    private sealed class DeckCardInstance(int instanceId, SimulationCard card)
    {
        public int InstanceId { get; } = instanceId;

        public SimulationCard Card { get; set; } = card;

        public DeckCardInstance Clone()
        {
            return new DeckCardInstance(InstanceId, Card);
        }
    }

    private sealed record PlayEvent(
        SimulationCard Card,
        decimal Value,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained);

    private sealed record SearchResult(
        SimulationState State,
        decimal Value,
        int CardsPlayed,
        int CardsDrawn,
        int EnergySpent,
        int EnergyGained,
        int StarSpent,
        int StarGained,
        IReadOnlyList<SimulationCard> PlayedCards);

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
        IReadOnlyList<SimulationCard> PlayedCards);

    private sealed class CardPlayAccumulator(string modelId, string typeName)
    {
        public string ModelId { get; } = modelId;

        public string TypeName { get; } = typeName;

        public int PlayCount { get; set; }

        public decimal TotalIntrinsicValue { get; set; }
    }
}
