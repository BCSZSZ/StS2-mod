namespace CardValueOverlay.Modeling.Extraction;

public sealed record EncounterPatternEntry(
    string ModelId,
    string TypeName,
    string FullTypeName,
    IReadOnlyList<EncounterActReference> Acts,
    string RoomType,
    bool IsWeak,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<EncounterMonsterSlot> MonsterSlots,
    IReadOnlyList<string> PossibleMonsterTypeNames,
    int? FixedMonsterCount,
    bool HasConditionalMonsterSelection,
    IReadOnlyList<string> Warnings,
    string Provenance,
    double Confidence);
