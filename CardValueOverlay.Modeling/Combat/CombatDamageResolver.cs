namespace CardValueOverlay.Modeling.Combat;

public readonly record struct CombatDamageResult(
    int Attempted,
    int BlockBroken,
    int HpLost,
    int Overkill,
    double OffenseReward);

public sealed class CombatDamageResolver
{
    public CombatDamageResult ResolvePlayerAttack(
        CombatInformationState state,
        int monsterIndex,
        int baseDamage,
        int hitCount,
        CombatMutationJournal journal)
    {
        CombatMonsterState target = state.Monsters[monsterIndex];
        if (!target.IsAlive)
        {
            return default;
        }

        int perHit = ApplyAttackModifiers(baseDamage, state.Player.Strength, state.Player.Weak, target.Vulnerable);
        int attempted = 0;
        int blockBroken = 0;
        int hpLost = 0;
        int overkill = 0;
        for (int hit = 0; hit < hitCount && target.IsAlive; hit++)
        {
            attempted += perHit;
            int absorbed = Math.Min(target.Block, perHit);
            if (absorbed > 0)
            {
                journal.AddScalar(state, CombatScalarField.MonsterBlock, -absorbed, monsterIndex);
                blockBroken += absorbed;
            }

            int throughBlock = perHit - absorbed;
            int lost = Math.Min(target.Hp, throughBlock);
            if (lost > 0)
            {
                journal.AddScalar(state, CombatScalarField.MonsterHp, -lost, monsterIndex);
                hpLost += lost;
            }

            overkill += Math.Max(0, throughBlock - lost);
        }

        journal.AddLedger(state, CombatLedgerField.AttemptedDamage, attempted);
        journal.AddLedger(state, CombatLedgerField.EnemyBlockBroken, blockBroken);
        journal.AddLedger(state, CombatLedgerField.ActualEnemyHpDamage, hpLost);
        journal.AddLedger(state, CombatLedgerField.OverkillDamage, overkill);
        return new CombatDamageResult(attempted, blockBroken, hpLost, overkill, hpLost);
    }

    public CombatDamageResult ResolveMonsterAttack(
        CombatInformationState state,
        int monsterIndex,
        int baseDamage,
        int hitCount,
        CombatMutationJournal journal)
    {
        CombatMonsterState attacker = state.Monsters[monsterIndex];
        if (!attacker.IsAlive || !state.Player.IsAlive)
        {
            return default;
        }

        int perHit = ApplyAttackModifiers(baseDamage, attacker.Strength, attacker.Weak, state.Player.Vulnerable);
        int attempted = 0;
        int blockBroken = 0;
        int hpLost = 0;
        int overkill = 0;
        for (int hit = 0; hit < hitCount && state.Player.IsAlive; hit++)
        {
            attempted += perHit;
            int absorbed = Math.Min(state.Player.Block, perHit);
            if (absorbed > 0)
            {
                journal.AddScalar(state, CombatScalarField.PlayerBlock, -absorbed);
                blockBroken += absorbed;
            }

            int throughBlock = perHit - absorbed;
            int lost = Math.Min(state.Player.Hp, throughBlock);
            if (lost > 0)
            {
                journal.AddScalar(state, CombatScalarField.PlayerHp, -lost);
                hpLost += lost;
            }

            overkill += Math.Max(0, throughBlock - lost);
        }

        journal.AddLedger(state, CombatLedgerField.PlayerHpLost, hpLost);
        return new CombatDamageResult(attempted, blockBroken, hpLost, overkill, 0d);
    }

    public int GainPlayerBlock(CombatInformationState state, int baseBlock, CombatMutationJournal journal)
    {
        int amount = CalculatePlayerBlockAmount(state, baseBlock);
        journal.AddScalar(state, CombatScalarField.PlayerBlock, amount);
        return amount;
    }

    public int CalculatePlayerBlockAmount(CombatInformationState state, int baseBlock)
    {
        int amount = Math.Max(0, baseBlock + state.Player.Dexterity);
        if (state.Player.Frail > 0)
        {
            amount = (int)Math.Floor(amount * 0.75d);
        }

        return amount;
    }

    public void GainMonsterBlock(CombatInformationState state, int monsterIndex, int amount, CombatMutationJournal journal) =>
        journal.AddScalar(state, CombatScalarField.MonsterBlock, Math.Max(0, amount), monsterIndex);

    public int HealPlayer(CombatInformationState state, int amount, CombatMutationJournal journal)
    {
        int healed = Math.Min(Math.Max(0, amount), state.Player.MaxHp - state.Player.Hp);
        journal.AddScalar(state, CombatScalarField.PlayerHp, healed);
        journal.AddLedger(state, CombatLedgerField.PlayerHpHealed, healed);
        return healed;
    }

    public int HealMonster(CombatInformationState state, int monsterIndex, int amount, CombatMutationJournal journal)
    {
        CombatMonsterState monster = state.Monsters[monsterIndex];
        int healed = Math.Min(Math.Max(0, amount), monster.MaxHp - monster.Hp);
        journal.AddScalar(state, CombatScalarField.MonsterHp, healed, monsterIndex);
        journal.AddLedger(state, CombatLedgerField.EnemyHpRestored, healed);
        return healed;
    }

    public int LosePlayerHp(CombatInformationState state, int amount, CombatMutationJournal journal)
    {
        int lost = Math.Min(Math.Max(0, amount), state.Player.Hp);
        journal.AddScalar(state, CombatScalarField.PlayerHp, -lost);
        journal.AddLedger(state, CombatLedgerField.PlayerHpLost, lost);
        return lost;
    }

    private static int ApplyAttackModifiers(int baseDamage, int strength, int weak, int vulnerable)
    {
        double damage = Math.Max(0, baseDamage + strength);
        if (weak > 0)
        {
            damage *= 0.75d;
        }

        if (vulnerable > 0)
        {
            damage *= 1.5d;
        }

        return Math.Max(0, (int)Math.Floor(damage + 1e-9));
    }
}
