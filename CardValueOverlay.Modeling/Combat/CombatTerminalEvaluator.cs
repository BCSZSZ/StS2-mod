namespace CardValueOverlay.Modeling.Combat;

public sealed record CombatTerminalValue(
    double Value,
    double HpUtility,
    double ReferenceTailHpLoss,
    bool DeathDominanceApplied);

public sealed class CombatTerminalEvaluator
{
    private readonly HpContinuationEvaluator _hp = new();
    private readonly ReferenceCombatPolicy _referencePolicy;
    private readonly Dictionary<(CombatStateKey State, string ContextId), CombatTerminalValue> _cache = [];

    public CombatTerminalEvaluator(ReferenceCombatPolicy referencePolicy)
    {
        _referencePolicy = referencePolicy;
    }

    public CombatTerminalValue Evaluate(CombatInformationState state, HpContinuationContext context)
    {
        (CombatStateKey State, string ContextId) key = (state.GetTerminalKey(), context.Id);
        if (_cache.TryGetValue(key, out CombatTerminalValue? cached))
        {
            return cached;
        }

        CombatTerminalValue result;
        if (!state.Player.IsAlive)
        {
            double deathLoss = CombatValueBounds.DeathOpportunityLoss(state, context, _hp);
            result = new CombatTerminalValue(-deathLoss, -deathLoss, 0d, true);
            _cache[key] = result;
            return result;
        }

        double tailLoss = state.AllMonstersDead ? 0d : _referencePolicy.EstimateFutureHpLoss(state);
        int projectedHp = Math.Max(1, state.Player.Hp - (int)Math.Ceiling(tailLoss));
        double utility = _hp.EvaluateAlive(projectedHp, state.Player.MaxHp, context);
        result = new CombatTerminalValue(utility, utility, tailLoss, false);
        _cache[key] = result;
        return result;
    }
}
