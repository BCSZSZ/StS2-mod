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

public static class DynamicSetupCatalog
{
    public const string BeamSlot = "beam";
    public const string PlaySlot = "play";

    public const string AnointedRareDrawAverageDecisionValue =
        "anointed.rareDrawAverageDecisionValue";

    public const string CosmicIndifferenceMaxDeckPlayValue =
        "cosmicIndifference.maxDeckPlayValue";

    private static readonly DynamicSetupCatalogEntry[] Entries =
    [
        new(
            "Anointed",
            new DynamicSetupDescriptor
            {
                Key = AnointedRareDrawAverageDecisionValue,
                AppliesToBaseTypeName = "Anointed",
                Slots = [BeamSlot, PlaySlot],
                Formula = "average decision value of Rare cards currently in draw pile",
                RuntimeBasis = "drawPile cards with rarity == Rare",
                ReportingNote = "dynamic beam/play setup; static setup values remain 0"
            }),
        new(
            "CosmicIndifference",
            new DynamicSetupDescriptor
            {
                Key = CosmicIndifferenceMaxDeckPlayValue,
                AppliesToBaseTypeName = "CosmicIndifference",
                Slots = [PlaySlot],
                Formula = "0.8 * max non-exhaust deck card immediate/resource play value",
                RuntimeBasis = "non-exhaust deck cards in combat state",
                ReportingNote = "dynamic play setup; beam setup unchanged"
            })
    ];

    public static IReadOnlyList<DynamicSetupDescriptor> ForCardTypeName(string typeName)
    {
        string baseTypeName = BaseTypeName(typeName);
        return Entries
            .Where(entry => string.Equals(entry.BaseTypeName, baseTypeName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Descriptor)
            .ToArray();
    }

    public static string BaseTypeName(string typeName)
    {
        int upgradeSeparator = typeName.IndexOf('+', StringComparison.Ordinal);
        return upgradeSeparator < 0 ? typeName : typeName[..upgradeSeparator];
    }

    private sealed record DynamicSetupCatalogEntry(
        string BaseTypeName,
        DynamicSetupDescriptor Descriptor);
}
