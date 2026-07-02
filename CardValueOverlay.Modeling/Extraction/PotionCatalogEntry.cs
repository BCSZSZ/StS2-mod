namespace CardValueOverlay.Modeling.Extraction;

// A single canonical variable declared in a potion's CanonicalVars (e.g. DamageVar(20),
// PowerVar<StrengthPower>(2), CardsVar(3)). Amount is the BaseValue; PowerName is set for PowerVar<T>.
public sealed record PotionVar(string Kind, decimal Amount, string? PowerName = null);

// One potion in the extracted potion catalog. Mirrors CardFactCatalogEntry for cards, but for the
// potion type system: declarative axes (rarity/usage/target/CanBeGeneratedInCombat/pool) plus the
// numeric CanonicalVars and the effect tags parsed from OnUse. EffectTags are the value channels the
// potion's effect touches; NeedsNewChannel lists effect kinds the simulator has no channel for yet.
public sealed record PotionCatalogEntry(
    string ModelId,
    string TypeName,
    string FullTypeName,
    string LocKey,
    string Rarity,
    string Usage,
    string TargetType,
    bool CanBeGeneratedInCombat,
    string Pool,
    bool InRandomInCombatPool,
    IReadOnlyList<PotionVar> Vars,
    IReadOnlyList<string> EffectTags,
    IReadOnlyList<string> NeedsNewChannel,
    IReadOnlyList<string> Unresolved,
    string Provenance,
    double Confidence);
