using System.Text.Json;
using CardValueOverlay.Modeling.Estimation;
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

    public void WriteMonsterMoveProfiles(
        IReadOnlyList<MonsterMoveProfileEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "monster_move_profiles.generated.json"), entries);
    }

    public void WriteCardValueCandidates(IReadOnlyList<CardValueEstimate> estimates, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "card_value_candidates.generated.json"), estimates);
        WriteCandidateMarkdown(Path.Combine(generatedRoot, "card_value_candidates.md"), estimates);
    }

    public void WriteEnemyExpectations(IReadOnlyList<EnemyExpectationProfile> profiles, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "enemy_expectations.generated.json"), profiles);
        WriteEnemyExpectationMarkdown(Path.Combine(generatedRoot, "enemy_expectations.md"), profiles);
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

    private static void WriteCandidateMarkdown(string path, IReadOnlyList<CardValueEstimate> estimates)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Card Value Candidates");
        writer.WriteLine();
        writer.WriteLine("| Card | Cost | Type | Value | Upgraded | Smith | Confidence | Warnings |");
        writer.WriteLine("| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (CardValueEstimate estimate in estimates.OrderByDescending(item => item.EstimatedValue).ThenBy(item => item.TypeName, StringComparer.Ordinal))
        {
            writer.WriteLine(
                $"| {Escape(estimate.TypeName)} | {estimate.Cost?.ToString() ?? ""} | {Escape(estimate.CardType ?? "")} | {estimate.EstimatedValue:0.###} | {estimate.UpgradedEstimatedValue:0.###} | {estimate.SmithValue:0.###} | {estimate.Confidence:0.###} | {estimate.Warnings.Count} |");
        }
    }

    private static void WriteEnemyExpectationMarkdown(string path, IReadOnlyList<EnemyExpectationProfile> profiles)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Enemy Expectations");
        writer.WriteLine();
        writer.WriteLine("| Enemy | HP | Damage/move | Asc damage/move | Attack rate | Block/move | Weak/move | Frail/move | Vuln/move | Moves | Warnings |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (EnemyExpectationProfile profile in profiles.OrderByDescending(item => item.AverageDamagePerMove).ThenBy(item => item.TypeName, StringComparer.Ordinal))
        {
            string hp = profile.MinHp.HasValue && profile.MaxHp.HasValue
                ? $"{profile.MinHp:0.###}-{profile.MaxHp:0.###}"
                : "";
            string ascDamage = profile.AscensionAverageDamagePerMove.HasValue
                ? profile.AscensionAverageDamagePerMove.Value.ToString("0.###")
                : "";

            writer.WriteLine(
                $"| {Escape(profile.TypeName)} | {hp} | {profile.AverageDamagePerMove:0.###} | {ascDamage} | {profile.AttackMoveRate:0.###} | {profile.AverageBlockPerMove:0.###} | {profile.ExpectedWeakPerMove:0.###} | {profile.ExpectedFrailPerMove:0.###} | {profile.ExpectedVulnerablePerMove:0.###} | {profile.MoveCount} | {profile.Warnings.Count} |");
        }
    }
}
