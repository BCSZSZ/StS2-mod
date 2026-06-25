using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Export;

public sealed class GeneratedDataWriter
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void WriteAll(ExtractionRunResult result, ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        Directory.CreateDirectory(paths.GeneratedOutputRoot);
        Directory.CreateDirectory(paths.ManualTagsRoot);

        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "game_version.generated.json"), result.GameVersion);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "card_catalog.generated.json"), result.Cards);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "enemy_catalog.generated.json"), result.Enemies);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "encounter_catalog.generated.json"), result.Encounters);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "intent_catalog.generated.json"), result.Intents);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "localization.generated.json"), result.Localization);
        WriteJson(Path.Combine(paths.GeneratedOutputRoot, "unresolved_extraction_items.generated.json"), result.UnresolvedItems);
        WriteMarkdown(Path.Combine(paths.GeneratedOutputRoot, "unresolved_extraction_items.md"), result.UnresolvedItems);
    }

    public void WriteCardEffectTerms(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "card_effect_terms.generated.json"), entries);
    }

    private void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, _jsonOptions));
    }

    private static void WriteMarkdown(string path, IReadOnlyList<UnresolvedExtractionItem> items)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Unresolved Extraction Items");
        writer.WriteLine();
        writer.WriteLine("| Area | Severity | Message | Recommended action |");
        writer.WriteLine("| --- | --- | --- | --- |");
        foreach (UnresolvedExtractionItem item in items)
        {
            writer.WriteLine($"| {Escape(item.Area)} | {Escape(item.Severity)} | {Escape(item.Message)} | {Escape(item.RecommendedAction)} |");
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
