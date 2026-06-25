namespace CardValueOverlay.Modeling.Extraction;

public sealed record MonsterMoveProfileEntry(
    string ModelId,
    string TypeName,
    string FullTypeName,
    MonsterHpRange? HpRange,
    IReadOnlyList<MonsterMoveStateEntry> Moves,
    string? InitialStateId,
    IReadOnlyList<string> Unresolved,
    string Provenance,
    double Confidence);
