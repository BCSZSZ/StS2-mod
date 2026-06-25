namespace CardValueOverlay.Modeling.Extraction;

public sealed record EncounterMonsterSlot(
    int Position,
    string? SlotName,
    string? MonsterTypeName,
    IReadOnlyList<string> PossibleMonsterTypeNames,
    string Source,
    double Confidence);
