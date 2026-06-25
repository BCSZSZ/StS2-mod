namespace CardValueOverlay.Modeling.Extraction;

public sealed record MonsterMoveStateEntry(
    string StateId,
    string? MoveMethod,
    IReadOnlyList<string> Intents,
    IReadOnlyList<MonsterMoveEffectTerm> Effects,
    IReadOnlyList<string> FollowUpStateIds,
    IReadOnlyList<string> Warnings,
    double Confidence);
