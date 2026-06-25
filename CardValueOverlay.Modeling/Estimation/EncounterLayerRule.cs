namespace CardValueOverlay.Modeling.Estimation;

public sealed record EncounterLayerRule(
    int ActNumber,
    IReadOnlyList<string> ActTypeNames,
    int StartLayer,
    int BaseNumberOfRooms,
    int TotalFloors,
    int WeakEndLayer,
    int BossStartLayer,
    int BossEndLayer,
    int? AncientLayer,
    int EndLayer,
    int NumberOfWeakEncounters,
    int WeakLayerCount);
