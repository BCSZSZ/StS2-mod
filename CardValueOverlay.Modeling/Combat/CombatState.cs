using System.Text;

namespace CardValueOverlay.Modeling.Combat;

public sealed class CombatPlayerState
{
    public int Hp { get; internal set; }
    public int MaxHp { get; internal set; }
    public int Block { get; internal set; }
    public int Strength { get; internal set; }
    public int Dexterity { get; internal set; }
    public int Weak { get; internal set; }
    public int Vulnerable { get; internal set; }
    public int Frail { get; internal set; }
    public bool IsAlive => Hp > 0;
}

public sealed class CombatMonsterState
{
    public required string StableId { get; init; }
    public required string TypeName { get; init; }
    public int Hp { get; internal set; }
    public int MaxHp { get; init; }
    public int Block { get; internal set; }
    public int Strength { get; internal set; }
    public int Weak { get; internal set; }
    public int Vulnerable { get; internal set; }
    public int Frail { get; internal set; }
    public required string IntentStateId { get; set; }
    public bool IsAlive => Hp > 0;
}

public sealed class CombatOrbitPowerState
{
    public int Amount { get; init; }
    public int EnergyProgress { get; internal set; }
}

public sealed class CombatRewardLedger
{
    public double ActualEnemyHpDamage { get; internal set; }
    public double EnemyHpRestored { get; internal set; }
    public double AttemptedDamage { get; internal set; }
    public double EnemyBlockBroken { get; internal set; }
    public double OverkillDamage { get; internal set; }
    public double PlayerHpLost { get; internal set; }
    public double PlayerHpHealed { get; internal set; }
    public double UnusedPlayerBlock { get; internal set; }

    public double NetOffenseValue => ActualEnemyHpDamage - EnemyHpRestored;
}

public enum CombatPile
{
    Hand,
    KnownTop,
    UnknownDraw,
    Discard,
    Exhaust,
    Play
}

public sealed class CombatInformationState
{
    internal List<int> MutableHand { get; } = [];
    internal List<int> MutableKnownTop { get; } = [];
    internal List<int> MutableUnknownDraw { get; } = [];
    internal List<int> MutableDiscard { get; } = [];
    internal List<int> MutableExhaust { get; } = [];
    internal List<int> MutablePlay { get; } = [];
    internal Dictionary<int, CombatCardInstanceState> MutableCardInstances { get; } = [];
    internal List<CombatOrbitPowerState> MutableOrbitPowers { get; } = [];

    public required CombatPlayerState Player { get; init; }
    public required IReadOnlyList<CombatMonsterState> Monsters { get; init; }
    public CombatRewardLedger Ledger { get; } = new();
    public int Turn { get; internal set; } = 1;
    public int Energy { get; internal set; }
    public int MaxEnergy { get; init; } = 3;
    public int EnergyNextTurn { get; internal set; }
    public int Stars { get; internal set; }
    public int StarsNextTurn { get; internal set; }
    public int BlockNextTurn { get; internal set; }
    public int DrawNextTurn { get; internal set; }
    public int RetainHandTurns { get; internal set; }
    public int ChildOfTheStarsBlockPerStar { get; internal set; }
    public int PaleBlueDotDraw { get; internal set; }
    public int FastenDefendBlock { get; internal set; }
    public int CardsPlayedThisTurn { get; internal set; }
    public int CardsPlayedPreviousTurn { get; internal set; }
    public int AttacksPlayedThisTurn { get; internal set; }
    public int SkillsPlayedThisTurn { get; internal set; }
    public int InitialEnemyHpTotal { get; init; }
    public int NextCardInstanceId { get; internal set; }
    public CombatPendingCardSelectionState? PendingCardSelection { get; internal set; }

    public IReadOnlyList<int> Hand => MutableHand;
    public IReadOnlyList<int> KnownTop => MutableKnownTop;
    public IReadOnlyList<int> UnknownDraw => MutableUnknownDraw;
    public IReadOnlyList<int> Discard => MutableDiscard;
    public IReadOnlyList<int> Exhaust => MutableExhaust;
    public IReadOnlyList<int> Play => MutablePlay;
    public IReadOnlyDictionary<int, CombatCardInstanceState> CardInstances => MutableCardInstances;
    public IReadOnlyList<CombatOrbitPowerState> OrbitPowers => MutableOrbitPowers;

    public int UnknownDrawCount => MutableUnknownDraw.Count;
    public bool AllMonstersDead => Monsters.All(monster => !monster.IsAlive);
    public bool IsTerminal => !Player.IsAlive || AllMonstersDead;

    public void ValidateIntegrity()
    {
        if (Player.Hp < 0 || Player.Hp > Player.MaxHp || Player.Block < 0 ||
            Energy < 0 || EnergyNextTurn < 0 || Stars < 0 || StarsNextTurn < 0 ||
            BlockNextTurn < 0 || DrawNextTurn < 0 || RetainHandTurns < 0 ||
            ChildOfTheStarsBlockPerStar < 0 || PaleBlueDotDraw < 0 || FastenDefendBlock < 0 ||
            CardsPlayedPreviousTurn < 0)
        {
            throw new InvalidOperationException("Player combat state is outside its valid range.");
        }

        if (Monsters.Any(monster => monster.Hp < 0 || monster.Hp > monster.MaxHp || monster.Block < 0))
        {
            throw new InvalidOperationException("Monster combat state is outside its valid range.");
        }

        if (MutableOrbitPowers.Any(power => power.Amount <= 0 || power.EnergyProgress is < 0 or >= 4))
        {
            throw new InvalidOperationException("Orbit power state is outside its valid range.");
        }

        HashSet<int> zonedInstances = [];
        foreach (List<int> pile in EnumerateMutablePiles())
        {
            foreach (int instanceId in pile)
            {
                if (!MutableCardInstances.ContainsKey(instanceId))
                {
                    throw new InvalidOperationException($"Card instance {instanceId} is in a pile but not in the registry.");
                }
                if (!zonedInstances.Add(instanceId))
                {
                    throw new InvalidOperationException($"Card instance {instanceId} belongs to more than one combat zone.");
                }
            }
        }

        if (zonedInstances.Count != MutableCardInstances.Count ||
            MutableCardInstances.Any(pair => pair.Key != pair.Value.InstanceId || !zonedInstances.Contains(pair.Key)))
        {
            throw new InvalidOperationException("Every registered card instance must belong to exactly one combat zone.");
        }
        if (MutableCardInstances.Values.Any(card => card.DefinitionId < 0 || card.ForgeDamageBonus < 0) ||
            (MutableCardInstances.Count > 0 && NextCardInstanceId <= MutableCardInstances.Keys.Max()))
        {
            throw new InvalidOperationException("Card instance state is outside its valid range.");
        }
        if (PendingCardSelection is not null)
        {
            if (PendingCardSelection.RemainingCount <= 0 ||
                PendingCardSelection.RemainingCount > PendingCardSelection.Spec.Count ||
                !MutablePlay.Contains(PendingCardSelection.PlayedInstanceId))
            {
                throw new InvalidOperationException("Pending card selection state is invalid.");
            }
        }
        else if (MutablePlay.Count != 0)
        {
            throw new InvalidOperationException("The play zone may remain occupied only while a card choice is pending.");
        }
    }

    public string Encode()
    {
        StringBuilder builder = new(256);
        builder.Append(Turn).Append('|')
            .Append(Energy).Append(',').Append(EnergyNextTurn).Append(',')
            .Append(Stars).Append(',').Append(StarsNextTurn).Append(',')
            .Append(BlockNextTurn).Append(',').Append(DrawNextTurn).Append(',').Append(RetainHandTurns).Append('|')
            .Append(ChildOfTheStarsBlockPerStar).Append(',').Append(PaleBlueDotDraw).Append(',').Append(FastenDefendBlock).Append('|')
            .Append(CardsPlayedThisTurn).Append(',').Append(CardsPlayedPreviousTurn).Append(',')
            .Append(AttacksPlayedThisTurn).Append(',').Append(SkillsPlayedThisTurn)
            .Append("|P:").Append(Player.Hp).Append(',').Append(Player.Block).Append(',')
            .Append(Player.Strength).Append(',').Append(Player.Dexterity).Append(',')
            .Append(Player.Weak).Append(',').Append(Player.Vulnerable).Append(',').Append(Player.Frail);
        foreach (CombatMonsterState monster in Monsters)
        {
            builder.Append("|M:").Append(monster.TypeName).Append(',').Append(monster.Hp).Append(',')
                .Append(monster.Block).Append(',').Append(monster.Strength).Append(',')
                .Append(monster.Weak).Append(',').Append(monster.Vulnerable).Append(',')
                .Append(monster.Frail).Append(',').Append(monster.IntentStateId);
        }

        AppendSortedInstances(builder, "|H:", MutableHand);
        AppendOrderedInstances(builder, "|K:", MutableKnownTop);
        AppendSortedInstances(builder, "|U:", MutableUnknownDraw);
        AppendSortedInstances(builder, "|D:", MutableDiscard);
        AppendSortedInstances(builder, "|X:", MutableExhaust);
        AppendOrderedInstances(builder, "|Y:", MutablePlay);
        builder.Append("|O:");
        foreach (CombatOrbitPowerState power in MutableOrbitPowers
            .OrderBy(power => power.Amount)
            .ThenBy(power => power.EnergyProgress))
        {
            builder.Append(power.Amount).Append('x').Append(power.EnergyProgress).Append(',');
        }
        builder.Append("|N:").Append(NextCardInstanceId);
        if (PendingCardSelection is not null)
        {
            builder.Append("|Q:")
                .Append((int)PendingCardSelection.Spec.SourcePile).Append(',')
                .Append((int)PendingCardSelection.Spec.DestinationPile).Append(',')
                .Append((int)PendingCardSelection.Spec.DestinationPosition).Append(',')
                .Append(PendingCardSelection.Spec.Count).Append(',')
                .Append(PendingCardSelection.PlayedInstanceId).Append(',')
                .Append((int)PendingCardSelection.PlayedCardFinalPile).Append(',')
                .Append(PendingCardSelection.RemainingCount);
        }

        return builder.ToString();
    }

    public CombatStateKey GetCanonicalKey()
    {
        CombatStateKeyBuilder builder = new();
        builder.Add(Turn);
        builder.Add(Energy);
        builder.Add(EnergyNextTurn);
        builder.Add(Stars);
        builder.Add(StarsNextTurn);
        builder.Add(BlockNextTurn);
        builder.Add(DrawNextTurn);
        builder.Add(RetainHandTurns);
        builder.Add(ChildOfTheStarsBlockPerStar);
        builder.Add(PaleBlueDotDraw);
        builder.Add(FastenDefendBlock);
        builder.Add(CardsPlayedThisTurn);
        builder.Add(CardsPlayedPreviousTurn);
        builder.Add(AttacksPlayedThisTurn);
        builder.Add(SkillsPlayedThisTurn);
        builder.Add(Player.Hp);
        builder.Add(Player.Block);
        builder.Add(Player.Strength);
        builder.Add(Player.Dexterity);
        builder.Add(Player.Weak);
        builder.Add(Player.Vulnerable);
        builder.Add(Player.Frail);
        foreach (CombatMonsterState monster in Monsters)
        {
            builder.Add(monster.TypeName);
            builder.Add(monster.Hp);
            builder.Add(monster.Block);
            builder.Add(monster.Strength);
            builder.Add(monster.Weak);
            builder.Add(monster.Vulnerable);
            builder.Add(monster.Frail);
            builder.Add(monster.IntentStateId);
        }

        builder.AddUnorderedCardInstances(MutableHand, MutableCardInstances);
        builder.AddOrderedCardInstances(MutableKnownTop, MutableCardInstances);
        builder.AddUnorderedCardInstances(MutableUnknownDraw, MutableCardInstances);
        builder.AddUnorderedCardInstances(MutableDiscard, MutableCardInstances);
        builder.AddUnorderedCardInstances(MutableExhaust, MutableCardInstances);
        builder.AddOrderedCardInstances(MutablePlay, MutableCardInstances);
        builder.AddUnorderedOrbitPowers(MutableOrbitPowers);
        if (PendingCardSelection is null)
        {
            builder.Add(0);
        }
        else
        {
            builder.Add(1);
            builder.Add((int)PendingCardSelection.Spec.SourcePile);
            builder.Add((int)PendingCardSelection.Spec.DestinationPile);
            builder.Add((int)PendingCardSelection.Spec.DestinationPosition);
            builder.Add(PendingCardSelection.Spec.Count);
            CombatCardInstanceState played = GetCardInstance(PendingCardSelection.PlayedInstanceId);
            builder.Add(played.DefinitionId);
            builder.Add(played.ForgeDamageBonus);
            builder.Add((int)PendingCardSelection.PlayedCardFinalPile);
            builder.Add(PendingCardSelection.RemainingCount);
        }
        return builder.Build();
    }

    public CombatStateKey GetTerminalKey()
    {
        CombatStateKeyBuilder builder = new();
        builder.Add(Player.Hp);
        builder.Add(Player.MaxHp);
        builder.Add(InitialEnemyHpTotal);
        foreach (CombatMonsterState monster in Monsters)
        {
            builder.Add(monster.TypeName);
            builder.Add(monster.IsAlive ? 1 : 0);
            builder.Add(monster.IntentStateId);
        }
        return builder.Build();
    }

    internal List<int> GetPile(CombatPile pile) => pile switch
    {
        CombatPile.Hand => MutableHand,
        CombatPile.KnownTop => MutableKnownTop,
        CombatPile.UnknownDraw => MutableUnknownDraw,
        CombatPile.Discard => MutableDiscard,
        CombatPile.Exhaust => MutableExhaust,
        CombatPile.Play => MutablePlay,
        _ => throw new ArgumentOutOfRangeException(nameof(pile))
    };

    public CombatCardInstanceState GetCardInstance(int instanceId) =>
        MutableCardInstances.TryGetValue(instanceId, out CombatCardInstanceState? card)
            ? card
            : throw new KeyNotFoundException($"Combat card instance {instanceId} was not found.");

    public int GetDefinitionId(int instanceId) => GetCardInstance(instanceId).DefinitionId;

    public CombatCardInstanceKey GetPhysicalKey(int instanceId) => GetCardInstance(instanceId).PhysicalKey;

    public int CountDefinition(IEnumerable<int> instanceIds, int definitionId) =>
        instanceIds.Count(instanceId => GetDefinitionId(instanceId) == definitionId);

    internal IEnumerable<List<int>> EnumerateMutablePiles()
    {
        yield return MutableHand;
        yield return MutableKnownTop;
        yield return MutableUnknownDraw;
        yield return MutableDiscard;
        yield return MutableExhaust;
        yield return MutablePlay;
    }

    private void AppendSortedInstances(StringBuilder builder, string prefix, List<int> values)
    {
        builder.Append(prefix);
        foreach (int instanceId in values
            .OrderBy(GetPhysicalKey)
            .ThenBy(value => value))
        {
            AppendInstance(builder, instanceId);
        }
    }

    private void AppendOrderedInstances(StringBuilder builder, string prefix, List<int> values)
    {
        builder.Append(prefix);
        foreach (int instanceId in values)
        {
            AppendInstance(builder, instanceId);
        }
    }

    private void AppendInstance(StringBuilder builder, int instanceId)
    {
        CombatCardInstanceState card = GetCardInstance(instanceId);
        builder.Append(instanceId).Append('=').Append(card.DefinitionId)
            .Append('+').Append(card.ForgeDamageBonus).Append(',');
    }
}

public readonly record struct CombatStateKey(ulong First, ulong Second);

internal struct CombatStateKeyBuilder
{
    private ulong _first;
    private ulong _second;

    public CombatStateKeyBuilder()
    {
        _first = 0x243F6A8885A308D3UL;
        _second = 0x13198A2E03707344UL;
    }

    public void Add(int value) => Add(unchecked((ulong)(uint)value));

    public void Add(string value)
    {
        ulong hash = 1469598103934665603UL;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }
        Add(hash);
    }

    public void AddOrdered(IReadOnlyList<int> values)
    {
        Add(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            Add(index);
            Add(values[index]);
        }
    }

    public void AddUnordered(IReadOnlyList<int> values)
    {
        ulong sumA = 0, sumB = 0;
        foreach (int value in values)
        {
            ulong mixed = Mix(unchecked((ulong)(uint)value) + 0x9E3779B97F4A7C15UL);
            sumA += mixed;
            sumB += RotateLeft(mixed, value & 63) ^ Mix(mixed + 0xD1B54A32D192ED03UL);
        }
        Add(values.Count);
        Add(sumA);
        Add(sumB);
    }

    public void AddOrderedCardInstances(
        IReadOnlyList<int> instanceIds,
        IReadOnlyDictionary<int, CombatCardInstanceState> instances)
    {
        Add(instanceIds.Count);
        for (int index = 0; index < instanceIds.Count; index++)
        {
            CombatCardInstanceState card = instances[instanceIds[index]];
            Add(index);
            Add(card.DefinitionId);
            Add(card.ForgeDamageBonus);
        }
    }

    public void AddUnorderedCardInstances(
        IReadOnlyList<int> instanceIds,
        IReadOnlyDictionary<int, CombatCardInstanceState> instances)
    {
        ulong sumA = 0, sumB = 0;
        foreach (int instanceId in instanceIds)
        {
            CombatCardInstanceState card = instances[instanceId];
            ulong identity = (unchecked((ulong)(uint)card.DefinitionId) << 32) |
                unchecked((uint)card.ForgeDamageBonus);
            ulong mixed = Mix(identity + 0x9E3779B97F4A7C15UL);
            sumA += mixed;
            sumB += RotateLeft(mixed, card.ForgeDamageBonus & 63) ^ Mix(mixed + 0xD1B54A32D192ED03UL);
        }
        Add(instanceIds.Count);
        Add(sumA);
        Add(sumB);
    }

    public void AddOrderedCardInstancesWithIdentity(
        IReadOnlyList<int> instanceIds,
        IReadOnlyDictionary<int, CombatCardInstanceState> instances)
    {
        Add(instanceIds.Count);
        for (int index = 0; index < instanceIds.Count; index++)
        {
            CombatCardInstanceState card = instances[instanceIds[index]];
            Add(index);
            Add(instanceIds[index]);
            Add(card.DefinitionId);
            Add(card.ForgeDamageBonus);
        }
    }

    public void AddUnorderedCardInstancesWithIdentity(
        IReadOnlyList<int> instanceIds,
        IReadOnlyDictionary<int, CombatCardInstanceState> instances)
    {
        ulong sumA = 0, sumB = 0;
        foreach (int instanceId in instanceIds)
        {
            CombatCardInstanceState card = instances[instanceId];
            ulong identity = Mix(unchecked((ulong)(uint)instanceId)) ^
                RotateLeft(Mix(unchecked((ulong)(uint)card.DefinitionId)), 21) ^
                RotateLeft(Mix(unchecked((ulong)(uint)card.ForgeDamageBonus)), 42);
            ulong mixed = Mix(identity + 0x9E3779B97F4A7C15UL);
            sumA += mixed;
            sumB += RotateLeft(mixed, instanceId & 63) ^ Mix(mixed + 0xD1B54A32D192ED03UL);
        }
        Add(instanceIds.Count);
        Add(sumA);
        Add(sumB);
    }

    public void AddUnorderedOrbitPowers(IReadOnlyList<CombatOrbitPowerState> values)
    {
        ulong sumA = 0, sumB = 0;
        foreach (CombatOrbitPowerState power in values)
        {
            ulong identity = (unchecked((ulong)(uint)power.Amount) << 32) |
                unchecked((uint)power.EnergyProgress);
            ulong mixed = Mix(identity + 0x9E3779B97F4A7C15UL);
            sumA += mixed;
            sumB += RotateLeft(mixed, power.EnergyProgress & 63) ^ Mix(mixed + 0xD1B54A32D192ED03UL);
        }
        Add(values.Count);
        Add(sumA);
        Add(sumB);
    }

    public CombatStateKey Build() => new(_first, _second);

    private void Add(ulong value)
    {
        _first = Mix(_first ^ value);
        _second = Mix(_second + value + 0x9E3779B97F4A7C15UL);
    }

    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> ((64 - count) & 63));

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
