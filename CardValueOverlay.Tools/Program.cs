using System.Text;
using System.Xml.Linq;
using CardValueOverlay.Core.Analysis;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.Values;

namespace CardValueOverlay.Tools;

internal static class Program
{
    private const string DefaultConfigPath = "CardValueOverlay/data/card_values.json";
    private const string DefaultSts2XmlPath =
        "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.xml";

    public static int Main(string[] args)
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
        AverageExpectationResult result = ExpectationCalculator.CalculateAverage(cardKeys, new ValueResolver(config));

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

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("CardValueOverlay.Tools");
        Console.WriteLine("  validate [--config path]");
        Console.WriteLine("  average --cards keyA,keyB [--config path]");
        Console.WriteLine("  average --file card_keys.txt [--config path]");
        Console.WriteLine("  extract-cards [--sts2-xml path]");
    }
}
