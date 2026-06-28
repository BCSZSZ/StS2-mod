using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Export;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private static int TrainCardValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string trainingDecksPath = GetOption(args, "--training-decks")
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_100_decks.json");
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        int runs = GetIntOption(args, "--runs") ?? 1000;
        int seed = GetIntOption(args, "--seed") ?? 1;
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays") ?? 8;
        int maxBranchingCards = GetIntOption(args, "--max-branch") ?? 2;
        int? limitCards = GetIntOption(args, "--limit-cards");
        int? limitDecks = GetIntOption(args, "--limit-decks");
        int skipDecks = Math.Max(0, GetIntOption(args, "--skip-decks") ?? 0);
        int degreeOfParallelism = Math.Max(1, GetIntOption(args, "--degree-of-parallelism") ?? 1);
        bool resume = HasFlag(args, "--resume");
        bool profile = HasFlag(args, "--profile");
        bool writeConfig = HasFlag(args, "--write-config");
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        string outputPath = Path.Combine(generatedRoot, "training_card_values.generated.json");

        if (!File.Exists(trainingDecksPath))
        {
            return Fail($"Missing training deck file at {trainingDecksPath}.");
        }

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        TrainingDeckFile trainingDeckFile =
            JsonSerializer.Deserialize<TrainingDeckFile>(File.ReadAllText(trainingDecksPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read training decks from {trainingDecksPath}.");
        IReadOnlyList<TrainingDeck> trainingDecks = trainingDeckFile.Decks
            .Skip(skipDecks)
            .Take(limitDecks ?? int.MaxValue)
            .ToArray();
        if (trainingDecks.Count == 0)
        {
            return Fail("Training deck file did not contain any decks.");
        }

        int[] layers = trainingDecks
            .Select(TrainingLayer)
            .Distinct()
            .Order()
            .ToArray();
        Dictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer = layers.ToDictionary(
            layer => layer,
            layer => new SimulationCardLibraryBuilder().Build(entries, calibration, layer, includeUpgrades: true, memberships));
        IReadOnlyList<TrainingCandidate> candidates = SelectTrainingCandidates(librariesByLayer[layers[0]])
            .Take(limitCards ?? int.MaxValue)
            .ToArray();
        if (candidates.Count == 0)
        {
            return Fail("No Regent or Colorless candidate cards were found in the simulation library.");
        }

        Dictionary<int, Dictionary<string, SimulationCard>> byModelIdByLayer = librariesByLayer.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
        Dictionary<int, Dictionary<string, SimulationCard>> byTypeNameByLayer = librariesByLayer.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(card => card.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));

        TrainingValueHorizonSpec[] horizons =
        [
            new(TrainingValueHorizon.Shortline, "shortline", 4),
            new(TrainingValueHorizon.Midline, "midline", 8),
            new(TrainingValueHorizon.Longline, "longline", 14)
        ];
        int maxHorizonTurns = horizons.Max(horizon => horizon.Turns);
        List<PreparedTrainingDeck> preparedDecks = trainingDecks
            .Select((deck, index) => PrepareTrainingDeck(deck, skipDecks + index, byModelIdByLayer, byTypeNameByLayer))
            .ToList();
        int baselineRunDegreeOfParallelism = degreeOfParallelism > 1 && preparedDecks.Count > 1
            ? 1
            : degreeOfParallelism;
        ConcurrentDictionary<(int DeckIndex, TrainingValueHorizon Horizon), decimal> baselines = [];
        int completedBaselines = 0;
        Action<PreparedTrainingDeck> trainBaseline = deck =>
        {
            Stopwatch? stopwatch = profile ? Stopwatch.StartNew() : null;
            DeckSimulationOptions options = BuildTrainingOptions(
                maxHorizonTurns,
                runs,
                seed,
                deck.Index,
                handSize,
                maxHandSize,
                baseEnergy,
                baseStars,
                maxCardsPlayed,
                maxBranchingCards,
                librariesByLayer[deck.Layer],
                generatedCardPools,
                baselineRunDegreeOfParallelism);
            DeckMonteCarloSimulator baselineSimulator = new();
            IReadOnlyList<decimal> baseline = baselineSimulator.SimulateExpectedTurnValues(deck.Cards, options);
            foreach (TrainingValueHorizonSpec horizon in horizons)
            {
                baselines[(deck.Index, horizon.Horizon)] = CumulativeExpectedValue(baseline, horizon.Turns);
            }

            int done = Interlocked.Increment(ref completedBaselines);
            if (stopwatch is not null)
            {
                Console.WriteLine($"baseline {done}/{preparedDecks.Count}: deck={deck.Index} group={deck.Group} layer={deck.Layer} cards={deck.Cards.Count} elapsedSeconds={stopwatch.Elapsed.TotalSeconds:0.###}");
            }
            else
            {
                Console.WriteLine($"baseline {done}/{preparedDecks.Count}: deck={deck.Index}");
            }
        };

        if (degreeOfParallelism <= 1)
        {
            foreach (PreparedTrainingDeck deck in preparedDecks)
            {
                trainBaseline(deck);
            }
        }
        else
        {
            Parallel.ForEach(
                preparedDecks,
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                trainBaseline);
        }

        TrainingValueMetadata metadata = new()
        {
            Source = "dashen_77_selected_100",
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            DeckCount = preparedDecks.Count,
            RunsPerDeck = runs,
            MaxCardsPlayedPerTurn = maxCardsPlayed,
            MaxBranchingCards = maxBranchingCards,
            Horizons = horizons.ToDictionary(horizon => horizon.Key, horizon => horizon.Turns, StringComparer.OrdinalIgnoreCase),
            Note = "Values are mean deck-level cumulative EV deltas from adding one card copy to each selected training deck. Shortline and midline are read from the same 14-turn simulation prefix as longline. Batch training uses a bounded play-search beam recorded in maxBranchingCards."
        };
        Dictionary<string, CardValueEntry> cardEntries = new(StringComparer.OrdinalIgnoreCase);
        List<TrainingCardWarning> warnings = [];
        if (resume && File.Exists(outputPath))
        {
            TrainingCardValueOutput? existingOutput =
                JsonSerializer.Deserialize<TrainingCardValueOutput>(File.ReadAllText(outputPath), jsonOptions);
            if (existingOutput is not null
                && existingOutput.SchemaVersion == TrainingCardValueOutput.CurrentSchemaVersion
                && existingOutput.Training.RunsPerDeck == runs
                && existingOutput.TrainingDeckOffset == skipDecks
                && existingOutput.TrainingDeckLimit == limitDecks
                && existingOutput.TrainingDeckCount == preparedDecks.Count)
            {
                cardEntries = new Dictionary<string, CardValueEntry>(existingOutput.Cards, StringComparer.OrdinalIgnoreCase);
                warnings = existingOutput.Warnings.ToList();
                metadata = metadata with { GeneratedAt = existingOutput.Training.GeneratedAt };
                Console.WriteLine($"resuming from {cardEntries.Count} completed cards in {outputPath}");
            }
            else
            {
                Console.WriteLine($"resume ignored: existing output does not match schemaVersion={TrainingCardValueOutput.CurrentSchemaVersion}, runs={runs}, skipDecks={skipDecks}, limitDecks={limitDecks?.ToString() ?? "<none>"}, and deckCount={preparedDecks.Count}.");
            }
        }

        object outputLock = new();
        int completed = cardEntries.Count;
        IReadOnlyList<TrainingCandidate> remainingCandidates = candidates
            .Where(candidate => !cardEntries.ContainsKey(candidate.ModelId))
            .ToArray();
        Action<TrainingCandidate> trainOne = candidate =>
        {
            TrainingHorizonValues unupgraded = EstimateCandidateForm(
                candidate.Unupgraded,
                horizons,
                preparedDecks,
                librariesByLayer,
                generatedCardPools,
                baselines,
                runs,
                seed,
                handSize,
                maxHandSize,
                baseEnergy,
                baseStars,
                maxCardsPlayed,
                maxBranchingCards,
                profile,
                degreeOfParallelism);
            TrainingHorizonValues upgraded = candidate.Upgraded is null
                ? new TrainingHorizonValues()
                : EstimateCandidateForm(
                    candidate.Upgraded,
                    horizons,
                    preparedDecks,
                    librariesByLayer,
                    generatedCardPools,
                    baselines,
                    runs,
                    seed,
                    handSize,
                    maxHandSize,
                    baseEnergy,
                    baseStars,
                    maxCardsPlayed,
                    maxBranchingCards,
                    profile,
                    degreeOfParallelism);
            CardValueEntry entry = new()
            {
                TypeName = candidate.TypeName,
                Pools = candidate.Pools,
                TrainingValues = new CardTrainingValues
                {
                    Unupgraded = unupgraded,
                    Upgraded = upgraded
                },
                Note = "Deck-level delta EV averaged across the Dashen 100-deck Regent training set."
            };
            List<TrainingCardWarning> candidateWarnings = [];
            AddCandidateWarnings(candidateWarnings, candidate.Unupgraded);
            if (candidate.Upgraded is not null)
            {
                AddCandidateWarnings(candidateWarnings, candidate.Upgraded);
            }

            lock (outputLock)
            {
                cardEntries[candidate.ModelId] = entry;
                warnings.AddRange(candidateWarnings);
                completed++;
                WriteTrainingOutput(outputPath, metadata, trainingDecksPath, skipDecks, limitDecks, candidates.Count, preparedDecks.Count, cardEntries, warnings, jsonOptions);
                Console.WriteLine($"trained {completed}/{candidates.Count}: {candidate.ModelId} {candidate.TypeName}");
            }
        };

        foreach (TrainingCandidate candidate in remainingCandidates)
        {
            trainOne(candidate);
        }

        WriteTrainingOutput(outputPath, metadata, trainingDecksPath, skipDecks, limitDecks, candidates.Count, preparedDecks.Count, cardEntries, warnings, jsonOptions);

        if (writeConfig)
        {
            CardValueConfig existing = File.Exists(configPath)
                ? CardValueConfigLoader.LoadFromFile(configPath)
                : CardValueConfig.CreateDefault();
            CardValueConfig config = existing with
            {
                SchemaVersion = CardValueConfig.SupportedSchemaVersion,
                Training = metadata,
                Cards = cardEntries
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            };
            string? parent = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            WriteTextWithRetry(configPath, CardValueConfigLoader.ToJson(config));
        }

        Console.WriteLine("training card value simulation complete");
        Console.WriteLine($"trainingDecks: {preparedDecks.Count}");
        Console.WriteLine($"skipDecks: {skipDecks}");
        Console.WriteLine($"limitDecks: {limitDecks?.ToString() ?? "<none>"}");
        Console.WriteLine($"candidates: {candidates.Count}");
        Console.WriteLine($"runsPerDeck: {runs}");
        Console.WriteLine($"degreeOfParallelism: {degreeOfParallelism}");
        Console.WriteLine($"output: {outputPath}");
        if (writeConfig)
        {
            Console.WriteLine($"config: {configPath}");
        }

        return 0;
    }

    private static TrainingHorizonValues EstimateCandidateForm(
        SimulationCard candidate,
        IReadOnlyList<TrainingValueHorizonSpec> horizons,
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        IReadOnlyDictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer,
        GeneratedCardPoolCatalog generatedCardPools,
        IReadOnlyDictionary<(int DeckIndex, TrainingValueHorizon Horizon), decimal> baselines,
        int runs,
        int seed,
        int handSize,
        int maxHandSize,
        int baseEnergy,
        int baseStars,
        int maxCardsPlayed,
        int maxBranchingCards,
        bool profile,
        int deckDegreeOfParallelism)
    {
        decimal[] sums = new decimal[horizons.Count];
        object sumsLock = new();
        int completedDecks = 0;
        Action<PreparedTrainingDeck> estimateOneDeck = deck =>
        {
            SimulationCard layerCandidate = ResolveLayerCard(candidate, librariesByLayer[deck.Layer]);
            DeckSimulationOptions options = BuildTrainingOptions(
                horizons.Max(horizon => horizon.Turns),
                runs,
                seed,
                deck.Index,
                handSize,
                maxHandSize,
                baseEnergy,
                baseStars,
                maxCardsPlayed,
                maxBranchingCards,
                librariesByLayer[deck.Layer],
                generatedCardPools);
            Stopwatch? stopwatch = profile ? Stopwatch.StartNew() : null;
            DeckMonteCarloSimulator simulator = new();
            IReadOnlyList<decimal> withCandidate = simulator.SimulateExpectedTurnValues([.. deck.Cards, layerCandidate], options);
            decimal[] deltas = new decimal[horizons.Count];
            for (int index = 0; index < horizons.Count; index++)
            {
                TrainingValueHorizonSpec horizon = horizons[index];
                decimal delta = CumulativeExpectedValue(withCandidate, horizon.Turns)
                    - baselines[(deck.Index, horizon.Horizon)];
                deltas[index] = delta;
            }

            lock (sumsLock)
            {
                for (int index = 0; index < deltas.Length; index++)
                {
                    sums[index] += deltas[index];
                }
            }

            int done = Interlocked.Increment(ref completedDecks);
            if (stopwatch is not null)
            {
                Console.WriteLine($"candidate={candidate.ModelId} deck={deck.Index} group={deck.Group} layer={deck.Layer} cards={deck.Cards.Count + 1} elapsedSeconds={stopwatch.Elapsed.TotalSeconds:0.###}");
            }
            else if (done % 10 == 0 || done == preparedDecks.Count)
            {
                Console.WriteLine($"candidate={candidate.ModelId} decks {done}/{preparedDecks.Count}");
            }
        };

        if (deckDegreeOfParallelism <= 1)
        {
            foreach (PreparedTrainingDeck deck in preparedDecks)
            {
                estimateOneDeck(deck);
            }
        }
        else
        {
            Parallel.ForEach(
                preparedDecks,
                new ParallelOptions { MaxDegreeOfParallelism = deckDegreeOfParallelism },
                estimateOneDeck);
        }

        Dictionary<TrainingValueHorizon, decimal> values = [];
        for (int index = 0; index < horizons.Count; index++)
        {
            TrainingValueHorizonSpec horizon = horizons[index];
            values[horizon.Horizon] = Round(sums[index] / preparedDecks.Count);
        }

        return new TrainingHorizonValues
        {
            Shortline = (double)values[TrainingValueHorizon.Shortline],
            Midline = (double)values[TrainingValueHorizon.Midline],
            Longline = (double)values[TrainingValueHorizon.Longline]
        };
    }

    private static void WriteTrainingOutput(
        string outputPath,
        TrainingValueMetadata metadata,
        string trainingDecksPath,
        int trainingDeckOffset,
        int? trainingDeckLimit,
        int candidateCount,
        int trainingDeckCount,
        IReadOnlyDictionary<string, CardValueEntry> cardEntries,
        IReadOnlyList<TrainingCardWarning> warnings,
        JsonSerializerOptions jsonOptions)
    {
        TrainingCardValueOutput output = new(
            TrainingCardValueOutput.CurrentSchemaVersion,
            metadata,
            trainingDecksPath,
            trainingDeckOffset,
            trainingDeckLimit,
            candidateCount,
            trainingDeckCount,
            cardEntries
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            warnings
                .Distinct()
                .OrderBy(warning => warning.ModelId, StringComparer.Ordinal)
                .ThenBy(warning => warning.Warning, StringComparer.Ordinal)
                .ToArray());
        WriteTextWithRetry(outputPath, JsonSerializer.Serialize(output, jsonOptions));
    }

    private static void WriteTextWithRetry(string path, string content)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, content);
                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(150 * attempt));
            }
        }
    }

    private static DeckSimulationOptions BuildTrainingOptions(
        int turns,
        int runs,
        int seed,
        int deckIndex,
        int handSize,
        int maxHandSize,
        int baseEnergy,
        int baseStars,
        int maxCardsPlayed,
        int maxBranchingCards,
        IReadOnlyList<SimulationCard> library,
        GeneratedCardPoolCatalog generatedCardPools,
        int runDegreeOfParallelism = 1)
    {
        return new DeckSimulationOptions
        {
            Turns = turns,
            Runs = runs,
            RunDegreeOfParallelism = runDegreeOfParallelism,
            Seed = seed + deckIndex * 1009 + turns * 17,
            HandSize = handSize,
            MaxHandSize = maxHandSize,
            BaseEnergy = baseEnergy,
            BaseStars = baseStars,
            StarsPersistBetweenTurns = true,
            MaxCardsPlayedPerTurn = maxCardsPlayed,
            MaxBranchingCards = maxBranchingCards,
            CardLibrary = library,
            GeneratedCardPools = generatedCardPools
        };
    }

    private static PreparedTrainingDeck PrepareTrainingDeck(
        TrainingDeck deck,
        int index,
        IReadOnlyDictionary<int, Dictionary<string, SimulationCard>> byModelIdByLayer,
        IReadOnlyDictionary<int, Dictionary<string, SimulationCard>> byTypeNameByLayer)
    {
        int layer = TrainingLayer(deck);
        Dictionary<string, SimulationCard> byModelId = byModelIdByLayer[layer];
        Dictionary<string, SimulationCard> byTypeName = byTypeNameByLayer[layer];
        List<SimulationCard> cards = [];
        foreach (TrainingDeckCard card in deck.Cards)
        {
            string modelId = card.Upgrade > 0 ? $"{card.Id}+{card.Upgrade}" : card.Id;
            SimulationCard simulationCard = byModelId.TryGetValue(modelId, out SimulationCard? modelMatch)
                ? modelMatch
                : byTypeName.TryGetValue(card.TypeName, out SimulationCard? typeMatch)
                    ? typeMatch
                    : throw new InvalidOperationException($"Training deck {deck.RunId} card {modelId}/{card.TypeName} was not found in simulation library.");
            for (int i = 0; i < card.Count; i++)
            {
                cards.Add(simulationCard);
            }
        }

        return new PreparedTrainingDeck(index, deck.RunId, deck.Group, layer, cards);
    }

    private static int TrainingLayer(TrainingDeck deck)
    {
        if (deck.Floor.HasValue)
        {
            return deck.Floor.Value;
        }

        return deck.Group switch
        {
            "floor8" => 8,
            "act2Start" => 17,
            "final" => 47,
            _ => 17
        };
    }

    private static IReadOnlyList<TrainingCandidate> SelectTrainingCandidates(IReadOnlyList<SimulationCard> library)
    {
        Dictionary<string, SimulationCard> byModelId = library
            .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return library
            .Where(card => card.UpgradeLevel == 0)
            .Where(card => card.Pools.Any(pool => pool is "Regent" or "Colorless"))
            .OrderBy(card => card.ModelId, StringComparer.Ordinal)
            .Select(card =>
            {
                byModelId.TryGetValue($"{card.ModelId}+1", out SimulationCard? upgraded);
                return new TrainingCandidate(
                    card.ModelId,
                    card.TypeName,
                    card.Pools,
                    card,
                    upgraded);
            })
            .ToArray();
    }

    private static SimulationCard ResolveLayerCard(SimulationCard candidate, IReadOnlyList<SimulationCard> layerLibrary)
    {
        return layerLibrary.FirstOrDefault(card => string.Equals(card.ModelId, candidate.ModelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Candidate {candidate.ModelId} was not found in the layer-specific simulation library.");
    }

    private static void AddCandidateWarnings(List<TrainingCardWarning> warnings, SimulationCard card)
    {
        foreach (string warning in card.Warnings.Where(warning => warning.StartsWith("Unsupported simulation action", StringComparison.Ordinal)))
        {
            warnings.Add(new TrainingCardWarning(card.ModelId, card.TypeName, warning));
        }
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    private static decimal CumulativeExpectedValue(IReadOnlyList<decimal> expectedTurnValues, int turns)
    {
        if (expectedTurnValues.Count < turns)
        {
            throw new InvalidOperationException($"Simulation result only has {expectedTurnValues.Count} turns; cannot read {turns}-turn cumulative value.");
        }

        return Round(expectedTurnValues.Take(turns).Sum());
    }

    private static JsonSerializerOptions CreateTrainingJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new TrainingValueHorizonJsonConverter());
        options.Converters.Add(new LayeredValueTableJsonConverter());
        return options;
    }

    private sealed record TrainingValueHorizonSpec(
        TrainingValueHorizon Horizon,
        string Key,
        int Turns);

    private sealed record PreparedTrainingDeck(
        int Index,
        string RunId,
        string Group,
        int Layer,
        IReadOnlyList<SimulationCard> Cards);

    private sealed record TrainingCandidate(
        string ModelId,
        string TypeName,
        IReadOnlyList<string> Pools,
        SimulationCard Unupgraded,
        SimulationCard? Upgraded);

    private sealed record TrainingCardValueOutput(
        int SchemaVersion,
        TrainingValueMetadata Training,
        string TrainingDecksPath,
        int TrainingDeckOffset,
        int? TrainingDeckLimit,
        int CandidateCount,
        int TrainingDeckCount,
        IReadOnlyDictionary<string, CardValueEntry> Cards,
        IReadOnlyList<TrainingCardWarning> Warnings)
    {
        public const int CurrentSchemaVersion = 3;
    }

    private sealed record TrainingCardWarning(
        string ModelId,
        string TypeName,
        string Warning);

    private sealed record TrainingDeckFile(
        IReadOnlyList<TrainingDeck> Decks);

    private sealed record TrainingDeck(
        string RunId,
        string Group,
        int? Floor,
        IReadOnlyList<TrainingDeckCard> Cards);

    private sealed record TrainingDeckCard(
        string Id,
        string TypeName,
        int Upgrade,
        int Count);
}
