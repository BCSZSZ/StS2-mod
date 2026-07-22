namespace CardValueOverlay.Modeling.Combat;

public static class CombatValueBounds
{
    public static double DeathOpportunityLoss(
        CombatInformationState state,
        HpContinuationContext context,
        HpContinuationEvaluator evaluator)
    {
        double worstAliveUtility = evaluator.EvaluateAlive(1, state.Player.MaxHp, context);
        return state.InitialEnemyHpTotal + context.FutureReserveValue + Math.Abs(worstAliveUtility) + 1d;
    }
}
