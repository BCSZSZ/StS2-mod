namespace CardValueOverlay.Modeling.Combat;

public sealed record ReferenceTailOptions(
    int TailTurns = 2,
    int ExpectedBlockPerTurn = 5);

public sealed class ReferenceCombatPolicy
{
    private readonly IReadOnlyDictionary<string, CombatMonsterDefinition> _monsters;
    private readonly ReferenceTailOptions _options;

    public ReferenceCombatPolicy(
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        ReferenceTailOptions? options = null)
    {
        _monsters = monsters;
        _options = options ?? new ReferenceTailOptions();
    }

    public double EstimateFutureHpLoss(CombatInformationState state)
    {
        if (state.IsTerminal || _options.TailTurns <= 0)
        {
            return 0d;
        }

        Dictionary<(string TypeName, string StateId), double> stateWeights = [];
        foreach (CombatMonsterState monster in state.Monsters.Where(monster => monster.IsAlive))
        {
            stateWeights[(monster.TypeName, monster.IntentStateId)] =
                stateWeights.GetValueOrDefault((monster.TypeName, monster.IntentStateId)) + 1d;
        }

        double totalIncoming = 0d;
        for (int turn = 0; turn < _options.TailTurns && stateWeights.Count > 0; turn++)
        {
            Dictionary<(string TypeName, string StateId), double> next = [];
            double turnDamage = 0d;
            foreach (((string typeName, string stateId), double weight) in stateWeights)
            {
                if (!_monsters.TryGetValue(typeName, out CombatMonsterDefinition? monster) ||
                    !monster.Intents.TryGetValue(stateId, out MonsterIntentDefinition? intent))
                {
                    continue;
                }

                turnDamage += weight * intent.Effects
                    .Where(effect => effect.Kind == MonsterIntentEffectKind.Attack)
                    .Sum(effect => effect.Amount * effect.HitCount);
                foreach (MonsterIntentTransition transition in intent.Transitions)
                {
                    (string, string) key = (typeName, transition.StateId);
                    next[key] = next.GetValueOrDefault(key) + weight * transition.Probability;
                }
            }

            totalIncoming += Math.Max(0d, turnDamage - _options.ExpectedBlockPerTurn);
            stateWeights = next;
        }

        return Math.Min(state.Player.Hp, totalIncoming);
    }
}
