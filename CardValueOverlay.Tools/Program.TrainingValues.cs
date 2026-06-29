using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_16_decks.json");
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string setupPrioritiesPath = GetOption(args, "--setup-priorities")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
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
        string? candidateFilter = GetOption(args, "--candidate");
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);
        bool resume = HasFlag(args, "--resume");
        bool profile = HasFlag(args, "--profile");
        bool writeConfig = !HasFlag(args, "--no-write-config") || HasFlag(args, "--write-config");
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        string trainingOutputRoot = Path.Combine(generatedRoot, "training_card_values");
        Directory.CreateDirectory(trainingOutputRoot);
        string latestOutputPath = Path.Combine(trainingOutputRoot, "latest.generated.json");
        string outputPath = GetOption(args, "--output-json") ?? latestOutputPath;

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
        SimulationSetupPriorityCatalog setupPriorities = LoadOptionalSimulationSetupPriorities(setupPrioritiesPath, jsonOptions);
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
            layer => new SimulationCardLibraryBuilder().Build(
                entries,
                calibration,
                layer,
                includeUpgrades: true,
                memberships,
                setupPriorities));
        IReadOnlyList<TrainingCandidate> candidates = SelectTrainingCandidates(librariesByLayer[layers[0]]);
        if (!string.IsNullOrWhiteSpace(candidateFilter))
        {
            candidates = candidates
                .Where(candidate =>
                    string.Equals(candidate.ModelId, candidateFilter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.TypeName, candidateFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        candidates = candidates
            .Take(limitCards ?? int.MaxValue)
            .ToArray();
        if (candidates.Count == 0)
        {
            return Fail(string.IsNullOrWhiteSpace(candidateFilter)
                ? "No Regent or Colorless candidate cards were found in the simulation library."
                : $"Candidate {candidateFilter} was not found in the simulation library.");
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
                baselineRunDegreeOfParallelism,
                searchCardScorer);
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
            Source = "dashen_77_selected_16",
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
                HashSet<string> candidateModelIds = candidates
                    .Select(candidate => candidate.ModelId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                cardEntries = cardEntries
                    .Where(pair => candidateModelIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
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
                degreeOfParallelism,
                searchCardScorer);
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
                    degreeOfParallelism,
                    searchCardScorer);
            CardValueEntry entry = new()
            {
                TypeName = candidate.TypeName,
                Pools = candidate.Pools,
                TrainingValues = new CardTrainingValues
                {
                    Unupgraded = unupgraded,
                    Upgraded = upgraded
                },
                Note = "Deck-level delta EV averaged across the Dashen small Regent training set."
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
        string archivePath = ArchiveTrainingOutput(outputPath, latestOutputPath, BuildTrainingArchivePath(trainingOutputRoot, metadata));

        if (writeConfig)
        {
            WriteRuntimeConfigFromTrainingOutput(latestOutputPath, configPath, jsonOptions);
        }

        Console.WriteLine("training card value simulation complete");
        Console.WriteLine($"trainingDecks: {preparedDecks.Count}");
        Console.WriteLine($"skipDecks: {skipDecks}");
        Console.WriteLine($"limitDecks: {limitDecks?.ToString() ?? "<none>"}");
        Console.WriteLine($"candidates: {candidates.Count}");
        Console.WriteLine($"candidateFilter: {candidateFilter ?? "<none>"}");
        Console.WriteLine($"runsPerDeck: {runs}");
        Console.WriteLine($"degreeOfParallelism: {degreeOfParallelism}");
        Console.WriteLine($"searchPolicy: {(searchCardScorer is null ? "heuristic" : "neural")}");
        Console.WriteLine($"output: {outputPath}");
        Console.WriteLine($"latest: {latestOutputPath}");
        Console.WriteLine($"archive: {archivePath}");
        if (writeConfig)
        {
            Console.WriteLine($"config: {configPath}");
        }

        return 0;
    }

    private static int InstallTrainingValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        string trainingOutputRoot = Path.Combine(generatedRoot, "training_card_values");
        Directory.CreateDirectory(trainingOutputRoot);
        string latestOutputPath = Path.Combine(trainingOutputRoot, "latest.generated.json");
        string inputPath = GetOption(args, "--input") ?? latestOutputPath;
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;

        if (!File.Exists(inputPath))
        {
            return Fail($"Missing training values at {inputPath}.");
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        TrainingCardValueOutput output = ReadTrainingOutput(inputPath, jsonOptions);
        string archivePath = ArchiveTrainingOutput(inputPath, latestOutputPath, BuildTrainingArchivePath(trainingOutputRoot, output.Training));
        WriteRuntimeConfigFromTrainingOutput(latestOutputPath, configPath, jsonOptions);

        Console.WriteLine("training values installed");
        Console.WriteLine($"input: {inputPath}");
        Console.WriteLine($"latest: {latestOutputPath}");
        Console.WriteLine($"archive: {archivePath}");
        Console.WriteLine($"config: {configPath}");
        Console.WriteLine($"source: {output.Training.Source}");
        Console.WriteLine($"deckCount: {output.Training.DeckCount}");
        Console.WriteLine($"runsPerDeck: {output.Training.RunsPerDeck}");
        Console.WriteLine($"cards: {output.Cards.Count}");
        return 0;
    }

    private static int InstallPlayValueEstimates(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;
        int layer = GetIntOption(args, "--layer") ?? 17;

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        string trainingOutputRoot = Path.Combine(generatedRoot, "training_card_values");
        Directory.CreateDirectory(trainingOutputRoot);
        string latestOutputPath = Path.Combine(trainingOutputRoot, "latest.generated.json");
        string outputPath = GetOption(args, "--output-json") ?? latestOutputPath;

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(membershipsPath))
        {
            return Fail($"Missing card pool memberships at {membershipsPath}. Run parse-card-pools first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships =
            JsonSerializer.Deserialize<List<CardPoolMembershipEntry>>(File.ReadAllText(membershipsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card pool memberships from {membershipsPath}.");
        Dictionary<string, CardPoolMembershipEntry> membershipsByModelId = memberships
            .GroupBy(membership => membership.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<CardFactCatalogEntry> candidates = entries
            .Where(entry => IsRuntimePlayValueCandidate(entry, membershipsByModelId))
            .OrderBy(entry => entry.ModelId, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Count == 0)
        {
            return Fail("No Regent or Colorless non-multiplayer candidates were found.");
        }

        ValueCalibration baseCalibration = ValueCalibration.Load(calibrationPath);
        RuntimeResourceReference[] horizons =
        [
            new(TrainingValueHorizon.Shortline, "shortline", 4, Draw: 5.1m, Energy: 8.8m, Star: 2.7m),
            new(TrainingValueHorizon.Midline, "midline", 8, Draw: 5.2m, Energy: 10.0m, Star: 5.3m),
            new(TrainingValueHorizon.Longline, "longline", 14, Draw: 5.1m, Energy: 11.2m, Star: 6.3m)
        ];
        Dictionary<TrainingValueHorizon, Dictionary<string, CardValueEstimate>> estimatesByHorizon = [];
        CardValueEstimator estimator = new();
        foreach (RuntimeResourceReference horizon in horizons)
        {
            ValueCalibration horizonCalibration = BuildRuntimePlayValueCalibration(baseCalibration, horizon);
            estimatesByHorizon[horizon.Horizon] = candidates
                .Select(entry => estimator.Estimate(entry, horizonCalibration, layer))
                .ToDictionary(estimate => estimate.ModelId, StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, CardValueEntry> cardEntries = new(StringComparer.OrdinalIgnoreCase);
        List<TrainingCardWarning> warnings = [];
        foreach (CardFactCatalogEntry candidate in candidates)
        {
            CardValueEstimate shortline = estimatesByHorizon[TrainingValueHorizon.Shortline][candidate.ModelId];
            CardValueEstimate midline = estimatesByHorizon[TrainingValueHorizon.Midline][candidate.ModelId];
            CardValueEstimate longline = estimatesByHorizon[TrainingValueHorizon.Longline][candidate.ModelId];
            CardPoolMembershipEntry membership = membershipsByModelId[candidate.ModelId];
            cardEntries[candidate.ModelId] = new CardValueEntry
            {
                TypeName = candidate.TypeName,
                Pools = membership.Pools,
                TrainingValues = new CardTrainingValues
                {
                    Unupgraded = new TrainingHorizonValues
                    {
                        Shortline = (double)shortline.EstimatedValue,
                        Midline = (double)midline.EstimatedValue,
                        Longline = (double)longline.EstimatedValue
                    },
                    Upgraded = new TrainingHorizonValues
                    {
                        Shortline = (double)shortline.UpgradedEstimatedValue,
                        Midline = (double)midline.UpgradedEstimatedValue,
                        Longline = (double)longline.UpgradedEstimatedValue
                    }
                },
                Note = $"Static play-value estimate from card facts at layer {layer}; concrete resources use 2026-06-28 rounded reference values."
            };

            foreach (string warning in shortline.Warnings
                .Concat(midline.Warnings)
                .Concat(longline.Warnings)
                .Distinct(StringComparer.Ordinal))
            {
                warnings.Add(new TrainingCardWarning(candidate.ModelId, candidate.TypeName, warning));
            }
        }

        TrainingValueMetadata metadata = new()
        {
            Source = $"static_play_value_layer{layer}_20260628",
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            DeckCount = 0,
            RunsPerDeck = 0,
            MaxCardsPlayedPerTurn = 0,
            MaxBranchingCards = 0,
            Horizons = horizons.ToDictionary(horizon => horizon.Key, horizon => horizon.Turns, StringComparer.OrdinalIgnoreCase),
            Note = "Runtime display values generated from card facts and model_calibration.json static play-value estimates. Defense uses one midgame layer, not runtime floor scaling. Concrete immediate and next-turn draw, energy, and star effects use the rounded 2026-06-28 resource play-value references."
        };

        WriteTrainingOutput(
            outputPath,
            metadata,
            "<static-play-value-estimates>",
            trainingDeckOffset: 0,
            trainingDeckLimit: null,
            candidateCount: candidates.Count,
            trainingDeckCount: 0,
            cardEntries,
            warnings,
            jsonOptions);
        string archivePath = ArchiveTrainingOutput(outputPath, latestOutputPath, BuildTrainingArchivePath(trainingOutputRoot, metadata));
        WriteRuntimeConfigFromTrainingOutput(latestOutputPath, configPath, jsonOptions);

        Console.WriteLine("play-value estimates installed");
        Console.WriteLine($"layer: {layer}");
        Console.WriteLine($"candidates: {candidates.Count}");
        Console.WriteLine($"warnings: {warnings.Select(warning => (warning.ModelId, warning.Warning)).Distinct().Count()}");
        Console.WriteLine($"output: {outputPath}");
        Console.WriteLine($"latest: {latestOutputPath}");
        Console.WriteLine($"archive: {archivePath}");
        Console.WriteLine($"config: {configPath}");
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
        int deckDegreeOfParallelism,
        ISearchCardScorer? searchCardScorer)
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
                generatedCardPools,
                1,
                searchCardScorer);
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

    private static TrainingCardValueOutput ReadTrainingOutput(string path, JsonSerializerOptions jsonOptions)
    {
        TrainingCardValueOutput? output =
            JsonSerializer.Deserialize<TrainingCardValueOutput>(File.ReadAllText(path), jsonOptions);
        if (output is null)
        {
            throw new InvalidOperationException($"Failed to read training values from {path}.");
        }

        if (output.SchemaVersion != TrainingCardValueOutput.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Training values at {path} have schemaVersion={output.SchemaVersion}; expected {TrainingCardValueOutput.CurrentSchemaVersion}.");
        }

        return output;
    }

    private static void WriteRuntimeConfigFromTrainingOutput(
        string trainingOutputPath,
        string configPath,
        JsonSerializerOptions jsonOptions)
    {
        TrainingCardValueOutput output = ReadTrainingOutput(trainingOutputPath, jsonOptions);
        CardValueConfig existing = File.Exists(configPath)
            ? CardValueConfigLoader.LoadFromFile(configPath)
            : CardValueConfig.CreateDefault();
        CardValueConfig config = existing with
        {
            SchemaVersion = CardValueConfig.SupportedSchemaVersion,
            Training = output.Training,
            Cards = output.Cards
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

    private static string ArchiveTrainingOutput(string sourcePath, string latestPath, string archivePath)
    {
        CopyFileIfDifferent(sourcePath, latestPath);
        CopyFileIfDifferent(sourcePath, archivePath);
        return archivePath;
    }

    private static void CopyFileIfDifferent(string sourcePath, string targetPath)
    {
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string targetFullPath = Path.GetFullPath(targetPath);
        if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? parent = Path.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(sourceFullPath, targetFullPath, overwrite: true);
    }

    private static string BuildTrainingArchivePath(string trainingOutputRoot, TrainingValueMetadata metadata)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string source = SanitizeFileComponent(string.IsNullOrWhiteSpace(metadata.Source) ? "training" : metadata.Source);
        string fileName = string.Join(
            "_",
            timestamp,
            source,
            $"d{metadata.DeckCount}",
            $"r{metadata.RunsPerDeck}",
            $"b{metadata.MaxBranchingCards}",
            $"p{metadata.MaxCardsPlayedPerTurn}") + ".generated.json";
        return Path.Combine(trainingOutputRoot, fileName);
    }

    private static string SanitizeFileComponent(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = value
            .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static bool IsRuntimePlayValueCandidate(
        CardFactCatalogEntry entry,
        IReadOnlyDictionary<string, CardPoolMembershipEntry> membershipsByModelId)
    {
        if (!membershipsByModelId.TryGetValue(entry.ModelId, out CardPoolMembershipEntry? membership))
        {
            return false;
        }

        return membership.Pools.Any(pool => pool is "Regent" or "Colorless")
            && !membership.IsMultiplayerOnly
            && !HasMultiplayerTarget(entry);
    }

    private static bool HasMultiplayerTarget(CardFactCatalogEntry entry)
    {
        return IsAllyTarget(entry.TargetType)
            || entry.Actions.Any(action => IsAllyTarget(action.TargetType));
    }

    private static bool IsAllyTarget(string? targetType)
    {
        return targetType is not null
            && targetType.Contains("Ally", StringComparison.OrdinalIgnoreCase);
    }

    private static ValueCalibration BuildRuntimePlayValueCalibration(
        ValueCalibration source,
        RuntimeResourceReference reference)
    {
        Dictionary<string, decimal> resourceValues = new(source.ResourceValues, StringComparer.OrdinalIgnoreCase)
        {
            ["draw"] = reference.Draw,
            ["energy"] = reference.Energy,
            ["star"] = reference.Star,
            ["nextTurnDrawMultiplier"] = 1m,
            ["nextTurnEnergyMultiplier"] = 1m,
            ["nextTurnStarMultiplier"] = 1m,
            ["selfHpLossPenalty"] = 1.5m
        };

        return new ValueCalibration
        {
            SchemaVersion = source.SchemaVersion,
            LayerBreakpoints = source.LayerBreakpoints.ToArray(),
            BaselineCardValues = new Dictionary<string, decimal>(source.BaselineCardValues, StringComparer.OrdinalIgnoreCase),
            BlockToDamage = new Dictionary<string, decimal>(source.BlockToDamage, StringComparer.OrdinalIgnoreCase),
            DefensePressure = new Dictionary<string, decimal>(source.DefensePressure, StringComparer.OrdinalIgnoreCase),
            ExpectedCombatTurns = new Dictionary<string, decimal>(source.ExpectedCombatTurns, StringComparer.OrdinalIgnoreCase),
            EnergyDrawExchange = new Dictionary<string, decimal>(source.EnergyDrawExchange, StringComparer.OrdinalIgnoreCase),
            TargetingPenalties = new Dictionary<string, decimal>(source.TargetingPenalties, StringComparer.OrdinalIgnoreCase),
            DamageUnitValue = new Dictionary<string, decimal>(source.DamageUnitValue, StringComparer.OrdinalIgnoreCase),
            ResourceValues = resourceValues,
            PowerValues = new Dictionary<string, decimal>(source.PowerValues, StringComparer.OrdinalIgnoreCase),
            KeywordValues = new Dictionary<string, decimal>(source.KeywordValues, StringComparer.OrdinalIgnoreCase),
            ScalingAssumptions = new Dictionary<string, decimal>(source.ScalingAssumptions, StringComparer.OrdinalIgnoreCase),
            DebuffStackMultipliers = new Dictionary<string, decimal>(source.DebuffStackMultipliers, StringComparer.OrdinalIgnoreCase),
            WeakValueParameters = new Dictionary<string, decimal>(source.WeakValueParameters, StringComparer.OrdinalIgnoreCase),
            VulnerableValueParameters = new Dictionary<string, decimal>(source.VulnerableValueParameters, StringComparer.OrdinalIgnoreCase)
        };
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
        int runDegreeOfParallelism = 1,
        ISearchCardScorer? searchCardScorer = null,
        SearchPolicyDataCollector? searchPolicyCollector = null,
        string searchPolicySource = "simulation",
        SearchPolicyGroupMetadata? searchPolicyMetadata = null)
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
            GeneratedCardPools = generatedCardPools,
            SearchCardScorer = searchCardScorer,
            SearchPolicyCollector = searchPolicyCollector,
            SearchPolicySource = searchPolicySource,
            SearchPolicyMetadata = searchPolicyMetadata
        };
    }

    private static SimulationSetupPriorityCatalog LoadOptionalSimulationSetupPriorities(
        string path,
        JsonSerializerOptions jsonOptions)
    {
        return SimulationSetupPriorityCatalog.LoadOrEmpty(path, jsonOptions);
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
        return options;
    }

    private sealed record TrainingValueHorizonSpec(
        TrainingValueHorizon Horizon,
        string Key,
        int Turns);

    private sealed record RuntimeResourceReference(
        TrainingValueHorizon Horizon,
        string Key,
        int Turns,
        decimal Draw,
        decimal Energy,
        decimal Star);

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
