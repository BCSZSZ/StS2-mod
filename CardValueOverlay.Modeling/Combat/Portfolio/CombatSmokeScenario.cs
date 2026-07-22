using System.Text;
using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record CombatSmokeScenarioOptions(int Turns, int Runs, int Seed);

public sealed record CombatSmokeScenarioSpec(
    int Ascension,
    int PlayerHp,
    int PlayerMaxHp,
    string EncounterId,
    int MonsterHp,
    string InitialIntentStateId,
    string HpContextId,
    string ExpectedStatus,
    string Oracle);

public sealed record CombatSmokeScenarioFile(
    string Name,
    string Description,
    string DeckFile,
    CombatSmokeScenarioOptions Options,
    CombatSmokeScenarioSpec CombatPhase1,
    IReadOnlyList<string> Assumptions);

public sealed record CombatSmokeDeckCard(
    string TypeName,
    string ModelId,
    int Count,
    int Upgrade);

public sealed record CombatSmokeDeckFile(
    string Name,
    string Description,
    IReadOnlyList<CombatSmokeDeckCard> Cards,
    IReadOnlyList<string> Assumptions);

public sealed record LoadedCombatSmokeScenario(
    string Name,
    string Description,
    int Horizon,
    CombatSample Sample,
    CompiledCombatDeck Deck,
    EncounterCombatDefinition Encounter,
    HpContinuationContext HpContext,
    string ExpectedStatus,
    string Oracle,
    IReadOnlyList<string> Assumptions);

public sealed record CombatSmokeResult(
    string Name,
    int Horizon,
    string ExpectedStatus,
    CombatSolveStatus SolverStatus,
    CombatPhysicalMetrics Metrics,
    long CanonicalStates,
    long MemoHits,
    long OutcomeBranches,
    double WallMilliseconds,
    long AllocatedBytes);

public sealed record CombatSmokeReport(
    int SchemaVersion,
    int CombatSemanticsVersion,
    string GeneratedAt,
    bool RuntimeCandidate,
    string Status,
    IReadOnlyList<CombatSmokeResult> Results,
    IReadOnlyList<string> Warnings);

public sealed class CombatSmokeScenarioLoader
{
    public LoadedCombatSmokeScenario Load(
        string scenarioPath,
        CombatCardCatalog cards,
        IReadOnlyList<EncounterCombatDefinition> encounters,
        HpContinuationCatalog hpCatalog)
    {
        CombatSmokeScenarioFile scenario = JsonSerializer.Deserialize<CombatSmokeScenarioFile>(
            File.ReadAllText(scenarioPath), CombatJson.Options)
            ?? throw new InvalidOperationException($"Combat smoke scenario '{scenarioPath}' is empty.");
        if (scenario.CombatPhase1.Ascension != 10 || scenario.Options.Runs != 1 ||
            scenario.Options.Turns is not (4 or 8 or 12))
        {
            throw new InvalidOperationException($"Combat smoke scenario '{scenario.Name}' must use A10, one exact run, and horizon 4/8/12.");
        }

        string root = Path.GetDirectoryName(Path.GetFullPath(scenarioPath))
            ?? throw new InvalidOperationException($"Combat smoke scenario '{scenarioPath}' has no directory.");
        string deckPath = Path.GetFullPath(Path.Combine(root, scenario.DeckFile));
        CombatSmokeDeckFile deckFile = JsonSerializer.Deserialize<CombatSmokeDeckFile>(
            File.ReadAllText(deckPath), CombatJson.Options)
            ?? throw new InvalidOperationException($"Combat smoke deck '{deckPath}' is empty.");
        List<int> cardIds = [];
        List<string> unsupported = [];
        foreach (CombatSmokeDeckCard reference in deckFile.Cards)
        {
            if (!cards.TryGet($"{reference.ModelId}+{reference.Upgrade}", out CombatCardDefinition? card) &&
                !cards.TryGet(reference.TypeName, out card))
            {
                unsupported.Add($"Card '{reference.ModelId}/{reference.TypeName}+{reference.Upgrade}' was not compiled.");
                continue;
            }
            if (!card.IsSupported)
            {
                unsupported.Add($"Card '{card.StableKey}' unsupported: {string.Join("; ", card.UnsupportedReasons)}");
            }
            cardIds.AddRange(Enumerable.Repeat(card.DefinitionId, reference.Count));
        }

        CompiledCombatDeck deck = new(
            $"smoke:{deckFile.Name}",
            "phase1-smoke",
            cardIds,
            unsupported.Count == 0,
            unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        EncounterCombatDefinition encounter = encounters.Single(item =>
            string.Equals(item.StableId, scenario.CombatPhase1.EncounterId, StringComparison.Ordinal));
        HpContinuationContext hp = hpCatalog.Get(scenario.CombatPhase1.HpContextId);
        bool supported = deck.IsSupported && encounter.IsSupported;
        string? reason = supported
            ? null
            : string.Join("; ", deck.UnsupportedReasons.Concat(encounter.UnsupportedReasons).Distinct(StringComparer.Ordinal));
        Dictionary<int, int> monsterHp = encounter.Monsters.ToDictionary(
            slot => slot.Position,
            _ => scenario.CombatPhase1.MonsterHp);
        Dictionary<int, string> initialIntents = encounter.Monsters.ToDictionary(
            slot => slot.Position,
            _ => scenario.CombatPhase1.InitialIntentStateId);
        CombatSample sample = new(
            $"smoke:{scenario.Name}",
            $"act{encounter.Act}-{encounter.Tier}",
            encounter.Act,
            encounter.Tier,
            deck.DeckId,
            deck.Group,
            encounter.StableId,
            scenario.CombatPhase1.PlayerHp,
            scenario.CombatPhase1.PlayerMaxHp,
            hp.Id,
            unchecked((ulong)(uint)scenario.Options.Seed),
            1d,
            1d,
            1d,
            supported,
            reason,
            monsterHp,
            initialIntents);
        return new LoadedCombatSmokeScenario(
            scenario.Name,
            scenario.Description,
            scenario.Options.Turns,
            sample,
            deck,
            encounter,
            hp,
            scenario.CombatPhase1.ExpectedStatus,
            scenario.CombatPhase1.Oracle,
            [.. deckFile.Assumptions, .. scenario.Assumptions]);
    }
}

public sealed class CombatSmokeReportWriter
{
    public (string JsonPath, string MarkdownPath) Write(
        CombatSmokeReport report,
        string outputDirectory = "data/generated/combat_aware")
    {
        Directory.CreateDirectory(outputDirectory);
        string jsonPath = Path.Combine(outputDirectory, "phase1_smoke.generated.json");
        string markdownPath = Path.Combine(outputDirectory, "phase1_smoke.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, CombatJson.Options));
        File.WriteAllText(markdownPath, ToMarkdown(report), new UTF8Encoding(false));
        return (jsonPath, markdownPath);
    }

    private static string ToMarkdown(CombatSmokeReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Combat-aware Phase 1 smoke scenarios");
        builder.AppendLine();
        builder.AppendLine($"- Status: **{report.Status}**");
        builder.AppendLine($"- Runtime candidate: **{report.RuntimeCandidate.ToString().ToLowerInvariant()}**");
        builder.AppendLine();
        builder.AppendLine("| Scenario | horizon | solver | value | damage | HP loss | tail loss | death | states | branches | wall ms | allocated | expected support |");
        builder.AppendLine("| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (CombatSmokeResult result in report.Results.OrderBy(item => item.Horizon))
        {
            builder.AppendLine($"| {result.Name} | {result.Horizon} | {result.SolverStatus} | {result.Metrics.Value:0.###} | " +
                $"{result.Metrics.ActualEnemyHpDamage:0.###} | {result.Metrics.PlayerHpLost:0.###} | " +
                $"{result.Metrics.ReferenceTailHpLoss:0.###} | {result.Metrics.DeathProbability:P1} | " +
                $"{result.CanonicalStates} | {result.OutcomeBranches} | {result.WallMilliseconds:0.###} | " +
                $"{result.AllocatedBytes} | {result.ExpectedStatus} |");
        }
        builder.AppendLine();
        foreach (string warning in report.Warnings) builder.AppendLine($"- {warning}");
        return builder.ToString();
    }
}
