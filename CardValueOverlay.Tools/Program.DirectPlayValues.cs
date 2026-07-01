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
    private const int DirectPlayValueSchemaVersion = 1;

    private static readonly HashSet<string> DirectPlayDeltaAllowedIncompleteActions = new(StringComparer.Ordinal)
    {
        "draw",
        "drawNextTurn",
        "selectCards",
        "moveCardBetweenPiles",
        "transformCard",
        "createCard",
        "createCardChoices",
        "autoPlay",
        "power",
        "grantReplay"
    };

    private static int EstimateDirectPlayValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string deckSourcePath = GetOption(args, "--deck-source")
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_100_decks.json");
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        string setupPrioritiesPath = GetOption(args, "--setup-priorities")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
        string autoPlayEffectsPath = GetOption(args, "--card-autoplay-effects")
            ?? Path.Combine(outputRoot, "manual-tags", "card_autoplay_effects.json");
        string? deckGroup = GetOption(args, "--deck-group");
        int deckCount = GetIntOption(args, "--deck-count") ?? 1;
        int deckSeed = GetIntOption(args, "--deck-seed") ?? 20260630;
        int runs = GetIntOption(args, "--runs") ?? 400;
        int seed = GetIntOption(args, "--seed") ?? 1;
        DirectPlayHorizonSpec[] horizons = ParseDirectPlayHorizons(GetOption(args, "--horizons") ?? "shortline:4,midline:8");
        int turns = GetIntOption(args, "--turns") ?? horizons.Max(horizon => horizon.Turns);
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays") ?? 8;
        int maxBranchingCards = GetIntOption(args, "--max-branch") ?? 4;
        int? limitDecks = GetIntOption(args, "--limit-decks");
        int? limitForms = GetIntOption(args, "--limit-forms");
        int skipForms = Math.Max(0, GetIntOption(args, "--skip-forms") ?? 0);
        int degreeOfParallelism = Math.Max(1, GetIntOption(args, "--degree-of-parallelism") ?? 4);
        int runDegree = Math.Max(1, GetIntOption(args, "--run-degree") ?? 4);
        string? candidateFilter = GetOption(args, "--candidate");
        IReadOnlySet<string>? candidateFileFilter = LoadCandidateFileFilter(GetOption(args, "--candidate-file"));
        DirectPlayValueStrategy requestedStrategy = ParseDirectPlayValueStrategy(GetOption(args, "--value-strategy") ?? "auto");
        bool pinProbeBranch = HasFlag(args, "--pin-probe-branch");
        bool resume = HasFlag(args, "--resume");
        bool profile = HasFlag(args, "--profile");
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);

        if (string.IsNullOrWhiteSpace(deckGroup))
        {
            return Fail("estimate-direct-play-values requires --deck-group. Pass floor8, act2Start, final, or another group present in --deck-source.");
        }

        if (deckCount <= 0)
        {
            return Fail("--deck-count must be positive.");
        }

        if (runs <= 0)
        {
            return Fail("--runs must be positive.");
        }

        if (turns < horizons.Max(horizon => horizon.Turns))
        {
            return Fail("--turns must be at least the largest --horizons turn count.");
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
        string directOutputRoot = Path.Combine(generatedRoot, "direct_play_values");
        Directory.CreateDirectory(directOutputRoot);
        string latestJsonPath = Path.Combine(directOutputRoot, "latest.generated.json");
        string latestReportPath = Path.Combine(directOutputRoot, "latest.generated.md");
        string outputJsonPath = GetOption(args, "--output-json") ?? latestJsonPath;
        string outputReportPath = GetOption(args, "--output-md") ?? Path.ChangeExtension(outputJsonPath, ".md");
        string selectedDecksPath = GetOption(args, "--selected-decks-output")
            ?? Path.Combine(
                directOutputRoot,
                $"selected_{SanitizeFileComponent(deckGroup)}_d{deckCount}_seed{deckSeed}.generated.json");

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        SimulationSetupPriorityCatalog setupPriorities = LoadOptionalSimulationSetupPriorities(setupPrioritiesPath, jsonOptions);
        IReadOnlyList<AutoPlayEffectEntry> autoPlayEffects = LoadOptionalAutoPlayEffects(autoPlayEffectsPath, jsonOptions);
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
            return Fail("No selected decks are active after --limit-decks.");
        }

        int[] layers = activeDecks
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
                setupPriorities,
                autoPlayEffects));
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

        if (candidateFileFilter is not null)
        {
            baseCandidates = FilterDirectCandidatesByFile(baseCandidates, candidateFileFilter);
        }

        IReadOnlyList<DirectCandidateForm> allForms = BuildDirectCandidateForms(baseCandidates, requestedStrategy);
        IReadOnlyList<DirectCandidateForm> eligibleForms = allForms
            .Where(form => form.Eligible)
            .Skip(skipForms)
            .Take(limitForms ?? int.MaxValue)
            .ToArray();
        if (eligibleForms.Count == 0)
        {
            return Fail(string.IsNullOrWhiteSpace(candidateFilter)
                ? "No eligible direct play-value forms were found."
                : $"Candidate {candidateFilter} has no eligible direct play-value forms.");
        }

        DirectPlayValueMetadata metadata = new(
            "direct_play_value_20260630",
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
            DirectPlayValueStrategyKey(requestedStrategy),
            horizons,
            skipForms,
            limitForms,
            "Each eligible Regent/Colorless card form is added as one unique probe copy to each selected deck. source-credit uses simulator source attribution. play-delta compares a normal simulation against the same probe card blocked from play, then divides run-scaled EV delta by normal direct play count.");
        List<DirectExcludedFormOutput> excludedForms = allForms
            .Where(form => !form.Eligible)
            .Select(form => new DirectExcludedFormOutput(
                form.BaseModelId,
                form.ModelId,
                form.TypeName,
                form.UpgradeLevel,
                form.ExclusionReasons))
            .ToList();
        List<string> warnings = [];
        Dictionary<string, DirectCardPlayValueOutput> completedCards = [];
        if (resume && File.Exists(outputJsonPath))
        {
            DirectPlayValueOutput? existing =
                JsonSerializer.Deserialize<DirectPlayValueOutput>(File.ReadAllText(outputJsonPath), jsonOptions);
            if (existing is not null
                && existing.SchemaVersion == DirectPlayValueSchemaVersion
                && existing.Metadata.Runs == runs
                && existing.Metadata.ActiveDeckCount == preparedDecks.Count
                && existing.Metadata.MaxBranchingCards == maxBranchingCards
                && existing.Metadata.Turns == turns
                && string.Equals(existing.Metadata.ValueStrategy, DirectPlayValueStrategyKey(requestedStrategy), StringComparison.OrdinalIgnoreCase)
                && HorizonsMatch(existing.Metadata.Horizons, horizons))
            {
                HashSet<string> activeBaseIds = eligibleForms
                    .Select(form => form.BaseModelId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                completedCards = existing.Cards
                    .Where(pair => activeBaseIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                warnings = existing.Warnings.ToList();
                metadata = metadata with { GeneratedAt = existing.Metadata.GeneratedAt };
                Console.WriteLine($"resuming from {completedCards.Count} completed direct play-value card entries in {outputJsonPath}");
            }
            else
            {
                Console.WriteLine("resume ignored: existing direct play-value output does not match current run shape.");
            }
        }

        ConcurrentDictionary<string, DirectCardPlayValueOutput> concurrentCards =
            new(completedCards, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<IGrouping<string, DirectCandidateForm>> formGroups = eligibleForms
            .GroupBy(form => form.BaseModelId, StringComparer.OrdinalIgnoreCase)
            .Where(group => !concurrentCards.ContainsKey(group.Key))
            .ToArray();
        // Inner run parallelism only engages when the outer (per-card) parallelism has
        // nothing to spread across (single deck/candidate); otherwise it would oversubscribe.
        int effectiveRunDegree = degreeOfParallelism > 1 && formGroups.Count > 1 ? 1 : runDegree;
        object writeLock = new();
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        int completed = concurrentCards.Count;

        Action<IGrouping<string, DirectCandidateForm>> estimateCard = group =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            DirectCandidateForm? unupgradedForm = group.FirstOrDefault(form => form.UpgradeLevel == 0);
            DirectCandidateForm? upgradedForm = group.FirstOrDefault(form => form.UpgradeLevel > 0);
            DirectFormPlayValueOutput? unupgraded = unupgradedForm is null
                ? null
                : EstimateDirectForm(
                    unupgradedForm,
                    preparedDecks,
                    librariesByLayer,
                    generatedCardPools,
                    turns,
                    horizons,
                    runs,
                    seed,
                    handSize,
                    maxHandSize,
                    baseEnergy,
                    baseStars,
                    maxCardsPlayed,
                    maxBranchingCards,
                    effectiveRunDegree,
                    searchCardScorer,
                    pinProbeBranch);
            DirectFormPlayValueOutput? upgraded = upgradedForm is null
                ? null
                : EstimateDirectForm(
                    upgradedForm,
                    preparedDecks,
                    librariesByLayer,
                    generatedCardPools,
                    turns,
                    horizons,
                    runs,
                    seed,
                    handSize,
                    maxHandSize,
                    baseEnergy,
                    baseStars,
                    maxCardsPlayed,
                    maxBranchingCards,
                    effectiveRunDegree,
                    searchCardScorer,
                    pinProbeBranch);
            DirectCandidateForm representative = group.First();
            concurrentCards[group.Key] = new DirectCardPlayValueOutput(
                representative.BaseModelId,
                representative.BaseTypeName,
                representative.Pools,
                unupgraded,
                upgraded);

            int done = Interlocked.Increment(ref completed);
            if (profile || done % 10 == 0 || done == formGroups.Count + completedCards.Count)
            {
                Console.WriteLine($"direct play-value {done}/{formGroups.Count + completedCards.Count}: {representative.BaseModelId} {representative.BaseTypeName} elapsedSeconds={stopwatch.Elapsed.TotalSeconds:0.###}");
            }

            lock (writeLock)
            {
                WriteDirectPlayValueOutput(
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
                    warnings,
                    jsonOptions);
            }
        };

        if (degreeOfParallelism <= 1)
        {
            foreach (IGrouping<string, DirectCandidateForm> group in formGroups)
            {
                estimateCard(group);
            }
        }
        else
        {
            Parallel.ForEach(
                formGroups,
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                estimateCard);
        }

        totalStopwatch.Stop();
        WriteDirectPlayValueOutput(
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
            warnings,
            jsonOptions);
        string archiveJsonPath = BuildDirectPlayValueArchivePath(directOutputRoot, metadata, ".json");
        string archiveReportPath = BuildDirectPlayValueArchivePath(directOutputRoot, metadata, ".md");
        CopyFileIfDifferent(outputJsonPath, latestJsonPath);
        CopyFileIfDifferent(outputJsonPath, archiveJsonPath);
        CopyFileIfDifferent(outputReportPath, latestReportPath);
        CopyFileIfDifferent(outputReportPath, archiveReportPath);

        Console.WriteLine("direct play-value simulation complete");
        Console.WriteLine($"selectedDecks: {selectedDecks.Count}");
        Console.WriteLine($"activeDecks: {preparedDecks.Count}");
        Console.WriteLine($"baseCandidates: {baseCandidates.Count}");
        Console.WriteLine($"allForms: {allForms.Count}");
        Console.WriteLine($"eligibleForms: {eligibleForms.Count}");
        Console.WriteLine($"completedCards: {concurrentCards.Count}");
        Console.WriteLine($"valueStrategy: {DirectPlayValueStrategyKey(requestedStrategy)}");
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

    private static int InstallDirectPlayValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string inputPath = GetOption(args, "--input")
            ?? Path.Combine(outputRoot, "generated", "direct_play_values", "latest.generated.json");
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;
        string setupOutputPath = GetOption(args, "--setup-output")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
        string setupSourceHorizon = GetOption(args, "--setup-source-horizon") ?? "midline";
        IReadOnlySet<string> installHorizons = ParseDirectInstallHorizons(GetOption(args, "--horizons") ?? "shortline,midline");
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>? groupWeights =
            ParseDirectInstallGroupWeights(GetOption(args, "--group-weights"));

        if (!File.Exists(inputPath))
        {
            return Fail($"Missing direct play values at {inputPath}.");
        }

        if (!File.Exists(configPath))
        {
            return Fail($"Missing runtime config at {configPath}.");
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        DirectPlayValueOutput output =
            JsonSerializer.Deserialize<DirectPlayValueOutput>(File.ReadAllText(inputPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read direct play values from {inputPath}.");
        if (output.SchemaVersion != DirectPlayValueSchemaVersion)
        {
            return Fail($"Direct play values schemaVersion={output.SchemaVersion}; expected {DirectPlayValueSchemaVersion}.");
        }

        CardValueConfig existing = CardValueConfigLoader.LoadFromFile(configPath);
        List<string> missing = output.Cards.Keys
            .Where(cardKey => !existing.Cards.ContainsKey(cardKey))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
        {
            return Fail("Runtime config is missing generated direct play-value cards: " + string.Join(", ", missing));
        }

        Dictionary<string, CardValueEntry> cards = existing.Cards
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        int updatedForms = 0;
        foreach ((string cardKey, DirectCardPlayValueOutput generated) in output.Cards)
        {
            CardValueEntry entry = cards[cardKey];
            TrainingHorizonValues unupgraded = entry.TrainingValues.Unupgraded;
            TrainingHorizonValues upgraded = entry.TrainingValues.Upgraded;
            if (generated.Unupgraded is not null)
            {
                unupgraded = WithInstalledDirectValues(unupgraded, generated.Unupgraded, installHorizons, groupWeights);
                updatedForms++;
            }

            if (generated.Upgraded is not null)
            {
                upgraded = WithInstalledDirectValues(upgraded, generated.Upgraded, installHorizons, groupWeights);
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

        WriteDirectSetupPriorities(setupOutputPath, setupSourceHorizon, output, groupWeights, jsonOptions);
        WriteTextWithRetry(configPath, CardValueConfigLoader.ToJson(config));
        Console.WriteLine("direct play values installed");
        Console.WriteLine($"input: {inputPath}");
        Console.WriteLine($"config: {configPath}");
        Console.WriteLine($"cards: {output.Cards.Count}");
        Console.WriteLine($"formsUpdated: {updatedForms}");
        Console.WriteLine($"updatedHorizons: {string.Join(", ", installHorizons.Order(StringComparer.Ordinal))}");
        Console.WriteLine("preservedHorizons: " + string.Join(", ", new[] { "shortline", "midline", "longline" }.Where(horizon => !installHorizons.Contains(horizon))));
        Console.WriteLine($"groupWeights: {(groupWeights is null ? "<none>" : FormatDirectGroupWeights(groupWeights))}");
        Console.WriteLine($"setupOutput: {setupOutputPath}");
        Console.WriteLine($"setupSourceHorizon: {setupSourceHorizon}");
        return 0;
    }

    private static DirectFormPlayValueOutput EstimateDirectForm(
        DirectCandidateForm form,
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        IReadOnlyDictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer,
        GeneratedCardPoolCatalog generatedCardPools,
        int turns,
        IReadOnlyList<DirectPlayHorizonSpec> horizons,
        int runs,
        int seed,
        int handSize,
        int maxHandSize,
        int baseEnergy,
        int baseStars,
        int maxCardsPlayed,
        int maxBranchingCards,
        int runDegreeOfParallelism,
        ISearchCardScorer? searchCardScorer,
        bool pinProbeBranch)
    {
        DirectPlayValueStrategy strategy = form.Strategy
            ?? throw new InvalidOperationException($"Form {form.ModelId} is eligible but has no resolved play-value strategy.");
        List<DirectDeckFormResult> deckResults = [];
        foreach (PreparedTrainingDeck deck in preparedDecks)
        {
            SimulationCard layerCard = librariesByLayer[deck.Layer].First(card =>
                string.Equals(card.ModelId, form.ModelId, StringComparison.OrdinalIgnoreCase));
            string probeModelId = BuildDirectProbeModelId(deck, form);
            SimulationCard probeCard = layerCard with { ModelId = probeModelId };
            SimulationCard[] variantDeck = [.. deck.Cards, probeCard];
            ISearchCardScorer? effectiveSearchCardScorer = pinProbeBranch
                ? new PinnedModelIdSearchCardScorer([probeModelId], 1_000_000d, searchCardScorer)
                : searchCardScorer;
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
                runDegreeOfParallelism,
                searchCardScorer: effectiveSearchCardScorer);
            Dictionary<string, DirectHorizonPlayValue> horizonResults = strategy switch
            {
                DirectPlayValueStrategy.SourceCredit => EstimateDirectSourceCreditHorizons(
                    new DeckMonteCarloSimulator().SimulateTrackedCard(
                        variantDeck,
                        options,
                        probeModelId,
                        collectCredits: true),
                    probeModelId,
                    horizons),
                DirectPlayValueStrategy.PlayDelta => EstimateDirectPlayDeltaHorizons(
                    variantDeck,
                    options,
                    probeModelId,
                    horizons,
                    runs),
                _ => throw new InvalidOperationException($"Resolved strategy {form.Strategy} is not executable.")
            };
            deckResults.Add(new DirectDeckFormResult(
                deck.Index,
                deck.RunId,
                deck.Group,
                deck.Layer,
                probeModelId,
                DirectPlayValueStrategyKey(strategy),
                horizonResults));
        }

        return new DirectFormPlayValueOutput(
            form.ModelId,
            form.TypeName,
            form.UpgradeLevel,
            DirectPlayValueStrategyKey(strategy),
            horizons.ToDictionary(
                horizon => horizon.Key,
                horizon => AggregateDirectHorizon(deckResults.Select(result => result.Horizons[horizon.Key])),
                StringComparer.OrdinalIgnoreCase),
            deckResults);
    }

    private static TrainingHorizonValues WithInstalledDirectValues(
        TrainingHorizonValues existing,
        DirectFormPlayValueOutput generated,
        IReadOnlySet<string> installHorizons,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>? groupWeights)
    {
        TrainingHorizonValues result = existing;
        foreach (string horizon in installHorizons)
        {
            decimal? installedValue = DirectInstalledHorizonValue(generated, horizon, groupWeights);
            if (!installedValue.HasValue)
            {
                continue;
            }

            double value = (double)RoundOneDecimal(installedValue.Value);
            result = horizon switch
            {
                "shortline" => result with { Shortline = value },
                "midline" => result with { Midline = value },
                "longline" => result with { Longline = value },
                _ => result
            };
        }

        return result;
    }

    private static decimal? DirectInstalledHorizonValue(
        DirectFormPlayValueOutput generated,
        string horizon,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>? groupWeights)
    {
        if (groupWeights is null)
        {
            return generated.Horizons.TryGetValue(horizon, out DirectHorizonAggregate? aggregate)
                ? aggregate.WeightedValuePerPlay
                : null;
        }

        if (!groupWeights.TryGetValue(horizon, out IReadOnlyDictionary<string, decimal>? horizonWeights))
        {
            return generated.Horizons.TryGetValue(horizon, out DirectHorizonAggregate? aggregate)
                ? aggregate.WeightedValuePerPlay
                : null;
        }

        decimal weightedSum = 0m;
        decimal includedWeight = 0m;
        foreach ((string group, decimal weight) in horizonWeights)
        {
            DirectHorizonPlayValue[] groupValues = generated.DeckResults
                .Where(result => string.Equals(result.Group, group, StringComparison.OrdinalIgnoreCase))
                .Select(result => result.Horizons.TryGetValue(horizon, out DirectHorizonPlayValue? value) ? value : null)
                .Where(value => value is not null)
                .Select(value => value!)
                .ToArray();
            if (groupValues.Length == 0)
            {
                continue;
            }

            DirectHorizonAggregate aggregate = AggregateDirectHorizon(groupValues);
            if (!aggregate.WeightedValuePerPlay.HasValue)
            {
                continue;
            }

            weightedSum += aggregate.WeightedValuePerPlay.Value * weight;
            includedWeight += weight;
        }

        return includedWeight == 0m
            ? null
            : Round(weightedSum / includedWeight);
    }

    private static IReadOnlySet<string> ParseDirectInstallHorizons(string value)
    {
        HashSet<string> horizons = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawHorizon in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string horizon = rawHorizon.Trim().ToLowerInvariant();
            if (horizon is not ("shortline" or "midline" or "longline"))
            {
                throw new InvalidOperationException("--horizons must contain only shortline, midline, and/or longline.");
            }

            horizons.Add(horizon);
        }

        if (horizons.Count == 0)
        {
            throw new InvalidOperationException("--horizons must include at least one horizon.");
        }

        return horizons;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>? ParseDirectInstallGroupWeights(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Dictionary<string, IReadOnlyDictionary<string, decimal>> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawHorizon in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] horizonParts = rawHorizon.Split('=', 2, StringSplitOptions.TrimEntries);
            if (horizonParts.Length != 2 || string.IsNullOrWhiteSpace(horizonParts[0]))
            {
                throw new InvalidOperationException("--group-weights entries must look like shortline=floor8:0.7,act2Start:0.2,final:0.1.");
            }

            string horizon = horizonParts[0].Trim().ToLowerInvariant();
            if (horizon is not ("shortline" or "midline" or "longline"))
            {
                throw new InvalidOperationException("--group-weights horizon keys must be shortline, midline, and/or longline.");
            }

            Dictionary<string, decimal> weights = new(StringComparer.OrdinalIgnoreCase);
            foreach (string rawGroup in horizonParts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string[] groupParts = rawGroup.Split(':', 2, StringSplitOptions.TrimEntries);
                if (groupParts.Length != 2
                    || string.IsNullOrWhiteSpace(groupParts[0])
                    || !decimal.TryParse(groupParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal weight)
                    || weight < 0m)
                {
                    throw new InvalidOperationException("--group-weights group entries must look like floor8:0.7 and weights must be non-negative numbers.");
                }

                weights[groupParts[0].Trim()] = weight;
            }

            if (weights.Count == 0 || weights.Values.Sum() <= 0m)
            {
                throw new InvalidOperationException($"--group-weights for {horizon} must contain at least one positive weight.");
            }

            result[horizon] = weights;
        }

        return result.Count == 0
            ? null
            : result;
    }

    private static string FormatDirectGroupWeights(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> groupWeights)
    {
        return string.Join(
            ";",
            groupWeights
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + string.Join(
                    ",",
                    pair.Value
                        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(group => FormattableString.Invariant($"{group.Key}:{group.Value:0.###}")))));
    }

    private static void WriteDirectSetupPriorities(
        string setupOutputPath,
        string setupSourceHorizon,
        DirectPlayValueOutput output,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>? groupWeights,
        JsonSerializerOptions jsonOptions)
    {
        SimulationSetupPriorityCatalog existing = LoadOptionalSimulationSetupPriorities(setupOutputPath, jsonOptions);
        Dictionary<string, SimulationSetupPriorityEntry> entries = existing.Cards
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach ((string cardKey, DirectCardPlayValueOutput card) in output.Cards)
        {
            entries.TryGetValue(cardKey, out SimulationSetupPriorityEntry? existingEntry);
            entries[cardKey] = new SimulationSetupPriorityEntry
            {
                TypeName = card.TypeName,
                Unupgraded = SetupPriorityFromDirectForm(card.Unupgraded, setupSourceHorizon, groupWeights) ?? existingEntry?.Unupgraded,
                Upgraded = SetupPriorityFromDirectForm(card.Upgraded, setupSourceHorizon, groupWeights) ?? existingEntry?.Upgraded
            };
        }

        SimulationSetupPriorityCatalog catalog = new()
        {
            SchemaVersion = SimulationSetupPriorityCatalog.CurrentSchemaVersion,
            Source = output.Metadata.Source,
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            SetupSourceHorizon = setupSourceHorizon,
            Cards = entries
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        string? parent = Path.GetDirectoryName(setupOutputPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        WriteTextWithRetry(setupOutputPath, JsonSerializer.Serialize(catalog, jsonOptions));
    }

    private static decimal? SetupPriorityFromDirectForm(
        DirectFormPlayValueOutput? form,
        string setupSourceHorizon,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>? groupWeights)
    {
        if (form is null)
        {
            return null;
        }

        decimal? value = DirectInstalledHorizonValue(form, setupSourceHorizon, groupWeights);
        return value.HasValue
            ? RoundOneDecimal(Math.Max(0m, value.Value))
            : null;
    }

    private static Dictionary<string, DirectHorizonPlayValue> EstimateDirectSourceCreditHorizons(
        TrackedCardSimulationReport report,
        string probeModelId,
        IReadOnlyList<DirectPlayHorizonSpec> horizons)
    {
        Dictionary<string, DirectHorizonPlayValue> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (DirectPlayHorizonSpec horizon in horizons)
        {
            TrackedCardTurnSummary[] credits = report.Turns
                .Where(turn => turn.Turn <= horizon.Turns)
                .ToArray();
            int playCount = credits.Sum(turn => turn.DirectPlayCount);
            decimal directValue = credits.Sum(credit => credit.DirectValue);
            decimal forgeValue = credits.Sum(credit => credit.ForgeRealizedValue);
            decimal powerValue = credits.Sum(credit => credit.PowerRealizedValue);
            decimal energyValue = credits.Sum(credit => credit.EnergyRealizedValue);
            decimal starValue = credits.Sum(credit => credit.StarRealizedValue);
            decimal total = Round(directValue + forgeValue + powerValue + energyValue + starValue);
            List<string> warnings = [];
            if (playCount == 0)
            {
                warnings.Add($"{horizon.Key}: probe card was not played.");
            }

            results[horizon.Key] = new DirectHorizonPlayValue(
                horizon.Key,
                horizon.Turns,
                DirectPlayValueStrategyKey(DirectPlayValueStrategy.SourceCredit),
                null,
                PrefixExpectedValue(report, horizon.Turns),
                null,
                null,
                playCount,
                0,
                Round(directValue),
                Round(forgeValue),
                Round(powerValue),
                Round(energyValue),
                Round(starValue),
                total,
                playCount == 0 ? null : Round(total / playCount),
                playCount > 0,
                warnings);
        }

        return results;
    }

    private static Dictionary<string, DirectHorizonPlayValue> EstimateDirectPlayDeltaHorizons(
        IReadOnlyList<SimulationCard> variantDeck,
        DeckSimulationOptions options,
        string probeModelId,
        IReadOnlyList<DirectPlayHorizonSpec> horizons,
        int runs)
    {
        DeckMonteCarloSimulator simulator = new();
        TrackedCardSimulationReport normalReport = simulator.SimulateTrackedCard(
            variantDeck,
            options,
            probeModelId,
            collectCredits: false);
        TrackedCardSimulationReport blockedReport = simulator.SimulateTrackedCard(
            variantDeck,
            options with { BlockedPlayModelIds = [probeModelId] },
            probeModelId,
            collectCredits: false);
        Dictionary<string, DirectHorizonPlayValue> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (DirectPlayHorizonSpec horizon in horizons)
        {
            decimal normalValue = PrefixExpectedValue(normalReport, horizon.Turns);
            decimal blockedValue = PrefixExpectedValue(blockedReport, horizon.Turns);
            decimal delta = Round(normalValue - blockedValue);
            decimal runScaledDelta = Round(delta * runs);
            int normalPlayCount = PrefixPlayCount(normalReport, probeModelId, horizon.Turns);
            int blockedPlayCount = PrefixPlayCount(blockedReport, probeModelId, horizon.Turns);
            List<string> warnings = [];
            if (blockedPlayCount != 0)
            {
                warnings.Add($"{horizon.Key}: blocked play count was {blockedPlayCount}; expected 0.");
            }

            if (normalPlayCount == 0)
            {
                warnings.Add($"{horizon.Key}: normal play count was 0.");
            }
            else if (normalPlayCount < Math.Max(3, runs / 20))
            {
                warnings.Add($"{horizon.Key}: low normal play count {normalPlayCount}.");
            }

            results[horizon.Key] = new DirectHorizonPlayValue(
                horizon.Key,
                horizon.Turns,
                DirectPlayValueStrategyKey(DirectPlayValueStrategy.PlayDelta),
                blockedValue,
                normalValue,
                delta,
                runScaledDelta,
                normalPlayCount,
                blockedPlayCount,
                null,
                null,
                null,
                null,
                null,
                runScaledDelta,
                normalPlayCount == 0 ? null : Round(runScaledDelta / normalPlayCount),
                normalPlayCount > 0 && blockedPlayCount == 0,
                warnings);
        }

        return results;
    }

    private static DirectHorizonAggregate AggregateDirectHorizon(IEnumerable<DirectHorizonPlayValue> values)
    {
        DirectHorizonPlayValue[] array = values.ToArray();
        int totalPlayCount = array.Where(value => value.Valid).Sum(value => value.NormalPlayCount);
        decimal totalValue = array.Where(value => value.Valid).Sum(value => value.TotalValue);
        decimal sampleSum = array
            .Where(value => value.Valid && value.ValuePerPlay.HasValue)
            .Sum(value => value.ValuePerPlay!.Value);
        int validDecks = array.Count(value => value.Valid);
        int invalidDecks = array.Length - validDecks;
        return new DirectHorizonAggregate(
            array.Length == 0 ? 0 : array[0].Turns,
            totalPlayCount,
            Round(totalValue),
            totalPlayCount == 0 ? null : Round(totalValue / totalPlayCount),
            validDecks == 0 ? null : Round(sampleSum / validDecks),
            validDecks,
            invalidDecks);
    }

    private static decimal PrefixExpectedValue(TrackedCardSimulationReport report, int turns)
    {
        if (report.Turns.Count < turns)
        {
            throw new InvalidOperationException($"Simulation result only has {report.Turns.Count} turns; cannot read {turns}-turn cumulative value.");
        }

        return Round(report.Turns.Take(turns).Sum(turn => turn.ExpectedValue));
    }

    private static int PrefixPlayCount(TrackedCardSimulationReport report, string modelId, int turns)
    {
        _ = modelId;
        return report.Turns
            .Where(turn => turn.Turn <= turns)
            .Sum(turn => turn.PlayCount);
    }

    private static IReadOnlyList<DirectCandidateForm> BuildDirectCandidateForms(
        IReadOnlyList<TrainingCandidate> candidates,
        DirectPlayValueStrategy requestedStrategy)
    {
        List<DirectCandidateForm> forms = [];
        foreach (TrainingCandidate candidate in candidates)
        {
            forms.Add(BuildDirectCandidateForm(candidate.ModelId, candidate.TypeName, candidate.Pools, candidate.Unupgraded, requestedStrategy));
            if (candidate.Upgraded is not null)
            {
                forms.Add(BuildDirectCandidateForm(candidate.ModelId, candidate.TypeName, candidate.Pools, candidate.Upgraded, requestedStrategy));
            }
        }

        return forms;
    }

    private static IReadOnlySet<string>? LoadCandidateFileFilter(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Missing candidate file at {path}.");
        }

        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            candidates.Add(line);
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"Candidate file {path} did not contain any candidates.");
        }

        return candidates;
    }

    private static IReadOnlyList<TrainingCandidate> FilterDirectCandidatesByFile(
        IReadOnlyList<TrainingCandidate> candidates,
        IReadOnlySet<string> filter)
    {
        TrainingCandidate[] filtered = candidates
            .Where(candidate => filter.Contains(candidate.ModelId) || filter.Contains(candidate.TypeName))
            .ToArray();
        HashSet<string> matched = filtered
            .SelectMany(candidate => new[] { candidate.ModelId, candidate.TypeName })
            .Where(filter.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] missing = filter
            .Where(candidate => !matched.Contains(candidate))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException("Candidate file contains unknown direct play-value candidates: " + string.Join(", ", missing));
        }

        return filtered;
    }

    private static DirectCandidateForm BuildDirectCandidateForm(
        string baseModelId,
        string baseTypeName,
        IReadOnlyList<string> pools,
        SimulationCard card,
        DirectPlayValueStrategy requestedStrategy)
    {
        IReadOnlyList<string> sourceCreditReasons = SourceCreditExclusionReasons(card);
        IReadOnlyList<string> playDeltaReasons = PlayDeltaExclusionReasons(card);
        DirectPlayValueStrategy? resolvedStrategy = requestedStrategy switch
        {
            DirectPlayValueStrategy.SourceCredit => sourceCreditReasons.Count == 0 ? DirectPlayValueStrategy.SourceCredit : null,
            DirectPlayValueStrategy.PlayDelta => playDeltaReasons.Count == 0 ? DirectPlayValueStrategy.PlayDelta : null,
            DirectPlayValueStrategy.Auto => sourceCreditReasons.Count == 0
                ? DirectPlayValueStrategy.SourceCredit
                : playDeltaReasons.Count == 0 && HasOnlyAllowedIncompleteAttribution(card)
                    ? DirectPlayValueStrategy.PlayDelta
                    : null,
            _ => null
        };
        List<string> reasons = [];
        if (resolvedStrategy is null)
        {
            reasons.AddRange(requestedStrategy switch
            {
                DirectPlayValueStrategy.SourceCredit => sourceCreditReasons,
                DirectPlayValueStrategy.PlayDelta => playDeltaReasons,
                DirectPlayValueStrategy.Auto => sourceCreditReasons.Concat(playDeltaReasons).Distinct(StringComparer.Ordinal),
                _ => ["Unsupported direct play-value strategy."]
            });
        }

        return new DirectCandidateForm(
            baseModelId,
            baseTypeName,
            pools,
            card.ModelId,
            card.TypeName,
            card.UpgradeLevel,
            resolvedStrategy,
            resolvedStrategy is not null,
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> SourceCreditExclusionReasons(SimulationCard card)
    {
        List<string> reasons = [];
        if (!card.IsPlayable)
        {
            reasons.Add("Card is not playable.");
        }

        reasons.AddRange(card.Warnings.Where(IsFloor8PlayValueBlockingWarning));
        return reasons;
    }

    private static IReadOnlyList<string> PlayDeltaExclusionReasons(SimulationCard card)
    {
        List<string> reasons = [];
        if (!card.IsPlayable)
        {
            reasons.Add("Card is not playable.");
        }

        foreach (string warning in card.Warnings)
        {
            if (warning.StartsWith("Unsupported simulation action", StringComparison.Ordinal)
                || warning.Contains("Generic calculated damage scaling requires manual review", StringComparison.Ordinal))
            {
                reasons.Add(warning);
                continue;
            }

            string? incompleteAction = IncompleteAttributionAction(warning);
            if (incompleteAction is not null
                && !DirectPlayDeltaAllowedIncompleteActions.Contains(incompleteAction))
            {
                reasons.Add(warning);
            }
        }

        return reasons;
    }

    private static bool HasOnlyAllowedIncompleteAttribution(SimulationCard card)
    {
        string[] incompleteActions = card.Warnings
            .Select(IncompleteAttributionAction)
            .Where(action => action is not null)
            .Select(action => action!)
            .ToArray();
        return incompleteActions.Length > 0
            && incompleteActions.All(DirectPlayDeltaAllowedIncompleteActions.Contains);
    }

    private static string? IncompleteAttributionAction(string warning)
    {
        const string prefix = "Attribution incomplete for action '";
        if (!warning.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int start = prefix.Length;
        int end = warning.IndexOf('\'', start);
        return end > start
            ? warning[start..end]
            : null;
    }

    private static DirectPlayHorizonSpec[] ParseDirectPlayHorizons(string value)
    {
        DirectPlayHorizonSpec[] horizons = value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item =>
            {
                string[] pieces = item.Split(':', 2, StringSplitOptions.TrimEntries);
                if (pieces.Length != 2 || !int.TryParse(pieces[1], out int turns) || turns <= 0)
                {
                    throw new InvalidOperationException("--horizons entries must look like shortline:4,midline:8.");
                }

                string key = pieces[0];
                if (key.Length == 0)
                {
                    throw new InvalidOperationException("--horizons keys must be non-empty.");
                }

                return new DirectPlayHorizonSpec(key, turns);
            })
            .ToArray();
        if (horizons.Length == 0)
        {
            throw new InvalidOperationException("--horizons must contain at least one horizon.");
        }

        if (horizons.Select(horizon => horizon.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != horizons.Length)
        {
            throw new InvalidOperationException("--horizons keys must be unique.");
        }

        return horizons;
    }

    private static bool HorizonsMatch(
        IReadOnlyList<DirectPlayHorizonSpec> left,
        IReadOnlyList<DirectPlayHorizonSpec> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left
            .OrderBy(horizon => horizon.Key, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                right.OrderBy(horizon => horizon.Key, StringComparer.OrdinalIgnoreCase),
                DirectPlayHorizonSpecComparer.Instance);
    }

    private static DirectPlayValueStrategy ParseDirectPlayValueStrategy(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "source-credit" or "sourcecredit" => DirectPlayValueStrategy.SourceCredit,
            "play-delta" or "playdelta" => DirectPlayValueStrategy.PlayDelta,
            "auto" => DirectPlayValueStrategy.Auto,
            _ => throw new InvalidOperationException("--value-strategy must be source-credit, play-delta, or auto.")
        };
    }

    private static string DirectPlayValueStrategyKey(DirectPlayValueStrategy strategy)
    {
        return strategy switch
        {
            DirectPlayValueStrategy.SourceCredit => "source-credit",
            DirectPlayValueStrategy.PlayDelta => "play-delta",
            DirectPlayValueStrategy.Auto => "auto",
            _ => throw new InvalidOperationException($"Unsupported direct play-value strategy {strategy}.")
        };
    }

    private static string BuildDirectProbeModelId(PreparedTrainingDeck deck, DirectCandidateForm form)
    {
        return string.Join(
            ".",
            "PROBE",
            "DIRECT",
            deck.Index.ToString(CultureInfo.InvariantCulture),
            DirectPlayValueStrategyKey(form.Strategy ?? throw new InvalidOperationException($"Form {form.ModelId} has no resolved play-value strategy.")),
            form.UpgradeLevel.ToString(CultureInfo.InvariantCulture),
            SanitizeProbeModelComponent(form.ModelId));
    }

    private static string BuildDirectPlayValueArchivePath(
        string outputRoot,
        DirectPlayValueMetadata metadata,
        string extension)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string fileName = string.Join(
            "_",
            timestamp,
            SanitizeFileComponent(metadata.Source),
            SanitizeFileComponent(metadata.DeckGroup),
            SanitizeFileComponent(metadata.ValueStrategy),
            $"d{metadata.ActiveDeckCount}",
            $"r{metadata.Runs}",
            $"b{metadata.MaxBranchingCards}",
            $"t{metadata.Turns}") + ".generated" + extension;
        return Path.Combine(outputRoot, fileName);
    }

    private static void WriteDirectPlayValueOutput(
        string outputJsonPath,
        string outputReportPath,
        DirectPlayValueMetadata metadata,
        IReadOnlyList<TrainingDeck> selectedDecks,
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        int baseCandidateCount,
        int allFormCount,
        int eligibleFormCount,
        IReadOnlyDictionary<string, DirectCardPlayValueOutput> cards,
        IReadOnlyList<DirectExcludedFormOutput> excludedForms,
        IReadOnlyList<string> warnings,
        JsonSerializerOptions jsonOptions)
    {
        DirectPlayValueOutput output = new(
            DirectPlayValueSchemaVersion,
            metadata,
            selectedDecks.Select(deck => new DirectSelectedDeckOutput(deck.RunId, deck.Group, deck.Floor, deck.Cards.Sum(card => card.Count))).ToArray(),
            preparedDecks.Select(deck => new DirectSelectedDeckOutput(deck.RunId, deck.Group, deck.Layer, deck.Cards.Count)).ToArray(),
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
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        WriteTextWithRetry(outputJsonPath, JsonSerializer.Serialize(output, jsonOptions));
        WriteTextWithRetry(outputReportPath, BuildDirectPlayValueReport(output));
    }

    private static string BuildDirectPlayValueReport(DirectPlayValueOutput output)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Direct Play Values");
        builder.AppendLine();
        builder.AppendLine($"Generated: {output.Metadata.GeneratedAt}");
        builder.AppendLine($"Deck group: {output.Metadata.DeckGroup}");
        builder.AppendLine($"Decks: {output.Metadata.ActiveDeckCount}/{output.Metadata.DeckCount} active");
        builder.AppendLine($"Runs: {output.Metadata.Runs}");
        builder.AppendLine($"Max branch: {output.Metadata.MaxBranchingCards}");
        builder.AppendLine($"Strategy: {output.Metadata.ValueStrategy}");
        builder.AppendLine($"Forms: {output.EligibleFormCount}/{output.AllFormCount} eligible");
        builder.AppendLine();
        builder.AppendLine("| Card | Upgrade | Strategy | Horizon | Value/play | Plays | Valid decks | Invalid decks |");
        builder.AppendLine("|---|---:|---|---|---:|---:|---:|---:|");
        foreach (DirectCardPlayValueOutput card in output.Cards.Values.OrderBy(card => card.TypeName, StringComparer.Ordinal))
        {
            AppendDirectReportRows(builder, card.TypeName, 0, card.Unupgraded);
            AppendDirectReportRows(builder, card.TypeName, 1, card.Upgraded);
        }

        builder.AppendLine();
        builder.AppendLine("## Excluded Forms");
        builder.AppendLine();
        foreach (DirectExcludedFormOutput excluded in output.ExcludedForms)
        {
            builder.AppendLine($"- {excluded.ModelId} {excluded.TypeName}: {string.Join("; ", excluded.Reasons)}");
        }

        return builder.ToString();
    }

    private static void AppendDirectReportRows(
        StringBuilder builder,
        string typeName,
        int upgrade,
        DirectFormPlayValueOutput? form)
    {
        if (form is null)
        {
            return;
        }

        foreach (KeyValuePair<string, DirectHorizonAggregate> horizon in form.Horizons.OrderBy(pair => pair.Value.Turns).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {typeName} | {upgrade} | {form.Strategy} | {horizon.Key} | {FormatNullableDecimal(horizon.Value.WeightedValuePerPlay)} | {horizon.Value.PlayCount} | {horizon.Value.ValidDecks} | {horizon.Value.InvalidDecks} |");
        }
    }

    private enum DirectPlayValueStrategy
    {
        SourceCredit,
        PlayDelta,
        Auto
    }

    private sealed record DirectPlayHorizonSpec(
        string Key,
        int Turns);

    private sealed class DirectPlayHorizonSpecComparer : IEqualityComparer<DirectPlayHorizonSpec>
    {
        public static readonly DirectPlayHorizonSpecComparer Instance = new();

        public bool Equals(DirectPlayHorizonSpec? x, DirectPlayHorizonSpec? y)
        {
            return x is not null
                && y is not null
                && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase)
                && x.Turns == y.Turns;
        }

        public int GetHashCode(DirectPlayHorizonSpec obj)
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key), obj.Turns);
        }
    }

    private sealed record DirectCandidateForm(
        string BaseModelId,
        string BaseTypeName,
        IReadOnlyList<string> Pools,
        string ModelId,
        string TypeName,
        int UpgradeLevel,
        DirectPlayValueStrategy? Strategy,
        bool Eligible,
        IReadOnlyList<string> ExclusionReasons);

    private sealed record DirectPlayValueMetadata(
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
        string ValueStrategy,
        IReadOnlyList<DirectPlayHorizonSpec> Horizons,
        int SkipForms,
        int? LimitForms,
        string Note);

    private sealed record DirectPlayValueOutput(
        int SchemaVersion,
        DirectPlayValueMetadata Metadata,
        IReadOnlyList<DirectSelectedDeckOutput> SelectedDecks,
        IReadOnlyList<DirectSelectedDeckOutput> ActiveDecks,
        int BaseCandidateCount,
        int AllFormCount,
        int EligibleFormCount,
        IReadOnlyDictionary<string, DirectCardPlayValueOutput> Cards,
        IReadOnlyList<DirectExcludedFormOutput> ExcludedForms,
        IReadOnlyList<string> Warnings);

    private sealed record DirectSelectedDeckOutput(
        string RunId,
        string Group,
        int? Floor,
        int CardCount);

    private sealed record DirectCardPlayValueOutput(
        string ModelId,
        string TypeName,
        IReadOnlyList<string> Pools,
        DirectFormPlayValueOutput? Unupgraded,
        DirectFormPlayValueOutput? Upgraded);

    private sealed record DirectFormPlayValueOutput(
        string ModelId,
        string TypeName,
        int UpgradeLevel,
        string Strategy,
        IReadOnlyDictionary<string, DirectHorizonAggregate> Horizons,
        IReadOnlyList<DirectDeckFormResult> DeckResults);

    private sealed record DirectDeckFormResult(
        int DeckIndex,
        string RunId,
        string Group,
        int Layer,
        string ProbeModelId,
        string Strategy,
        IReadOnlyDictionary<string, DirectHorizonPlayValue> Horizons);

    private sealed record DirectHorizonPlayValue(
        string Key,
        int Turns,
        string Strategy,
        decimal? BlockedPrefixExpectedValue,
        decimal? NormalPrefixExpectedValue,
        decimal? DeltaExpectedValue,
        decimal? RunScaledDeltaValue,
        int NormalPlayCount,
        int BlockedPlayCount,
        decimal? DirectValue,
        decimal? ForgeRealizedValue,
        decimal? PowerRealizedValue,
        decimal? EnergyRealizedValue,
        decimal? StarRealizedValue,
        decimal TotalValue,
        decimal? ValuePerPlay,
        bool Valid,
        IReadOnlyList<string> Warnings);

    private sealed record DirectHorizonAggregate(
        int Turns,
        int PlayCount,
        decimal TotalValue,
        decimal? WeightedValuePerPlay,
        decimal? SampleMeanValuePerPlay,
        int ValidDecks,
        int InvalidDecks);

    private sealed record DirectExcludedFormOutput(
        string BaseModelId,
        string ModelId,
        string TypeName,
        int UpgradeLevel,
        IReadOnlyList<string> Reasons);
}
