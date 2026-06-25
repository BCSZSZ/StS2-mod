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

internal static class Program
{
    private const string DefaultConfigPath = "CardValueOverlay/data/card_values.json";
    private const string DefaultSts2XmlPath =
        "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.xml";

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
                "parse-card-effects" => await ParseCardEffects(args[1..]),
                "parse-card-pools" => await ParseCardPools(args[1..]),
                "parse-monster-moves" => await ParseMonsterMoves(args[1..]),
                "parse-encounter-patterns" => await ParseEncounterPatterns(args[1..]),
                "estimate-card-values" => EstimateCardValues(args[1..]),
                "write-card-review-list" => WriteCardReviewList(args[1..]),
                "estimate-enemy-expectations" => EstimateEnemyExpectations(args[1..]),
                "estimate-encounter-weighted-enemy-pressure" => EstimateEncounterWeightedEnemyPressure(args[1..]),
                "estimate-defense-calibration" => EstimateDefenseCalibration(args[1..]),
                "simulate-card-resources" => SimulateCardResources(args[1..]),
                "simulate-deck-scenario" => SimulateDeckScenario(args[1..], null),
                "compare-hegemony-energy" => SimulateDeckScenario(
                    args[1..],
                    "data/manual-tags/simulation_scenarios/hegemony_energy_comparison.json"),
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
        string kind = GetOption(args, "--kind") ?? "card";
        int layer = GetIntOption(args, "--layer") ?? 1;

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
        AverageExpectationResult result = kind.ToLowerInvariant() switch
        {
            "card" or "value" or "manual" => ExpectationCalculator.CalculateAverage(cardKeys, resolver, layer),
            "smith" or "upgrade" => ExpectationCalculator.CalculateSmithAverage(cardKeys, resolver, layer),
            _ => throw new InvalidOperationException("average --kind must be card or smith.")
        };

        Console.WriteLine($"kind: {kind}");
        Console.WriteLine($"layer: {layer}");
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
        string effectsPath = GetOption(args, "--effects")
            ?? Path.Combine(outputRoot, "extracted", "card_effect_terms.generated.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (!File.Exists(effectsPath))
        {
            return Fail($"Missing card effect terms at {effectsPath}. Run parse-card-effects first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<CardEffectTermCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardEffectTermCatalogEntry>>(File.ReadAllText(effectsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card effect terms from {effectsPath}");
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        IReadOnlyList<SimulationCard> cards = new SimulationCardLibraryBuilder().Build(entries, calibration, layer);
        IReadOnlyList<SimulationCard> deck = SelectSimulationDeck(args, cards);
        DeckSimulationOptions options = new()
        {
            Turns = GetIntOption(args, "--turns") ?? 8,
            Runs = GetIntOption(args, "--runs") ?? 1000,
            Seed = GetIntOption(args, "--seed") ?? 1,
            HandSize = GetIntOption(args, "--hand-size") ?? 5,
            BaseEnergy = GetIntOption(args, "--energy") ?? 3,
            BaseStars = GetIntOption(args, "--stars") ?? 0,
            StarsPersistBetweenTurns = HasFlag(args, "--stars-persist"),
            MaxCardsPlayedPerTurn = GetIntOption(args, "--max-plays") ?? 16,
            MaxBranchingCards = GetIntOption(args, "--max-branch") ?? 8
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
        Console.WriteLine($"cards: {cards.Count}");
        Console.WriteLine($"deck: {deck.Count}");
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
        string effectsPath = GetOption(args, "--effects")
            ?? Path.Combine(outputRoot, "extracted", "card_effect_terms.generated.json");
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

        if (!File.Exists(effectsPath))
        {
            return Fail($"Missing card effect terms at {effectsPath}. Run parse-card-effects first.");
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
        IReadOnlyList<CardEffectTermCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardEffectTermCatalogEntry>>(File.ReadAllText(effectsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card effect terms from {effectsPath}");
        SimulationScenario scenario =
            JsonSerializer.Deserialize<SimulationScenario>(File.ReadAllText(scenarioPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read simulation scenario from {scenarioPath}");
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        IReadOnlyList<SimulationCard> cards = new SimulationCardLibraryBuilder().Build(entries, calibration, layer);
        DeckSimulationOptions scenarioOptions = scenario.Options ?? new DeckSimulationOptions();
        DeckSimulationOptions options = new()
        {
            Turns = GetIntOption(args, "--turns") ?? scenarioOptions.Turns,
            Runs = GetIntOption(args, "--runs") ?? scenarioOptions.Runs,
            Seed = GetIntOption(args, "--seed") ?? scenarioOptions.Seed,
            HandSize = GetIntOption(args, "--hand-size") ?? scenarioOptions.HandSize,
            BaseEnergy = GetIntOption(args, "--energy") ?? scenarioOptions.BaseEnergy,
            BaseStars = GetIntOption(args, "--stars") ?? scenarioOptions.BaseStars,
            StarsPersistBetweenTurns = HasFlag(args, "--stars-persist") || scenarioOptions.StarsPersistBetweenTurns,
            MaxCardsPlayedPerTurn = GetIntOption(args, "--max-plays") ?? scenarioOptions.MaxCardsPlayedPerTurn,
            MaxBranchingCards = GetIntOption(args, "--max-branch") ?? scenarioOptions.MaxBranchingCards,
            PmfBucketSize = scenarioOptions.PmfBucketSize
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
        builder.AppendLine("| Variant | Deck size | EV/turn | Delta/turn vs baseline | Delta/turn vs previous | Total EV | Total variance |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (SimulationScenarioVariantResult result in report.Results)
        {
            builder.AppendLine(
                $"| {result.Label} | {result.DeckSize} | {result.ExpectedValuePerTurn:0.###} | "
                + $"{result.DeltaPerTurnFromBaseline:0.###} | {FormatNullable(result.DeltaPerTurnFromPrevious)} | "
                + $"{result.TotalExpectedValue:0.###} | "
                + $"{result.TotalVariance:0.###} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Deck");
        builder.AppendLine();
        builder.AppendLine("| Card | TypeName | ModelId | Count | Notes |");
        builder.AppendLine("| --- | --- | --- | ---: | --- |");
        foreach (SimulationScenarioDeckEntry card in report.Deck)
        {
            string name = card.DisplayName ?? card.TypeName;
            builder.AppendLine($"| {name} | {card.TypeName} | {card.ModelId} | {card.Count} | {card.Notes} |");
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
        string sts2XmlPath = GetOption(args, "--sts2-xml") ?? DefaultSts2XmlPath;
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

    private static async Task<int> ParseCardEffects(string[] args)
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

        IReadOnlyList<CardEffectTermCatalogEntry> entries = await new CardEffectTermExtractor()
            .ExtractAsync(options, refreshDecompile);
        IReadOnlyList<string> validationErrors = new CardEffectTermValidator().Validate(entries);
        foreach (string error in validationErrors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        if (validationErrors.Count > 0)
        {
            return 1;
        }

        new GeneratedDataWriter().WriteCardEffectTerms(entries, options);
        int parsedCount = entries.Count(entry => entry.Terms.Count > 0);
        int unresolvedCount = entries.Count(entry => entry.Unresolved.Count > 0);
        Console.WriteLine("card effects parsed");
        Console.WriteLine($"cards: {entries.Count}");
        Console.WriteLine($"withTerms: {parsedCount}");
        Console.WriteLine($"unresolved: {unresolvedCount}");
        Console.WriteLine($"output: {Path.Combine(paths.ExtractedOutputRoot, "card_effect_terms.generated.json")}");
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
        string effectsPath = GetOption(args, "--effects")
            ?? Path.Combine(outputRoot, "extracted", "card_effect_terms.generated.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (!File.Exists(effectsPath))
        {
            return Fail($"Missing card effect terms at {effectsPath}. Run parse-card-effects first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<CardEffectTermCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardEffectTermCatalogEntry>>(File.ReadAllText(effectsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card effect terms from {effectsPath}");
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
        IReadOnlyList<SimulationCard> cards)
    {
        string? inlineCards = GetOption(args, "--cards");
        string? deckFile = GetOption(args, "--deck");
        List<string> requestedCards = [];
        if (!string.IsNullOrWhiteSpace(inlineCards))
        {
            requestedCards.AddRange(inlineCards.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        if (!string.IsNullOrWhiteSpace(deckFile))
        {
            requestedCards.AddRange(File.ReadAllLines(deckFile)
                .SelectMany(ParseDeckLine));
        }

        if (requestedCards.Count == 0)
        {
            IEnumerable<SimulationCard> allCards = cards;
            if (HasFlag(args, "--playable-only"))
            {
                allCards = allCards.Where(card => card.IsPlayable);
            }

            return allCards.ToArray();
        }

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

    private static IEnumerable<string> ParseDeckLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return [];
        }

        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2
            && parts[^1].StartsWith('x')
            && int.TryParse(parts[^1][1..], out int count)
            && count > 0)
        {
            return Enumerable.Repeat(string.Join(' ', parts[..^1]), count);
        }

        return [trimmed];
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
        string defaultGameRoot = Environment.GetEnvironmentVariable("STS2_PATH")
            ?? "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2";
        string gameRoot = GetOption(args, "--game-root") ?? defaultGameRoot;
        string dataDir = GetOption(args, "--data-dir")
            ?? Path.Combine(gameRoot.Replace('/', Path.DirectorySeparatorChar), "data_sts2_windows_x86_64");

        return new ModelingExtractionOptions
        {
            GameRoot = gameRoot,
            Sts2DataDir = dataDir,
            OutputRoot = GetOption(args, "--output") ?? "data",
            DecompileOutputRoot = GetOption(args, "--decompile-dir"),
            IlSpyPath = GetOption(args, "--ilspy")
                ?? Environment.GetEnvironmentVariable("ILSPYCMD_PATH")
                ?? Environment.GetEnvironmentVariable("LIAO_ILSPYCMD")
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

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("CardValueOverlay.Tools");
        Console.WriteLine("  validate [--config path]");
        Console.WriteLine("  average --cards keyA,keyB [--kind card|smith] [--layer n] [--config path]");
        Console.WriteLine("  average --file card_keys.txt [--kind card|smith] [--layer n] [--config path]");
        Console.WriteLine("    upgraded cards can be written as key+ or key:upgraded");
        Console.WriteLine("  extract-cards [--sts2-xml path]");
        Console.WriteLine("  extract-game-data [--game-root path] [--data-dir path] [--output data] [--ilspy path]");
        Console.WriteLine("  parse-card-effects [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  parse-card-pools [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  parse-monster-moves [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  parse-encounter-patterns [--game-root path] [--data-dir path] [--output data] [--ilspy path] [--decompile-dir path] [--refresh-decompile]");
        Console.WriteLine("  estimate-card-values [--output data] [--layer n] [--effects path] [--calibration path]");
        Console.WriteLine("  write-card-review-list [--output data] [--estimates path] [--memberships path]");
        Console.WriteLine("  estimate-enemy-expectations [--output data] [--profiles path]");
        Console.WriteLine("  estimate-encounter-weighted-enemy-pressure [--output data] [--profiles path] [--patterns path] [--turns n]");
        Console.WriteLine("    --turns defaults to 8 and must be at least 8 for opening/sustain/peak metrics.");
        Console.WriteLine("  estimate-defense-calibration [--output data] [--expectations path] [--calibration path]");
        Console.WriteLine("  simulate-card-resources [--output data] [--layer n] [--runs n] [--turns n] [--seed n]");
        Console.WriteLine("    [--cards modelId,typeName] [--deck file] [--playable-only] [--stars-persist] [--no-marginals]");
        Console.WriteLine("  simulate-deck-scenario --scenario path [--output data] [--layer n] [--runs n] [--turns n]");
        Console.WriteLine("  compare-hegemony-energy [--output data] [--layer n] [--runs n] [--turns n]");
        Console.WriteLine("  validate-generated-data [--game-root path] [--data-dir path] [--ilspy path]");
    }
}
