namespace CardValueOverlay.Modeling.Combat;

public sealed class InformationStateSolver
{
    private readonly CombatTransitionKernel _kernel;
    private readonly CombatChanceResolver _chance;
    private readonly CombatTerminalEvaluator _terminal;
    private readonly CombatSimulationOptions _options;
    private readonly CombatMemoization _memo = new();
    private readonly CombatMutationJournal _journal = new();
    private readonly Dictionary<CombatStateKey, CombatAction> _policy = [];
    private readonly List<CombatAction>[] _actionBuffers;

    private long _canonicalStates;
    private long _memoHits;
    private long _decisionNodes;
    private long _chanceNodes;
    private long _outcomeBranches;
    private int _maximumDepth;
    private bool _usedSparse;

    public InformationStateSolver(
        CombatTransitionKernel kernel,
        CombatChanceResolver chance,
        CombatTerminalEvaluator terminal,
        CombatSimulationOptions options)
    {
        _kernel = kernel;
        _chance = chance;
        _terminal = terminal;
        _options = options;
        options.Validate();
        _actionBuffers = Enumerable.Range(0, options.MaximumDecisionDepth + 2)
            .Select(_ => new List<CombatAction>(16))
            .ToArray();
    }

    public CombatSolveResult Solve(CombatInformationState state, HpContinuationContext hpContext)
    {
        ResetTelemetry();
        string initialEncoding = state.Encode();
        try
        {
            (double value, CombatAction bestAction) = SolveNode(state, hpContext, 0, isRoot: true);
            if (!string.Equals(initialEncoding, state.Encode(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Information-state solver did not restore the root state after search.");
            }

            return new CombatSolveResult(
                value,
                bestAction,
                _usedSparse ? CombatSolveStatus.SparseEstimate : CombatSolveStatus.Exact,
                _canonicalStates,
                _memoHits,
                _decisionNodes,
                _chanceNodes,
                _outcomeBranches,
                _maximumDepth,
                Policy: new Dictionary<CombatStateKey, CombatAction>(_policy));
        }
        catch (ExactBudgetExceededException ex)
        {
            _journal.UndoTo(state, 0);
            return new CombatSolveResult(
                double.NaN,
                CombatAction.EndTurn,
                CombatSolveStatus.ExactBudgetExceeded,
                _canonicalStates,
                _memoHits,
                _decisionNodes,
                _chanceNodes,
                _outcomeBranches,
                _maximumDepth,
                ex.Message);
        }
    }

    private (double Value, CombatAction BestAction) SolveNode(
        CombatInformationState state,
        HpContinuationContext hpContext,
        int depth,
        bool isRoot)
    {
        _maximumDepth = Math.Max(_maximumDepth, depth);
        if (depth > _options.MaximumDecisionDepth)
        {
            throw new ExactBudgetExceededException($"Decision depth exceeded {_options.MaximumDecisionDepth}.");
        }

        if (state.IsTerminal || state.Turn > _options.HorizonTurns)
        {
            return (_terminal.Evaluate(state, hpContext).Value, CombatAction.EndTurn);
        }

        CombatStateKey key = state.GetCanonicalKey();
        if (!isRoot && _options.EnableMemoization && _memo.TryGet(key, out double memoized))
        {
            _memoHits++;
            return (memoized, CombatAction.EndTurn);
        }

        _canonicalStates++;
        if (_canonicalStates > _options.MaximumCanonicalStates)
        {
            throw new ExactBudgetExceededException($"Canonical state budget exceeded {_options.MaximumCanonicalStates}.");
        }

        _decisionNodes++;
        double bestValue = double.NegativeInfinity;
        CombatAction bestAction = CombatAction.EndTurn;
        List<CombatAction> actions = _actionBuffers[depth];
        _kernel.FillLegalActions(state, actions);
        for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
        {
            CombatAction action = actions[actionIndex];
            int actionMark = _journal.Mark();
            CombatPreparedTransition prepared = _kernel.Prepare(state, action, _journal);
            IReadOnlyList<CombatDrawOutcome> draws = _chance.EnumerateDrawOutcomes(
                state,
                prepared.DrawCount,
                _options.MaxHandSize);
            int exactOutcomeCount = CountCombinedOutcomes(draws, prepared.MonsterTransitions);
            if (_options.SolveMode == CombatSolveMode.Exact && exactOutcomeCount > _options.ExactOutcomeLimit)
            {
                _journal.UndoTo(state, actionMark);
                throw new ExactBudgetExceededException(
                    $"Exact chance outcome count {exactOutcomeCount} exceeded per-node limit {_options.ExactOutcomeLimit}.");
            }
            IReadOnlyList<CombatChanceOutcome> outcomes = _chance.Combine(
                draws,
                prepared.MonsterTransitions,
                _options.SolveMode,
                _options.SparseChanceSamples);
            if (outcomes.Count > 1)
            {
                _chanceNodes++;
            }
            if (outcomes.Count < exactOutcomeCount)
            {
                _usedSparse = true;
            }
            _outcomeBranches += outcomes.Count;
            if (_outcomeBranches > _options.MaximumChanceBranches)
            {
                _journal.UndoTo(state, actionMark);
                throw new ExactBudgetExceededException(
                    $"Chance branch budget exceeded {_options.MaximumChanceBranches}.");
            }

            double expected = 0d;
            foreach (CombatChanceOutcome outcome in outcomes)
            {
                int outcomeMark = _journal.Mark();
                _chance.Apply(state, outcome, _journal);
                double continuation = SolveNode(state, hpContext, depth + 1, isRoot: false).Value;
                expected += outcome.Probability * (prepared.ImmediateReward + continuation);
                _journal.UndoTo(state, outcomeMark);
            }
            _journal.UndoTo(state, actionMark);

            if (expected > bestValue + 1e-12 ||
                (Math.Abs(expected - bestValue) <= 1e-12 &&
                 CombatAction.CompareStable(action, bestAction) < 0))
            {
                bestValue = expected;
                bestAction = action;
            }
        }

        if (_options.EnableMemoization)
        {
            _memo.Set(key, bestValue);
        }
        _policy[key] = bestAction;
        return (bestValue, bestAction);
    }

    private static int CountCombinedOutcomes(
        IReadOnlyList<CombatDrawOutcome> draws,
        IReadOnlyList<IReadOnlyList<MonsterIntentTransition>> transitions)
    {
        long total = Math.Max(1, draws.Count);
        foreach (IReadOnlyList<MonsterIntentTransition> transition in transitions)
        {
            total *= Math.Max(1, transition.Count);
            if (total > int.MaxValue)
            {
                return int.MaxValue;
            }
        }
        return (int)total;
    }

    private void ResetTelemetry()
    {
        _memo.Clear();
        _policy.Clear();
        _journal.EnsureEmpty();
        _canonicalStates = 0;
        _memoHits = 0;
        _decisionNodes = 0;
        _chanceNodes = 0;
        _outcomeBranches = 0;
        _maximumDepth = 0;
        _usedSparse = false;
    }

    private sealed class ExactBudgetExceededException(string message) : Exception(message);
}
