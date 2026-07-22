using System.Text;
using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record CombatCoverageCellReport(
    string CellId,
    int Act,
    string Tier,
    int LossBudget,
    int MinimumUniqueEncounters,
    int MatchedUniqueEncounters,
    int FullySupportedUniqueEncounters,
    int ProposalSamples,
    int SupportedSamples,
    double SupportedFraction,
    double TargetWeight,
    double SupportedTargetWeightMass,
    bool GatePassed,
    IReadOnlyList<string> TopBlockers);

public sealed record CombatCoverageBlocker(
    string Scope,
    string Reason,
    int Count);

public sealed record CombatCoverageReport(
    int SchemaVersion,
    int CombatSemanticsVersion,
    string GeneratedAt,
    string PortfolioId,
    string PortfolioHash,
    int Ascension,
    bool RuntimeCandidate,
    string Status,
    bool CoverageGatePassed,
    double SupportedTargetWeightMass,
    double EffectiveSampleSize,
    int SupportedCardForms,
    int TotalCardForms,
    int SupportedMonsters,
    int TotalMonsters,
    int SupportedEncounterRealizations,
    int TotalEncounterRealizations,
    int SupportedDeckSnapshots,
    int TotalDeckSnapshots,
    IReadOnlyList<CombatCoverageCellReport> Cells,
    IReadOnlyList<CombatCoverageBlocker> Blockers,
    IReadOnlyList<string> Warnings);

public sealed class CombatCoverageReportBuilder
{
    public CombatCoverageReport Build(
        LoadedCombatPortfolio portfolio,
        HpContinuationCatalog hp,
        CombatSamplePlan samplePlan,
        CombatCardCatalog cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        IReadOnlyList<EncounterCombatDefinition> encounters,
        IReadOnlyDictionary<string, CompiledCombatDeck> decks)
    {
        List<CombatCoverageCellReport> cells = [];
        foreach (CombatPortfolioCell cell in portfolio.Definition.Cells.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            CombatSample[] samples = samplePlan.Samples.Where(sample => sample.CellId == cell.Id).ToArray();
            CombatSample[] supportedSamples = samples.Where(sample => sample.Supported).ToArray();
            EncounterCombatDefinition[] matched = encounters.Where(encounter =>
                encounter.Act == cell.Act &&
                string.Equals(encounter.Tier, cell.Tier, StringComparison.OrdinalIgnoreCase) &&
                cell.EncounterSelectors.Any(selector => CombatPortfolioSampler.MatchesSelector(encounter, selector)))
                .ToArray();
            IGrouping<string, EncounterCombatDefinition>[] encounterGroups = matched
                .GroupBy(encounter => encounter.ModelId, StringComparer.Ordinal)
                .ToArray();
            int fullySupported = encounterGroups.Count(group => group.All(realization => realization.IsSupported));
            double supportedFraction = samples.Length == 0 ? 0d : supportedSamples.Length / (double)samples.Length;
            double supportedMass = supportedSamples.Sum(sample => sample.TargetProbability);
            string[] blockers = samples
                .Where(sample => !sample.Supported && !string.IsNullOrWhiteSpace(sample.UnsupportedReason))
                .GroupBy(sample => sample.UnsupportedReason!, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Take(5)
                .Select(group => $"x{group.Count()}: {group.Key}")
                .ToArray();
            bool passed = supportedFraction >= 0.70d && fullySupported >= cell.MinimumUniqueEncounters;
            cells.Add(new CombatCoverageCellReport(
                cell.Id,
                cell.Act,
                cell.Tier,
                hp.Get(cell.HpContextId).LossBudget,
                cell.MinimumUniqueEncounters,
                encounterGroups.Length,
                fullySupported,
                samples.Length,
                supportedSamples.Length,
                supportedFraction,
                cell.TargetWeight,
                supportedMass,
                passed,
                blockers));
        }

        double supportedTargetMass = samplePlan.Samples.Where(sample => sample.Supported).Sum(sample => sample.TargetProbability);
        bool gatePassed = supportedTargetMass >= 0.80d && cells.All(cell => cell.GatePassed);
        CombatCoverageBlocker[] blockersByScope = BuildBlockers(monsters, encounters, decks, samplePlan);
        return new CombatCoverageReport(
            1,
            new CombatSimulationOptions().SemanticsVersion,
            DateTimeOffset.UtcNow.ToString("O"),
            portfolio.Definition.PortfolioId,
            portfolio.ContentHash,
            portfolio.Definition.Ascension,
            false,
            gatePassed ? "coverage-gate-passed-research-only" : "coverage-gate-blocked",
            gatePassed,
            supportedTargetMass,
            samplePlan.EffectiveSampleSize,
            cards.Cards.Count(card => card.IsSupported),
            cards.Cards.Count,
            monsters.Values.Count(monster => monster.IsSupported),
            monsters.Count,
            encounters.Count(encounter => encounter.IsSupported),
            encounters.Count,
            decks.Values.Count(deck => deck.IsSupported),
            decks.Count,
            cells,
            blockersByScope,
            [
                .. samplePlan.Warnings,
                "Coverage pass never changes runtimeCandidate in Phase 1.",
                "Target weights and HP coefficients remain research priors."
            ]);
    }

    private static CombatCoverageBlocker[] BuildBlockers(
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        IReadOnlyList<EncounterCombatDefinition> encounters,
        IReadOnlyDictionary<string, CompiledCombatDeck> decks,
        CombatSamplePlan samplePlan)
    {
        IEnumerable<CombatCoverageBlocker> Group(string scope, IEnumerable<string> reasons) => reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .Select(group => new CombatCoverageBlocker(scope, group.Key, group.Count()));

        return Group("monster", monsters.Values.Where(item => !item.IsSupported).SelectMany(item => item.UnsupportedReasons))
            .Concat(Group("encounter", encounters.Where(item => !item.IsSupported).SelectMany(item => item.UnsupportedReasons)))
            .Concat(Group("deck", decks.Values.Where(item => !item.IsSupported).SelectMany(item => item.UnsupportedReasons)))
            .Concat(Group("sample", samplePlan.Samples.Where(item => !item.Supported).Select(item => item.UnsupportedReason ?? string.Empty)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.Reason, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }
}

public sealed class CombatCoverageReportWriter
{
    public (string JsonPath, string MarkdownPath) Write(
        CombatCoverageReport report,
        string outputDirectory = "data/generated/combat_aware")
    {
        Directory.CreateDirectory(outputDirectory);
        string jsonPath = Path.Combine(outputDirectory, "phase1_coverage.generated.json");
        string markdownPath = Path.Combine(outputDirectory, "phase1_coverage.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, CombatJson.Options));
        File.WriteAllText(markdownPath, ToMarkdown(report), new UTF8Encoding(false));
        return (jsonPath, markdownPath);
    }

    private static string ToMarkdown(CombatCoverageReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Combat-aware Phase 1 coverage gate");
        builder.AppendLine();
        builder.AppendLine($"- Status: **{report.Status}**");
        builder.AppendLine($"- Coverage gate: **{(report.CoverageGatePassed ? "PASS" : "NO-GO")}**");
        builder.AppendLine($"- Runtime candidate: **{report.RuntimeCandidate.ToString().ToLowerInvariant()}**");
        builder.AppendLine($"- Supported target-weight mass: **{report.SupportedTargetWeightMass:P1}** (required >= 80%)");
        builder.AppendLine($"- Supported card forms: {report.SupportedCardForms}/{report.TotalCardForms}");
        builder.AppendLine($"- Supported monsters: {report.SupportedMonsters}/{report.TotalMonsters}");
        builder.AppendLine($"- Supported encounter realizations: {report.SupportedEncounterRealizations}/{report.TotalEncounterRealizations}");
        builder.AppendLine($"- Supported deck snapshots: {report.SupportedDeckSnapshots}/{report.TotalDeckSnapshots}");
        builder.AppendLine();
        builder.AppendLine("> This artifact preserves unsupported target mass. It does not redistribute missing cells or authorize training/runtime cutover.");
        builder.AppendLine();
        builder.AppendLine("| Cell | budget | supported samples | fraction | matched encounters | fully supported | minimum | target mass supported | gate |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (CombatCoverageCellReport cell in report.Cells)
        {
            builder.AppendLine($"| {cell.CellId} | {cell.LossBudget} | {cell.SupportedSamples}/{cell.ProposalSamples} | {cell.SupportedFraction:P1} | " +
                $"{cell.MatchedUniqueEncounters} | {cell.FullySupportedUniqueEncounters} | {cell.MinimumUniqueEncounters} | " +
                $"{cell.SupportedTargetWeightMass:P1} | {(cell.GatePassed ? "PASS" : "NO-GO")} |");
        }
        builder.AppendLine();
        builder.AppendLine("## Top blockers");
        builder.AppendLine();
        foreach (CombatCoverageBlocker blocker in report.Blockers.Take(30))
        {
            builder.AppendLine($"- [{blocker.Scope}] x{blocker.Count}: {blocker.Reason}");
        }
        return builder.ToString();
    }
}
