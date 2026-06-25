namespace CardValueOverlay.Modeling.Extraction;

public sealed record MonsterMoveNumeric(
    string Expression,
    decimal? Value,
    decimal? AscensionValue,
    string? AscensionLevel,
    double Confidence);
