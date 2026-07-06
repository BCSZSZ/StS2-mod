using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record SimulationCard
{
    public required string ModelId { get; init; }

    public required string TypeName { get; init; }

    public required string FullTypeName { get; init; }

    public int? Cost { get; init; }

    public string? CardType { get; init; }

    public string? Rarity { get; init; }

    public string? TargetType { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Pools { get; init; } = [];

    public int UpgradeLevel { get; init; }

    public int Layer { get; init; }

    public double StaticEstimatedValue { get; init; }

    public double IntrinsicValue { get; init; }

    public double DamageValue { get; init; }

    public double BaseDamage { get; init; }

    public double DamageModifierMultiplier { get; init; }

    public double DamageUnitValue { get; init; } = 1d;

    public string? ScalingDamageKind { get; init; }

    public double ScalingDamageBase { get; init; }

    public double ScalingDamagePerUnit { get; init; }

    public double ScalingDamageTargetMultiplier { get; init; } = 1d;

    // Raw damage this attack permanently gains every time it is DRAWN (KinglyPunch AfterCardDrawn).
    // The growth accumulates per card instance during a combat; see DeckCardInstance.BonusDrawDamage.
    public double DamageIncreasePerDraw { get; init; }

    // Energy cost this card loses every time it is DRAWN (KinglyKick AfterCardDrawn: -1/draw,
    // this combat). Accumulates per instance; see DeckCardInstance.BonusDrawCostReduction.
    public int CostReductionPerDraw { get; init; }

    // Player energy lost every time this card is DRAWN (Void AfterCardDrawn: LoseEnergy(1)). Applied
    // immediately to player energy in the draw loop; not a per-instance accumulator.
    public int EnergyLossPerDraw { get; init; }

    public double BaseBlock { get; init; }

    public int BlockEffectCount { get; init; }

    public double BlockValuePerBlock { get; init; }

    public double AoeDamageMultiplier { get; init; } = 1.3d;

    // Unified per-card setup value: resolved from the CardSetupValueCatalog by the library builder.
    // BeamSetupValue steers beam entry (which cards get explored); PlaySetupValue steers the decision
    // line (which line wins). This is the sole setup prior - there is no legacy fallback.
    public double BeamSetupValue { get; init; }

    public double PlaySetupValue { get; init; }

    public int EnergyCost { get; init; }

    public int StarCost { get; init; }

    public bool HasExplicitStarCost { get; init; }

    public bool HasStarCostX { get; init; }

    public int Draw { get; init; }

    public bool DrawsToHandFull { get; init; }

    public int DrawNextTurn { get; init; }

    public int BlockNextTurn { get; init; }

    public int EnergyGain { get; init; }

    public int EnergyNextTurn { get; init; }

    public int StarGain { get; init; }

    public int StarNextTurn { get; init; }

    public int Forge { get; init; }

    public int ReplayGrant { get; init; }

    public int Vulnerable { get; init; }

    public bool Exhausts { get; init; }

    public bool EndsTurn { get; init; }

    public bool Unplayable { get; init; }

    public bool Ethereal { get; init; }

    public bool Retain { get; init; }

    public bool Innate { get; init; }

    public double Confidence { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<CardActionFact> Actions { get; init; } = [];

    public AutoPlayEffect? AutoPlay { get; init; }

    public bool IsPlayable => !Unplayable && EnergyCost >= 0;

    public bool IsPower => string.Equals(CardType, "Power", StringComparison.OrdinalIgnoreCase);

    public bool IsAttack => string.Equals(CardType, "Attack", StringComparison.OrdinalIgnoreCase);

    public bool HasTag(string tag)
    {
        return Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasSimulatedResourceEffect =>
        Draw > 0
        || DrawsToHandFull
        || DrawNextTurn > 0
        || BlockNextTurn > 0
        || EnergyGain > 0
        || EnergyNextTurn > 0
        || StarGain > 0
        || StarNextTurn > 0
        || StarCost > 0
        || Forge > 0
        || ReplayGrant > 0
        || Vulnerable > 0
        || ScalingDamageKind is not null
        || AutoPlay is not null
        || Actions.Any(action => action.Kind is
            "hpLoss"
            or "persistentPowerTrigger"
            or "xCostDamage"
            or "moveCardBetweenPiles"
            or "transformCard"
            or "createCard"
            or "createCardChoices"
            or "autoPlay");
}
