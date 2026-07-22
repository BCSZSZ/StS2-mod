namespace CardValueOverlay.Modeling.Combat;

public enum CombatActionKind
{
    PlayCard,
    ChooseCard,
    ResolveEmptyCardChoice,
    EndTurn
}

public readonly record struct CombatAction(
    CombatActionKind Kind,
    int CardDefinitionId = -1,
    int TargetMonsterIndex = -1,
    int CardForgeDamageBonus = 0)
{
    public static CombatAction EndTurn { get; } = new(CombatActionKind.EndTurn);

    public string StableKey => Kind switch
    {
        CombatActionKind.EndTurn => "end-turn",
        CombatActionKind.ResolveEmptyCardChoice => "choose:none",
        CombatActionKind.ChooseCard =>
            $"choose:{CardDefinitionId:D6}:forge:{CardForgeDamageBonus:D6}",
        _ => $"play:{CardDefinitionId:D6}:forge:{CardForgeDamageBonus:D6}:target:{TargetMonsterIndex:D3}"
    };

    public CombatCardInstanceKey CardPhysicalKey => new(CardDefinitionId, CardForgeDamageBonus);

    public static int CompareStable(CombatAction left, CombatAction right)
    {
        if (left.Kind != right.Kind)
        {
            return ActionRank(left.Kind).CompareTo(ActionRank(right.Kind));
        }
        int card = left.CardDefinitionId.CompareTo(right.CardDefinitionId);
        if (card != 0) return card;
        int forge = left.CardForgeDamageBonus.CompareTo(right.CardForgeDamageBonus);
        return forge != 0 ? forge : left.TargetMonsterIndex.CompareTo(right.TargetMonsterIndex);
    }

    private static int ActionRank(CombatActionKind kind) => kind switch
    {
        CombatActionKind.EndTurn => 0,
        CombatActionKind.ResolveEmptyCardChoice => 1,
        CombatActionKind.ChooseCard => 2,
        CombatActionKind.PlayCard => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public enum CombatSolveStatus
{
    Exact,
    SparseEstimate,
    ExactBudgetExceeded,
    Unsupported
}

public sealed record CombatSolveResult(
    double Value,
    CombatAction BestAction,
    CombatSolveStatus Status,
    long CanonicalStates,
    long MemoHits,
    long DecisionNodes,
    long ChanceNodes,
    long OutcomeBranches,
    int MaximumDepth,
    string? Message = null,
    IReadOnlyDictionary<CombatStateKey, CombatAction>? Policy = null);
