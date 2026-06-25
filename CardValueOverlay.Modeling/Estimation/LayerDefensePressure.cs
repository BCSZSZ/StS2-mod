namespace CardValueOverlay.Modeling.Estimation;

public sealed record LayerDefensePressure(
    int Layer,
    string PressureSource,
    decimal AscensionMix,
    decimal EffectiveDamagePerMove,
    decimal CurrentBlockToDamage,
    decimal DamageUnitValue,
    decimal CandidateValuePerBlock,
    decimal RequiredBlockPerMoveAtCurrentConversion);
