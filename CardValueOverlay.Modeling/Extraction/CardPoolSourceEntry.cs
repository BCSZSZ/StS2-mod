namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardPoolSourceEntry(
    string PoolName,
    string PoolTypeName,
    IReadOnlyList<string> CardTypeNames);
