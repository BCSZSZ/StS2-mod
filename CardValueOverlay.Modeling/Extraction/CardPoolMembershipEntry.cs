namespace CardValueOverlay.Modeling.Extraction;

public sealed record CardPoolMembershipEntry(
    string ModelId,
    string TypeName,
    string FullTypeName,
    IReadOnlyList<string> Pools,
    string MultiplayerConstraint,
    bool IsMultiplayerOnly,
    bool IsSingleplayerOnly,
    IReadOnlyList<string> Warnings,
    string Provenance,
    double Confidence);
