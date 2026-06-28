using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private const int Floor8PlayValueSchemaVersion = 1;

    private static int EstimateFloor8PlayValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string deckSourcePath = GetOption(args, "--deck-source")
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_100_decks.json");
        string selectedDecksPath = GetOption(args, "--selected-decks-output")
            ?? Path.Combine("history-analysis", "data", "dashen_77_floor8_random_16_decks.json");
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        string deckGroup = GetOption(args, "--deck-group") ?? "floor8";
        int deckCount = GetIntOption(args, "--deck-count") ?? 16;
        int deckSeed = GetIntOption(args, "--deck-seed") ?? 20260629;
        int runs = GetIntOption(args, "--runs") ?? 400;
        int seed = GetIntOption(args, "--seed") ?? 1;
        int turns = GetIntOption(args, "--turns") ?? 8;
        int shortTurns = GetIntOption(args, "--short-turns") ?? 4;
        int midTurns = GetIntOption(args, "--mid-turns") ?? 8;
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays") ?? 8;
        int maxBranchingCards = GetIntOption(args, "--max-branch") ?? 4;
        int? limitDecks = GetIntOption(args, "--limit-decks");
        int? limitForms = GetIntOption(args, "--limit-forms");
        int skipForms = Math.Max(0, GetIntOption(args, "--skip-forms") ?? 0);
        int degreeOfParallelism = Math.Max(1, GetIntOption(args, "--degree-of-parallelism") ?? 1);
        bool resume = HasFlag(args, "--resume");
        bool profile = HasFlag(args, "--profile");
        string? candidateFilter = GetOption(args, "--candidate");
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);

        if (deckCount <= 0)
        {
            return Fail("--deck-count must be positive.");
        }

        if (runs <= 0)
        {
            return Fail("--runs must be positive.");
        }

        if (turns < midTurns || midTurns < shortTurns || shortTurns <= 0)
        {
            return Fail("--turns must be >= --mid-turns, --mid-turns must be >= --short-turns, and --short-turns must be positive.");
        }

        if (!File.Exists(deckSourcePath))
        {
            return Fail($"Missing deck source file at {deckSourcePath}.");
        }

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        string floor8OutputRoot = Path.Combine(generatedRoot, "floor8_play_values");
        Directory.CreateDirectory(floor8OutputRoot);
        string latestJsonPath = Path.Combine(floor8OutputRoot, "latest.generated.json");
        string latestReportPath = Path.Combine(floor8OutputRoot, "latest.generated.md");
        string outputJsonPath = GetOption(args, "--output-json") ?? latestJsonPath;
        string outputReportPath = GetOption(args, "--output-md") ?? Path.ChangeExtension(outputJsonPath, ".md");

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        TrainingDeckFile sourceDeckFile =
            JsonSerializer.Deserialize<TrainingDeckFile>(File.ReadAllText(deckSourcePath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read deck source from {deckSourcePath}.");
        IReadOnlyList<TrainingDeck> selectedDecks = SelectRandomTrainingDecks(sourceDeckFile.Decks, deckGroup, deckCount, deckSeed);
        WriteSelectedFloor8Decks(selectedDecksPath, selectedDecks, jsonOptions);
        IReadOnlyList<TrainingDeck> activeDecks = selectedDecks
            .Take(limitDecks ?? int.MaxValue)
            .ToArray();
        if (activeDecks.Count == 0)
        {
            return Fail("No selected floor8 decks are active after --limit-decks.");
        }

        int[] layers = activeDecks
            .Select(TrainingLayer)
            .Distinct()
            .Order()
            .ToArray();
        Dictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer = layers.ToDictionary(
            layer => layer,
            layer => new SimulationCardLibraryBuilder().Build(entries, calibration, layer, includeUpgrades: true, memberships));
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
        List<PreparedTrainingDeck> preparedDecks = activeDecks
            .Select((deck, index) => PrepareTrainingDeck(deck, index, byModelIdByLayer, byTypeNameByLayer))
            .ToList();

        Dictionary<string, CardFactCatalogEntry> factsByBaseModelId = entries
            .GroupBy(entry => BaseModelId(entry.ModelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CardPoolMembershipEntry> membershipsByModelId = memberships
            .GroupBy(membership => membership.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<TrainingCandidate> baseCandidates = SelectTrainingCandidates(librariesByLayer[layers[0]])
            .Where(candidate => factsByBaseModelId.ContainsKey(candidate.ModelId))
            .Where(candidate => IsRuntimePlayValueCandidate(factsByBaseModelId[candidate.ModelId], membershipsByModelId))
            .OrderBy(candidate => candidate.ModelId, StringComparer.Ordinal)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(candidateFilter))
        {
            baseCandidates = baseCandidates
                .Where(candidate =>
                    string.Equals(candidate.ModelId, candidateFilter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.TypeName, candidateFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        IReadOnlyList<Floor8CandidateForm> allForms = BuildFloor8CandidateForms(baseCandidates);
        IReadOnlyList<Floor8CandidateForm> eligibleForms = allForms
            .Where(form => form.Eligible)
            .Skip(skipForms)
            .Take(limitForms ?? int.MaxValue)
            .ToArray();
        if (eligibleForms.Count == 0)
        {
            return Fail(string.IsNullOrWhiteSpace(candidateFilter)
                ? "No eligible floor8 play-value forms were found."
                : $"Candidate {candidateFilter} has no eligible floor8 play-value forms.");
        }

        Floor8PlayValueMetadata metadata = new(
            "floor8_direct_play_value_20260629",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            deckSourcePath,
            selectedDecksPath,
            deckGroup,
            deckCount,
            deckSeed,
            preparedDecks.Count,
            runs,
            seed,
            maxCardsPlayed,
            maxBranchingCards,
            turns,
            shortTurns,
            midTurns,
            skipForms,
            limitForms,
            "Each eligible non-multiplayer Regent/Colorless card form is added as one probe copy to each selected floor8 deck. Values are weighted credited value per direct play from simulator source attribution, using 4-turn and 8-turn prefixes from the same 8-turn simulation.");
        Dictionary<string, Floor8CardPlayValueOutput> completedCards = [];
        List<Floor8ExcludedFormOutput> excludedForms = allForms
            .Where(form => !form.Eligible)
            .Select(form => new Floor8ExcludedFormOutput(
                form.BaseModelId,
                form.ModelId,
                form.TypeName,
                form.UpgradeLevel,
                form.ExclusionReasons))
            .ToList();
        List<Floor8DeckBaselineOutput> baselines = RunFloor8Baselines(
            preparedDecks,
            librariesByLayer,
            generatedCardPools,
            turns,
            runs,
            seed,
            handSize,
            maxHandSize,
            baseEnergy,
            baseStars,
            maxCardsPlayed,
            maxBranchingCards,
            searchCardScorer,
            profile);
        List<string> warnings = [];

        if (resume && File.Exists(outputJsonPath))
        {
            Floor8PlayValueOutput? existing =
                JsonSerializer.Deserialize<Floor8PlayValueOutput>(File.ReadAllText(outputJsonPath), jsonOptions);
            if (existing is not null
                && existing.SchemaVersion == Floor8PlayValueSchemaVersion
                && existing.Metadata.Runs == runs
                && existing.Metadata.ActiveDeckCount == preparedDecks.Count
                && existing.Metadata.MaxBranchingCards == maxBranchingCards
                && existing.Metadata.Turns == turns
                && existing.Metadata.ShortTurns == shortTurns
                && existing.Metadata.MidTurns == midTurns)
            {
                completedCards = new Dictionary<string, Floor8CardPlayValueOutput>(existing.Cards, StringComparer.OrdinalIgnoreCase);
                HashSet<string> activeBaseIds = eligibleForms
                    .Select(form => form.BaseModelId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                completedCards = completedCards
                    .Where(pair => activeBaseIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                warnings = existing.Warnings.ToList();
                metadata = metadata with { GeneratedAt = existing.Metadata.GeneratedAt };
                Console.WriteLine($"resuming from {completedCards.Count} completed card entries in {outputJsonPath}");
            }
            else
            {
                Console.WriteLine("resume ignored: existing floor8 play-value output does not match current run shape.");
            }
        }

        ConcurrentDictionary<string, Floor8CardPlayValueOutput> concurrentCards =
            new(completedCards, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<IGrouping<string, Floor8CandidateForm>> remainingGroups = eligibleForms
            .GroupBy(form => form.BaseModelId, StringComparer.OrdinalIgnoreCase)
            .Where(group => !concurrentCards.ContainsKey(group.Key))
            .ToArray();
        object writeLock = new();
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        int completed = concurrentCards.Count;
        Action<IGrouping<string, Floor8CandidateForm>> estimateCard = group =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Floor8CandidateForm? unupgradedForm = group.FirstOrDefault(form => form.UpgradeLevel == 0);
            Floor8CandidateForm? upgradedForm = group.FirstOrDefault(form => form.UpgradeLevel > 0);
            Floor8FormPlayValueOutput? unupgraded = unupgradedForm is null
                ? null
                : EstimateFloor8Form(
                    unupgradedForm,
                    preparedDecks,
                    librariesByLayer,
                    generatedCardPools,
                    turns,
                    shortTurns,
                    midTurns,
                    runs,
                    seed,
                    handSize,
                    maxHandSize,
                    baseEnergy,
                    baseStars,
                    maxCardsPlayed,
                    maxBranchingCards,
                    searchCardScorer);
            Floor8FormPlayValueOutput? upgraded = upgradedForm is null
                ? null
                : EstimateFloor8Form(
                    upgradedForm,
                    preparedDecks,
                    librariesByLayer,
                    generatedCardPools,
                    turns,
                    shortTurns,
                    midTurns,
                    runs,
                    seed,
                    handSize,
                    maxHandSize,
                    baseEnergy,
                    baseStars,
                    maxCardsPlayed,
                    maxBranchingCards,
                    searchCardScorer);
            Floor8CandidateForm representative = group.First();
            Floor8CardPlayValueOutput cardOutput = new(
                representative.BaseModelId,
                representative.BaseTypeName,
                representative.Pools,
                unupgraded,
                upgraded);
            concurrentCards[group.Key] = cardOutput;
            int done = Interlocked.Increment(ref completed);
            if (profile || done % 10 == 0 || done == remainingGroups.Count + completedCards.Count)
            {
                Console.WriteLine($"floor8 play-value {done}/{remainingGroups.Count + completedCards.Count}: {representative.BaseModelId} {representative.BaseTypeName} elapsedSeconds={stopwatch.Elapsed.TotalSeconds:0.###}");
            }

            lock (writeLock)
            {
                WriteFloor8PlayValueOutput(
                    outputJsonPath,
                    outputReportPath,
                    metadata,
                    selectedDecks,
                    preparedDecks,
                    baseCandidates.Count,
                    allForms.Count,
                    eligibleForms.Count,
                    concurrentCards,
                    excludedForms,
                    baselines,
                    warnings,
                    jsonOptions);
            }
        };

        if (degreeOfParallelism <= 1)
        {
            foreach (IGrouping<string, Floor8CandidateForm> group in remainingGroups)
            {
                estimateCard(group);
            }
        }
        else
        {
            Parallel.ForEach(
                remainingGroups,
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                estimateCard);
        }

        totalStopwatch.Stop();
        WriteFloor8PlayValueOutput(
            outputJsonPath,
            outputReportPath,
            metadata,
            selectedDecks,
            preparedDecks,
            baseCandidates.Count,
            allForms.Count,
            eligibleForms.Count,
            concurrentCards,
            excludedForms,
            baselines,
            warnings,
            jsonOptions);
        string archiveJsonPath = BuildFloor8PlayValueArchivePath(floor8OutputRoot, metadata, ".json");
        string archiveReportPath = BuildFloor8PlayValueArchivePath(floor8OutputRoot, metadata, ".md");
        CopyFileIfDifferent(outputJsonPath, latestJsonPath);
        CopyFileIfDifferent(outputJsonPath, archiveJsonPath);
        CopyFileIfDifferent(outputReportPath, latestReportPath);
        CopyFileIfDifferent(outputReportPath, archiveReportPath);

        Console.WriteLine("floor8 play-value simulation complete");
        Console.WriteLine($"selectedDecks: {selectedDecks.Count}");
        Console.WriteLine($"activeDecks: {preparedDecks.Count}");
        Console.WriteLine($"baseCandidates: {baseCandidates.Count}");
        Console.WriteLine($"allForms: {allForms.Count}");
        Console.WriteLine($"eligibleForms: {eligibleForms.Count}");
        Console.WriteLine($"completedCards: {concurrentCards.Count}");
        Console.WriteLine($"runs: {runs}");
        Console.WriteLine($"turns: {turns}");
        Console.WriteLine($"degreeOfParallelism: {degreeOfParallelism}");
        Console.WriteLine($"elapsedSeconds: {totalStopwatch.Elapsed.TotalSeconds:0.###}");
        Console.WriteLine($"selectedDecksOutput: {selectedDecksPath}");
        Console.WriteLine($"output: {outputJsonPath}");
        Console.WriteLine($"latest: {latestJsonPath}");
        Console.WriteLine($"archive: {archiveJsonPath}");
        Console.WriteLine($"report: {latestReportPath}");
        return 0;
    }

    private static int InstallFloor8PlayValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string inputPath = GetOption(args, "--input")
            ?? Path.Combine(outputRoot, "generated", "floor8_play_values", "latest.generated.json");
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;

        if (!File.Exists(inputPath))
        {
            return Fail($"Missing floor8 play values at {inputPath}.");
        }

        if (!File.Exists(configPath))
        {
            return Fail($"Missing runtime config at {configPath}.");
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        Floor8PlayValueOutput output =
            JsonSerializer.Deserialize<Floor8PlayValueOutput>(File.ReadAllText(inputPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read floor8 play values from {inputPath}.");
        if (output.SchemaVersion != Floor8PlayValueSchemaVersion)
        {
            return Fail($"Floor8 play values schemaVersion={output.SchemaVersion}; expected {Floor8PlayValueSchemaVersion}.");
        }

        CardValueConfig existing = CardValueConfigLoader.LoadFromFile(configPath);
        List<string> missing = output.Cards.Keys
            .Where(cardKey => !existing.Cards.ContainsKey(cardKey))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
        {
            return Fail("Runtime config is missing generated floor8 cards: " + string.Join(", ", missing));
        }

        Dictionary<string, CardValueEntry> cards = existing.Cards
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        int updatedForms = 0;
        foreach ((string cardKey, Floor8CardPlayValueOutput generated) in output.Cards)
        {
            CardValueEntry entry = cards[cardKey];
            TrainingHorizonValues unupgraded = entry.TrainingValues.Unupgraded;
            TrainingHorizonValues upgraded = entry.TrainingValues.Upgraded;
            if (generated.Unupgraded is not null)
            {
                unupgraded = WithInstalledFloor8Values(unupgraded, generated.Unupgraded);
                updatedForms++;
            }

            if (generated.Upgraded is not null)
            {
                upgraded = WithInstalledFloor8Values(upgraded, generated.Upgraded);
                updatedForms++;
            }

            cards[cardKey] = entry with
            {
                TrainingValues = entry.TrainingValues with
                {
                    Unupgraded = unupgraded,
                    Upgraded = upgraded
                }
            };
        }

        CardValueConfig config = existing with
        {
            SchemaVersion = CardValueConfig.SupportedSchemaVersion,
            Cards = cards
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        ConfigValidationResult validation = CardValueConfigLoader.Validate(config);
        if (validation.Errors.Count > 0)
        {
            return Fail("Updated runtime config is invalid: " + string.Join("; ", validation.Errors));
        }

        WriteTextWithRetry(configPath, CardValueConfigLoader.ToJson(config));
        Console.WriteLine("floor8 play values installed");
        Console.WriteLine($"input: {inputPath}");
        Console.WriteLine($"config: {configPath}");
        Console.WriteLine($"cards: {output.Cards.Count}");
        Console.WriteLine($"formsUpdated: {updatedForms}");
        Console.WriteLine("updatedHorizons: shortline, midline");
        Console.WriteLine("preservedHorizons: longline");
        return 0;
    }

    private static TrainingHorizonValues WithInstalledFloor8Values(
        TrainingHorizonValues existing,
        Floor8FormPlayValueOutput generated)
    {
        return existing with
        {
            Shortline = generated.Shortline.WeightedValuePerPlay.HasValue
                ? (double?)RoundOneDecimal(generated.Shortline.WeightedValuePerPlay.Value)
                : existing.Shortline,
            Midline = generated.Midline.WeightedValuePerPlay.HasValue
                ? (double?)RoundOneDecimal(generated.Midline.WeightedValuePerPlay.Value)
                : existing.Midline
        };
    }

    private static IReadOnlyList<TrainingDeck> SelectRandomTrainingDecks(
        IReadOnlyList<TrainingDeck> decks,
        string group,
        int count,
        int seed)
    {
        List<TrainingDeck> candidates = decks
            .Where(deck => string.Equals(deck.Group, group, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count < count)
        {
            throw new InvalidOperationException($"Deck source only has {candidates.Count} decks in group '{group}', but {count} were requested.");
        }

        Random rng = new(seed);
        for (int index = candidates.Count - 1; index > 0; index--)
        {
            int swapIndex = rng.Next(index + 1);
            (candidates[index], candidates[swapIndex]) = (candidates[swapIndex], candidates[index]);
        }

        return candidates.Take(count).ToArray();
    }

    private static void WriteSelectedFloor8Decks(
        string path,
        IReadOnlyList<TrainingDeck> selectedDecks,
        JsonSerializerOptions jsonOptions)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        WriteTextWithRetry(path, JsonSerializer.Serialize(new TrainingDeckFile(selectedDecks), jsonOptions));
    }

    private static IReadOnlyList<Floor8CandidateForm> BuildFloor8CandidateForms(
        IReadOnlyList<TrainingCandidate> candidates)
    {
        List<Floor8CandidateForm> forms = [];
        foreach (TrainingCandidate candidate in candidates)
        {
            forms.Add(BuildFloor8CandidateForm(candidate.ModelId, candidate.TypeName, candidate.Pools, candidate.Unupgraded));
            if (candidate.Upgraded is not null)
            {
                forms.Add(BuildFloor8CandidateForm(candidate.ModelId, candidate.TypeName, candidate.Pools, candidate.Upgraded));
            }
        }

        return forms;
    }

    private static Floor8CandidateForm BuildFloor8CandidateForm(
        string baseModelId,
        string baseTypeName,
        IReadOnlyList<string> pools,
        SimulationCard card)
    {
        List<string> reasons = [];
        if (!card.IsPlayable)
        {
            reasons.Add("Card is not playable.");
        }

        reasons.AddRange(card.Warnings.Where(IsFloor8PlayValueBlockingWarning));
        return new Floor8CandidateForm(
            baseModelId,
            baseTypeName,
            pools,
            card.ModelId,
            card.TypeName,
            card.UpgradeLevel,
            reasons.Count == 0,
            reasons);
    }

    private static bool IsFloor8PlayValueBlockingWarning(string warning)
    {
        return warning.StartsWith("Unsupported simulation action", StringComparison.Ordinal)
            || warning.StartsWith("Attribution incomplete", StringComparison.Ordinal)
            || warning.Contains("Generic calculated damage scaling requires manual review", StringComparison.Ordinal);
    }

    private static List<Floor8DeckBaselineOutput> RunFloor8Baselines(
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        IReadOnlyDictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer,
        GeneratedCardPoolCatalog generatedCardPools,
        int turns,
        int runs,
        int seed,
        int handSize,
        int maxHandSize,
        int baseEnergy,
        int baseStars,
        int maxCardsPlayed,
        int maxBranchingCards,
        ISearchCardScorer? searchCardScorer,
        bool profile)
    {
        List<Floor8DeckBaselineOutput> baselines = [];
        for (int index = 0; index < preparedDecks.Count; index++)
        {
            PreparedTrainingDeck deck = preparedDecks[index];
            DeckSimulationOptions options = BuildTrainingOptions(
                turns,
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
                searchCardScorer: searchCardScorer);
            Stopwatch stopwatch = Stopwatch.StartNew();
            DeckSimulationReport report = new DeckMonteCarloSimulator().Simulate(deck.Cards, options);
            stopwatch.Stop();
            Floor8DeckBaselineOutput baseline = new(
                deck.Index,
                deck.RunId,
                deck.Group,
                deck.Layer,
                deck.Cards.Count,
                report.TotalExpectedValue,
                Round(report.Turns.Take(4).Sum(turn => turn.ExpectedValue)),
                Round(report.Turns.Take(8).Sum(turn => turn.ExpectedValue)),
                RoundSeconds(stopwatch.Elapsed.TotalSeconds));
            baselines.Add(baseline);
            if (profile)
            {
                Console.WriteLine($"floor8 baseline {index + 1}/{preparedDecks.Count}: deck={deck.Index} runId={deck.RunId} cards={deck.Cards.Count} totalValue={baseline.TotalExpectedValue:0.###} elapsedSeconds={baseline.ElapsedSeconds:0.###}");
            }
        }

        return baselines;
    }

    private static Floor8FormPlayValueOutput EstimateFloor8Form(
        Floor8CandidateForm form,
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        IReadOnlyDictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer,
        GeneratedCardPoolCatalog generatedCardPools,
        int turns,
        int shortTurns,
        int midTurns,
        int runs,
        int seed,
        int handSize,
        int maxHandSize,
        int baseEnergy,
        int baseStars,
        int maxCardsPlayed,
        int maxBranchingCards,
        ISearchCardScorer? searchCardScorer)
    {
        List<Floor8DeckFormResult> deckResults = [];
        foreach (PreparedTrainingDeck deck in preparedDecks)
        {
            SimulationCard layerCard = librariesByLayer[deck.Layer].First(card =>
                string.Equals(card.ModelId, form.ModelId, StringComparison.OrdinalIgnoreCase));
            string probeModelId = BuildFloor8ProbeModelId(deck, form);
            SimulationCard probeCard = layerCard with { ModelId = probeModelId };
            SimulationCard[] variantDeck = [.. deck.Cards, probeCard];
            DeckSimulationOptions options = BuildTrainingOptions(
                turns,
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
                searchCardScorer: searchCardScorer);
            DeckSimulationReport report = new DeckMonteCarloSimulator().Simulate(variantDeck, options);
            Floor8HorizonPlayValue shortline = PrefixCreditValue(report, probeModelId, shortTurns);
            Floor8HorizonPlayValue midline = PrefixCreditValue(report, probeModelId, midTurns);
            deckResults.Add(new Floor8DeckFormResult(
                deck.Index,
                deck.RunId,
                deck.Group,
                deck.Layer,
                probeModelId,
                shortline,
                midline));
        }

        return new Floor8FormPlayValueOutput(
            form.ModelId,
            form.TypeName,
            form.UpgradeLevel,
            AggregateFloor8Horizon(deckResults.Select(result => result.Shortline)),
            AggregateFloor8Horizon(deckResults.Select(result => result.Midline)),
            deckResults);
    }

    private static Floor8HorizonPlayValue PrefixCreditValue(
        DeckSimulationReport report,
        string modelId,
        int turns)
    {
        CardValueCreditTurnSummary[] credits = report.CardValueCreditsByTurn
            .Where(credit => credit.Turn <= turns && string.Equals(credit.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        int playCount = credits.Sum(credit => credit.DirectPlayCount);
        decimal directValue = credits.Sum(credit => credit.DirectValue);
        decimal forgeValue = credits.Sum(credit => credit.ForgeRealizedValue);
        decimal powerValue = credits.Sum(credit => credit.PowerRealizedValue);
        decimal energyValue = credits.Sum(credit => credit.EnergyRealizedValue);
        decimal starValue = credits.Sum(credit => credit.StarRealizedValue);
        decimal total = directValue + forgeValue + powerValue + energyValue + starValue;
        return new Floor8HorizonPlayValue(
            turns,
            playCount,
            Round(directValue),
            Round(forgeValue),
            Round(powerValue),
            Round(energyValue),
            Round(starValue),
            Round(total),
            playCount == 0 ? null : Round(total / playCount),
            playCount > 0);
    }

    private static Floor8HorizonAggregate AggregateFloor8Horizon(IEnumerable<Floor8HorizonPlayValue> values)
    {
        Floor8HorizonPlayValue[] array = values.ToArray();
        int totalPlayCount = array.Sum(value => value.PlayCount);
        decimal totalCreditedValue = array.Sum(value => value.TotalCreditedValue);
        decimal sampleSum = array
            .Where(value => value.Valid && value.ValuePerPlay.HasValue)
            .Sum(value => value.ValuePerPlay!.Value);
        int validDecks = array.Count(value => value.Valid);
        int invalidDecks = array.Length - validDecks;
        return new Floor8HorizonAggregate(
            totalPlayCount,
            Round(totalCreditedValue),
            totalPlayCount == 0 ? null : Round(totalCreditedValue / totalPlayCount),
            validDecks == 0 ? null : Round(sampleSum / validDecks),
            validDecks,
            invalidDecks);
    }

    private static void WriteFloor8PlayValueOutput(
        string outputJsonPath,
        string outputReportPath,
        Floor8PlayValueMetadata metadata,
        IReadOnlyList<TrainingDeck> selectedDecks,
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        int baseCandidateCount,
        int allFormCount,
        int eligibleFormCount,
        IReadOnlyDictionary<string, Floor8CardPlayValueOutput> cards,
        IReadOnlyList<Floor8ExcludedFormOutput> excludedForms,
        IReadOnlyList<Floor8DeckBaselineOutput> baselines,
        IReadOnlyList<string> warnings,
        JsonSerializerOptions jsonOptions)
    {
        Floor8PlayValueOutput output = new(
            Floor8PlayValueSchemaVersion,
            metadata,
            selectedDecks.Select(deck => new Floor8SelectedDeckOutput(deck.RunId, deck.Group, deck.Floor, deck.Cards.Sum(card => card.Count))).ToArray(),
            preparedDecks.Select(deck => new Floor8SelectedDeckOutput(deck.RunId, deck.Group, deck.Layer, deck.Cards.Count)).ToArray(),
            baseCandidateCount,
            allFormCount,
            eligibleFormCount,
            cards
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            excludedForms
                .OrderBy(form => form.ModelId, StringComparer.Ordinal)
                .ThenBy(form => form.UpgradeLevel)
                .ToArray(),
            baselines,
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        WriteTextWithRetry(outputJsonPath, JsonSerializer.Serialize(output, jsonOptions));
        WriteTextWithRetry(outputReportPath, BuildFloor8PlayValueReport(output));
    }

    private static string BuildFloor8PlayValueReport(Floor8PlayValueOutput output)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Floor8 Play Values");
        builder.AppendLine();
        builder.AppendLine($"Generated: {output.Metadata.GeneratedAt}");
        builder.AppendLine($"Decks: {output.Metadata.ActiveDeckCount}/{output.Metadata.DeckCount} active");
        builder.AppendLine($"Runs: {output.Metadata.Runs}");
        builder.AppendLine($"Max branch: {output.Metadata.MaxBranchingCards}");
        builder.AppendLine($"Forms: {output.EligibleFormCount}/{output.AllFormCount} eligible");
        builder.AppendLine();
        builder.AppendLine("| Card | Upgrade | Short | Mid | Short Plays | Mid Plays |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (Floor8CardPlayValueOutput card in output.Cards.Values.OrderBy(card => card.TypeName, StringComparer.Ordinal))
        {
            AppendFloor8ReportRow(builder, card.TypeName, 0, card.Unupgraded);
            AppendFloor8ReportRow(builder, card.TypeName, 1, card.Upgraded);
        }

        builder.AppendLine();
        builder.AppendLine("## Excluded Forms");
        builder.AppendLine();
        foreach (Floor8ExcludedFormOutput excluded in output.ExcludedForms)
        {
            builder.AppendLine($"- {excluded.ModelId} {excluded.TypeName}: {string.Join("; ", excluded.Reasons)}");
        }

        return builder.ToString();
    }

    private static void AppendFloor8ReportRow(
        StringBuilder builder,
        string typeName,
        int upgrade,
        Floor8FormPlayValueOutput? form)
    {
        if (form is null)
        {
            return;
        }

        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"| {typeName} | {upgrade} | {FormatNullableDecimal(form.Shortline.WeightedValuePerPlay)} | {FormatNullableDecimal(form.Midline.WeightedValuePerPlay)} | {form.Shortline.PlayCount} | {form.Midline.PlayCount} |");
    }

    private static string FormatNullableDecimal(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "";
    }

    private static string BuildFloor8ProbeModelId(PreparedTrainingDeck deck, Floor8CandidateForm form)
    {
        return string.Join(
            ".",
            "PROBE",
            "FLOOR8",
            deck.Index.ToString(CultureInfo.InvariantCulture),
            form.UpgradeLevel.ToString(CultureInfo.InvariantCulture),
            SanitizeProbeModelComponent(form.ModelId));
    }

    private static string BuildFloor8PlayValueArchivePath(
        string outputRoot,
        Floor8PlayValueMetadata metadata,
        string extension)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string fileName = string.Join(
            "_",
            timestamp,
            SanitizeFileComponent(metadata.Source),
            $"d{metadata.ActiveDeckCount}",
            $"r{metadata.Runs}",
            $"b{metadata.MaxBranchingCards}",
            $"t{metadata.Turns}") + ".generated" + extension;
        return Path.Combine(outputRoot, fileName);
    }

    private static decimal RoundOneDecimal(decimal value)
    {
        return Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private sealed record Floor8CandidateForm(
        string BaseModelId,
        string BaseTypeName,
        IReadOnlyList<string> Pools,
        string ModelId,
        string TypeName,
        int UpgradeLevel,
        bool Eligible,
        IReadOnlyList<string> ExclusionReasons);

    private sealed record Floor8PlayValueMetadata(
        string Source,
        string GeneratedAt,
        string DeckSourcePath,
        string SelectedDecksPath,
        string DeckGroup,
        int DeckCount,
        int DeckSeed,
        int ActiveDeckCount,
        int Runs,
        int Seed,
        int MaxCardsPlayedPerTurn,
        int MaxBranchingCards,
        int Turns,
        int ShortTurns,
        int MidTurns,
        int SkipForms,
        int? LimitForms,
        string Note);

    private sealed record Floor8PlayValueOutput(
        int SchemaVersion,
        Floor8PlayValueMetadata Metadata,
        IReadOnlyList<Floor8SelectedDeckOutput> SelectedDecks,
        IReadOnlyList<Floor8SelectedDeckOutput> ActiveDecks,
        int BaseCandidateCount,
        int AllFormCount,
        int EligibleFormCount,
        IReadOnlyDictionary<string, Floor8CardPlayValueOutput> Cards,
        IReadOnlyList<Floor8ExcludedFormOutput> ExcludedForms,
        IReadOnlyList<Floor8DeckBaselineOutput> Baselines,
        IReadOnlyList<string> Warnings);

    private sealed record Floor8SelectedDeckOutput(
        string RunId,
        string Group,
        int? Floor,
        int CardCount);

    private sealed record Floor8DeckBaselineOutput(
        int DeckIndex,
        string RunId,
        string Group,
        int Layer,
        int CardCount,
        decimal TotalExpectedValue,
        decimal ShortlineExpectedValue,
        decimal MidlineExpectedValue,
        double ElapsedSeconds);

    private sealed record Floor8CardPlayValueOutput(
        string ModelId,
        string TypeName,
        IReadOnlyList<string> Pools,
        Floor8FormPlayValueOutput? Unupgraded,
        Floor8FormPlayValueOutput? Upgraded);

    private sealed record Floor8FormPlayValueOutput(
        string ModelId,
        string TypeName,
        int UpgradeLevel,
        Floor8HorizonAggregate Shortline,
        Floor8HorizonAggregate Midline,
        IReadOnlyList<Floor8DeckFormResult> DeckResults);

    private sealed record Floor8DeckFormResult(
        int DeckIndex,
        string RunId,
        string Group,
        int Layer,
        string ProbeModelId,
        Floor8HorizonPlayValue Shortline,
        Floor8HorizonPlayValue Midline);

    private sealed record Floor8HorizonPlayValue(
        int Turns,
        int PlayCount,
        decimal DirectValue,
        decimal ForgeRealizedValue,
        decimal PowerRealizedValue,
        decimal EnergyRealizedValue,
        decimal StarRealizedValue,
        decimal TotalCreditedValue,
        decimal? ValuePerPlay,
        bool Valid);

    private sealed record Floor8HorizonAggregate(
        int PlayCount,
        decimal TotalCreditedValue,
        decimal? WeightedValuePerPlay,
        decimal? SampleMeanValuePerPlay,
        int ValidDecks,
        int InvalidDecks);

    private sealed record Floor8ExcludedFormOutput(
        string BaseModelId,
        string ModelId,
        string TypeName,
        int UpgradeLevel,
        IReadOnlyList<string> Reasons);
}
