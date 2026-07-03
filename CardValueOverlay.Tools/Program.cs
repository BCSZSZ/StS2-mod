using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CardValueOverlay.Core.Analysis;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.Values;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Export;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;
using CardValueOverlay.Modeling.Validation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private const string DefaultConfigPath = "CardValueOverlay/data/card_values.json";
    private static readonly string DefaultSimulationDeckPath = Path.Combine(
        "data",
        "manual-tags",
        "simulation_decks",
        "regent_starter_a10.json");

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "validate" => Validate(args[1..]),
                "average" => Average(args[1..]),
                "extract-cards" => ExtractCards(args[1..]),
                "extract-game-data" => await ExtractGameData(args[1..]),
                "parse-card-facts" => await ParseCardFacts(args[1..]),
                "parse-card-pools" => await ParseCardPools(args[1..]),
                "write-generation-pools" => WriteGenerationPools(args[1..]),
                "parse-potions" => ParsePotions(args[1..]),
                "write-potion-pools" => WritePotionPools(args[1..]),
                "write-card-value-reference" => WriteCardValueReference(args[1..]),
                "parse-monster-moves" => await ParseMonsterMoves(args[1..]),
                "parse-encounter-patterns" => await ParseEncounterPatterns(args[1..]),
                "estimate-card-values" => EstimateCardValues(args[1..]),
                "write-card-review-list" => WriteCardReviewList(args[1..]),
                "estimate-enemy-expectations" => EstimateEnemyExpectations(args[1..]),
                "estimate-encounter-weighted-enemy-pressure" => EstimateEncounterWeightedEnemyPressure(args[1..]),
                "estimate-defense-calibration" => EstimateDefenseCalibration(args[1..]),
                "simulate-card-resources" => SimulateCardResources(args[1..]),
                "simulate-deck-scenario" => SimulateDeckScenario(args[1..], null),
                "benchmark-training-decks" => BenchmarkTrainingDecks(args[1..]),
                "train-card-values" => TrainCardValues(args[1..]),
                "install-training-values" => InstallTrainingValues(args[1..]),
                "install-play-value-estimates" => InstallPlayValueEstimates(args[1..]),
                "estimate-resource-play-values" => EstimateResourcePlayValues(args[1..]),
                "estimate-direct-play-values" => EstimateDirectPlayValues(args[1..]),
                "install-direct-play-values" => InstallDirectPlayValues(args[1..]),
                "estimate-floor8-play-values" => EstimateFloor8PlayValues(args[1..]),
                "install-floor8-play-values" => InstallFloor8PlayValues(args[1..]),
                "collect-search-policy-data" => CollectSearchPolicyData(args[1..]),
                "compare-hegemony-energy" => SimulateDeckScenario(
                    args[1..],
                    "data/manual-tags/simulation_scenarios/hegemony_energy_comparison.json"),
                "list-run-history-decks" => ListRunHistoryDecks(args[1..]),
                "write-simulation-deck" => WriteSimulationDeck(args[1..]),
                "validate-generated-data" => await ValidateGeneratedData(args[1..]),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Validate(string[] args)
    {
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;
        CardValueConfig config = CardValueConfigLoader.LoadFromFile(configPath);
        ConfigValidationResult result = CardValueConfigLoader.Validate(config);

        foreach (string warning in result.Warnings)
        {
            Console.WriteLine($"warning: {warning}");
        }

        foreach (string error in result.Errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.WriteLine(result.IsValid ? "config valid" : "config invalid");
        return result.IsValid ? 0 : 1;
    }

    private static int Average(string[] args)
    {
        string configPath = GetOption(args, "--config") ?? DefaultConfigPath;
        string? inlineCards = GetOption(args, "--cards");
        string? cardFile = GetOption(args, "--file");
        TrainingValueHorizon horizon = ParseHorizon(GetOption(args, "--horizon") ?? "midline");

        List<string> cardKeys = [];
        if (!string.IsNullOrWhiteSpace(inlineCards))
        {
            cardKeys.AddRange(inlineCards.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        if (!string.IsNullOrWhiteSpace(cardFile))
        {
            cardKeys.AddRange(File.ReadAllLines(cardFile)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#')));
        }

        if (cardKeys.Count == 0)
        {
            return Fail("average requires --cards cardA,cardB or --file path.");
        }

        CardValueConfig config = CardValueConfigLoader.LoadFromFile(configPath);
        ValueResolver resolver = new(config);
        AverageExpectationResult result = ExpectationCalculator.CalculateAverage(cardKeys, resolver, horizon);

        Console.WriteLine($"horizon: {HorizonKey(horizon)}");
        Console.WriteLine($"requested: {result.RequestedCount}");
        Console.WriteLine($"valued: {result.ValuedCount}");
        Console.WriteLine($"missing: {result.MissingCount}");
        Console.WriteLine($"average: {(result.Average.HasValue ? result.Average.Value.ToString("0.###") : "<empty>")}");

        if (result.MissingKeys.Count > 0)
        {
            Console.WriteLine("missingKeys:");
            foreach (string key in result.MissingKeys)
            {
                Console.WriteLine($"  {key}");
            }
        }

        return 0;
    }

    private static int SimulateCardResources(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        int layer = GetIntOption(args, "--layer") ?? 1;
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string setupPrioritiesPath = GetOption(args, "--setup-priorities")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
        string autoPlayEffectsPath = GetOption(args, "--card-autoplay-effects")
            ?? Path.Combine(outputRoot, "manual-tags", "card_autoplay_effects.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        SimulationSetupPriorityCatalog setupPriorities = LoadOptionalSimulationSetupPriorities(setupPrioritiesPath, jsonOptions);
        IReadOnlyList<AutoPlayEffectEntry> autoPlayEffects = LoadOptionalAutoPlayEffects(autoPlayEffectsPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        IReadOnlyList<SimulationCard> cards = new SimulationCardLibraryBuilder().Build(
            entries,
            calibration,
            layer,
            includeUpgrades: true,
            memberships,
            setupPriorities,
            autoPlayEffects);
        string deckSource;
        IReadOnlyList<SimulationCard> deck = SelectSimulationDeck(args, cards, outputRoot, jsonOptions, out deckSource);
        DeckSimulationOptions defaults = new();
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);
        DeckSimulationOptions options = new()
        {
            Turns = GetIntOption(args, "--turns") ?? defaults.Turns,
            Runs = GetIntOption(args, "--runs") ?? defaults.Runs,
            Seed = GetIntOption(args, "--seed") ?? defaults.Seed,
            HandSize = GetIntOption(args, "--hand-size") ?? defaults.HandSize,
            MaxHandSize = GetIntOption(args, "--max-hand-size") ?? defaults.MaxHandSize,
            BaseEnergy = GetIntOption(args, "--energy") ?? defaults.BaseEnergy,
            BaseStars = GetIntOption(args, "--stars") ?? defaults.BaseStars,
            StarsPersistBetweenTurns = HasFlag(args, "--stars-persist") || defaults.StarsPersistBetweenTurns,
            MaxCardsPlayedPerTurn = GetIntOption(args, "--max-plays") ?? defaults.MaxCardsPlayedPerTurn,
            MaxBranchingCards = GetIntOption(args, "--max-branch") ?? defaults.MaxBranchingCards,
            CardLibrary = cards,
            GeneratedCardPools = generatedCardPools,
            SearchCardScorer = searchCardScorer
        };

        GeneratedDataWriter writer = new();
        writer.WriteSimulationCardLibrary(cards, outputRoot);
        DeckSimulationReport report = new DeckMonteCarloSimulator().Simulate(deck, options);
        if (!HasFlag(args, "--no-marginals"))
        {
            IReadOnlyList<ResourceMarginalEstimate> marginals = new ResourceMarginalEstimator()
                .Estimate(deck, options, report);
            report = report with { MarginalEstimates = marginals };
        }

        writer.WriteDeckSimulationReport(report, outputRoot);

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Console.WriteLine("card resource simulation complete");
        Console.WriteLine($"layer: {layer}");
        Console.WriteLine($"libraryCards: {cards.Count}");
        Console.WriteLine($"deck: {deck.Count}");
        Console.WriteLine($"deckSource: {deckSource}");
        Console.WriteLine($"runs: {options.Runs}");
        Console.WriteLine($"turns: {options.Turns}");
        Console.WriteLine($"totalEV: {report.TotalExpectedValue:0.###}");
        Console.WriteLine($"library: {Path.Combine(generatedRoot, "simulation_card_library.generated.json")}");
        Console.WriteLine($"output: {Path.Combine(generatedRoot, "deck_simulation.generated.json")}");
        Console.WriteLine($"report: {Path.Combine(generatedRoot, "deck_simulation.md")}");
        return 0;
    }

    private static int SimulateDeckScenario(string[] args, string? defaultScenarioPath)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        int layer = GetIntOption(args, "--layer") ?? 1;
        string? scenarioPath = GetOption(args, "--scenario") ?? defaultScenarioPath;
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string setupPrioritiesPath = GetOption(args, "--setup-priorities")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
        string autoPlayEffectsPath = GetOption(args, "--card-autoplay-effects")
            ?? Path.Combine(outputRoot, "manual-tags", "card_autoplay_effects.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (string.IsNullOrWhiteSpace(scenarioPath))
        {
            return Fail("simulate-deck-scenario requires --scenario path.");
        }

        if (!File.Exists(scenarioPath))
        {
            return Fail($"Missing simulation scenario at {scenarioPath}.");
        }

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}");
        SimulationScenario scenario =
            JsonSerializer.Deserialize<SimulationScenario>(File.ReadAllText(scenarioPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read simulation scenario from {scenarioPath}");
        scenario = LoadScenarioDeck(scenario, scenarioPath, jsonOptions);
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        SimulationSetupPriorityCatalog setupPriorities = LoadOptionalSimulationSetupPriorities(setupPrioritiesPath, jsonOptions);
        IReadOnlyList<AutoPlayEffectEntry> autoPlayEffects = LoadOptionalAutoPlayEffects(autoPlayEffectsPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        IReadOnlyList<SimulationCard> cards = new SimulationCardLibraryBuilder().Build(
            entries,
            calibration,
            layer,
            includeUpgrades: true,
            memberships,
            setupPriorities,
            autoPlayEffects);
        DeckSimulationOptions scenarioOptions = scenario.Options ?? new DeckSimulationOptions();
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);
        DeckSimulationOptions options = new()
        {
            Turns = GetIntOption(args, "--turns") ?? scenarioOptions.Turns,
            Runs = GetIntOption(args, "--runs") ?? scenarioOptions.Runs,
            Seed = GetIntOption(args, "--seed") ?? scenarioOptions.Seed,
            HandSize = GetIntOption(args, "--hand-size") ?? scenarioOptions.HandSize,
            MaxHandSize = GetIntOption(args, "--max-hand-size") ?? scenarioOptions.MaxHandSize,
            BaseEnergy = GetIntOption(args, "--energy") ?? scenarioOptions.BaseEnergy,
            BaseStars = GetIntOption(args, "--stars") ?? scenarioOptions.BaseStars,
            StarsPersistBetweenTurns = HasFlag(args, "--stars-persist") || scenarioOptions.StarsPersistBetweenTurns,
            MaxCardsPlayedPerTurn = GetIntOption(args, "--max-plays") ?? scenarioOptions.MaxCardsPlayedPerTurn,
            MaxBranchingCards = GetIntOption(args, "--max-branch") ?? scenarioOptions.MaxBranchingCards,
            PmfBucketSize = scenarioOptions.PmfBucketSize,
            RunDegreeOfParallelism = Math.Max(1, GetIntOption(args, "--run-degree")
                ?? (scenarioOptions.RunDegreeOfParallelism > 1 ? scenarioOptions.RunDegreeOfParallelism : 4)),
            CardLibrary = cards,
            GeneratedCardPools = generatedCardPools,
            SearchCardScorer = searchCardScorer
        };
        SimulationScenarioReport report = new SimulationScenarioRunner()
            .Run(scenario, cards, calibration, layer, options);

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        string fileStem = SlugFileName(report.Name);
        string jsonPath = Path.Combine(generatedRoot, $"{fileStem}.generated.json");
        string markdownPath = Path.Combine(generatedRoot, $"{fileStem}.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions));
        File.WriteAllText(markdownPath, BuildSimulationScenarioMarkdown(report));

        Console.WriteLine("deck scenario simulation complete");
        Console.WriteLine($"scenario: {scenarioPath}");
        Console.WriteLine($"deck: {report.Deck.Sum(card => card.Count)} cards");
        Console.WriteLine($"runs: {options.Runs}");
        Console.WriteLine($"turns: {options.Turns}");
        foreach (SimulationScenarioVariantResult result in report.Results)
        {
            Console.WriteLine(
                $"{result.Id}: EV/turn {result.ExpectedValuePerTurn:0.###}, "
                + $"delta/turn {result.DeltaPerTurnFromBaseline:0.###}, "
                + $"totalEV {result.TotalExpectedValue:0.###}, "
                + $"deltaTotal {result.DeltaFromBaseline:0.###}");
        }

        Console.WriteLine($"output: {jsonPath}");
        Console.WriteLine($"report: {markdownPath}");
        return 0;
    }

    private static IReadOnlyList<CardPoolMembershipEntry> LoadOptionalCardPoolMemberships(
        string membershipsPath,
        JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(membershipsPath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<CardPoolMembershipEntry>>(File.ReadAllText(membershipsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card pool memberships from {membershipsPath}");
    }

    private static GeneratedCardPoolCatalog LoadOptionalGeneratedCardPools(
        string generatedCardPoolsPath,
        JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(generatedCardPoolsPath))
        {
            return GeneratedCardPoolCatalog.Empty;
        }

        return JsonSerializer.Deserialize<GeneratedCardPoolCatalog>(File.ReadAllText(generatedCardPoolsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read generated card pools from {generatedCardPoolsPath}");
    }

    private static IReadOnlyList<AutoPlayEffectEntry> LoadOptionalAutoPlayEffects(
        string autoPlayEffectsPath,
        JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(autoPlayEffectsPath))
        {
            return [];
        }

        AutoPlayEffectCatalog catalog =
            JsonSerializer.Deserialize<AutoPlayEffectCatalog>(File.ReadAllText(autoPlayEffectsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card auto-play effects from {autoPlayEffectsPath}");
        return catalog.Cards;
    }

    private static SimulationScenario LoadScenarioDeck(
        SimulationScenario scenario,
        string scenarioPath,
        JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(scenario.DeckFile))
        {
            return scenario;
        }

        string scenarioRoot = Path.GetDirectoryName(Path.GetFullPath(scenarioPath))
            ?? Directory.GetCurrentDirectory();
        string deckPath = Path.IsPathRooted(scenario.DeckFile)
            ? scenario.DeckFile
            : Path.GetFullPath(Path.Combine(scenarioRoot, scenario.DeckFile));
        if (!File.Exists(deckPath))
        {
            throw new InvalidOperationException($"Missing simulation deck at {deckPath}.");
        }

        SimulationDeckDefinition deck =
            JsonSerializer.Deserialize<SimulationDeckDefinition>(File.ReadAllText(deckPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read simulation deck from {deckPath}");
        if (deck.Cards.Count == 0)
        {
            throw new InvalidOperationException($"Simulation deck '{deck.Name}' is empty.");
        }

        IReadOnlyList<string> deckAssumptions =
        [
            $"Deck source: {deck.Name} ({Path.GetFileName(deckPath)}).",
            .. deck.Assumptions.Select(assumption => $"Deck assumption: {assumption}")
        ];
        return scenario with
        {
            Deck = [.. deck.Cards, .. scenario.Deck],
            Assumptions = [.. deckAssumptions, .. scenario.Assumptions]
        };
    }

    private static string BuildSimulationScenarioMarkdown(SimulationScenarioReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# {report.Name}");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(report.Description))
        {
            builder.AppendLine(report.Description);
            builder.AppendLine();
        }

        builder.AppendLine($"Layer: {report.Layer}");
        builder.AppendLine($"Runs: {report.Options.Runs}");
        builder.AppendLine($"Turns: {report.Options.Turns}");
        builder.AppendLine($"Hand size: {report.Options.HandSize}");
        builder.AppendLine();
        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("| Variant | Deck size | EV/turn | Delta/turn vs baseline | Delta/turn vs previous | Total EV | Delta total vs baseline | Delta total vs previous | Total variance | Turn variance sum | 2*covariance sum |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (SimulationScenarioVariantResult result in report.Results)
        {
            builder.AppendLine(
                $"| {result.Label} | {result.DeckSize} | {result.ExpectedValuePerTurn:0.###} | "
                + $"{result.DeltaPerTurnFromBaseline:0.###} | {FormatNullable(result.DeltaPerTurnFromPrevious)} | "
                + $"{result.TotalExpectedValue:0.###} | {result.DeltaFromBaseline:0.###} | "
                + $"{FormatNullable(result.DeltaFromPrevious)} | "
                + $"{result.TotalVariance:0.###} | {result.TurnVarianceSum:0.###} | "
                + $"{result.TurnCovarianceContribution:0.###} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Deck");
        builder.AppendLine();
        builder.AppendLine("| Card | TypeName | ModelId | Count | Upgrade | Notes |");
        builder.AppendLine("| --- | --- | --- | ---: | ---: | --- |");
        foreach (SimulationScenarioDeckEntry card in report.Deck)
        {
            string name = card.DisplayName ?? card.TypeName;
            builder.AppendLine($"| {name} | {card.TypeName} | {card.ModelId} | {card.Count} | {card.Upgrade} | {card.Notes} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Top Played Cards");
        foreach (SimulationScenarioVariantResult result in report.Results)
        {
            builder.AppendLine();
            builder.AppendLine($"### {result.Label}");
            foreach (CardPlaySummary card in result.PlayedCards.Take(10))
            {
                builder.AppendLine($"- {card.TypeName}: {card.AveragePlaysPerRun:0.###}/run, {card.AverageValuePerPlay:0.###} value/play");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Credited Card Values");
        foreach (SimulationScenarioVariantResult result in report.Results)
        {
            builder.AppendLine();
            builder.AppendLine($"### {result.Label}");
            builder.AppendLine("| Card | Direct plays | Direct value/play | Forge realized/play | Power realized/play | Energy realized/play | Star realized/play | Credited value/play | Direct total | Forge total | Power total | Energy total | Star total |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (CardValueCreditSummary card in result.CardValueCredits
                .Where(card => card.TotalCreditedValue != 0m || card.DirectPlayCount > 0)
                .Take(12))
            {
                builder.AppendLine(
                    $"| {EscapeMarkdownCell(card.TypeName)} | {card.DirectPlayCount} | "
                    + $"{card.AverageDirectValuePerPlay:0.###} | "
                    + $"{card.AverageForgeRealizedValuePerPlay:0.###} | "
                    + $"{card.AveragePowerRealizedValuePerPlay:0.###} | "
                    + $"{card.AverageEnergyRealizedValuePerPlay:0.###} | "
                    + $"{card.AverageStarRealizedValuePerPlay:0.###} | "
                    + $"{card.AverageCreditedValuePerPlay:0.###} | "
                    + $"{card.DirectValue:0.###} | {card.ForgeRealizedValue:0.###} | {card.PowerRealizedValue:0.###} | "
                    + $"{card.EnergyRealizedValue:0.###} | {card.StarRealizedValue:0.###} |");
            }
        }

        if (report.Assumptions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Assumptions");
            foreach (string assumption in report.Assumptions)
            {
                builder.AppendLine($"- {assumption}");
            }
        }

        return builder.ToString();
    }

    private static string EscapeMarkdownCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string FormatNullable(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("0.###") : "-";
    }

    private static string SlugFileName(string value)
    {
        string slug = new(value
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
            .ToArray());
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        return slug.Trim('_');
    }

    private static int ExtractCards(string[] args)
    {
        string sts2XmlPath = GetOption(args, "--sts2-xml") ?? MachineProfilePaths.DefaultSts2XmlPath;
        XDocument document = XDocument.Load(sts2XmlPath);
        const string prefix = "T:MegaCrit.Sts2.Core.Models.Cards.";

        IEnumerable<string> cardTypeNames = document
            .Descendants("member")
            .Select(member => member.Attribute("name")?.Value)
            .Where(name => name is not null && name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name![prefix.Length..])
            .Where(name => name.Length > 0 && !name.Contains('.'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (string typeName in cardTypeNames)
        {
            Console.WriteLine(typeName);
        }

        return 0;
    }

    private static async Task<int> ExtractGameData(string[] args)
    {
        ModelingExtractionOptions options = BuildExtractionOptions(args);
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult pathValidation = ExtractionValidationResult.Validate(paths);
        PrintPathValidation(pathValidation);
        if (!pathValidation.IsValid)
        {
            return 1;
        }

        ExtractionRunResult result = await new GameDataExtractor().ExtractAsync(options);
        IReadOnlyList<string> validationErrors = new GeneratedDataValidator().Validate(result);
        if (validationErrors.Count > 0)
        {
            foreach (string error in validationErrors)
            {
                Console.Error.WriteLine($"error: {error}");
            }

            return 1;
        }

        new GeneratedDataWriter().WriteAll(result, options);
        PrintExtractionSummary(result, paths);
        return 0;
    }

    private static async Task<int> ValidateGeneratedData(string[] args)
    {
        ModelingExtractionOptions options = BuildExtractionOptions(args);
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult pathValidation = ExtractionValidationResult.Validate(paths);
        PrintPathValidation(pathValidation);
        if (!pathValidation.IsValid)
        {
            return 1;
        }

        ExtractionRunResult result = await new GameDataExtractor().ExtractAsync(options);
        IReadOnlyList<string> validationErrors = new GeneratedDataValidator().Validate(result);
        foreach (string warning in result.UnresolvedItems.Where(item => item.Severity != "info").Select(item => item.Message))
        {
            Console.WriteLine($"warning: {warning}");
        }

        foreach (string error in validationErrors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.WriteLine(validationErrors.Count == 0 ? "generated data valid" : "generated data invalid");
        return validationErrors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> ParseCardFacts(string[] args)
    {
        ModelingExtractionOptions options = BuildExtractionOptions(args);
        bool refreshDecompile = HasFlag(args, "--refresh-decompile");
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult pathValidation = ExtractionValidationResult.Validate(paths);
        PrintPathValidation(pathValidation);
        if (!pathValidation.IsValid)
        {
            return 1;
        }

        IReadOnlyList<CardFactCatalogEntry> entries = await new CardFactExtractor()
            .ExtractAsync(options, refreshDecompile);
        IReadOnlyList<string> validationErrors = new CardFactValidator().Validate(entries);
        foreach (string error in validationErrors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        if (validationErrors.Count > 0)
        {
            return 1;
        }

        new GeneratedDataWriter().WriteCardFacts(entries, options);
        int parsedCount = entries.Count(entry => entry.Actions.Count > 0);
        int rawOperationCount = entries.Sum(entry => entry.RawOperations.Count);
        int unresolvedCount = entries.Count(entry => entry.Unresolved.Count > 0);
        Console.WriteLine("card facts parsed");
        Console.WriteLine($"cards: {entries.Count}");
        Console.WriteLine($"withActions: {parsedCount}");
        Console.WriteLine($"rawOperations: {rawOperationCount}");
        Console.WriteLine($"unresolved: {unresolvedCount}");
        Console.WriteLine($"output: {Path.Combine(paths.ExtractedOutputRoot, "card_facts.generated.json")}");
        return 0;
    }

    private static async Task<int> ParseMonsterMoves(string[] args)
    {
        ModelingExtractionOptions options = BuildExtractionOptions(args);
        bool refreshDecompile = HasFlag(args, "--refresh-decompile");
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult pathValidation = ExtractionValidationResult.Validate(paths);
        PrintPathValidation(pathValidation);
        if (!pathValidation.IsValid)
        {
            return 1;
        }

        IReadOnlyList<MonsterMoveProfileEntry> entries = await new MonsterMoveProfileExtractor()
            .ExtractAsync(options, refreshDecompile);
        IReadOnlyList<string> validationErrors = new MonsterMoveProfileValidator().Validate(entries);
        foreach (string error in validationErrors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        if (validationErrors.Count > 0)
        {
            return 1;
        }

        new GeneratedDataWriter().WriteMonsterMoveProfiles(entries, options);
        int parsedCount = entries.Count(entry => entry.Moves.Count > 0);
        int unresolvedCount = entries.Count(entry => entry.Unresolved.Count > 0 || entry.Moves.Any(move => move.Warnings.Count > 0));
        Console.WriteLine("monster moves parsed");
        Console.WriteLine($"monsters: {entries.Count}");
        Console.WriteLine($"withMoves: {parsedCount}");
        Console.WriteLine($"needsReview: {unresolvedCount}");
        Console.WriteLine($"output: {Path.Combine(paths.ExtractedOutputRoot, "monster_move_profiles.generated.json")}");
        return 0;
    }

    private static async Task<int> ParseCardPools(string[] args)
    {
        ModelingExtractionOptions options = BuildExtractionOptions(args);
        bool refreshDecompile = HasFlag(args, "--refresh-decompile");
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult pathValidation = ExtractionValidationResult.Validate(paths);
        PrintPathValidation(pathValidation);
        if (!pathValidation.IsValid)
        {
            return 1;
        }

        IReadOnlyList<CardPoolMembershipEntry> entries = await new CardPoolMembershipExtractor()
            .ExtractAsync(options, refreshDecompile);

        new GeneratedDataWriter().WriteCardPoolMemberships(entries, options);
        int inPools = entries.Count(entry => entry.Pools.Count > 0);
        int multiplayerOnly = entries.Count(entry => entry.IsMultiplayerOnly);
        int singleplayerOnly = entries.Count(entry => entry.IsSingleplayerOnly);
        int needsReview = entries.Count(entry => entry.Warnings.Count > 0);
        Console.WriteLine("card pools parsed");
        Console.WriteLine($"cards: {entries.Count}");
        Console.WriteLine($"inPools: {inPools}");
        Console.WriteLine($"multiplayerOnly: {multiplayerOnly}");
        Console.WriteLine($"singleplayerOnly: {singleplayerOnly}");
        Console.WriteLine($"needsReview: {needsReview}");
        Console.WriteLine($"output: {Path.Combine(paths.ExtractedOutputRoot, "card_pool_memberships.generated.json")}");
        return 0;
    }

    private static async Task<int> ParseEncounterPatterns(string[] args)
    {
        ModelingExtractionOptions options = BuildExtractionOptions(args);
        bool refreshDecompile = HasFlag(args, "--refresh-decompile");
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult pathValidation = ExtractionValidationResult.Validate(paths);
        PrintPathValidation(pathValidation);
        if (!pathValidation.IsValid)
        {
            return 1;
        }

        IReadOnlyList<EncounterPatternEntry> entries = await new EncounterPatternExtractor()
            .ExtractAsync(options, refreshDecompile);
        IReadOnlyList<string> validationErrors = new EncounterPatternValidator().Validate(entries);
        foreach (string error in validationErrors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        if (validationErrors.Count > 0)
        {
            return 1;
        }

        new GeneratedDataWriter().WriteEncounterPatterns(entries, options);
        int needsReview = entries.Count(entry => entry.Warnings.Count > 0 || entry.Confidence < 0.7);
        Console.WriteLine("encounter patterns parsed");
        Console.WriteLine($"encounters: {entries.Count}");
        foreach (IGrouping<string, EncounterPatternEntry> group in entries.GroupBy(entry => entry.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{group.Key}: {group.Count()}");
        }

        Console.WriteLine($"needsReview: {needsReview}");
        Console.WriteLine($"output: {Path.Combine(paths.ExtractedOutputRoot, "encounter_patterns.generated.json")}");
        Console.WriteLine($"report: {Path.Combine(paths.GeneratedOutputRoot, "encounter_patterns.md")}");
        return 0;
    }

    private static int EstimateCardValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        int layer = GetIntOption(args, "--layer") ?? 1;
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}");
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);

        IReadOnlyList<CardValueEstimate> estimates = new CardValueEstimator().Estimate(entries, calibration, layer);
        new GeneratedDataWriter().WriteCardValueCandidates(estimates, outputRoot);

        int needsReview = estimates.Count(estimate => estimate.Warnings.Count > 0 || estimate.Confidence < 0.7);
        Console.WriteLine("card value candidates estimated");
        Console.WriteLine($"layer: {layer}");
        Console.WriteLine($"cards: {estimates.Count}");
        Console.WriteLine($"needsReview: {needsReview}");
        Console.WriteLine($"output: {Path.Combine(Path.GetFullPath(outputRoot), "generated", "card_value_candidates.generated.json")}");
        return 0;
    }

    private static int EstimateEnemyExpectations(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string profilesPath = GetOption(args, "--profiles")
            ?? Path.Combine(outputRoot, "extracted", "monster_move_profiles.generated.json");

        if (!File.Exists(profilesPath))
        {
            return Fail($"Missing monster move profiles at {profilesPath}. Run parse-monster-moves first.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<MonsterMoveProfileEntry> entries =
            JsonSerializer.Deserialize<List<MonsterMoveProfileEntry>>(File.ReadAllText(profilesPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read monster move profiles from {profilesPath}");

        IReadOnlyList<EnemyExpectationProfile> expectations = new EnemyExpectationEstimator().Estimate(entries);
        new GeneratedDataWriter().WriteEnemyExpectations(expectations, outputRoot);

        int needsReview = expectations.Count(profile => profile.Warnings.Count > 0 || profile.Confidence < 0.7);
        Console.WriteLine("enemy expectations estimated");
        Console.WriteLine($"enemies: {expectations.Count}");
        Console.WriteLine($"needsReview: {needsReview}");
        Console.WriteLine($"output: {Path.Combine(Path.GetFullPath(outputRoot), "generated", "enemy_expectations.generated.json")}");
        return 0;
    }

    private static int WriteCardReviewList(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string estimatesPath = GetOption(args, "--estimates")
            ?? Path.Combine(outputRoot, "generated", "card_value_candidates.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");

        if (!File.Exists(estimatesPath))
        {
            return Fail($"Missing card value candidates at {estimatesPath}. Run estimate-card-values first.");
        }

        if (!File.Exists(membershipsPath))
        {
            return Fail($"Missing card pool memberships at {membershipsPath}. Run parse-card-pools first.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<CardValueEstimate> estimates =
            JsonSerializer.Deserialize<List<CardValueEstimate>>(File.ReadAllText(estimatesPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card value candidates from {estimatesPath}");
        IReadOnlyList<CardPoolMembershipEntry> memberships =
            JsonSerializer.Deserialize<List<CardPoolMembershipEntry>>(File.ReadAllText(membershipsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card pool memberships from {membershipsPath}");

        new GeneratedDataWriter().WriteCardValueReviewList(estimates, memberships, outputRoot);

        Console.WriteLine("card review list written");
        Console.WriteLine($"cards: {estimates.Count}");
        Console.WriteLine($"memberships: {memberships.Count}");
        Console.WriteLine($"output: {Path.Combine(Path.GetFullPath(outputRoot), "generated", "card_value_review_list.md")}");
        return 0;
    }

    private static int EstimateDefenseCalibration(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string expectationsPath = GetOption(args, "--expectations")
            ?? Path.Combine(outputRoot, "generated", "enemy_expectations.generated.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (!File.Exists(expectationsPath))
        {
            return Fail($"Missing enemy expectations at {expectationsPath}. Run estimate-enemy-expectations first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<EnemyExpectationProfile> expectations =
            JsonSerializer.Deserialize<List<EnemyExpectationProfile>>(File.ReadAllText(expectationsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read enemy expectations from {expectationsPath}");
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);

        DefenseCalibrationReport report = new DefenseCalibrationEstimator().Estimate(expectations, calibration);
        new GeneratedDataWriter().WriteDefenseCalibrationReport(report, outputRoot);

        Console.WriteLine("defense calibration estimated");
        Console.WriteLine("damageBasis: Ascension 10");
        Console.WriteLine($"enemies: {report.EnemyCount}");
        Console.WriteLine($"needsReview: {report.NeedsReviewCount}");
        Console.WriteLine($"avgDamagePerMove: {report.AverageDamagePerMove:0.###}");
        Console.WriteLine($"p90DamagePerMove: {report.P90DamagePerMove:0.###}");
        Console.WriteLine($"output: {Path.Combine(Path.GetFullPath(outputRoot), "generated", "defense_calibration.generated.json")}");
        return 0;
    }

    private static IReadOnlyList<SimulationCard> SelectSimulationDeck(
        string[] args,
        IReadOnlyList<SimulationCard> cards,
        string outputRoot,
        JsonSerializerOptions jsonOptions,
        out string deckSource)
    {
        string? inlineCards = GetOption(args, "--cards");
        string? deckFile = GetOption(args, "--deck");
        if (!string.IsNullOrWhiteSpace(inlineCards))
        {
            deckSource = "--cards";
            IReadOnlyList<string> requestedCards = inlineCards
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return ResolveRequestedSimulationCards(requestedCards, cards);
        }

        string resolvedDeckFile = deckFile ?? Path.Combine(outputRoot, "manual-tags", "simulation_decks", "regent_starter_a10.json");
        if (!File.Exists(resolvedDeckFile))
        {
            throw new InvalidOperationException($"Missing simulation deck at {resolvedDeckFile}.");
        }

        SimulationDeckDefinition deck =
            JsonSerializer.Deserialize<SimulationDeckDefinition>(File.ReadAllText(resolvedDeckFile), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read simulation deck from {resolvedDeckFile}");
        if (deck.Cards.Count == 0)
        {
            throw new InvalidOperationException($"Simulation deck '{deck.Name}' is empty.");
        }

        deckSource = string.IsNullOrWhiteSpace(deckFile)
            ? $"{deck.Name} ({DefaultSimulationDeckPath})"
            : $"{deck.Name} ({resolvedDeckFile})";
        return ResolveSimulationDeck(deck, cards);
    }

    private static IReadOnlyList<SimulationCard> ResolveRequestedSimulationCards(
        IReadOnlyList<string> requestedCards,
        IReadOnlyList<SimulationCard> cards)
    {
        Dictionary<string, SimulationCard> byModelId = cards
            .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SimulationCard> byTypeName = cards
            .GroupBy(card => card.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<SimulationCard> deck = [];
        foreach (string requestedCard in requestedCards)
        {
            if (byModelId.TryGetValue(requestedCard, out SimulationCard? modelMatch)
                || byTypeName.TryGetValue(requestedCard, out modelMatch))
            {
                deck.Add(modelMatch);
                continue;
            }

            throw new InvalidOperationException($"Unknown simulation card '{requestedCard}'. Use modelId or typeName.");
        }

        return deck;
    }

    private static IReadOnlyList<SimulationCard> ResolveSimulationDeck(
        SimulationDeckDefinition definition,
        IReadOnlyList<SimulationCard> cards)
    {
        Dictionary<string, SimulationCard> byModelId = cards
            .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SimulationCard> byTypeName = cards
            .GroupBy(card => card.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<SimulationCard> deck = [];
        foreach (SimulationDeckCardSpec spec in definition.Cards)
        {
            if (spec.Count <= 0)
            {
                continue;
            }

            if (spec.Patch is not null)
            {
                throw new InvalidOperationException("simulate-card-resources --deck does not support card patches. Use simulate-deck-scenario for DIY or patched card scenarios.");
            }

            SimulationCard card = ResolveSimulationDeckCard(spec, byTypeName, byModelId);
            for (int i = 0; i < spec.Count; i++)
            {
                deck.Add(card);
            }
        }

        if (deck.Count == 0)
        {
            throw new InvalidOperationException($"Simulation deck '{definition.Name}' did not resolve to any cards.");
        }

        return deck;
    }

    private static SimulationCard ResolveSimulationDeckCard(
        SimulationDeckCardSpec spec,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId)
    {
        string? modelId = spec.CloneModelId ?? spec.ModelId;
        if (spec.Upgrade > 0 && !string.IsNullOrWhiteSpace(modelId))
        {
            string upgradedModelId = $"{modelId}+{spec.Upgrade}";
            if (byModelId.TryGetValue(upgradedModelId, out SimulationCard? upgradedModelMatch))
            {
                return upgradedModelMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(modelId) && byModelId.TryGetValue(modelId, out SimulationCard? modelMatch))
        {
            return modelMatch;
        }

        string? typeName = spec.CloneTypeName ?? spec.TypeName;
        if (spec.Upgrade > 0 && !string.IsNullOrWhiteSpace(typeName))
        {
            string upgradedTypeName = $"{typeName}+{spec.Upgrade}";
            if (byTypeName.TryGetValue(upgradedTypeName, out SimulationCard? upgradedTypeMatch))
            {
                return upgradedTypeMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(typeName) && byTypeName.TryGetValue(typeName, out SimulationCard? typeMatch))
        {
            return typeMatch;
        }

        string id = spec.ModelId ?? spec.TypeName ?? spec.DisplayName ?? "<missing>";
        throw new InvalidOperationException($"Unknown simulation card '{id}'. Use modelId or typeName.");
    }

    private static int EstimateEncounterWeightedEnemyPressure(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        int turnCount = GetIntOption(args, "--turns") ?? 8;
        string profilesPath = GetOption(args, "--profiles")
            ?? Path.Combine(outputRoot, "extracted", "monster_move_profiles.generated.json");
        string patternsPath = GetOption(args, "--patterns")
            ?? Path.Combine(outputRoot, "extracted", "encounter_patterns.generated.json");

        if (!File.Exists(profilesPath))
        {
            return Fail($"Missing monster move profiles at {profilesPath}. Run parse-monster-moves first.");
        }

        if (!File.Exists(patternsPath))
        {
            return Fail($"Missing encounter patterns at {patternsPath}. Run parse-encounter-patterns first.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<MonsterMoveProfileEntry> profiles =
            JsonSerializer.Deserialize<List<MonsterMoveProfileEntry>>(File.ReadAllText(profilesPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read monster move profiles from {profilesPath}");
        IReadOnlyList<EncounterPatternEntry> patterns =
            JsonSerializer.Deserialize<List<EncounterPatternEntry>>(File.ReadAllText(patternsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read encounter patterns from {patternsPath}");

        EncounterWeightedEnemyPressureReport report = new EncounterWeightedEnemyPressureEstimator()
            .Estimate(profiles, patterns, turnCount);
        new GeneratedDataWriter().WriteEncounterWeightedEnemyPressureReport(report, outputRoot);

        int needsReview = report.Encounters.Count(encounter => encounter.Warnings.Count > 0 || encounter.Confidence < 0.7);
        Console.WriteLine("encounter-weighted enemy pressure estimated");
        Console.WriteLine("damageBasis: Ascension 10");
        Console.WriteLine($"turns: {report.TurnCount}");
        Console.WriteLine($"opening: T1-T{report.OpeningTurnCount}");
        Console.WriteLine($"sustain: T{report.SustainStartTurn}-T{report.SustainEndTurn}");
        Console.WriteLine($"encounters: {report.Encounters.Count}");
        Console.WriteLine($"layerSegments: {report.LayerSegments.Count}");
        Console.WriteLine($"needsReview: {needsReview}");
        foreach (EncounterLayerPressureSegment segment in report.LayerSegments)
        {
            Console.WriteLine(
                $"{segment.StartLayer}-{segment.EndLayer} {segment.ActLabel} {segment.SegmentKind}: "
                + $"weighted {segment.AverageWeightedPressure:0.###}, "
                + $"opening {segment.AverageOpeningDamage:0.###} ({segment.AverageOpeningDamagePerTurn:0.###}/turn), "
                + $"sustain {segment.AverageSustainDamage:0.###} ({segment.AverageSustainDamagePerTurn:0.###}/turn), "
                + $"peak {segment.AveragePeakDamage:0.###}, scaling {segment.AverageScalingDeltaPerTurn:0.###}/turn");
        }

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Console.WriteLine($"output: {Path.Combine(generatedRoot, "encounter_weighted_enemy_pressure.generated.json")}");
        Console.WriteLine($"report: {Path.Combine(generatedRoot, "encounter_weighted_enemy_pressure.md")}");
        return 0;
    }

    private static ModelingExtractionOptions BuildExtractionOptions(string[] args)
    {
        string? gameRootOption = GetOption(args, "--game-root");
        string gameRoot = gameRootOption ?? MachineProfilePaths.DefaultSts2Path;
        string dataDir = GetOption(args, "--data-dir")
            ?? (gameRootOption is null
                ? MachineProfilePaths.DefaultSts2DataDir
                : Path.Combine(gameRoot.Replace('/', Path.DirectorySeparatorChar), "data_sts2_windows_x86_64"));

        return new ModelingExtractionOptions
        {
            GameRoot = gameRoot,
            Sts2DataDir = dataDir,
            OutputRoot = GetOption(args, "--output") ?? "data",
            DecompileOutputRoot = GetOption(args, "--decompile-dir"),
            IlSpyPath = GetOption(args, "--ilspy")
        };
    }

    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintPathValidation(ExtractionValidationResult result)
    {
        foreach (string warning in result.Warnings)
        {
            Console.WriteLine($"warning: {warning}");
        }

        foreach (string error in result.Errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }
    }

    private static void PrintExtractionSummary(ExtractionRunResult result, ExtractionPaths paths)
    {
        Console.WriteLine("game data extracted");
        Console.WriteLine($"version: {result.GameVersion.Version ?? "<unknown>"}");
        Console.WriteLine($"cards: {result.Cards.Count}");
        Console.WriteLine($"enemies: {result.Enemies.Count}");
        Console.WriteLine($"encounters: {result.Encounters.Count}");
        Console.WriteLine($"intents: {result.Intents.Count}");
        Console.WriteLine($"unresolved: {result.UnresolvedItems.Count}");
        Console.WriteLine($"output: {paths.OutputRoot}");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int? GetIntOption(string[] args, string name)
    {
        string? value = GetOption(args, name);
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, out int parsed))
        {
            throw new InvalidOperationException($"{name} must be an integer.");
        }

        return parsed;
    }

    private static ISearchCardScorer? LoadSearchCardScorer(string[] args)
    {
        string policy = GetOption(args, "--search-policy") ?? "heuristic";
        return policy.Trim().ToLowerInvariant() switch
        {
            "heuristic" => null,
            "neural" => NeuralSearchCardScorer.Load(
                GetOption(args, "--search-policy-model")
                ?? Path.Combine("data", "manual-tags", "search_policy_ranker.json")),
            _ => throw new InvalidOperationException("--search-policy must be heuristic or neural.")
        };
    }

    private static TrainingValueHorizon ParseHorizon(string value)
    {
        return value.Trim() switch
        {
            "shortline" or "short" or "4" or "4turn" or "4turns" => TrainingValueHorizon.Shortline,
            "midline" or "mid" or "medium" or "8" or "8turn" or "8turns" => TrainingValueHorizon.Midline,
            "longline" or "long" or "14" or "14turn" or "14turns" => TrainingValueHorizon.Longline,
            _ => throw new InvalidOperationException("--horizon must be shortline, midline, or longline.")
        };
    }

    private static string HorizonKey(TrainingValueHorizon horizon)
    {
        return horizon switch
        {
            TrainingValueHorizon.Shortline => "shortline",
            TrainingValueHorizon.Midline => "midline",
            TrainingValueHorizon.Longline => "longline",
            _ => throw new InvalidOperationException($"Unsupported horizon {horizon}.")
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("CardValueOverlay.Tools");
        Console.WriteLine("  validate [--config path]");
        Console.WriteLine("  average --cards keyA,keyB [--horizon shortline|midline|longline] [--config path]");
        Console.WriteLine("  average --file card_keys.txt [--horizon shortline|midline|longline] [--config path]");
        Console.WriteLine("    upgraded cards can be written as key+ or key:upgraded");
        Console.WriteLine("  extract-cards [--sts2-xml path]");
        Console.WriteLine("  extract-game-data [--game-root path] [--data-dir path] [--output data] [--ilspy path]");
        Console.WriteLine("  parse-card-facts [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  parse-card-pools [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  write-generation-pools [--output data] [--layer n] [--facts path] [--memberships path] [--generated-card-pools path] [--calibration path]");
        Console.WriteLine("    Regenerates simulation_generated_card_pools.json: expands Regent/current-hero/Colorless generator pools to the full simulatable set and records unsimulatable same-subject cards in 'unsupportedPools' (record-only).");
        Console.WriteLine("  parse-potions [--output data] [--decompile-dir path]");
        Console.WriteLine("  write-potion-pools [--output data] [--potion-facts path] [--output-file data/manual-tags/simulation_potion_pools.json]");
        Console.WriteLine("    Parses the decompiled potion sources into data/extracted/potion_facts.generated.json (full 64-potion roster: rarity/usage/target/pool/vars + effect tags).");
        Console.WriteLine("  write-card-value-reference [--config CardValueOverlay/data/card_values.json] [--localization history-analysis/data/localized_names_en_zhs.json] [--output-dir data/manual-tags] [--date YYYY-MM-DD] [--script scripts/generate_card_value_reference.py]");
        Console.WriteLine("    Regenerates card_values_reference_<date>.{md,xlsx} via `uv run` (PEP 723 openpyxl auto-provisioned; requires uv on PATH). Play-delta main table + static-layer17 appendix, EN/中文 names.");
        Console.WriteLine("  parse-monster-moves [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  parse-encounter-patterns [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  estimate-card-values [--output data] [--layer n] [--facts path] [--calibration path]");
        Console.WriteLine("  write-card-review-list [--output data] [--estimates path] [--memberships path]");
        Console.WriteLine("  estimate-enemy-expectations [--output data] [--profiles path]");
        Console.WriteLine("  estimate-encounter-weighted-enemy-pressure [--output data] [--profiles path] [--patterns path] [--turns n]");
        Console.WriteLine("    --turns defaults to 8 and must be at least 8 for opening/sustain/peak metrics.");
        Console.WriteLine("  estimate-defense-calibration [--output data] [--expectations path] [--calibration path]");
        Console.WriteLine("  simulate-card-resources [--output data] [--layer n] [--runs n] [--turns n] [--seed n]");
        Console.WriteLine("    [--cards modelId,typeName] [--deck simulation_deck.json] [--stars-persist] [--no-marginals]");
        Console.WriteLine("    [--search-policy heuristic|neural] [--search-policy-model data/manual-tags/search_policy_ranker.json]");
        Console.WriteLine("  simulate-deck-scenario --scenario path [--output data] [--layer n] [--runs n] [--turns n]");
        Console.WriteLine("    [--search-policy heuristic|neural] [--search-policy-model data/manual-tags/search_policy_ranker.json]");
        Console.WriteLine("  benchmark-training-decks --training-decks path [--runs 40] [--turns 14] [--max-branch 2]");
        Console.WriteLine("    [--degree-of-parallelism 1] [--run-degree 4] [--profile] [--output-json path] [--output-md path]");
        Console.WriteLine("  train-card-values [--training-decks path] [--output data] [--output-json path] [--runs 1000] [--write-config]");
        Console.WriteLine("    [--config CardValueOverlay/data/card_values.json] [--candidate modelIdOrTypeName] [--limit-cards n] [--skip-decks n] [--limit-decks n] [--degree-of-parallelism n] [--resume] [--profile] [--no-write-config]");
        Console.WriteLine("    [--search-policy heuristic|neural] [--search-policy-model data/manual-tags/search_policy_ranker.json]");
        Console.WriteLine("    [--max-plays n] defaults to 8 for bounded batch-training search.");
        Console.WriteLine("    [--max-branch n] defaults to 2 for bounded batch-training search; scenario simulation keeps its own default.");
        Console.WriteLine("  install-training-values [--input data/generated/training_card_values/latest.generated.json] [--config CardValueOverlay/data/card_values.json]");
        Console.WriteLine("  install-play-value-estimates [--output data] [--layer 17] [--facts path] [--memberships path] [--calibration path] [--config CardValueOverlay/data/card_values.json]");
        Console.WriteLine("  estimate-resource-play-values [--training-decks history-analysis/data/dashen_77_selected_16_decks.json] [--runs 100] [--samples-per-deck 4] [--max-branch 4]");
        Console.WriteLine("    [--profile] [--profile-kind benchmark|formal] [--benchmark-json path] [--selection-note text]");
        Console.WriteLine("    writes data/generated/resource_play_values/latest.generated.json plus timestamped JSON/MD archives.");
        Console.WriteLine("  estimate-direct-play-values [--deck-group group | --deck-mix \"floor8:0.30,act2Start:0.50,final:0.20\"] [--deck-source history-analysis/data/dashen_77_all_231_decks.json] [--deck-count 1] [--deck-seed n]");
        Console.WriteLine("    deck sampling: with neither --deck-group nor --deck-mix, the locked standard mix (30% floor8 / 50% act2Start / 20% final) is sampled from --deck-source; --deck-group samples one act; --deck-mix a custom ratio.");
        Console.WriteLine("    [--horizons shortline:4,midline:8] [--turns n] [--runs 400] [--max-branch 4] [--candidate modelIdOrTypeName]");
        Console.WriteLine("    [--candidate-file path] [--value-strategy source-credit|play-delta|auto] [--pin-probe-branch] [--limit-forms n] [--degree-of-parallelism n] [--run-degree n]");
        Console.WriteLine("    --degree-of-parallelism (default 4) parallelizes across cards; --run-degree (default 4) parallelizes one card/deck's runs and engages only when the per-card layer cannot.");
        Console.WriteLine("    value-strategy auto: complete-attribution probes use source-credit; probes with a non-numerically-attributable term (e.g. draw, like BigBang) use play-delta (normal vs blocked).");
        Console.WriteLine("    writes data/generated/direct_play_values/latest.generated.json plus timestamped JSON/MD archives.");
        Console.WriteLine("  install-direct-play-values [--input data/generated/direct_play_values/latest.generated.json] [--config CardValueOverlay/data/card_values.json]");
        Console.WriteLine("    [--horizons shortline,midline] [--setup-output data/manual-tags/simulation_setup_priorities.json] [--setup-source-horizon midline]");
        Console.WriteLine("    [--group-weights \"shortline=floor8:0.7,act2Start:0.2,final:0.1;midline=floor8:0.1,act2Start:0.7,final:0.2;longline=floor8:0.1,act2Start:0.15,final:0.75\"]");
        Console.WriteLine("  estimate-floor8-play-values [--deck-source history-analysis/data/dashen_77_all_231_decks.json] [--deck-count 16] [--runs 400] [--max-branch 4]");
        Console.WriteLine("    [--deck-seed 20260629] [--limit-forms n] [--skip-forms n] [--degree-of-parallelism n] [--run-degree n] [--resume] [--profile]");
        Console.WriteLine("    --run-degree (default 4) parallelizes one deck's runs and engages only when the per-card layer cannot.");
        Console.WriteLine("    writes data/generated/floor8_play_values/latest.generated.json plus timestamped JSON/MD archives.");
        Console.WriteLine("  install-floor8-play-values [--input data/generated/floor8_play_values/latest.generated.json] [--config CardValueOverlay/data/card_values.json]");
        Console.WriteLine("    updates only matching runtime trainingValues shortline and midline values.");
        Console.WriteLine("  collect-search-policy-data [--training-decks path] [--output-jsonl path] [--runs 50] [--max-groups 200000]");
        Console.WriteLine("    [--candidate modelIdOrTypeName] [--limit-cards n] [--candidate-decks 20] [--groups-per-deck-variant n]");
        Console.WriteLine("    [--teacher-max-branch 8] [--teacher-max-plays 8]");
        Console.WriteLine("  compare-hegemony-energy [--output data] [--layer n] [--runs n] [--turns n]");
        Console.WriteLine("  list-run-history-decks [--history-root path] [--catalog path] [--character id] [--ascension n] [--floor n] [--limit n] [--run-id id] [--output-json path] [--before-floor-rewards] [--json]");
        Console.WriteLine("  write-simulation-deck --input path --name deck_name [--run-id id] [--description text] [--source text] [--assumption text] [--output path]");
        Console.WriteLine("  validate-generated-data [--game-root path] [--data-dir path] [--ilspy path]");
    }
}
