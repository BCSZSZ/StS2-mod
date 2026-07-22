using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using CardValueOverlay.Modeling.Combat;
using CardValueOverlay.Modeling.Combat.Portfolio;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private sealed record CombatInputBundle(
        HpContinuationCatalog Hp,
        LoadedCombatPortfolio Portfolio,
        CombatCardCatalog Cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> Monsters,
        IReadOnlyList<EncounterCombatDefinition> Encounters,
        IReadOnlyList<CombatDeckSnapshot> DeckSnapshots,
        IReadOnlyDictionary<string, CompiledCombatDeck> Decks,
        CombatSamplePlan SamplePlan,
        string CombatModelHash);

    private sealed record SolverBenchmarkRow(
        int Workers,
        int Iterations,
        double TotalWallMilliseconds,
        double ThroughputSolvesPerSecond,
        double P50Milliseconds,
        double P95Milliseconds,
        double P99Milliseconds,
        double MeanAllocatedBytes,
        double MeanCanonicalStates,
        double MeanMemoHits,
        double MeanOutcomeBranches);

    private sealed record SolverBenchmarkReport(
        int SchemaVersion,
        int CombatSemanticsVersion,
        string GeneratedAt,
        string Fixture,
        string PathKind,
        bool ProfilerWallComparable,
        IReadOnlyList<SolverBenchmarkRow> Rows,
        IReadOnlyList<string> Notes);

    private static int ValidateCombatPortfolio(string[] args)
    {
        CombatInputBundle inputs = LoadCombatInputs(args);
        CombatCoverageReport coverage = new CombatCoverageReportBuilder().Build(
            inputs.Portfolio,
            inputs.Hp,
            inputs.SamplePlan,
            inputs.Cards,
            inputs.Monsters,
            inputs.Encounters,
            inputs.Decks);
        string outputDirectory = GetOption(args, "--output") ?? "data/generated/combat_aware";
        (string coverageJson, string coverageMarkdown) = new CombatCoverageReportWriter().Write(coverage, outputDirectory);
        (CombatSmokeReport Smoke, string JsonPath, string MarkdownPath) smoke = RunCombatSmokeScenarios(inputs, outputDirectory);
        Console.WriteLine($"portfolio: {inputs.Portfolio.Definition.PortfolioId}");
        Console.WriteLine($"status: {inputs.Portfolio.Definition.Status}/{inputs.Portfolio.Definition.TargetWeightStatus}");
        Console.WriteLine($"cards: {inputs.Cards.Cards.Count(card => card.IsSupported)}/{inputs.Cards.Cards.Count} supported forms");
        Console.WriteLine($"monsters: {inputs.Monsters.Values.Count(monster => monster.IsSupported)}/{inputs.Monsters.Count} supported");
        Console.WriteLine($"encounters: {inputs.Encounters.Count(encounter => encounter.IsSupported)}/{inputs.Encounters.Count} supported act-realizations");
        foreach (IGrouping<(int Act, string Tier), EncounterCombatDefinition> group in inputs.Encounters
            .GroupBy(encounter => (encounter.Act, encounter.Tier))
            .OrderBy(group => group.Key.Act)
            .ThenBy(group => group.Key.Tier, StringComparer.Ordinal))
        {
            Console.WriteLine($"  encounter coverage act{group.Key.Act}/{group.Key.Tier}: {group.Count(encounter => encounter.IsSupported)}/{group.Count()}");
        }
        if (args.Contains("--verbose", StringComparer.Ordinal))
        {
            foreach (EncounterCombatDefinition encounter in inputs.Encounters.Where(encounter => encounter.IsSupported).OrderBy(encounter => encounter.StableId, StringComparer.Ordinal))
            {
                Console.WriteLine($"  supported encounter: {encounter.StableId} [{string.Join(',', encounter.Monsters.Select(monster => monster.TypeName))}]");
            }
            foreach (IGrouping<string, string> reason in inputs.Monsters.Values
                .Where(monster => !monster.IsSupported)
                .SelectMany(monster => monster.UnsupportedReasons.Select(item => item))
                .GroupBy(item => item.Contains(':') ? item[(item.IndexOf(':') + 1)..].Trim() : item)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Take(20))
            {
                Console.WriteLine($"  monster blocker x{reason.Count()}: {reason.Key}");
            }
        }

        foreach (CombatCoverageCellReport cell in coverage.Cells)
        {
            Console.WriteLine($"{cell.CellId}: supported={cell.SupportedSamples}/{cell.ProposalSamples} ({cell.SupportedFraction:P1}), " +
                $"fully supported encounters={cell.FullySupportedUniqueEncounters}/{cell.MinimumUniqueEncounters}, budget={cell.LossBudget}");
        }

        Console.WriteLine($"supported target-weight mass: {coverage.SupportedTargetWeightMass:P1}");
        Console.WriteLine($"proposal ESS: {inputs.SamplePlan.EffectiveSampleSize:0.0}");
        foreach (string warning in inputs.SamplePlan.Warnings) Console.WriteLine($"warning: {warning}");
        Console.WriteLine($"coverage json: {coverageJson}");
        Console.WriteLine($"coverage markdown: {coverageMarkdown}");
        Console.WriteLine($"smoke status: {smoke.Smoke.Status}");
        Console.WriteLine($"smoke json: {smoke.JsonPath}");
        Console.WriteLine($"smoke markdown: {smoke.MarkdownPath}");
        Console.WriteLine(coverage.CoverageGatePassed ? "combat portfolio valid for Phase 1 coverage gate" : "combat portfolio blocked by Phase 1 coverage gate");
        bool smokePassed = smoke.Smoke.Results.All(result => result.SolverStatus == CombatSolveStatus.Exact);
        return coverage.CoverageGatePassed && smokePassed ? 0 : 1;
    }

    private static (CombatSmokeReport Report, string JsonPath, string MarkdownPath) RunCombatSmokeScenarios(
        CombatInputBundle inputs,
        string outputDirectory)
    {
        string[] paths =
        [
            "data/manual-tags/simulation_scenarios/regent_combat_phase1_shortline.json",
            "data/manual-tags/simulation_scenarios/regent_combat_phase1_midline.json",
            "data/manual-tags/simulation_scenarios/regent_combat_phase1_longline.json"
        ];
        CombatSmokeScenarioLoader loader = new();
        CombatSimulationRunner runner = new(inputs.Cards, inputs.Monsters);
        List<CombatSmokeResult> results = [];
        List<string> warnings = [];
        foreach (string path in paths)
        {
            LoadedCombatSmokeScenario scenario = loader.Load(path, inputs.Cards, inputs.Encounters, inputs.Hp);
            if (!scenario.Sample.Supported)
            {
                throw new InvalidOperationException(
                    $"Smoke scenario '{scenario.Name}' is unsupported: {scenario.Sample.UnsupportedReason}");
            }
            CombatContextResult result = runner.EvaluateContext(
                scenario.Sample,
                scenario.Deck,
                scenario.Encounter,
                scenario.HpContext,
                scenario.Horizon);
            results.Add(new CombatSmokeResult(
                scenario.Name,
                scenario.Horizon,
                scenario.ExpectedStatus,
                result.SolverStatus,
                result.Metrics,
                result.CanonicalStates,
                result.MemoHits,
                result.OutcomeBranches,
                result.WallTime.TotalMilliseconds,
                result.AllocatedBytes));
            warnings.AddRange(scenario.Assumptions);
            warnings.Add($"{scenario.Name} oracle: {scenario.Oracle}");
        }

        bool exact = results.All(result => result.SolverStatus == CombatSolveStatus.Exact);
        CombatSmokeReport report = new(
            1,
            new CombatSimulationOptions().SemanticsVersion,
            DateTimeOffset.UtcNow.ToString("O"),
            false,
            exact ? "exact-smoke-complete" : "smoke-budget-blocked",
            results,
            [
                .. warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                "Smoke fixtures are physical-kernel checks, not stage-matched training samples."
            ]);
        (string json, string markdown) = new CombatSmokeReportWriter().Write(report, outputDirectory);
        return (report, json, markdown);
    }

    private static int ReplayMonsterIntents(string[] args)
    {
        string selector = GetOption(args, "--encounter")
            ?? throw new InvalidOperationException("replay-monster-intents requires --encounter modelIdOrTypeName.");
        int turns = GetIntOption(args, "--turns") ?? 12;
        CombatInputBundle inputs = LoadCombatInputs(args);
        EncounterCombatDefinition encounter = inputs.Encounters.FirstOrDefault(candidate =>
            string.Equals(candidate.ModelId, selector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.TypeName, selector, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Encounter '{selector}' was not found.");
        if (!encounter.IsSupported)
        {
            Console.Error.WriteLine($"unsupported: {string.Join("; ", encounter.UnsupportedReasons)}");
            return 1;
        }

        Console.WriteLine($"encounter: {encounter.ModelId} act={encounter.Act} tier={encounter.Tier}");
        bool deterministic = true;
        bool replayValid = true;
        foreach (EncounterMonsterDefinition slot in encounter.Monsters)
        {
            string stateId = slot.Monster.InitialStateId;
            Console.WriteLine($"slot {slot.Position}: {slot.TypeName} HP(A10)={slot.Monster.MinHpA10}-{slot.Monster.MaxHpA10}");
            for (int turn = 1; turn <= turns; turn++)
            {
                MonsterIntentDefinition intent = slot.Monster.Intents[stateId];
                string effects = string.Join(", ", intent.Effects.Select(effect => $"{effect.Kind}:{effect.Amount}x{effect.HitCount}"));
                Console.WriteLine($"  t{turn}: {stateId} [{effects}]");
                if (intent.Transitions.Count != 1 || Math.Abs(intent.Transitions[0].Probability - 1d) > 1e-9)
                {
                    deterministic = false;
                    Console.WriteLine($"    chance: {string.Join(", ", intent.Transitions.Select(item => $"{item.StateId}={item.Probability:0.###}"))}");
                    break;
                }
                stateId = intent.Transitions[0].StateId;
            }
        }

        string matrixPath = GetOption(args, "--matrix") ?? "data/generated/monster_encounter_damage_matrices.generated.json";
        if (deterministic && encounter.Monsters.Count == 1 && File.Exists(matrixPath))
        {
            replayValid = CompareSingleMonsterReplayWithMatrix(encounter, turns, matrixPath);
        }
        else if (!File.Exists(matrixPath))
        {
            Console.WriteLine($"warning: matrix file '{matrixPath}' not found; state replay only.");
        }

        Console.WriteLine(deterministic
            ? "strict deterministic replay complete"
            : "strict replay reached a sourced chance transition; use the JSON distribution for branch-wise matrix comparison");
        return replayValid ? 0 : 1;
    }

    private static int BenchmarkInformationStateSolver(string[] args)
    {
        int iterations = Math.Max(1, GetIntOption(args, "--iterations") ?? 20);
        int[] workers = (GetOption(args, "--workers") ?? "1,2,4")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Math.Clamp(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture), 1, 4))
            .Distinct()
            .Order()
            .ToArray();
        (CombatCardCatalog cards, IReadOnlyDictionary<string, CombatMonsterDefinition> monsters, HpContinuationContext hp) = BuildCombatBenchmarkFixture();
        SolverBenchmarkRow[] rows = workers.Select(worker => RunSolverBenchmark(worker, iterations, cards, monsters, hp)).ToArray();
        string outputDirectory = GetOption(args, "--output") ?? "data/generated/combat_aware";
        Directory.CreateDirectory(outputDirectory);
        SolverBenchmarkReport report = new(
            1,
            new CombatSimulationOptions().SemanticsVersion,
            DateTimeOffset.UtcNow.ToString("O"),
            "8-card physical combat: damage/block/draw, deterministic alternating monster intent, horizon 4",
            "ordinary-wall-benchmark",
            false,
            rows,
            [
                "Workers parallelize independent solves; one solver remains single-threaded.",
                "Profiler-instrumented wall time must not be compared with these ordinary timings.",
                "The benchmark validates the new solver only; legacy comparison requires a separately declared semantic-equivalence fixture."
            ]);
        string jsonPath = Path.Combine(outputDirectory, "information_state_solver_benchmark.generated.json");
        string markdownPath = Path.Combine(outputDirectory, "information_state_solver_benchmark.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, CombatJson.Options));
        File.WriteAllText(markdownPath, BuildSolverBenchmarkMarkdown(report));
        foreach (SolverBenchmarkRow row in rows)
        {
            Console.WriteLine($"workers: {row.Workers}");
            Console.WriteLine($"iterations: {row.Iterations}");
            Console.WriteLine($"wall total ms: {row.TotalWallMilliseconds:0.###}");
            Console.WriteLine($"throughput solves/s: {row.ThroughputSolvesPerSecond:0.###}");
            Console.WriteLine($"p50/p95/p99 ms: {row.P50Milliseconds:0.###}/{row.P95Milliseconds:0.###}/{row.P99Milliseconds:0.###}");
            Console.WriteLine($"mean allocated bytes/solve: {row.MeanAllocatedBytes:0}");
            Console.WriteLine($"mean states/memo hits/outcome branches: {row.MeanCanonicalStates:0}/{row.MeanMemoHits:0}/{row.MeanOutcomeBranches:0}");
        }
        Console.WriteLine($"json: {jsonPath}");
        Console.WriteLine($"markdown: {markdownPath}");
        Console.WriteLine("note: this is the ordinary benchmark path; profiler wall time is intentionally not mixed in.");
        return 0;
    }

    private static SolverBenchmarkRow RunSolverBenchmark(
        int workers,
        int iterations,
        CombatCardCatalog cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        HpContinuationContext hp)
    {
        ConcurrentBag<double> milliseconds = [];
        ConcurrentBag<long> allocations = [];
        ConcurrentBag<long> canonicalStates = [];
        ConcurrentBag<long> memoHits = [];
        ConcurrentBag<long> chanceBranches = [];
        Stopwatch total = Stopwatch.StartNew();
        Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = workers }, ordinal =>
        {
            CombatSimulationOptions options = new() { HorizonTurns = 4, MaximumCanonicalStates = 100_000 };
            CombatInformationState state = CombatStateFactory.Create(
                60,
                80,
                [0, 0, 0, 1, 1, 1, 2, 2],
                [new CombatMonsterSeed($"dummy-{ordinal}", "BenchmarkMonster", 42, "ATTACK")],
                options,
                initialHand: [0, 1, 2]);
            CombatTransitionKernel kernel = new(cards, monsters, options);
            InformationStateSolver solver = new(
                kernel,
                new CombatChanceResolver(),
                new CombatTerminalEvaluator(new ReferenceCombatPolicy(monsters)),
                options);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch watch = Stopwatch.StartNew();
            CombatSolveResult result = solver.Solve(state, hp);
            watch.Stop();
            if (result.Status == CombatSolveStatus.ExactBudgetExceeded) throw new InvalidOperationException(result.Message);
            milliseconds.Add(watch.Elapsed.TotalMilliseconds);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            canonicalStates.Add(result.CanonicalStates);
            memoHits.Add(result.MemoHits);
            chanceBranches.Add(result.OutcomeBranches);
        });
        total.Stop();
        double[] times = milliseconds.Order().ToArray();
        return new SolverBenchmarkRow(
            workers,
            iterations,
            total.Elapsed.TotalMilliseconds,
            iterations / total.Elapsed.TotalSeconds,
            CombatPercentile(times, 0.50),
            CombatPercentile(times, 0.95),
            CombatPercentile(times, 0.99),
            allocations.Average(),
            canonicalStates.Average(),
            memoHits.Average(),
            chanceBranches.Average());
    }

    private static string BuildSolverBenchmarkMarkdown(SolverBenchmarkReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Information-state solver benchmark");
        builder.AppendLine();
        builder.AppendLine($"Fixture: {report.Fixture}");
        builder.AppendLine();
        builder.AppendLine("| workers | iterations | total ms | solves/s | p50 ms | p95 ms | p99 ms | allocated/solve | states | memo hits | chance branches |");
        builder.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (SolverBenchmarkRow row in report.Rows)
        {
            builder.AppendLine($"| {row.Workers} | {row.Iterations} | {row.TotalWallMilliseconds:0.###} | {row.ThroughputSolvesPerSecond:0.###} | " +
                $"{row.P50Milliseconds:0.###} | {row.P95Milliseconds:0.###} | {row.P99Milliseconds:0.###} | {row.MeanAllocatedBytes:0} | " +
                $"{row.MeanCanonicalStates:0} | {row.MeanMemoHits:0} | {row.MeanOutcomeBranches:0} |");
        }
        builder.AppendLine();
        foreach (string note in report.Notes) builder.AppendLine($"- {note}");
        return builder.ToString();
    }

    private static int EstimateCombatAwareDeckDelta(string[] args)
    {
        string candidateKey = GetOption(args, "--candidate")
            ?? throw new InvalidOperationException("estimate-combat-aware-deck-delta requires --candidate.");
        CombatInputBundle inputs = LoadCombatInputs(args);
        if (!inputs.Cards.TryGet(candidateKey, out CombatCardDefinition? candidate))
        {
            throw new InvalidOperationException($"Candidate '{candidateKey}' was not found.");
        }

        int[] horizons = ParseCombatHorizons(GetOption(args, "--horizons") ?? "4,8,12");
        CombatDeckDeltaOptions options = new(
            GetIntOption(args, "--minimum-samples") ?? 12,
            GetIntOption(args, "--maximum-samples") ?? 240,
            Math.Clamp(GetIntOption(args, "--degree-of-parallelism") ?? 4, 1, 4),
            !args.Contains("--no-cache", StringComparer.Ordinal));
        CombatDeckDeltaReport report = new CombatDeckDeltaEstimator(
            inputs.Cards,
            inputs.Monsters,
            inputs.Hp)
            .Estimate(
                inputs.SamplePlan,
                inputs.Decks,
                inputs.Encounters.ToDictionary(encounter => encounter.StableId, StringComparer.Ordinal),
                candidate,
                horizons,
                options,
                inputs.CombatModelHash);
        string outputDirectory = GetOption(args, "--output") ?? "data/generated/combat_aware";
        (string hpJson, string hpMarkdown) = new HpSensitivityReportWriter().Write(inputs.Hp, outputDirectory);
        (string json, string markdown) = new CombatReportWriter().Write(
            report,
            outputDirectory);
        Console.WriteLine($"hp sensitivity json: {hpJson}");
        Console.WriteLine($"hp sensitivity markdown: {hpMarkdown}");
        Console.WriteLine($"json: {json}");
        Console.WriteLine($"markdown: {markdown}");
        Console.WriteLine($"runtimeCandidate: {report.RuntimeCandidate.ToString().ToLowerInvariant()}");
        return report.Status == "research-review" ? 0 : 1;
    }

    private static CombatInputBundle LoadCombatInputs(string[] args)
    {
        string hpPath = GetOption(args, "--hp-calibration") ?? "data/manual-tags/hp_continuation_calibration.json";
        string portfolioPath = GetOption(args, "--portfolio") ?? "data/manual-tags/combat_value_portfolios.json";
        string factsPath = GetOption(args, "--facts") ?? "data/extracted/card_facts.generated.json";
        string profilesPath = GetOption(args, "--profiles") ?? "data/extracted/monster_move_profiles.generated.json";
        string patternsPath = GetOption(args, "--patterns") ?? "data/extracted/encounter_patterns.generated.json";
        string monsterOverridesPath = GetOption(args, "--monster-overrides") ?? "data/manual-tags/monster_move_overrides.json";
        string encounterOverridesPath = GetOption(args, "--encounter-overrides") ?? "data/manual-tags/combat_encounter_overrides.json";
        HpContinuationCatalog hp = HpContinuationCatalog.Load(hpPath);
        LoadedCombatPortfolio portfolio = CombatPortfolioLoader.Load(portfolioPath, hp, GetOption(args, "--portfolio-id"));
        CombatCardCatalog cards = new CombatCardCompiler().CompileFile(factsPath);
        MonsterOverrideCatalog overrides = MonsterOverrideCatalog.Load(monsterOverridesPath);
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = new MonsterIntentCompiler().CompileFile(profilesPath, overrides);
        EncounterOverrideCatalog encounterOverrides = EncounterOverrideCatalog.Load(encounterOverridesPath);
        IReadOnlyList<EncounterCombatDefinition> encounters = new EncounterCombatCompiler().CompileFile(patternsPath, monsters, encounterOverrides);
        CombatDeckSnapshotLoader deckLoader = new();
        IReadOnlyList<CombatDeckSnapshot> snapshots = deckLoader.Load(GetOption(args, "--deck-source") ?? portfolio.Definition.DeckSource);
        Dictionary<string, CompiledCombatDeck> decks = snapshots.ToDictionary(
            snapshot => snapshot.SnapshotId,
            snapshot => deckLoader.CompileDeck(snapshot, cards),
            StringComparer.Ordinal);
        int seed = GetIntOption(args, "--seed") ?? 1;
        CombatSamplePlan samplePlan = new CombatPortfolioSampler().BuildPlan(portfolio, snapshots, encounters, cards, seed);
        return new CombatInputBundle(
            hp,
            portfolio,
            cards,
            monsters,
            encounters,
            snapshots,
            decks,
            samplePlan,
            CombatJson.Sha256Files([factsPath, profilesPath, patternsPath, monsterOverridesPath, encounterOverridesPath]));
    }

    private static int[] ParseCombatHorizons(string value)
    {
        int[] horizons = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.Parse(item, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();
        if (horizons.Length == 0 || horizons.Any(horizon => horizon <= 0))
        {
            throw new InvalidOperationException("--horizons must contain positive comma-separated turn counts.");
        }
        return horizons;
    }

    private static bool CompareSingleMonsterReplayWithMatrix(
        EncounterCombatDefinition encounter,
        int turns,
        string matrixPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(matrixPath));
        JsonElement? encounterJson = document.RootElement.GetProperty("encounters").EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(item =>
                string.Equals(item?.GetProperty("modelId").GetString(), encounter.ModelId, StringComparison.Ordinal));
        if (!encounterJson.HasValue)
        {
            Console.Error.WriteLine($"matrix mismatch: {encounter.ModelId} not found in {matrixPath}.");
            return false;
        }

        JsonElement? table = encounterJson.Value.GetProperty("tables").EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(item => string.Equals(item?.GetProperty("id").GetString(), "full-sequence", StringComparison.Ordinal));
        if (!table.HasValue)
        {
            table = encounterJson.Value.GetProperty("tables").EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
        }
        if (!table.HasValue)
        {
            Console.Error.WriteLine($"matrix mismatch: {encounter.ModelId} has no exact table.");
            return false;
        }

        CombatMonsterDefinition monster = encounter.Monsters[0].Monster;
        string stateId = monster.InitialStateId;
        int strength = 0;
        bool valid = true;
        foreach (JsonElement row in table.Value.GetProperty("rows").EnumerateArray().Take(turns))
        {
            int turn = row.GetProperty("turn").GetInt32();
            JsonElement cell = row.GetProperty("cells")[0];
            string matrixState = cell.GetProperty("stateId").GetString() ?? string.Empty;
            if (!string.Equals(matrixState, stateId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"matrix mismatch t{turn}: compiler state {stateId}, matrix state {matrixState}.");
                valid = false;
                break;
            }

            MonsterIntentDefinition intent = monster.Intents[stateId];
            double expectedDamage = 0d;
            foreach (MonsterIntentEffect effect in intent.Effects)
            {
                if (effect.Kind == MonsterIntentEffectKind.Attack)
                {
                    expectedDamage += Math.Max(0, effect.Amount + strength) * effect.HitCount;
                }
                else if (effect.Kind == MonsterIntentEffectKind.GainStrength)
                {
                    strength += effect.Amount;
                }
            }
            double matrixDamage = cell.GetProperty("damage").GetDouble();
            if (Math.Abs(expectedDamage - matrixDamage) > 1e-9)
            {
                Console.Error.WriteLine($"matrix mismatch t{turn}/{stateId}: compiler A10 damage {expectedDamage:R}, matrix {matrixDamage:R}.");
                valid = false;
                break;
            }
            stateId = intent.Transitions[0].StateId;
        }

        Console.WriteLine(valid
            ? $"matrix replay matched: {Path.GetFileName(matrixPath)}"
            : "matrix replay failed");
        return valid;
    }

    private static (CombatCardCatalog Cards, IReadOnlyDictionary<string, CombatMonsterDefinition> Monsters, HpContinuationContext Hp) BuildCombatBenchmarkFixture()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(0, "BENCH.STRIKE", "BenchStrike", 0, "Attack", 1, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 6, 1, CombatCardTarget.Enemy, "benchmark", 0)], false, true, true, []),
            new CombatCardDefinition(1, "BENCH.DEFEND", "BenchDefend", 0, "Skill", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 5, 1, CombatCardTarget.Self, "benchmark", 0)], false, true, true, []),
            new CombatCardDefinition(2, "BENCH.DRAW", "BenchDraw", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Draw, 1, 1, CombatCardTarget.Self, "benchmark", 0)], true, true, true, [])
        ]);
        MonsterIntentDefinition attack = new(
            "ATTACK",
            [new MonsterIntentEffect(MonsterIntentEffectKind.Attack, 8, 1, "player", "benchmark", 0)],
            [new MonsterIntentTransition("DEFEND", 1, "benchmark")]);
        MonsterIntentDefinition defend = new(
            "DEFEND",
            [new MonsterIntentEffect(MonsterIntentEffectKind.Block, 5, 1, "self", "benchmark", 0)],
            [new MonsterIntentTransition("ATTACK", 1, "benchmark")]);
        CombatMonsterDefinition monster = new(
            "BENCH.MONSTER", "BenchmarkMonster", 42, 42, "ATTACK",
            new Dictionary<string, MonsterIntentDefinition>(StringComparer.Ordinal) { ["ATTACK"] = attack, ["DEFEND"] = defend },
            true, [], "benchmark");
        return (
            cards,
            new Dictionary<string, CombatMonsterDefinition>(StringComparer.Ordinal) { [monster.TypeName] = monster },
            new HpContinuationContext("benchmark", 1, "normal", 8, 0.2, 0.03, 50));
    }

    private static double CombatPercentile(IReadOnlyList<double> sorted, double probability)
    {
        if (sorted.Count == 0) return 0;
        int index = Math.Clamp((int)Math.Ceiling(sorted.Count * probability) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }
}
