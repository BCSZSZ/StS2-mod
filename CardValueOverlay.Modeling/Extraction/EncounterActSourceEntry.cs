namespace CardValueOverlay.Modeling.Extraction;

public sealed record EncounterActSourceEntry(
    string ActTypeName,
    int ActIndex,
    int ActNumber,
    bool IsDefault,
    int NumberOfWeakEncounters,
    int BaseNumberOfRooms,
    IReadOnlyList<string> EncounterTypeNames);
