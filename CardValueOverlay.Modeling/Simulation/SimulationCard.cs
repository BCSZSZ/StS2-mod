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

    public int Layer { get; init; }

    public decimal StaticEstimatedValue { get; init; }

    public decimal IntrinsicValue { get; init; }

    public int EnergyCost { get; init; }

    public int StarCost { get; init; }

    public int Draw { get; init; }

    public int DrawNextTurn { get; init; }

    public int EnergyGain { get; init; }

    public int EnergyNextTurn { get; init; }

    public int StarGain { get; init; }

    public int StarNextTurn { get; init; }

    public int Forge { get; init; }

    public bool Exhausts { get; init; }

    public bool Unplayable { get; init; }

    public bool Ethereal { get; init; }

    public bool Retain { get; init; }

    public bool Innate { get; init; }

    public double Confidence { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsPlayable => !Unplayable && EnergyCost >= 0;

    public bool HasSimulatedResourceEffect =>
        Draw > 0
        || DrawNextTurn > 0
        || EnergyGain > 0
        || EnergyNextTurn > 0
        || StarGain > 0
        || StarNextTurn > 0
        || StarCost > 0
        || Forge > 0;
}
