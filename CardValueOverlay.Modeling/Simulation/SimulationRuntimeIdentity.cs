using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public enum SimulationCardKind
{
    Unknown,
    Attack,
    Skill,
    Power,
    Status,
    Curse
}

public enum SimulationActionKind
{
    Unknown,
    Block,
    CreateCard,
    CreateCardChoices,
    DebuffVulnerable,
    DebuffWeak,
    HpLoss,
    MoveCardBetweenPiles,
    PersistentPowerTrigger,
    Power,
    SelfReturn,
    TransformCard,
    XCostDamage
}

public enum SimulationPowerKind
{
    Unknown,
    Arsenal,
    Automation,
    Calamity,
    Conqueror,
    CrushUnder,
    DarkShackles,
    Dexterity,
    DyingStar,
    Entropy,
    Fasten,
    Frail,
    Furnace,
    Genesis,
    Mayhem,
    Monologue,
    Nostalgia,
    Orbit,
    PaleBlueDot,
    Panache,
    Parry,
    PillarOfCreation,
    Plating,
    PrepTime,
    Reflect,
    RetainHand,
    RollingBoulder,
    SeekingEdge,
    SpectrumShift,
    Stratagem,
    Strength,
    SwordSage,
    TheBomb,
    TheSealedThrone,
    Thorns,
    Tyranny,
    Vigor,
    VoidForm
}

public readonly record struct SimulationActionIdentity(
    SimulationActionKind Kind,
    SimulationPowerKind PowerKind);

public sealed class SimulationRuntimeIdentity
{
    private const string PowerPrefix = "power:";

    private SimulationRuntimeIdentity(
        SimulationCardKind cardKind,
        string baseTypeName,
        CardBehaviorDefinition behavior,
        SimulationActionIdentity[] actions)
    {
        CardKind = cardKind;
        BaseTypeName = baseTypeName;
        Behavior = behavior;
        Actions = actions;
    }

    public SimulationCardKind CardKind { get; }

    public string BaseTypeName { get; }

    public CardBehaviorDefinition Behavior { get; }

    public SimulationActionIdentity[] Actions { get; }

    public bool HasAction(SimulationActionKind kind)
    {
        for (int index = 0; index < Actions.Length; index++)
        {
            if (Actions[index].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static SimulationRuntimeIdentity Create(SimulationCard card)
    {
        SimulationActionIdentity[] actions = new SimulationActionIdentity[card.Actions.Count];
        for (int index = 0; index < card.Actions.Count; index++)
        {
            CardActionFact action = card.Actions[index];
            actions[index] = new SimulationActionIdentity(
                ParseActionKind(action.Kind),
                ParsePowerKind(action.Parameter));
        }

        return new SimulationRuntimeIdentity(
            ParseCardKind(card.CardType),
            CardBehaviorCatalog.BaseTypeName(card.TypeName),
            CardBehaviorCatalog.ForCardIdentity(card.TypeName, card.ModelId),
            actions);
    }

    private static SimulationCardKind ParseCardKind(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ATTACK" => SimulationCardKind.Attack,
            "SKILL" => SimulationCardKind.Skill,
            "POWER" => SimulationCardKind.Power,
            "STATUS" => SimulationCardKind.Status,
            "CURSE" => SimulationCardKind.Curse,
            _ => SimulationCardKind.Unknown
        };
    }

    private static SimulationActionKind ParseActionKind(string value)
    {
        return value switch
        {
            "block" => SimulationActionKind.Block,
            "createCard" => SimulationActionKind.CreateCard,
            "createCardChoices" => SimulationActionKind.CreateCardChoices,
            "debuffVulnerable" => SimulationActionKind.DebuffVulnerable,
            "debuffWeak" => SimulationActionKind.DebuffWeak,
            "hpLoss" => SimulationActionKind.HpLoss,
            "moveCardBetweenPiles" => SimulationActionKind.MoveCardBetweenPiles,
            "persistentPowerTrigger" => SimulationActionKind.PersistentPowerTrigger,
            "power" => SimulationActionKind.Power,
            "selfReturn" => SimulationActionKind.SelfReturn,
            "transformCard" => SimulationActionKind.TransformCard,
            "xCostDamage" => SimulationActionKind.XCostDamage,
            _ => SimulationActionKind.Unknown
        };
    }

    private static SimulationPowerKind ParsePowerKind(string? parameter)
    {
        if (parameter is null || !parameter.StartsWith(PowerPrefix, StringComparison.Ordinal))
        {
            return SimulationPowerKind.Unknown;
        }

        ReadOnlySpan<char> key = parameter.AsSpan(PowerPrefix.Length);
        int separator = key.IndexOf(';');
        if (separator >= 0)
        {
            key = key[..separator];
        }

        return key switch
        {
            "Arsenal" => SimulationPowerKind.Arsenal,
            "Automation" => SimulationPowerKind.Automation,
            "Calamity" => SimulationPowerKind.Calamity,
            "Conqueror" => SimulationPowerKind.Conqueror,
            "CrushUnder" => SimulationPowerKind.CrushUnder,
            "DarkShackles" => SimulationPowerKind.DarkShackles,
            "Dexterity" => SimulationPowerKind.Dexterity,
            "DyingStar" => SimulationPowerKind.DyingStar,
            "Entropy" => SimulationPowerKind.Entropy,
            "Fasten" => SimulationPowerKind.Fasten,
            "Frail" => SimulationPowerKind.Frail,
            "Furnace" => SimulationPowerKind.Furnace,
            "Genesis" => SimulationPowerKind.Genesis,
            "Mayhem" => SimulationPowerKind.Mayhem,
            "Monologue" => SimulationPowerKind.Monologue,
            "Nostalgia" => SimulationPowerKind.Nostalgia,
            "Orbit" => SimulationPowerKind.Orbit,
            "PaleBlueDot" => SimulationPowerKind.PaleBlueDot,
            "Panache" => SimulationPowerKind.Panache,
            "Parry" => SimulationPowerKind.Parry,
            "PillarOfCreation" => SimulationPowerKind.PillarOfCreation,
            "Plating" => SimulationPowerKind.Plating,
            "PrepTime" => SimulationPowerKind.PrepTime,
            "Reflect" => SimulationPowerKind.Reflect,
            "RetainHand" => SimulationPowerKind.RetainHand,
            "RollingBoulder" => SimulationPowerKind.RollingBoulder,
            "SeekingEdge" => SimulationPowerKind.SeekingEdge,
            "SpectrumShift" => SimulationPowerKind.SpectrumShift,
            "Stratagem" => SimulationPowerKind.Stratagem,
            "Strength" => SimulationPowerKind.Strength,
            "SwordSage" => SimulationPowerKind.SwordSage,
            "TheBomb" => SimulationPowerKind.TheBomb,
            "TheSealedThrone" => SimulationPowerKind.TheSealedThrone,
            "Thorns" => SimulationPowerKind.Thorns,
            "Tyranny" => SimulationPowerKind.Tyranny,
            "Vigor" => SimulationPowerKind.Vigor,
            "VoidForm" => SimulationPowerKind.VoidForm,
            _ => SimulationPowerKind.Unknown
        };
    }

}
