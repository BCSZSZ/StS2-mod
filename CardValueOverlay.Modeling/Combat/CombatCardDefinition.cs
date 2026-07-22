namespace CardValueOverlay.Modeling.Combat;

public enum CombatCardTarget
{
    Self,
    Enemy,
    AllEnemies
}

public enum CombatCardEffectKind
{
    Damage,
    Block,
    Draw,
    DrawNextTurn,
    GainEnergy,
    GainEnergyNextTurn,
    GainStars,
    GainStarsNextTurn,
    GainBlockNextTurn,
    GainRetainHand,
    Forge,
    SelectAndMoveCard,
    InstallChildOfTheStars,
    InstallOrbit,
    InstallPaleBlueDot,
    InstallFasten,
    HealPlayer,
    LosePlayerHp,
    ApplyWeak,
    ApplyVulnerable,
    ApplyFrail,
    GainStrength,
    GainDexterity
}

public sealed record CombatCardEffect(
    CombatCardEffectKind Kind,
    int Amount,
    int HitCount,
    CombatCardTarget Target,
    string Source,
    int SourceOrder,
    string? DynamicVarName = null,
    CombatCardSelectionSpec? CardSelection = null);

public sealed record CombatCardDefinition(
    int DefinitionId,
    string ModelId,
    string TypeName,
    int UpgradeLevel,
    string CardType,
    int EnergyCost,
    CombatCardTarget Target,
    IReadOnlyList<CombatCardEffect> Effects,
    bool Exhausts,
    bool IsPlayable,
    bool IsSupported,
    IReadOnlyList<string> UnsupportedReasons,
    int StarCost = 0,
    bool Retains = false,
    bool Ethereal = false,
    bool Innate = false,
    bool DefendTagged = false)
{
    public string StableKey => $"{ModelId}+{UpgradeLevel}";
}

public sealed class CombatCardCatalog
{
    private readonly Dictionary<int, CombatCardDefinition> _byId;
    private readonly Dictionary<string, CombatCardDefinition> _byKey;

    public CombatCardCatalog(IEnumerable<CombatCardDefinition> cards)
    {
        CombatCardDefinition[] materialized = cards.OrderBy(card => card.DefinitionId).ToArray();
        _byId = materialized.ToDictionary(card => card.DefinitionId);
        _byKey = new Dictionary<string, CombatCardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (CombatCardDefinition card in materialized)
        {
            _byKey[card.StableKey] = card;
            _byKey.TryAdd(card.ModelId, card);
            _byKey.TryAdd(card.TypeName, card);
        }

        Cards = materialized;
    }

    public IReadOnlyList<CombatCardDefinition> Cards { get; }

    public CombatCardDefinition Get(int definitionId) =>
        _byId.TryGetValue(definitionId, out CombatCardDefinition? card)
            ? card
            : throw new KeyNotFoundException($"Combat card definition {definitionId} was not found.");

    public bool TryGet(string key, out CombatCardDefinition card) => _byKey.TryGetValue(key, out card!);
}
