namespace CardValueOverlay.Modeling.Combat;

public readonly record struct CombatCardInstanceKey(
    int DefinitionId,
    int ForgeDamageBonus = 0) : IComparable<CombatCardInstanceKey>
{
    public int CompareTo(CombatCardInstanceKey other)
    {
        int definition = DefinitionId.CompareTo(other.DefinitionId);
        return definition != 0 ? definition : ForgeDamageBonus.CompareTo(other.ForgeDamageBonus);
    }

    public override string ToString() => $"{DefinitionId}+forge:{ForgeDamageBonus}";
}

public sealed class CombatCardInstanceState
{
    public required int InstanceId { get; init; }
    public int DefinitionId { get; internal set; }
    public int ForgeDamageBonus { get; internal set; }

    public CombatCardInstanceKey PhysicalKey => new(DefinitionId, ForgeDamageBonus);
}
