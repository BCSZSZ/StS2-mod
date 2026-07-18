using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private static int AnalyzeStarPlay(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string deckSourcePath = GetOption(args, "--deck-source")
            ?? Path.Combine("history-analysis", "data", "dashen_77_all_308_decks.json");
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string cardSetupValuesPath = GetOption(args, "--card-setup-values")
            ?? Path.Combine(outputRoot, "manual-tags", "card_setup_values.json");
        string autoPlayEffectsPath = GetOption(args, "--card-autoplay-effects")
            ?? Path.Combine(outputRoot, "manual-tags", "card_autoplay_effects.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        string[] groups = SplitCsvOption(
            GetOption(args, "--deck-groups") ?? "floor8,act2Start,preAct2Boss,final");
        string[] runIds = SplitCsvOption(GetOption(args, "--run-ids") ?? string.Empty);
        int decksPerGroup = Math.Max(1, GetIntOption(args, "--decks-per-group") ?? 2);
        int minStarGainCards = Math.Max(1, GetIntOption(args, "--min-star-gain-cards") ?? 3);
        int minStarCostCards = Math.Max(1, GetIntOption(args, "--min-star-cost-cards") ?? 3);
        int[] horizons = SplitCsvOption(GetOption(args, "--turns") ?? "4,8,12")
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
            .Where(value => value > 0)
            .Distinct()
            .Order()
            .ToArray();
        int runs = Math.Max(1, GetIntOption(args, "--runs") ?? 50);
        int seed = GetIntOption(args, "--seed") ?? 1;
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays")
            ?? DeckSimulationOptions.DefaultResolvedPlaySafetyCap;
        int maxBranchingCards = GetIntOption(args, "--max-branch")
            ?? DeckSimulationOptions.DefaultBranchWidth;
        int runDegree = Math.Max(1, GetIntOption(args, "--run-degree") ?? 4);

        if (groups.Length == 0 || horizons.Length == 0)
        {
            return Fail("analyze-star-play requires at least one deck group and one positive turn horizon.");
        }

        foreach (string path in new[] { deckSourcePath, factsPath, calibrationPath })
        {
            if (!File.Exists(path))
            {
                return Fail($"Missing required star-play analysis input at {path}.");
            }
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        CardSetupValueCatalog cardSetupValues = CardSetupValueCatalog.LoadOrEmpty(cardSetupValuesPath, jsonOptions);
        IReadOnlyList<AutoPlayEffectEntry> autoPlayEffects = LoadOptionalAutoPlayEffects(autoPlayEffectsPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        TrainingDeckFile source =
            JsonSerializer.Deserialize<TrainingDeckFile>(File.ReadAllText(deckSourcePath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read training decks from {deckSourcePath}.");

        IReadOnlyList<(TrainingDeck Deck, int SourceIndex)> selected = SelectStarAnalysisDecks(
            source.Decks,
            groups,
            runIds,
            decksPerGroup);
        int[] layers = selected
            .Select(item => TrainingLayer(item.Deck))
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
                autoPlayEffects,
                cardSetupValues));
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
        IReadOnlyList<(TrainingDeck Source, PreparedTrainingDeck Prepared)> prepared = selected
            .Select(item => (
                item.Deck,
                PrepareTrainingDeck(item.Deck, item.SourceIndex, byModelIdByLayer, byTypeNameByLayer)))
            .ToArray();

        IReadOnlyList<StarAnalysisDeckProfile> deckProfiles = prepared
            .Select(item => BuildStarAnalysisDeckProfile(item.Source, item.Prepared))
            .ToArray();
        foreach (StarAnalysisDeckProfile deck in deckProfiles)
        {
            if (deck.StarGainCardCount < minStarGainCards || deck.StarCostCardCount < minStarCostCards)
            {
                return Fail(
                    $"Selected {deck.Group}/{deck.RunId} has {deck.StarGainCardCount} star-gain and "
                    + $"{deck.StarCostCardCount} star-cost cards; required at least "
                    + $"{minStarGainCards}/{minStarCostCards}.");
            }
        }

        List<StarAnalysisSimulationResult> results = [];
        int totalSimulations = prepared.Count * horizons.Length;
        int completed = 0;
        foreach ((TrainingDeck _, PreparedTrainingDeck deck) in prepared)
        {
            foreach (int turns in horizons)
            {
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
                    runDegree) with
                {
                    Seed = seed + deck.Index * 1009,
                    CollectAttribution = false,
                    CollectStarPlayDiagnostics = true
                };
                Stopwatch stopwatch = Stopwatch.StartNew();
                DeckSimulationReport report = new DeckMonteCarloSimulator().Simulate(deck.Cards, options);
                stopwatch.Stop();
                StarPlayDiagnosticsReport diagnostics = report.StarPlayDiagnostics
                    ?? throw new InvalidOperationException("Star-play diagnostics were not produced.");
                results.Add(new StarAnalysisSimulationResult(
                    deck.RunId,
                    deck.Group,
                    deck.Layer,
                    deck.Cards.Count,
                    turns,
                    report.TotalExpectedValue,
                    Round(report.TotalExpectedValue / turns),
                    Math.Round(stopwatch.Elapsed.TotalSeconds, 3, MidpointRounding.AwayFromZero),
                    diagnostics));
                completed++;
                Console.WriteLine(
                    $"star-play {completed}/{totalSimulations}: group={deck.Group} runId={deck.RunId} "
                    + $"turns={turns} runs={runs} elapsedSeconds={stopwatch.Elapsed.TotalSeconds:0.###}");
            }
        }

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated", "star_play_analysis");
        Directory.CreateDirectory(generatedRoot);
        string jsonPath = GetOption(args, "--output-json")
            ?? Path.Combine(generatedRoot, "latest.generated.json");
        string markdownPath = GetOption(args, "--output-md")
            ?? Path.Combine(generatedRoot, "latest.generated.md");
        string selectedDecksPath = GetOption(args, "--selected-decks-output")
            ?? Path.Combine(generatedRoot, "selected_decks.generated.json");
        StarAnalysisOutput output = new(
            1,
            DateTimeOffset.UtcNow.ToString("O"),
            Path.GetFullPath(deckSourcePath),
            groups,
            runIds,
            runs,
            horizons,
            seed,
            handSize,
            maxHandSize,
            baseEnergy,
            baseStars,
            maxBranchingCards,
            minStarGainCards,
            minStarCostCards,
            deckProfiles,
            results);
        WriteTextWithRetry(jsonPath, JsonSerializer.Serialize(output, jsonOptions));
        WriteTextWithRetry(markdownPath, BuildStarAnalysisMarkdown(output));
        WriteTextWithRetry(
            selectedDecksPath,
            JsonSerializer.Serialize(new TrainingDeckFile(selected.Select(item => item.Deck).ToArray()), jsonOptions));

        Console.WriteLine("star-play analysis complete");
        Console.WriteLine($"selected decks: {selectedDecksPath}");
        Console.WriteLine($"json: {jsonPath}");
        Console.WriteLine($"markdown: {markdownPath}");
        return 0;
    }

    private static IReadOnlyList<(TrainingDeck Deck, int SourceIndex)> SelectStarAnalysisDecks(
        IReadOnlyList<TrainingDeck> decks,
        IReadOnlyList<string> groups,
        IReadOnlyList<string> runIds,
        int decksPerGroup)
    {
        IReadOnlyList<(TrainingDeck Deck, int SourceIndex)> indexed = decks
            .Select((deck, index) => (deck, index))
            .ToArray();
        List<(TrainingDeck Deck, int SourceIndex)> selected = [];
        foreach (string group in groups)
        {
            IReadOnlyList<(TrainingDeck Deck, int SourceIndex)> groupDecks = indexed
                .Where(item => string.Equals(item.Deck.Group, group, StringComparison.OrdinalIgnoreCase))
                .Where(item => runIds.Count == 0 || runIds.Contains(item.Deck.RunId, StringComparer.OrdinalIgnoreCase))
                .OrderBy(item => item.Deck.RunId, StringComparer.Ordinal)
                .ToArray();
            if (groupDecks.Count < decksPerGroup)
            {
                throw new InvalidOperationException(
                    $"Deck source has only {groupDecks.Count} requested decks in group {group}; "
                    + $"{decksPerGroup} are required.");
            }

            selected.AddRange(groupDecks.Take(decksPerGroup));
        }

        return selected;
    }

    private static StarAnalysisDeckProfile BuildStarAnalysisDeckProfile(
        TrainingDeck source,
        PreparedTrainingDeck prepared)
    {
        IReadOnlyList<StarAnalysisDeckCard> starCards = prepared.Cards
            .Where(card => GainsStars(card) || CostsStars(card))
            .GroupBy(card => card.ReportModelId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                SimulationCard card = group.First();
                return new StarAnalysisDeckCard(
                    card.ReportModelId,
                    card.ReportTypeName,
                    group.Count(),
                    GainsStars(card),
                    CostsStars(card),
                    card.StarGain,
                    card.StarNextTurn,
                    card.StarCost);
            })
            .OrderBy(card => card.TypeName, StringComparer.Ordinal)
            .ToArray();
        return new StarAnalysisDeckProfile(
            source.RunId,
            source.Group,
            prepared.Layer,
            prepared.Cards.Count,
            starCards.Where(card => card.GainsStars).Sum(card => card.Count),
            starCards.Where(card => card.CostsStars).Sum(card => card.Count),
            prepared.Cards.Count(card => card.Warnings.Any(warning =>
                warning.StartsWith("Unsupported simulation action", StringComparison.Ordinal))),
            starCards);
    }

    private static string BuildStarAnalysisMarkdown(StarAnalysisOutput output)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Star Play Analysis");
        builder.AppendLine();
        builder.AppendLine($"Generated: {output.GeneratedAt}");
        builder.AppendLine();
        builder.AppendLine($"Runs per deck/horizon: {output.Runs}; horizons: {string.Join(", ", output.Horizons)}; seed: {output.Seed}.");
        builder.AppendLine();
        builder.AppendLine("A star-shortage block is counted only when a star-cost card is left in hand and every play condition except stars is satisfied. A missed prior gain opportunity means a star-gain card was legally playable earlier on the chosen run path but remained unplayed before the block.");
        builder.AppendLine();
        builder.AppendLine("## Selected decks");
        builder.AppendLine();
        builder.AppendLine("| Group | Run | Layer | Deck | Gain cards | Cost cards | Unsupported | Star cards |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (StarAnalysisDeckProfile deck in output.Decks)
        {
            string cards = string.Join(", ", deck.StarCards.Select(card =>
                $"{card.TypeName}x{card.Count}({(card.GainsStars ? "+" : string.Empty)}{(card.CostsStars ? "-" : string.Empty)})"));
            builder.AppendLine($"| {deck.Group} | {deck.RunId} | {deck.Layer} | {deck.DeckSize} | {deck.StarGainCardCount} | {deck.StarCostCardCount} | {deck.UnsupportedCardCount} | {cards} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("| Group | Run | Turns | EV/turn | Gain draw/play | Gain play/draw | Cost draw/play | Cost play/draw | First gain | First-play n | Star-block cards | Missed-before-block | Missed/card | Blocked runs | Missed blocked runs | Missed/run |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (StarAnalysisSimulationResult result in output.Results)
        {
            StarPlayDiagnosticsReport star = result.Diagnostics;
            builder.AppendLine(
                $"| {result.Group} | {result.RunId} | {result.Turns} | {result.ExpectedValuePerTurn:0.###} | "
                + $"{star.StarGainCards.DrawCount}/{star.StarGainCards.PlayCount} | {AsPercent(star.StarGainCards.PlaysPerDraw)} | "
                + $"{star.StarCostCards.DrawCount}/{star.StarCostCards.PlayCount} | {AsPercent(star.StarCostCards.PlaysPerDraw)} | "
                + $"{AsPercent(star.FirstStarCardWasGainProbability)} | {star.RunsWithAnyStarCardPlay} | "
                + $"{star.StarShortageBlockedCardCount} | {star.StarShortageBlockedCardCountWithMissedPriorGainOpportunity} | "
                + $"{AsPercent(star.MissedPriorGainOpportunityProbabilityPerBlockedCard)} | "
                + $"{star.RunsWithStarShortageBlock} | {star.RunsWithStarShortageBlockAndMissedPriorGainOpportunity} | "
                + $"{AsPercent(star.MissedPriorGainOpportunityProbabilityPerBlockedRun)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Per-card draw/play flow");
        foreach (StarAnalysisSimulationResult result in output.Results)
        {
            builder.AppendLine();
            builder.AppendLine($"### {result.Group} / {result.RunId} / {result.Turns} turns");
            builder.AppendLine();
            builder.AppendLine("| Card | Kind | Draw | Play | Play/draw | Runs drawn | Runs played |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (StarCardFlowSummary card in result.Diagnostics.Cards)
            {
                string kind = card.GainsStars && card.CostsStars ? "gain+cost" : card.GainsStars ? "gain" : "cost";
                builder.AppendLine($"| {card.TypeName} | {kind} | {card.DrawCount} | {card.PlayCount} | {AsPercent(card.PlaysPerDraw)} | {card.RunsWithDraw} | {card.RunsWithPlay} |");
            }
        }

        return builder.ToString();
    }

    private static string[] SplitCsvOption(string value) => value
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string AsPercent(decimal value) => $"{value * 100m:0.0}%";

    private static bool GainsStars(SimulationCard card) => card.StarGain > 0 || card.StarNextTurn > 0;

    private static bool CostsStars(SimulationCard card) => card.StarCost > 0 || card.HasStarCostX;

    private sealed record StarAnalysisOutput(
        int SchemaVersion,
        string GeneratedAt,
        string DeckSourcePath,
        IReadOnlyList<string> Groups,
        IReadOnlyList<string> RequestedRunIds,
        int Runs,
        IReadOnlyList<int> Horizons,
        int Seed,
        int HandSize,
        int MaxHandSize,
        int BaseEnergy,
        int BaseStars,
        int MaxBranchingCards,
        int MinStarGainCards,
        int MinStarCostCards,
        IReadOnlyList<StarAnalysisDeckProfile> Decks,
        IReadOnlyList<StarAnalysisSimulationResult> Results);

    private sealed record StarAnalysisDeckProfile(
        string RunId,
        string Group,
        int Layer,
        int DeckSize,
        int StarGainCardCount,
        int StarCostCardCount,
        int UnsupportedCardCount,
        IReadOnlyList<StarAnalysisDeckCard> StarCards);

    private sealed record StarAnalysisDeckCard(
        string ModelId,
        string TypeName,
        int Count,
        bool GainsStars,
        bool CostsStars,
        int StarGain,
        int StarNextTurn,
        int StarCost);

    private sealed record StarAnalysisSimulationResult(
        string RunId,
        string Group,
        int Layer,
        int DeckSize,
        int Turns,
        decimal TotalExpectedValue,
        decimal ExpectedValuePerTurn,
        double ElapsedSeconds,
        StarPlayDiagnosticsReport Diagnostics);
}
