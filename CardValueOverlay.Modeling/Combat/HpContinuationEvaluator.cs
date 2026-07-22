namespace CardValueOverlay.Modeling.Combat;

public sealed class HpContinuationEvaluator
{
    public double EvaluateAlive(int hp, int maxHp, HpContinuationContext context)
    {
        if (maxHp <= 0 || hp <= 0 || hp > maxHp)
        {
            throw new ArgumentOutOfRangeException(nameof(hp), "Alive HP must be in [1, maxHp].");
        }

        int loss = maxHp - hp;
        int effectiveBudget = Math.Min(context.LossBudget, Math.Max(0, maxHp - 1));
        int excess = Math.Max(0, loss - effectiveBudget);
        return -(context.LambdaSafe * loss + context.Kappa * excess * excess);
    }

    public double MarginalHpValue(int hp, int maxHp, HpContinuationContext context)
    {
        if (hp <= 0 || hp >= maxHp)
        {
            throw new ArgumentOutOfRangeException(nameof(hp));
        }

        return EvaluateAlive(hp + 1, maxHp, context) - EvaluateAlive(hp, maxHp, context);
    }

    public double HpLossCost(int hp, int maxHp, int damage, HpContinuationContext context)
    {
        int finalHp = Math.Max(1, hp - Math.Max(0, damage));
        return EvaluateAlive(hp, maxHp, context) - EvaluateAlive(finalHp, maxHp, context);
    }

    public double HealingValue(int hp, int maxHp, int healing, HpContinuationContext context)
    {
        int finalHp = Math.Min(maxHp, hp + Math.Max(0, healing));
        return EvaluateAlive(finalHp, maxHp, context) - EvaluateAlive(hp, maxHp, context);
    }
}
