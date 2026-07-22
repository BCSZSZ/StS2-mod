using System.Diagnostics;
using CardValueOverlay.Modeling.Combat.Portfolio;

namespace CardValueOverlay.Modeling.Combat;

public sealed class CombatSimulationRunner
{
    private readonly CombatCardCatalog _cards;
    private readonly IReadOnlyDictionary<string, CombatMonsterDefinition> _monsters;

    public CombatSimulationRunner(
        CombatCardCatalog cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters)
    {
        _cards = cards;
        _monsters = monsters;
    }

    public IReadOnlyDictionary<int, CombatContextResult> RunIndependentHorizons(
        CombatSample sample,
        CompiledCombatDeck deck,
        EncounterCombatDefinition encounter,
        HpContinuationContext hpContext,
        IReadOnlyList<int> horizons) => horizons
        .Distinct()
        .Order()
        .ToDictionary(
            horizon => horizon,
            horizon => EvaluateContext(sample, deck, encounter, hpContext, horizon));

    public CombatContextResult EvaluateContext(
        CombatSample sample,
        CompiledCombatDeck deck,
        EncounterCombatDefinition encounter,
        HpContinuationContext hpContext,
        int horizon)
    {
        if (!sample.Supported || !deck.IsSupported || !encounter.IsSupported)
        {
            throw new InvalidOperationException($"Combat sample '{sample.SampleId}' is unsupported.");
        }

        CombatSimulationOptions options = new() { HorizonTurns = horizon };
        CombatInformationState state = CreateState(sample, deck, encounter, options);
        CombatChanceResolver chance = new();
        CombatDrawOutcome[] initialDraws = chance
            .EnumerateInitialDrawOutcomes(state, _cards, options.HandSize, options.MaxHandSize)
            .ToArray();
        CombatPhysicalMetrics aggregate = CombatPhysicalMetrics.Zero;
        long states = 0, hits = 0, decisions = 0, chanceNodes = 0, branches = 0;
        CombatSolveStatus status = CombatSolveStatus.Exact;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        foreach (CombatDrawOutcome initialDraw in initialDraws)
        {
            CombatMutationJournal initialJournal = new();
            CombatChanceOutcome initialOutcome = new(
                initialDraw.Probability,
                initialDraw,
                Enumerable.Repeat(string.Empty, state.Monsters.Count).ToArray(),
                initialDraw.StableKey);
            int mark = initialJournal.Mark();
            chance.Apply(state, initialOutcome, initialJournal);
            CombatTransitionKernel kernel = new(_cards, _monsters, options);
            CombatTerminalEvaluator terminal = new(new ReferenceCombatPolicy(_monsters));
            InformationStateSolver solver = new(kernel, chance, terminal, options);
            CombatSolveResult solve = solver.Solve(state, hpContext);
            if (solve.Status == CombatSolveStatus.ExactBudgetExceeded)
            {
                status = solve.Status;
                initialJournal.UndoTo(state, mark);
                break;
            }

            CombatPhysicalMetrics metrics = EvaluatePolicy(
                state,
                hpContext,
                solve.Policy ?? throw new InvalidOperationException("Solver did not return a policy."),
                kernel,
                chance,
                terminal,
                options,
                new CombatMutationJournal());
            aggregate += metrics.Scale(initialDraw.Probability);
            states += solve.CanonicalStates;
            hits += solve.MemoHits;
            decisions += solve.DecisionNodes;
            chanceNodes += solve.ChanceNodes;
            branches += solve.OutcomeBranches;
            if (solve.Status == CombatSolveStatus.SparseEstimate) status = solve.Status;
            initialJournal.UndoTo(state, mark);
        }
        stopwatch.Stop();

        return new CombatContextResult(
            sample.SampleId,
            horizon,
            aggregate,
            status,
            states,
            hits,
            decisions,
            chanceNodes,
            branches,
            stopwatch.Elapsed,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore));
    }

    private CombatPhysicalMetrics EvaluatePolicy(
        CombatInformationState state,
        HpContinuationContext hpContext,
        IReadOnlyDictionary<CombatStateKey, CombatAction> policy,
        CombatTransitionKernel kernel,
        CombatChanceResolver chance,
        CombatTerminalEvaluator terminal,
        CombatSimulationOptions options,
        CombatMutationJournal journal)
    {
        if (state.IsTerminal || state.Turn > options.HorizonTurns)
        {
            CombatTerminalValue terminalValue = terminal.Evaluate(state, hpContext);
            return new CombatPhysicalMetrics(
                state.Ledger.NetOffenseValue + terminalValue.Value,
                state.Ledger.ActualEnemyHpDamage,
                state.Ledger.EnemyHpRestored,
                terminalValue.HpUtility,
                state.Ledger.PlayerHpLost,
                state.Ledger.OverkillDamage,
                state.Ledger.UnusedPlayerBlock,
                state.Player.IsAlive ? 0d : 1d,
                terminalValue.ReferenceTailHpLoss,
                state.Turn - 1);
        }

        CombatStateKey key = state.GetCanonicalKey();
        if (!policy.TryGetValue(key, out CombatAction action))
        {
            throw new InvalidOperationException($"Policy has no action for visible state '{key.First:x16}{key.Second:x16}'.");
        }

        int actionMark = journal.Mark();
        CombatPreparedTransition prepared = kernel.Prepare(state, action, journal);
        IReadOnlyList<CombatDrawOutcome> draws = chance.EnumerateDrawOutcomes(state, prepared.DrawCount, options.MaxHandSize);
        IReadOnlyList<CombatChanceOutcome> outcomes = chance.Combine(draws, prepared.MonsterTransitions, CombatSolveMode.Exact, int.MaxValue);
        CombatPhysicalMetrics aggregate = CombatPhysicalMetrics.Zero;
        foreach (CombatChanceOutcome outcome in outcomes)
        {
            int outcomeMark = journal.Mark();
            chance.Apply(state, outcome, journal);
            aggregate += EvaluatePolicy(state, hpContext, policy, kernel, chance, terminal, options, journal).Scale(outcome.Probability);
            journal.UndoTo(state, outcomeMark);
        }
        journal.UndoTo(state, actionMark);
        return aggregate;
    }

    private static CombatInformationState CreateState(
        CombatSample sample,
        CompiledCombatDeck deck,
        EncounterCombatDefinition encounter,
        CombatSimulationOptions options)
    {
        CombatMonsterSeed[] monsters = encounter.Monsters.Select(slot =>
        {
            int range = slot.Monster.MaxHpA10 - slot.Monster.MinHpA10 + 1;
            int hp = sample.MonsterHpByPosition?.GetValueOrDefault(slot.Position) ??
                (slot.Monster.MinHpA10 + SemanticRandomStreams.Index(
                    SemanticRandomStreams.ForMonsterTransition(sample.RunKey, $"{sample.EncounterId}:{slot.Position}", 0),
                    range));
            if (hp < slot.Monster.MinHpA10 || hp > slot.Monster.MaxHpA10)
            {
                throw new InvalidOperationException(
                    $"Sample '{sample.SampleId}' monster HP {hp} is outside A10 range {slot.Monster.MinHpA10}-{slot.Monster.MaxHpA10} for slot {slot.Position}.");
            }
            string initialIntent = sample.InitialIntentByPosition?.GetValueOrDefault(slot.Position) ?? slot.Monster.InitialStateId;
            if (!slot.Monster.Intents.ContainsKey(initialIntent))
            {
                throw new InvalidOperationException(
                    $"Sample '{sample.SampleId}' initial intent '{initialIntent}' is unknown for slot {slot.Position}.");
            }
            return new CombatMonsterSeed(
                $"{sample.EncounterId}:{slot.Position}",
                slot.TypeName,
                hp,
                initialIntent);
        }).ToArray();
        return CombatStateFactory.Create(
            sample.PlayerHp,
            sample.PlayerMaxHp,
            deck.CardDefinitionIds,
            monsters,
            options);
    }
}
