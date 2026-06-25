namespace CardValueOverlay.Modeling.Estimation;

public sealed record EncounterWeightedEnemyPressureReport(
    int TurnCount,
    int OpeningTurnCount,
    int SustainStartTurn,
    int SustainEndTurn,
    IReadOnlyList<EncounterLayerRule> LayerRules,
    IReadOnlyList<EncounterLayerPressureSegment> LayerSegments,
    IReadOnlyList<EncounterDamageProfile> Encounters,
    IReadOnlyList<string> Warnings,
    string Provenance);
