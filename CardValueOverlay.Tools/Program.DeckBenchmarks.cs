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
    private static int BenchmarkTrainingDecks(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string trainingDecksPath = GetOption(args, "--training-decks")
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_16_decks.json");
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string cardSetupValuesPath = GetOption(args, "--card-setup-values")
            ?? Path.Combine(outputRoot, "manual-tags", "card_setup_values.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        int runs = GetIntOption(args, "--runs") ?? 40;
        int turns = GetIntOption(args, "--turns") ?? TrainingHorizonTurnCounts.Longline;
        int seed = GetIntOption(args, "--seed") ?? 1;
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays")
            ?? DeckSimulationOptions.DefaultResolvedPlaySafetyCap;
        int maxBranchingCards = GetIntOption(args, "--max-branch")
            ?? DeckSimulationOptions.DefaultBranchWidth;
        int maxFullyBranchedCardsPlayed = GetIntOption(args, "--max-full-branch-plays")
            ?? DeckSimulationOptions.DefaultFullBranchDecisionDepth;
        int maxDeterministicPlayChain = GetIntOption(args, "--max-deterministic-chain")
            ?? DeckSimulationOptions.DefaultDeterministicPlayChainCap;
        int maxSearchNodes = GetIntOption(args, "--max-search-nodes")
            ?? DeckSimulationOptions.DefaultSearchNodeBudgetPerTurn;
        int transpositionCapacity = GetIntOption(args, "--transposition-capacity")
            ?? DeckSimulationOptions.DefaultTranspositionCapacityPerTurn;
        int degreeOfParallelism = Math.Max(1, GetIntOption(args, "--degree-of-parallelism") ?? 1);
        int runDegree = Math.Max(1, GetIntOption(args, "--run-degree") ?? 4);
        int? limitDecks = GetIntOption(args, "--limit-decks");
        int skipDecks = Math.Max(0, GetIntOption(args, "--skip-decks") ?? 0);
        bool profile = HasFlag(args, "--profile");
        bool collectSearchBranchDiagnostics = HasFlag(args, "--search-branch-diagnostics");
        bool collectSlowTailProfile = HasFlag(args, "--slow-tail-profile");
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);
        IStateValueEstimator? stateValueEstimator = LoadStateValueEstimator(args);

        if (runs <= 0)
        {
            return Fail("--runs must be positive.");
        }

        if (turns <= 0)
        {
            return Fail("--turns must be positive.");
        }

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

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        string benchmarkRoot = Path.Combine(generatedRoot, "deck_benchmarks");
        Directory.CreateDirectory(benchmarkRoot);
        string outputJsonPath = GetOption(args, "--output-json")
            ?? Path.Combine(benchmarkRoot, "latest.generated.json");
        string outputReportPath = GetOption(args, "--output-md")
            ?? Path.ChangeExtension(outputJsonPath, ".md");

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        CardSetupValueCatalog cardSetupValues = CardSetupValueCatalog.LoadOrEmpty(cardSetupValuesPath, jsonOptions);
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
            return Fail("Training deck file did not contain any active decks.");
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
                setupValues: cardSetupValues));
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
        List<PreparedTrainingDeck> preparedDecks = trainingDecks
            .Select((deck, index) => PrepareTrainingDeck(deck, skipDecks + index, byModelIdByLayer, byTypeNameByLayer))
            .ToList();

        ConcurrentBag<TrainingDeckBenchmarkDeckOutput> results = [];
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        int completed = 0;
        Action<PreparedTrainingDeck> benchmarkDeck = deck =>
        {
            SearchBranchDiagnosticsCollector? branchDiagnostics = collectSearchBranchDiagnostics || collectSlowTailProfile
                ? new SearchBranchDiagnosticsCollector()
                : null;
            SearchSlowTailProfiler? slowTailProfiler = collectSlowTailProfile
                ? new SearchSlowTailProfiler()
                : null;
            int effectiveRunDegree = degreeOfParallelism > 1 && preparedDecks.Count > 1
                ? 1
                : runDegree;
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
                    effectiveRunDegree,
                    searchCardScorer)
                with
                {
                    CollectAttribution = false,
                    MaxFullyBranchedCardsPlayedPerTurn = maxFullyBranchedCardsPlayed,
                    MaxDeterministicPlayChain = maxDeterministicPlayChain,
                    MaxSearchNodesPerTurn = maxSearchNodes,
                    TranspositionCapacityPerTurn = transpositionCapacity,
                    StateValue = stateValueEstimator,
                    SearchBranchDiagnostics = branchDiagnostics,
                    SlowTailProfiler = slowTailProfiler
            };
            Stopwatch stopwatch = Stopwatch.StartNew();
            DeckMonteCarloSimulator simulator = new();
            IReadOnlyList<decimal> values = simulator.SimulateExpectedTurnValues(deck.Cards, options);
            stopwatch.Stop();
            decimal totalValue = values.Take(turns).Sum();
            results.Add(new TrainingDeckBenchmarkDeckOutput(
                deck.Index,
                deck.RunId,
                deck.Group,
                deck.Layer,
                deck.Cards.Count,
                RoundSeconds(stopwatch.Elapsed.TotalSeconds),
                Round(totalValue),
                Round(totalValue / turns),
                branchDiagnostics?.Snapshot(),
                slowTailProfiler?.Snapshot()));
            int done = Interlocked.Increment(ref completed);
            if (profile)
            {
                Console.WriteLine($"deck benchmark {done}/{preparedDecks.Count}: deck={deck.Index} runId={deck.RunId} group={deck.Group} layer={deck.Layer} cards={deck.Cards.Count} elapsedSeconds={stopwatch.Elapsed.TotalSeconds:0.###}");
            }
        };

        if (degreeOfParallelism <= 1)
        {
            foreach (PreparedTrainingDeck deck in preparedDecks)
            {
                benchmarkDeck(deck);
            }
        }
        else
        {
            Parallel.ForEach(
                preparedDecks,
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                benchmarkDeck);
        }

        totalStopwatch.Stop();
        SearchBranchDiagnosticsSnapshot? aggregateBranchDiagnostics = collectSearchBranchDiagnostics || collectSlowTailProfile
            ? AggregateSearchBranchDiagnostics(results.Select(result => result.SearchBranchDiagnostics))
            : null;
        TrainingDeckBenchmarkOutput output = new(
            2,
            new TrainingDeckBenchmarkMetadata(
                "training_deck_benchmark_20260630",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                trainingDecksPath,
                trainingDecks.Count,
                runs,
                turns,
                seed,
                maxCardsPlayed,
                maxBranchingCards,
                maxFullyBranchedCardsPlayed,
                maxDeterministicPlayChain,
                maxSearchNodes,
                transpositionCapacity,
                degreeOfParallelism,
                runDegree,
                RoundSeconds(totalStopwatch.Elapsed.TotalSeconds),
                aggregateBranchDiagnostics,
                "One baseline Monte Carlo simulation per selected deck. Used for speed screening only; no candidate card is added."),
            results.OrderBy(deck => deck.DeckIndex).ToArray());
        WriteTextWithRetry(outputJsonPath, JsonSerializer.Serialize(output, jsonOptions));
        WriteTextWithRetry(outputReportPath, BuildTrainingDeckBenchmarkReport(output));

        Console.WriteLine("training deck benchmark complete");
        Console.WriteLine($"trainingDecks: {preparedDecks.Count}");
        Console.WriteLine($"runs: {runs}");
        Console.WriteLine($"turns: {turns}");
        Console.WriteLine($"maxBranch: {maxBranchingCards}");
        Console.WriteLine($"maxFullBranchPlays: {maxFullyBranchedCardsPlayed}");
        Console.WriteLine($"maxDeterministicChain: {maxDeterministicPlayChain}");
        Console.WriteLine($"maxSearchNodes: {maxSearchNodes}");
        Console.WriteLine($"transpositionCapacity: {transpositionCapacity}");
        Console.WriteLine($"degreeOfParallelism: {degreeOfParallelism}");
        Console.WriteLine($"runDegree: {runDegree}");
        Console.WriteLine($"elapsedSeconds: {totalStopwatch.Elapsed.TotalSeconds:0.###}");
        if (aggregateBranchDiagnostics is not null)
        {
            Console.WriteLine($"averageSelectedBranches: {aggregateBranchDiagnostics.AverageSelectedBranches:0.###}");
            Console.WriteLine($"averageFullyBranchedSelectedBranches: {aggregateBranchDiagnostics.AverageFullyBranchedSelectedBranches:0.###}");
            Console.WriteLine($"averageExtraBranches: {aggregateBranchDiagnostics.AverageExtraBranches:0.###}");
            Console.WriteLine($"extraAdmissionNodeRate: {aggregateBranchDiagnostics.ExtraAdmissionNodeRate:P3}");
            Console.WriteLine($"selectedBranchP95: {aggregateBranchDiagnostics.SelectedBranchP95}");
            Console.WriteLine($"fullyBranchedSelectedBranchP95: {aggregateBranchDiagnostics.FullyBranchedSelectedBranchP95}");
            Console.WriteLine($"maxSelectedBranches: {aggregateBranchDiagnostics.MaxSelectedBranches}");
        }
        Console.WriteLine($"output: {outputJsonPath}");
        Console.WriteLine($"report: {outputReportPath}");
        return 0;
    }

    private static string BuildTrainingDeckBenchmarkReport(TrainingDeckBenchmarkOutput output)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Training Deck Benchmark");
        builder.AppendLine();
        builder.AppendLine($"Generated: {output.Metadata.GeneratedAt}");
        builder.AppendLine($"Decks: {output.Metadata.DeckCount}");
        builder.AppendLine($"Runs: {output.Metadata.Runs}");
        builder.AppendLine($"Turns: {output.Metadata.Turns}");
        builder.AppendLine($"Max branch: {output.Metadata.MaxBranchingCards}");
        builder.AppendLine($"Max full-branch decisions: {output.Metadata.MaxFullyBranchedCardsPlayedPerTurn}");
        builder.AppendLine($"Max deterministic chain: {output.Metadata.MaxDeterministicPlayChain}");
        builder.AppendLine($"Max search nodes per turn: {output.Metadata.MaxSearchNodesPerTurn}");
        builder.AppendLine($"Transposition capacity: {output.Metadata.TranspositionCapacityPerTurn}");
        builder.AppendLine($"Elapsed seconds: {output.Metadata.ElapsedSeconds:0.###}");
        if (output.Metadata.SearchBranchDiagnostics is { } diagnostics)
        {
            builder.AppendLine($"Average selected branches: {diagnostics.AverageSelectedBranches:0.###}");
            builder.AppendLine($"Average selected branches during full branching: {diagnostics.AverageFullyBranchedSelectedBranches:0.###}");
            builder.AppendLine($"Average +k branches: {diagnostics.AverageExtraBranches:0.###}");
            builder.AppendLine($"Nodes with +k admission: {diagnostics.ExtraAdmissionNodeRate:P3}");
            builder.AppendLine($"Forced plays: {diagnostics.ForcedPlayNodes}");
            builder.AppendLine($"Detected loop states / resource-positive / pruned: {diagnostics.LoopDetectionHits} / {diagnostics.PositiveResourceLoopHits} / {diagnostics.PrunedLoopHits}");
            builder.AppendLine($"Search nodes / state clones / play-trace nodes: {diagnostics.SearchNodes} / {diagnostics.StateClones} / {diagnostics.PlayTraceNodes}");
            builder.AppendLine($"Work-budget fallback nodes: {diagnostics.WorkBudgetFallbackNodes}");
            builder.AppendLine($"Deterministic max chain: {diagnostics.MaxDeterministicChain}");
            builder.AppendLine($"Transposition lookups / hits / stores / hit rate: {diagnostics.TranspositionLookups} / {diagnostics.TranspositionHits} / {diagnostics.TranspositionStores} / {diagnostics.TranspositionHitRate:P3}");
            builder.AppendLine($"Selected branch p95 / full-branch p95 / max: {diagnostics.SelectedBranchP95} / {diagnostics.FullyBranchedSelectedBranchP95} / {diagnostics.MaxSelectedBranches}");
        }
        builder.AppendLine();
        builder.AppendLine("| Deck | RunId | Group | Layer | Cards | Seconds | EV/turn | Total EV |");
        builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|");
        foreach (TrainingDeckBenchmarkDeckOutput deck in output.Decks.OrderBy(deck => deck.DeckIndex))
        {
            builder.AppendLine(
                $"| {deck.DeckIndex} | {EscapeMarkdown(deck.RunId)} | {EscapeMarkdown(deck.Group)} | {deck.Layer} | {deck.CardCount} | {deck.ElapsedSeconds:0.###} | {deck.ExpectedValuePerTurn:0.###} | {deck.TotalExpectedValue:0.###} |");
        }

        foreach (TrainingDeckBenchmarkDeckOutput deck in output.Decks
                     .Where(value => value.SlowTailProfile is not null)
                     .OrderBy(value => value.DeckIndex))
        {
            AppendSlowTailProfile(builder, deck);
        }

        return builder.ToString();
    }

    private static void AppendSlowTailProfile(
        StringBuilder builder,
        TrainingDeckBenchmarkDeckOutput deck)
    {
        SearchTurnProfileSnapshot[] turns = deck.SlowTailProfile!.Turns.ToArray();
        if (turns.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## Slow-tail profile: deck {deck.DeckIndex} / {EscapeMarkdown(deck.RunId)}");
        builder.AppendLine();

        var runs = turns
            .GroupBy(value => value.Run)
            .Select(group => new
            {
                Run = group.Key,
                Milliseconds = group.Sum(value => value.ElapsedMilliseconds),
                Nodes = group.Sum(value => value.SearchNodes),
                Decisions = group.Sum(value => value.DecisionNodes),
                Clones = group.Sum(value => value.StateClones),
                Fallback = group.Sum(value => value.WorkBudgetFallbackNodes),
                Loops = group.Sum(value => value.LoopDetectionHits),
                Generated = group.Sum(value => value.GeneratedCards)
            })
            .OrderByDescending(value => value.Milliseconds)
            .ToArray();
        int slowRunCount = Math.Max(1, (int)Math.Ceiling(runs.Length * 0.01d));
        builder.AppendLine($"Slowest 1% runs ({slowRunCount} of {runs.Length}):");
        builder.AppendLine();
        builder.AppendLine("| Run | Milliseconds | Nodes | Decisions | Clones | Fallback | Loops | Generated |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var run in runs.Take(slowRunCount))
        {
            builder.AppendLine($"| {run.Run} | {run.Milliseconds:0.###} | {run.Nodes} | {run.Decisions} | {run.Clones} | {run.Fallback} | {run.Loops} | {run.Generated} |");
        }

        int slowTurnCount = Math.Max(1, (int)Math.Ceiling(turns.Length * 0.01d));
        builder.AppendLine();
        builder.AppendLine($"Slowest 1% run-turns ({slowTurnCount} of {turns.Length}):");
        builder.AppendLine();
        builder.AppendLine("| Run | Turn | Milliseconds | EV | Cards | Nodes | Greedy | Forced | Clones | Fallback | Loops | Pruned | Generated |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (SearchTurnProfileSnapshot turn in turns
                     .OrderByDescending(value => value.ElapsedMilliseconds)
                     .Take(slowTurnCount))
        {
            builder.AppendLine($"| {turn.Run} | {turn.Turn} | {turn.ElapsedMilliseconds:0.###} | {turn.ExpectedValue:0.###} | {turn.CardsPlayed} | {turn.SearchNodes} | {turn.GreedyDecisionNodes} | {turn.ForcedPlayNodes} | {turn.StateClones} | {turn.WorkBudgetFallbackNodes} | {turn.LoopDetectionHits} | {turn.PrunedLoopHits} | {turn.GeneratedCards} |");
        }

        var cardHotspots = turns
            .SelectMany(value => value.CardHotspots)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                Card = group.Key,
                Evaluations = group.Sum(value => value.Value.Evaluations),
                DescendantNodes = group.Sum(value => value.Value.TotalDescendantNodes),
                MaximumSubtree = group.Max(value => value.Value.MaximumDescendantNodes)
            })
            .OrderByDescending(value => value.DescendantNodes)
            .Take(12)
            .ToArray();
        builder.AppendLine();
        builder.AppendLine("Top candidate cards by inclusive descendant nodes:");
        builder.AppendLine();
        builder.AppendLine("| Card | Evaluations | Inclusive descendant nodes | Max subtree nodes |");
        builder.AppendLine("|---|---:|---:|---:|");
        foreach (var card in cardHotspots)
        {
            builder.AppendLine($"| {EscapeMarkdown(card.Card)} | {card.Evaluations} | {card.DescendantNodes} | {card.MaximumSubtree} |");
        }

        var powerHotspots = turns
            .SelectMany(value => value.ActivePowerExposures)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new { Power = group.Key, Exposures = group.Sum(value => value.Value) })
            .OrderByDescending(value => value.Exposures)
            .Take(12)
            .ToArray();
        if (powerHotspots.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Top active Powers by evaluated-play exposure:");
            builder.AppendLine();
            builder.AppendLine("| Power | Exposures |");
            builder.AppendLine("|---|---:|");
            foreach (var power in powerHotspots)
            {
                builder.AppendLine($"| {EscapeMarkdown(power.Power)} | {power.Exposures} |");
            }
        }

        var generatedPools = turns
            .SelectMany(value => value.GeneratedPools)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                Pool = group.Key,
                Events = group.Sum(value => value.Value.Events),
                Cards = group.Sum(value => value.Value.RequestedCards)
            })
            .OrderByDescending(value => value.Events)
            .Take(12)
            .ToArray();
        if (generatedPools.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Generated-card pools:");
            builder.AppendLine();
            builder.AppendLine("| Pool | Events | Requested cards |");
            builder.AppendLine("|---|---:|---:|");
            foreach (var pool in generatedPools)
            {
                builder.AppendLine($"| {EscapeMarkdown(pool.Pool)} | {pool.Events} | {pool.Cards} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Largest retained search paths (the retained set is the node-heavy tail):");
        builder.AppendLine();
        builder.AppendLine("| Run | Turn | Card | Play depth | Milliseconds | Descendant nodes | Candidate path |");
        builder.AppendLine("|---:|---:|---|---:|---:|---:|---|");
        foreach (var candidate in turns
                     .SelectMany(value => value.LargestCandidateSubtrees.Select(subtree => new
                     {
                         value.Run,
                         value.Turn,
                         Subtree = subtree
                     }))
                     .OrderByDescending(value => value.Subtree.DescendantNodes)
                     .Take(20))
        {
            string path = string.Join(" -> ", candidate.Subtree.CandidatePath);
            builder.AppendLine($"| {candidate.Run} | {candidate.Turn} | {EscapeMarkdown(candidate.Subtree.CardTypeName)} | {candidate.Subtree.ResolvedPlayDepth} | {candidate.Subtree.ElapsedMilliseconds:0.###} | {candidate.Subtree.DescendantNodes} | {EscapeMarkdown(path)} |");
        }
    }

    private sealed record TrainingDeckBenchmarkOutput(
        int SchemaVersion,
        TrainingDeckBenchmarkMetadata Metadata,
        IReadOnlyList<TrainingDeckBenchmarkDeckOutput> Decks);

    private sealed record TrainingDeckBenchmarkMetadata(
        string Source,
        string GeneratedAt,
        string TrainingDecksPath,
        int DeckCount,
        int Runs,
        int Turns,
        int Seed,
        int MaxCardsPlayedPerTurn,
        int MaxBranchingCards,
        int MaxFullyBranchedCardsPlayedPerTurn,
        int MaxDeterministicPlayChain,
        int MaxSearchNodesPerTurn,
        int TranspositionCapacityPerTurn,
        int DegreeOfParallelism,
        int RunDegree,
        double ElapsedSeconds,
        SearchBranchDiagnosticsSnapshot? SearchBranchDiagnostics,
        string Note);

    private sealed record TrainingDeckBenchmarkDeckOutput(
        int DeckIndex,
        string RunId,
        string Group,
        int Layer,
        int CardCount,
        double ElapsedSeconds,
        decimal TotalExpectedValue,
        decimal ExpectedValuePerTurn,
        SearchBranchDiagnosticsSnapshot? SearchBranchDiagnostics,
        SearchSlowTailProfileSnapshot? SlowTailProfile);

    private static SearchBranchDiagnosticsSnapshot AggregateSearchBranchDiagnostics(
        IEnumerable<SearchBranchDiagnosticsSnapshot?> snapshots)
    {
        SearchBranchDiagnosticsSnapshot[] values = snapshots.OfType<SearchBranchDiagnosticsSnapshot>().ToArray();
        Dictionary<int, long> histogram = MergeHistograms(values.Select(value => value.SelectedBranchHistogram));
        Dictionary<int, long> fullyBranchedHistogram = MergeHistograms(
            values.Select(value => value.FullyBranchedSelectedBranchHistogram));
        return new SearchBranchDiagnosticsSnapshot(
            values.Sum(value => value.DecisionNodes),
            values.Sum(value => value.FullyBranchedDecisionNodes),
            values.Sum(value => value.GreedyDecisionNodes),
            values.Sum(value => value.BaseBranches),
            values.Sum(value => value.SelectedBranches),
            values.Sum(value => value.ExtraBranches),
            values.Sum(value => value.FullyBranchedBaseBranches),
            values.Sum(value => value.FullyBranchedSelectedBranches),
            values.Sum(value => value.FullyBranchedExtraBranches),
            values.Sum(value => value.ExtraAdmissionNodes),
            values.Sum(value => value.ForcedPlayNodes),
            values.Sum(value => value.LoopDetectionHits),
            values.Sum(value => value.PositiveResourceLoopHits),
            values.Sum(value => value.PrunedLoopHits),
            values.Sum(value => value.SearchNodes),
            values.Sum(value => value.StateClones),
            values.Sum(value => value.PlayTraceNodes),
            values.Sum(value => value.WorkBudgetFallbackNodes),
            values.Sum(value => value.TranspositionLookups),
            values.Sum(value => value.TranspositionHits),
            values.Sum(value => value.TranspositionStores),
            values.Length == 0 ? 0 : values.Max(value => value.MaxDeterministicChain),
            values.Length == 0 ? 0 : values.Max(value => value.MaxSelectedBranches),
            histogram,
            fullyBranchedHistogram);
    }

    private static Dictionary<int, long> MergeHistograms(
        IEnumerable<IReadOnlyDictionary<int, long>> histograms)
    {
        Dictionary<int, long> result = [];
        foreach (IReadOnlyDictionary<int, long> histogram in histograms)
        {
            foreach (KeyValuePair<int, long> bucket in histogram)
            {
                result[bucket.Key] = result.GetValueOrDefault(bucket.Key) + bucket.Value;
            }
        }

        return result;
    }
}
