using CardValueOverlay.Modeling.Estimation;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record SimulationScenario
{
    public string Name { get; init; } = "simulation-scenario";

    public string? Description { get; init; }

    public string? DeckFile { get; init; }

    public IReadOnlyList<SimulationDeckCardSpec> Deck { get; init; } = [];

    public IReadOnlyList<SimulationScenarioVariant> Variants { get; init; } = [];

    public DeckSimulationOptions? Options { get; init; }

    public IReadOnlyList<string> Assumptions { get; init; } = [];
}

public sealed record SimulationDeckDefinition
{
    public string Name { get; init; } = "simulation-deck";

    public string? Description { get; init; }

    public IReadOnlyList<SimulationDeckCardSpec> Cards { get; init; } = [];

    public IReadOnlyList<string> Assumptions { get; init; } = [];
}

public sealed record SimulationDeckCardSpec
{
    public string? TypeName { get; init; }

    public string? ModelId { get; init; }

    public string? CloneTypeName { get; init; }

    public string? CloneModelId { get; init; }

    public string? DisplayName { get; init; }

    public int Count { get; init; } = 1;

    public SimulationCardPatch? Patch { get; init; }

    public string? Notes { get; init; }
}

public sealed record SimulationScenarioVariant
{
    public string Id { get; init; } = "variant";

    public string Label { get; init; } = "Variant";

    public IReadOnlyList<SimulationDeckCardRemoval> RemoveCards { get; init; } = [];

    public IReadOnlyList<SimulationDeckCardSpec> AddCards { get; init; } = [];

    public IReadOnlyList<SimulationCardPatchRule> CardPatches { get; init; } = [];
}

public sealed record SimulationDeckCardRemoval
{
    public string? MatchTypeName { get; init; }

    public string? MatchModelId { get; init; }

    public int Count { get; init; } = 1;
}

public sealed record SimulationCardPatchRule
{
    public string? MatchTypeName { get; init; }

    public string? MatchModelId { get; init; }

    public SimulationCardPatch Patch { get; init; } = new();
}

public sealed record SimulationCardPatch
{
    public string? ModelId { get; init; }

    public string? TypeName { get; init; }

    public string? FullTypeName { get; init; }

    public string? CardType { get; init; }

    public string? Rarity { get; init; }

    public string? TargetType { get; init; }

    public int? Cost { get; init; }

    public int? EnergyCost { get; init; }

    public int? StarCost { get; init; }

    public int? Draw { get; init; }

    public int? DrawNextTurn { get; init; }

    public int? EnergyGain { get; init; }

    public int? EnergyNextTurn { get; init; }

    public int? StarGain { get; init; }

    public int? StarNextTurn { get; init; }

    public int? Forge { get; init; }

    public int? Vulnerable { get; init; }

    public decimal? Damage { get; init; }

    public decimal? Block { get; init; }

    public decimal? IntrinsicValue { get; init; }

    public decimal? StaticEstimatedValue { get; init; }

    public bool? Exhausts { get; init; }

    public bool? Unplayable { get; init; }

    public bool? Ethereal { get; init; }

    public bool? Retain { get; init; }

    public bool? Innate { get; init; }

    public IReadOnlyList<string> AddWarnings { get; init; } = [];
}

public sealed record SimulationScenarioReport(
    string Name,
    string? Description,
    int Layer,
    DeckSimulationOptions Options,
    IReadOnlyList<SimulationScenarioDeckEntry> Deck,
    IReadOnlyList<SimulationScenarioVariantResult> Results,
    IReadOnlyList<string> Assumptions);

public sealed record SimulationScenarioDeckEntry(
    string TypeName,
    string ModelId,
    string? DisplayName,
    int Count,
    string? Notes);

public sealed record SimulationScenarioVariantResult(
    string Id,
    string Label,
    int DeckSize,
    decimal TotalExpectedValue,
    decimal ExpectedValuePerTurn,
    decimal DeltaFromBaseline,
    decimal? DeltaFromPrevious,
    decimal DeltaPerTurnFromBaseline,
    decimal? DeltaPerTurnFromPrevious,
    decimal TotalVariance,
    IReadOnlyList<CardPlaySummary> PlayedCards,
    IReadOnlyList<CardValueCreditSummary> CardValueCredits,
    IReadOnlyList<string> Warnings);

public sealed class SimulationScenarioRunner
{
    public SimulationScenarioReport Run(
        SimulationScenario scenario,
        IReadOnlyList<SimulationCard> library,
        ValueCalibration calibration,
        int layer,
        DeckSimulationOptions options)
    {
        if (scenario.Deck.Count == 0)
        {
            throw new InvalidOperationException("Simulation scenario deck is empty.");
        }

        Dictionary<string, SimulationCard> byTypeName = library
            .GroupBy(card => card.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SimulationCard> byModelId = library
            .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<SimulationCard> baseDeck = [];
        List<SimulationScenarioDeckEntry> deckEntries = [];
        foreach (SimulationDeckCardSpec spec in scenario.Deck)
        {
            if (spec.Count <= 0)
            {
                continue;
            }

            SimulationCard card = BuildCard(spec, byTypeName, byModelId, calibration, layer);
            for (int i = 0; i < spec.Count; i++)
            {
                baseDeck.Add(card);
            }

            deckEntries.Add(new SimulationScenarioDeckEntry(
                card.TypeName,
                card.ModelId,
                spec.DisplayName,
                spec.Count,
                spec.Notes));
        }

        IReadOnlyList<SimulationScenarioVariant> variants = scenario.Variants.Count == 0
            ? [new SimulationScenarioVariant { Id = "base", Label = "Base" }]
            : scenario.Variants;
        List<SimulationScenarioVariantResult> results = [];
        decimal? baselineValue = null;
        decimal? previousValue = null;
        foreach (SimulationScenarioVariant variant in variants)
        {
            IReadOnlyList<SimulationCard> deck = ApplyVariant(baseDeck, variant, byTypeName, byModelId, calibration, layer);
            DeckSimulationReport simulation = new DeckMonteCarloSimulator().Simulate(deck, options);
            baselineValue ??= simulation.TotalExpectedValue;
            decimal? deltaFromPrevious = previousValue.HasValue
                ? simulation.TotalExpectedValue - previousValue.Value
                : null;
            previousValue = simulation.TotalExpectedValue;

            results.Add(new SimulationScenarioVariantResult(
                variant.Id,
                variant.Label,
                deck.Count,
                simulation.TotalExpectedValue,
                Round(simulation.TotalExpectedValue / options.Turns),
                simulation.TotalExpectedValue - baselineValue.Value,
                deltaFromPrevious,
                Round((simulation.TotalExpectedValue - baselineValue.Value) / options.Turns),
                deltaFromPrevious.HasValue ? Round(deltaFromPrevious.Value / options.Turns) : null,
                simulation.TotalVariance,
                simulation.PlayedCards,
                simulation.CardValueCredits,
                simulation.Warnings));
        }

        return new SimulationScenarioReport(
            scenario.Name,
            scenario.Description,
            layer,
            options,
            deckEntries,
            results,
            scenario.Assumptions);
    }

    private static SimulationCard BuildCard(
        SimulationDeckCardSpec spec,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId,
        ValueCalibration calibration,
        int layer)
    {
        SimulationCard baseCard = ResolveBaseCard(spec, byTypeName, byModelId);
        return ApplyPatch(baseCard, spec.Patch, calibration, layer);
    }

    private static SimulationCard ResolveBaseCard(
        SimulationDeckCardSpec spec,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId)
    {
        string? modelId = spec.CloneModelId ?? spec.ModelId;
        if (!string.IsNullOrWhiteSpace(modelId) && byModelId.TryGetValue(modelId, out SimulationCard? modelMatch))
        {
            return modelMatch;
        }

        string? typeName = spec.CloneTypeName ?? spec.TypeName;
        if (!string.IsNullOrWhiteSpace(typeName) && byTypeName.TryGetValue(typeName, out SimulationCard? typeMatch))
        {
            return typeMatch;
        }

        string customTypeName = spec.Patch?.TypeName ?? spec.TypeName ?? spec.DisplayName ?? "CustomCard";
        return new SimulationCard
        {
            ModelId = spec.Patch?.ModelId ?? $"DIY.{customTypeName.ToUpperInvariant()}",
            TypeName = customTypeName,
            FullTypeName = spec.Patch?.FullTypeName ?? $"DIY.{customTypeName}",
            Cost = spec.Patch?.Cost ?? spec.Patch?.EnergyCost ?? 0,
            CardType = spec.Patch?.CardType ?? "Skill",
            Rarity = spec.Patch?.Rarity ?? "Custom",
            TargetType = spec.Patch?.TargetType ?? "Self",
            Layer = 1,
            EnergyCost = spec.Patch?.EnergyCost ?? spec.Patch?.Cost ?? 0,
            Confidence = 0.5,
            Warnings = ["DIY simulation card."]
        };
    }

    private static IReadOnlyList<SimulationCard> ApplyVariant(
        IReadOnlyList<SimulationCard> baseDeck,
        SimulationScenarioVariant variant,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId,
        ValueCalibration calibration,
        int layer)
    {
        List<SimulationCard> deck = baseDeck.ToList();
        foreach (SimulationDeckCardRemoval removal in variant.RemoveCards)
        {
            RemoveCards(deck, removal);
        }

        foreach (SimulationDeckCardSpec spec in variant.AddCards)
        {
            if (spec.Count <= 0)
            {
                continue;
            }

            SimulationCard addedCard = BuildCard(spec, byTypeName, byModelId, calibration, layer);
            for (int i = 0; i < spec.Count; i++)
            {
                deck.Add(addedCard);
            }
        }

        return deck
            .Select(card =>
            {
                SimulationCard current = card;
                foreach (SimulationCardPatchRule rule in variant.CardPatches.Where(rule => Matches(card, rule)))
                {
                    current = ApplyPatch(current, rule.Patch, calibration, layer);
                }

                return current;
            })
            .ToArray();
    }

    private static void RemoveCards(List<SimulationCard> deck, SimulationDeckCardRemoval removal)
    {
        int count = Math.Max(0, removal.Count);
        for (int i = 0; i < count; i++)
        {
            int index = deck.FindIndex(card => Matches(card, removal));
            if (index < 0)
            {
                throw new InvalidOperationException("Variant removeCards could not find a matching card.");
            }

            deck.RemoveAt(index);
        }
    }

    private static bool Matches(SimulationCard card, SimulationCardPatchRule rule)
    {
        bool hasMatcher = !string.IsNullOrWhiteSpace(rule.MatchTypeName)
            || !string.IsNullOrWhiteSpace(rule.MatchModelId);
        if (!hasMatcher)
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(rule.MatchTypeName)
                && string.Equals(card.TypeName, rule.MatchTypeName, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(rule.MatchModelId)
                && string.Equals(card.ModelId, rule.MatchModelId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Matches(SimulationCard card, SimulationDeckCardRemoval removal)
    {
        bool hasMatcher = !string.IsNullOrWhiteSpace(removal.MatchTypeName)
            || !string.IsNullOrWhiteSpace(removal.MatchModelId);
        if (!hasMatcher)
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(removal.MatchTypeName)
                && string.Equals(card.TypeName, removal.MatchTypeName, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(removal.MatchModelId)
                && string.Equals(card.ModelId, removal.MatchModelId, StringComparison.OrdinalIgnoreCase));
    }

    private static SimulationCard ApplyPatch(
        SimulationCard card,
        SimulationCardPatch? patch,
        ValueCalibration calibration,
        int layer)
    {
        if (patch is null)
        {
            return card;
        }

        decimal? calculatedIntrinsicValue = CalculateIntrinsicValue(patch, calibration, layer);
        decimal intrinsicValue = patch.IntrinsicValue ?? calculatedIntrinsicValue ?? card.IntrinsicValue;
        decimal staticEstimatedValue = patch.StaticEstimatedValue ?? calculatedIntrinsicValue ?? card.StaticEstimatedValue;
        bool hasValuePatch = patch.Damage.HasValue || patch.Block.HasValue;
        decimal damageValue = hasValuePatch
            ? CalculateDamageValue(patch, calibration, layer)
            : card.DamageValue;

        return card with
        {
            ModelId = patch.ModelId ?? card.ModelId,
            TypeName = patch.TypeName ?? card.TypeName,
            FullTypeName = patch.FullTypeName ?? card.FullTypeName,
            Cost = patch.Cost ?? card.Cost,
            CardType = patch.CardType ?? card.CardType,
            Rarity = patch.Rarity ?? card.Rarity,
            TargetType = patch.TargetType ?? card.TargetType,
            Layer = layer,
            StaticEstimatedValue = staticEstimatedValue,
            IntrinsicValue = intrinsicValue,
            DamageValue = damageValue,
            EnergyCost = patch.EnergyCost ?? patch.Cost ?? card.EnergyCost,
            StarCost = patch.StarCost ?? card.StarCost,
            Draw = patch.Draw ?? card.Draw,
            DrawNextTurn = patch.DrawNextTurn ?? card.DrawNextTurn,
            EnergyGain = patch.EnergyGain ?? card.EnergyGain,
            EnergyNextTurn = patch.EnergyNextTurn ?? card.EnergyNextTurn,
            StarGain = patch.StarGain ?? card.StarGain,
            StarNextTurn = patch.StarNextTurn ?? card.StarNextTurn,
            Forge = patch.Forge ?? card.Forge,
            Vulnerable = patch.Vulnerable ?? card.Vulnerable,
            Exhausts = patch.Exhausts ?? card.Exhausts,
            Unplayable = patch.Unplayable ?? card.Unplayable,
            Ethereal = patch.Ethereal ?? card.Ethereal,
            Retain = patch.Retain ?? card.Retain,
            Innate = patch.Innate ?? card.Innate,
            Warnings = [.. card.Warnings, .. patch.AddWarnings]
        };
    }

    private static decimal? CalculateIntrinsicValue(
        SimulationCardPatch patch,
        ValueCalibration calibration,
        int layer)
    {
        if (!patch.Damage.HasValue && !patch.Block.HasValue)
        {
            return null;
        }

        decimal damageUnit = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnit");
        decimal blockToDamage = calibration.GetLayeredValue(calibration.BlockToDamage, layer, "blockToDamage");
        decimal damageValue = CalculateDamageValue(patch, calibration, layer);
        decimal blockValue = (patch.Block ?? 0m) * blockToDamage * damageUnit;
        return damageValue + blockValue;
    }

    private static decimal CalculateDamageValue(
        SimulationCardPatch patch,
        ValueCalibration calibration,
        int layer)
    {
        decimal damageUnit = calibration.GetLayeredValue(calibration.DamageUnitValue, layer, "damageUnit");
        return (patch.Damage ?? 0m) * damageUnit;
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
}
