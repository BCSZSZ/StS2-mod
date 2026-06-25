namespace CardValueOverlay.Modeling.Extraction;

public sealed record EncounterActReference(
    string ActTypeName,
    int ActIndex,
    int ActNumber,
    bool IsDefault,
    int NumberOfWeakEncounters,
    int BaseNumberOfRooms);
