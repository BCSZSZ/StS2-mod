using System.Text.Json;
using CardValueOverlay.Modeling.RunHistory;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private static int ListRunHistoryDecks(string[] args)
    {
        RunHistoryDeckExtractionOptions options = new()
        {
            HistoryRoot = GetOption(args, "--history-root"),
            HistoryExportPath = GetOption(args, "--history-export") ?? GetOption(args, "--runs-export"),
            CatalogPath = GetOption(args, "--catalog") ?? "data/extracted/card_catalog.generated.json",
            Character = GetOption(args, "--character") ?? "CHARACTER.REGENT",
            Ascension = GetIntOption(args, "--ascension") ?? 10,
            Floor = GetIntOption(args, "--floor") ?? 5,
            Limit = GetIntOption(args, "--limit") ?? 5,
            RunId = GetOption(args, "--run-id"),
            IncludeFloorRewards = !HasFlag(args, "--before-floor-rewards")
        };

        RunHistoryDeckExtractionReport report = new RunHistoryDeckExtractor().Extract(options);
        JsonSerializerOptions jsonOptions = ToolJsonOptions();
        string? outputJson = GetOption(args, "--output-json");
        if (!string.IsNullOrWhiteSpace(outputJson))
        {
            string? parent = Path.GetDirectoryName(outputJson);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(outputJson, JsonSerializer.Serialize(report, jsonOptions));
        }

        if (HasFlag(args, "--json") || HasFlag(args, "--as-json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
            return 0;
        }

        PrintRunHistoryDeckReport(report);
        return 0;
    }

    private static int WriteSimulationDeck(string[] args)
    {
        string? name = GetOption(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fail("write-simulation-deck requires --name deck_name.");
        }

        string? inputPath = GetOption(args, "--input") ?? GetOption(args, "--input-path");
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Fail("write-simulation-deck requires --input path.");
        }

        string outputPath = GetOption(args, "--output")
            ?? GetOption(args, "--output-path")
            ?? Path.Combine("data", "manual-tags", "simulation_decks", $"{name}.json");
        SimulationDeckBuildOptions options = new()
        {
            Name = name,
            InputPath = inputPath,
            RunId = GetOption(args, "--run-id"),
            Description = GetOption(args, "--description"),
            Source = GetOption(args, "--source"),
            Assumptions = GetOptions(args, "--assumption")
        };
        SimulationDeckDefinitionBuilder builder = new();
        SimulationDeckDefinition deck = builder.BuildFromFile(options);
        builder.WriteToFile(deck, outputPath);

        Console.WriteLine($"wrote {outputPath}");
        return 0;
    }

    private static void PrintRunHistoryDeckReport(RunHistoryDeckExtractionReport report)
    {
        Console.WriteLine($"latest_matching_runs={report.Runs.Count}");
        foreach (RunHistoryDeckResult run in report.Runs)
        {
            Console.WriteLine();
            Console.WriteLine($"[{run.RunId}] build={run.Build} seed={run.Seed} deck_count={run.DeckCount}");
            Console.WriteLine($"path={run.Path}");
            Console.WriteLine($"events={string.Join("; ", run.Events)}");
            Console.WriteLine();
            Console.WriteLine("Count  Card                           TypeName");
            Console.WriteLine("-----  -----------------------------  --------------------");
            foreach (RunHistoryDeckCard card in run.Cards)
            {
                string suffix = card.Upgrade > 0 ? $"+{card.Upgrade}" : "";
                Console.WriteLine($"{card.Count,5}  {card.Id + suffix,-29}  {card.TypeName}");
            }
        }
    }

    private static IReadOnlyList<string> GetOptions(string[] args, string name)
    {
        List<string> values = [];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
            {
                values.Add(args[i + 1]);
            }
        }

        return values;
    }

    private static JsonSerializerOptions ToolJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
