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

    public void WriteCardPoolMemberships(
        IReadOnlyList<CardPoolMembershipEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "card_pool_memberships.generated.json"), entries);
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

    public void WriteDefenseCalibrationReport(DefenseCalibrationReport report, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "defense_calibration.generated.json"), report);
        WriteDefenseCalibrationMarkdown(Path.Combine(generatedRoot, "defense_calibration.md"), report);
    }

    public void WriteCardValueReviewList(
        IReadOnlyList<CardValueEstimate> estimates,
        IReadOnlyList<CardPoolMembershipEntry> memberships,
        string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        new CardValueReviewReportWriter().Write(
            Path.Combine(generatedRoot, "card_value_review_list.md"),
            estimates,
            memberships);
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

    private static void WriteDefenseCalibrationMarkdown(string path, DefenseCalibrationReport report)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Defense Calibration Report");
        writer.WriteLine();
        writer.WriteLine("| Metric | Value |");
        writer.WriteLine("| --- | ---: |");
        writer.WriteLine($"| Enemies | {report.EnemyCount} |");
        writer.WriteLine($"| Needs review | {report.NeedsReviewCount} |");
        writer.WriteLine($"| Average damage / move | {report.AverageDamagePerMove:0.###} |");
        writer.WriteLine($"| Ascension average damage / move | {report.AscensionAverageDamagePerMove:0.###} |");
        writer.WriteLine($"| Median damage / move | {report.MedianDamagePerMove:0.###} |");
        writer.WriteLine($"| P75 damage / move | {report.P75DamagePerMove:0.###} |");
        writer.WriteLine($"| P90 damage / move | {report.P90DamagePerMove:0.###} |");
        writer.WriteLine($"| Max average damage / move | {report.MaxDamagePerMove:0.###} |");
        writer.WriteLine($"| Average attack move rate | {report.AverageAttackMoveRate:0.###} |");
        writer.WriteLine($"| Weak / move | {report.AverageWeakPerMove:0.###} |");
        writer.WriteLine($"| Vulnerable / move | {report.AverageVulnerablePerMove:0.###} |");
        writer.WriteLine($"| Frail / move | {report.AverageFrailPerMove:0.###} |");
        writer.WriteLine($"| Strength gain / move | {report.AverageStrengthGainPerMove:0.###} |");
        writer.WriteLine();

        writer.WriteLine("## Fight Expectations");
        writer.WriteLine();
        writer.WriteLine("| Fight | Turns | Damage | Asc damage | Weak | Vulnerable | Frail | Strength gain |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (FightDefenseExpectation fight in report.FightExpectations)
        {
            writer.WriteLine($"| {Escape(fight.FightType)} | {fight.ExpectedTurns:0.###} | {fight.ExpectedDamage:0.###} | {fight.AscensionExpectedDamage:0.###} | {fight.ExpectedWeak:0.###} | {fight.ExpectedVulnerable:0.###} | {fight.ExpectedFrail:0.###} | {fight.ExpectedStrengthGain:0.###} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Layer Pressures");
        writer.WriteLine();
        writer.WriteLine("| Layer | Asc mix | Effective damage / move | Block-to-damage | Candidate value / block | Required block / move |");
        writer.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (LayerDefensePressure layer in report.LayerPressures)
        {
            writer.WriteLine($"| {layer.Layer} | {layer.AscensionMix:0.###} | {layer.EffectiveDamagePerMove:0.###} | {layer.CurrentBlockToDamage:0.###} | {layer.CandidateValuePerBlock:0.###} | {layer.RequiredBlockPerMoveAtCurrentConversion:0.###} |");
        }

        if (report.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            writer.WriteLine();
            foreach (string warning in report.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }
}
