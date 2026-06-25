namespace CardValueOverlay.Modeling.Extraction;

public sealed record MonsterHpRange(
    MonsterMoveNumeric? Min,
    MonsterMoveNumeric? Max);
