namespace CardValueOverlay.Modeling.Simulation;

public sealed record DynamicSetupDescriptor
{
    public required string Key { get; init; }

    public required string AppliesToBaseTypeName { get; init; }

    public required IReadOnlyList<string> Slots { get; init; }

    public required string Formula { get; init; }

    public required string RuntimeBasis { get; init; }

    public required string ReportingNote { get; init; }
}

public enum CardBehaviorKind
{
    AnointedRareDrawToHand,
    PuritySelectiveExhaust,
    PurityAlwaysExhaustible,
    PreferredTransformFodder,
    RetrieveSovereignBladesBeforeForge,
    DynamicForgeFromAttacksPlayed,
    UpgradedBombDamage,
    SovereignBlade,
    HeavenlyDrillMinimumEnergy,
    TurnEndFrail
}

public enum CardTransformCountMode
{
    ActionAmount,
    AllAvailable
}

public enum CardTransformSelectionMode
{
    LowestKeepScore,
    DisposableFodder
}

public sealed record CardTransformBehavior
{
    public string? GenericTargetOverride { get; init; }

    public CardTransformCountMode CountMode { get; init; } = CardTransformCountMode.ActionAmount;

    public CardTransformSelectionMode SelectionMode { get; init; } = CardTransformSelectionMode.LowestKeepScore;

    public bool RequireReplacementImprovement { get; init; }
}

public enum GeneratedCardBehavior
{
    CollisionCourse,
    CrashLanding,
    BundleOfJoy,
    ManifestAuthority,
    Quasar,
    JackOfAllTrades,
    Discovery,
    Jackpot,
    HeirloomHammer
}

public enum ScalingDamageBehaviorMode
{
    ParsedAction,
    ConditionalHitCount
}

public sealed record ScalingDamageBehavior(
    string Kind,
    ScalingDamageBehaviorMode Mode);

public sealed record CardBehaviorDefinition
{
    public required string BaseTypeName { get; init; }

    public IReadOnlyList<string> ModelIds { get; init; } = [];

    public IReadOnlyList<CardBehaviorKind> Behaviors { get; init; } = [];

    public CardTransformBehavior? Transform { get; init; }

    public GeneratedCardBehavior? GeneratedCards { get; init; }

    public ScalingDamageBehavior? ScalingDamage { get; init; }

    public IReadOnlyList<DynamicSetupDescriptor> DynamicSetups { get; init; } = [];

    public int ConstantRepeatHitCount { get; init; } = 1;

    public string? SupportedSourceLessMoveDestination { get; init; }

    public bool ForceBeamPowerFloor { get; init; }

    public bool UnsupportedCardGeneration { get; init; }

    public bool Has(CardBehaviorKind behavior)
    {
        return Behaviors.Contains(behavior);
    }
}

// Central registry for card-specific simulator behavior. Generic mechanics stay on SimulationCard
// facts; anything that depends on a particular card identity is declared here and implemented by
// the matching simulator lifecycle hook.
public static class CardBehaviorCatalog
{
    public const string BeamSetupSlot = "beam";
    public const string PlaySetupSlot = "play";

    public const string AnointedRareDrawAverageDecisionValue =
        "anointed.rareDrawAverageDecisionValue";

    public const string CosmicIndifferenceMaxDeckPlayValue =
        "cosmicIndifference.maxDeckPlayValue";

    private static readonly CardBehaviorDefinition Empty = new()
    {
        BaseTypeName = string.Empty
    };

    private static readonly IReadOnlyDictionary<string, CardBehaviorDefinition> Definitions =
        new CardBehaviorDefinition[]
        {
            new()
            {
                BaseTypeName = "Anointed",
                Behaviors = [CardBehaviorKind.AnointedRareDrawToHand],
                SupportedSourceLessMoveDestination = "Hand",
                DynamicSetups =
                [
                    new DynamicSetupDescriptor
                    {
                        Key = AnointedRareDrawAverageDecisionValue,
                        AppliesToBaseTypeName = "Anointed",
                        Slots = [BeamSetupSlot, PlaySetupSlot],
                        Formula = "average decision value of Rare cards currently in draw pile",
                        RuntimeBasis = "drawPile cards with rarity == Rare",
                        ReportingNote = "dynamic beam/play setup; static setup values remain 0"
                    }
                ]
            },
            new()
            {
                BaseTypeName = "BeatIntoShape",
                Behaviors = [CardBehaviorKind.DynamicForgeFromAttacksPlayed]
            },
            new()
            {
                BaseTypeName = "Begone",
                Transform = new CardTransformBehavior
                {
                    GenericTargetOverride = "MinionStrike",
                    SelectionMode = CardTransformSelectionMode.DisposableFodder
                }
            },
            new()
            {
                BaseTypeName = "BundleOfJoy",
                GeneratedCards = GeneratedCardBehavior.BundleOfJoy
            },
            new()
            {
                BaseTypeName = "Charge",
                Transform = new CardTransformBehavior
                {
                    SelectionMode = CardTransformSelectionMode.DisposableFodder
                }
            },
            new()
            {
                BaseTypeName = "CollisionCourse",
                GeneratedCards = GeneratedCardBehavior.CollisionCourse
            },
            new()
            {
                BaseTypeName = "CosmicIndifference",
                DynamicSetups =
                [
                    new DynamicSetupDescriptor
                    {
                        Key = CosmicIndifferenceMaxDeckPlayValue,
                        AppliesToBaseTypeName = "CosmicIndifference",
                        Slots = [PlaySetupSlot],
                        Formula = "0.8 * max non-exhaust deck card immediate/resource play value",
                        RuntimeBasis = "non-exhaust deck cards in combat state",
                        ReportingNote = "dynamic play setup; beam setup unchanged"
                    }
                ]
            },
            new()
            {
                BaseTypeName = "CrashLanding",
                GeneratedCards = GeneratedCardBehavior.CrashLanding
            },
            new()
            {
                BaseTypeName = "CrescentSpear",
                ScalingDamage = new("starCostCardCount", ScalingDamageBehaviorMode.ParsedAction)
            },
            new()
            {
                BaseTypeName = "DefendRegent",
                Behaviors =
                [
                    CardBehaviorKind.PurityAlwaysExhaustible,
                    CardBehaviorKind.PreferredTransformFodder
                ]
            },
            new()
            {
                BaseTypeName = "Discovery",
                GeneratedCards = GeneratedCardBehavior.Discovery
            },
            new()
            {
                BaseTypeName = "GoldAxe",
                ScalingDamage = new("cardsPlayedThisCombat", ScalingDamageBehaviorMode.ParsedAction)
            },
            new()
            {
                BaseTypeName = "Guards",
                Transform = new CardTransformBehavior
                {
                    GenericTargetOverride = "MinionSacrifice",
                    CountMode = CardTransformCountMode.AllAvailable,
                    SelectionMode = CardTransformSelectionMode.DisposableFodder,
                    RequireReplacementImprovement = true
                }
            },
            new()
            {
                BaseTypeName = "HeavenlyDrill",
                Behaviors = [CardBehaviorKind.HeavenlyDrillMinimumEnergy]
            },
            new()
            {
                BaseTypeName = "HeirloomHammer",
                GeneratedCards = GeneratedCardBehavior.HeirloomHammer
            },
            new()
            {
                BaseTypeName = "JackOfAllTrades",
                GeneratedCards = GeneratedCardBehavior.JackOfAllTrades
            },
            new()
            {
                BaseTypeName = "Jackpot",
                GeneratedCards = GeneratedCardBehavior.Jackpot
            },
            new()
            {
                BaseTypeName = "LunarBlast",
                ScalingDamage = new("skillsPlayedThisTurn", ScalingDamageBehaviorMode.ConditionalHitCount)
            },
            new()
            {
                BaseTypeName = "ManifestAuthority",
                GeneratedCards = GeneratedCardBehavior.ManifestAuthority
            },
            new()
            {
                BaseTypeName = "MindBlast",
                ScalingDamage = new("drawPileCount", ScalingDamageBehaviorMode.ParsedAction)
            },
            new()
            {
                BaseTypeName = "Monologue",
                ForceBeamPowerFloor = true
            },
            new()
            {
                BaseTypeName = "Purity",
                Behaviors = [CardBehaviorKind.PuritySelectiveExhaust]
            },
            new()
            {
                BaseTypeName = "Quasar",
                GeneratedCards = GeneratedCardBehavior.Quasar
            },
            new()
            {
                BaseTypeName = "Radiate",
                ScalingDamage = new("starsGainedThisTurn", ScalingDamageBehaviorMode.ConditionalHitCount)
            },
            new()
            {
                BaseTypeName = "SevenStars",
                ConstantRepeatHitCount = 7
            },
            new()
            {
                BaseTypeName = "Shame",
                Behaviors = [CardBehaviorKind.TurnEndFrail]
            },
            new()
            {
                BaseTypeName = "ShiningStrike",
                SupportedSourceLessMoveDestination = "Draw"
            },
            new()
            {
                BaseTypeName = "SovereignBlade",
                ModelIds = ["CARD.SOVEREIGN_BLADE", "GENERATED.SOVEREIGN_BLADE"],
                Behaviors = [CardBehaviorKind.SovereignBlade]
            },
            new()
            {
                BaseTypeName = "Splash",
                UnsupportedCardGeneration = true
            },
            new()
            {
                BaseTypeName = "StrikeRegent",
                Behaviors = [CardBehaviorKind.PurityAlwaysExhaustible]
            },
            new()
            {
                BaseTypeName = "SummonForth",
                Behaviors = [CardBehaviorKind.RetrieveSovereignBladesBeforeForge],
                SupportedSourceLessMoveDestination = "Hand"
            },
            new()
            {
                BaseTypeName = "Supermassive",
                ScalingDamage = new("generatedCardsCreated", ScalingDamageBehaviorMode.ParsedAction)
            },
            new()
            {
                BaseTypeName = "TheBomb",
                Behaviors = [CardBehaviorKind.UpgradedBombDamage],
                ForceBeamPowerFloor = true
            }
        }.ToDictionary(definition => definition.BaseTypeName, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, CardBehaviorDefinition> DefinitionsByModelId =
        Definitions.Values
            .SelectMany(definition => definition.ModelIds.Select(modelId => (modelId, definition)))
            .ToDictionary(item => item.modelId, item => item.definition, StringComparer.OrdinalIgnoreCase);

    public static CardBehaviorDefinition ForCard(SimulationCard card)
    {
        CardBehaviorDefinition byTypeName = ForCardTypeName(card.TypeName);
        return byTypeName.BaseTypeName.Length > 0
            ? byTypeName
            : DefinitionsByModelId.GetValueOrDefault(BaseTypeName(card.ModelId), Empty);
    }

    public static CardBehaviorDefinition ForCardTypeName(string typeName)
    {
        return Definitions.GetValueOrDefault(BaseTypeName(typeName), Empty);
    }

    public static bool Has(SimulationCard card, CardBehaviorKind behavior)
    {
        return ForCard(card).Has(behavior);
    }

    public static bool Has(string typeName, CardBehaviorKind behavior)
    {
        return ForCardTypeName(typeName).Has(behavior);
    }

    public static string BaseTypeName(string typeName)
    {
        int upgradeSeparator = typeName.IndexOf('+', StringComparison.Ordinal);
        return upgradeSeparator < 0 ? typeName : typeName[..upgradeSeparator];
    }
}
