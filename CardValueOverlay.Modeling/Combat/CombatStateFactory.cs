namespace CardValueOverlay.Modeling.Combat;

public sealed record CombatMonsterSeed(
    string StableId,
    string TypeName,
    int Hp,
    string InitialIntentStateId);

public static class CombatStateFactory
{
    public static CombatInformationState Create(
        int playerHp,
        int playerMaxHp,
        IEnumerable<int> deckDefinitionIds,
        IEnumerable<CombatMonsterSeed> monsters,
        CombatSimulationOptions options,
        IEnumerable<int>? initialHand = null,
        IEnumerable<int>? knownTop = null)
    {
        options.Validate();
        CombatMonsterState[] monsterStates = monsters.Select(seed => new CombatMonsterState
        {
            StableId = seed.StableId,
            TypeName = seed.TypeName,
            Hp = seed.Hp,
            MaxHp = seed.Hp,
            IntentStateId = seed.InitialIntentStateId
        }).ToArray();
        if (monsterStates.Length == 0)
        {
            throw new InvalidOperationException("A combat state requires at least one monster.");
        }

        CombatInformationState state = new()
        {
            Player = new CombatPlayerState { Hp = playerHp, MaxHp = playerMaxHp },
            Monsters = monsterStates,
            Energy = options.EnergyPerTurn,
            MaxEnergy = options.EnergyPerTurn,
            Stars = options.InitialStars,
            InitialEnemyHpTotal = monsterStates.Sum(monster => monster.MaxHp)
        };

        foreach (int card in deckDefinitionIds)
        {
            int instanceId = state.NextCardInstanceId++;
            state.MutableCardInstances.Add(instanceId, new CombatCardInstanceState
            {
                InstanceId = instanceId,
                DefinitionId = card
            });
            state.MutableUnknownDraw.Add(instanceId);
        }

        if (knownTop is not null)
        {
            foreach (int card in knownTop)
            {
                state.MutableKnownTop.Add(RemoveUnknown(state, card));
            }
        }

        if (initialHand is not null)
        {
            foreach (int card in initialHand)
            {
                state.MutableHand.Add(RemoveUnknown(state, card));
            }
        }

        state.ValidateIntegrity();
        return state;
    }

    private static int RemoveUnknown(CombatInformationState state, int definitionId)
    {
        int position = state.MutableUnknownDraw.FindIndex(
            instanceId => state.GetDefinitionId(instanceId) == definitionId);
        if (position < 0)
        {
            throw new InvalidOperationException($"Card definition {definitionId} is not available in the unknown draw multiset.");
        }

        int instanceId = state.MutableUnknownDraw[position];
        state.MutableUnknownDraw.RemoveAt(position);
        return instanceId;
    }
}
