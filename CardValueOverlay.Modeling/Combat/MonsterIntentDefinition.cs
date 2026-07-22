namespace CardValueOverlay.Modeling.Combat;

public enum MonsterIntentEffectKind
{
    Attack,
    Block,
    ApplyWeak,
    ApplyVulnerable,
    ApplyFrail,
    GainStrength,
    HealSelf
}

public sealed record MonsterIntentEffect(
    MonsterIntentEffectKind Kind,
    int Amount,
    int HitCount,
    string Target,
    string Source,
    int SourceOrder);

public sealed record MonsterIntentTransition(string StateId, double Probability, string Source);

public sealed record MonsterIntentDefinition(
    string StateId,
    IReadOnlyList<MonsterIntentEffect> Effects,
    IReadOnlyList<MonsterIntentTransition> Transitions);

public sealed record CombatMonsterDefinition(
    string ModelId,
    string TypeName,
    int MinHpA10,
    int MaxHpA10,
    string InitialStateId,
    IReadOnlyDictionary<string, MonsterIntentDefinition> Intents,
    bool IsSupported,
    IReadOnlyList<string> UnsupportedReasons,
    string SourceHash);

public sealed record EncounterMonsterDefinition(
    int Position,
    string TypeName,
    CombatMonsterDefinition Monster);

public sealed record EncounterCombatDefinition(
    string ModelId,
    string TypeName,
    int Act,
    string Tier,
    IReadOnlyList<EncounterMonsterDefinition> Monsters,
    bool IsSupported,
    IReadOnlyList<string> UnsupportedReasons,
    string RealizationId = "default",
    double RealizationProbability = 1d)
{
    public string StableId => RealizationId == "default"
        ? $"{ModelId}:act{Act}"
        : $"{ModelId}:act{Act}:{RealizationId}";
}
