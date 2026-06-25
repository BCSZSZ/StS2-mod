using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CardValueOverlay.Core.Analysis;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.Values;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Export;
using CardValueOverlay.Modeling.Extraction;
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
                "estimate-card-values" => EstimateCardValues(args[1..]),
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
        Console.WriteLine("  estimate-card-values [--output data] [--layer n] [--effects path] [--calibration path]");
        Console.WriteLine("  validate-generated-data [--game-root path] [--data-dir path] [--ilspy path]");
    }
}
