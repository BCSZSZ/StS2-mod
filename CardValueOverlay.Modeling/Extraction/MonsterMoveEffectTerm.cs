namespace CardValueOverlay.Modeling.Extraction;

public sealed record MonsterMoveEffectTerm(
    string Kind,
    MonsterMoveNumeric? Amount,
    MonsterMoveNumeric? HitCount,
    string? Target,
    string? Parameter,
    string Source,
    double Confidence);
