namespace CardValueOverlay.Modeling.Combat;

public enum CombatCardInsertionPosition
{
    Top,
    Bottom
}

public sealed record CombatCardSelectionSpec(
    CombatPile SourcePile,
    CombatPile DestinationPile,
    CombatCardInsertionPosition DestinationPosition,
    int Count);

public sealed record CombatPendingCardSelectionState(
    CombatCardSelectionSpec Spec,
    int PlayedInstanceId,
    CombatPile PlayedCardFinalPile,
    int RemainingCount);
