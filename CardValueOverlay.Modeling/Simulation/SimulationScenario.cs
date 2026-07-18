using System.Diagnostics;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;

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

    public int Upgrade { get; init; }

    public string? EnchantmentId { get; init; }

    public int? EnchantmentAmount { get; init; }

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

    public int? UpgradeLevel { get; init; }

    public int? Cost { get; init; }

    public int? EnergyCost { get; init; }

    public int? StarCost { get; init; }

    public int? PowerPlayPriority { get; init; }

    public int? Draw { get; init; }

    public int? DrawNextTurn { get; init; }

    public int? BlockNextTurn { get; init; }

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

    public IReadOnlyList<CardActionFact>? Actions { get; init; }

    public IReadOnlyList<CardActionFact> AddActions { get; init; } = [];

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
    int Upgrade,
    string? EnchantmentId,
    int? EnchantmentAmount,
    string? Notes);

public sealed record SimulationScenarioVariantResult(
    string Id,
    string Label,
    int DeckSize,
    double ElapsedMilliseconds,
    decimal TotalExpectedValue,
    decimal ExpectedValuePerTurn,
    decimal DeltaFromBaseline,
    decimal? DeltaFromPrevious,
    decimal DeltaPerTurnFromBaseline,
    decimal? DeltaPerTurnFromPrevious,
    decimal TotalVariance,
    decimal TurnVarianceSum,
    decimal TurnCovarianceContribution,
    IReadOnlyList<TurnSimulationSummary> Turns,
    IReadOnlyList<TurnCovariance> TurnCovariances,
    IReadOnlyList<CardPlaySummary> PlayedCards,
    IReadOnlyList<CardValueCreditSummary> CardValueCredits,
    IReadOnlyList<CardValueCreditTurnSummary> CardValueCreditsByTurn,
    IReadOnlyList<CardMoveChoiceSummary> CardMoveChoices,
    IReadOnlyList<CardTransformChoiceSummary> CardTransformChoices,
    IReadOnlyList<string> Warnings);

public sealed record PureEvScenarioBenchmarkReport(
    string Name,
    int Layer,
    DeckSimulationOptions Options,
    IReadOnlyList<PureEvScenarioVariantBenchmark> Results);

public sealed record PureEvScenarioVariantBenchmark(
    string Id,
    string Label,
    int DeckSize,
    double MeanTotalValue,
    double DeltaFromBaseline,
    double ElapsedMilliseconds,
    double CpuMilliseconds,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    SearchBudgetTelemetrySnapshot SearchBudget,
    SearchBranchDiagnosticsSnapshot? SearchBranchDiagnostics);

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

        List<ScenarioCardInstance> baseDeck = [];
        List<SimulationScenarioDeckEntry> deckEntries = [];
        int nextStableId = 0;
        foreach (SimulationDeckCardSpec spec in scenario.Deck)
        {
            if (spec.Count <= 0)
            {
                continue;
            }

            SimulationCard card = BuildCard(spec, byTypeName, byModelId, calibration, layer);
            for (int i = 0; i < spec.Count; i++)
            {
                baseDeck.Add(new ScenarioCardInstance(card, nextStableId++));
            }

            deckEntries.Add(new SimulationScenarioDeckEntry(
                card.ReportTypeName,
                card.ReportModelId,
                spec.DisplayName,
                spec.Count,
                card.UpgradeLevel,
                card.Enchantment?.Id,
                card.Enchantment?.Amount,
                spec.Notes));
        }

        IReadOnlyList<SimulationScenarioVariant> variants = scenario.Variants.Count == 0
            ? [new SimulationScenarioVariant { Id = "base", Label = "Base" }]
            : scenario.Variants;
        DeckSimulationOptions runOptions = options with { CardLibrary = library };
        List<SimulationScenarioVariantResult> results = [];
        decimal? baselineValue = null;
        decimal? previousValue = null;
        foreach (SimulationScenarioVariant variant in variants)
        {
            IReadOnlyList<ScenarioCardInstance> deck = ApplyVariant(
                baseDeck,
                variant,
                byTypeName,
                byModelId,
                calibration,
                layer);
            DeckSimulationOptions variantOptions = runOptions with
            {
                StartingInstanceIds = runOptions.CounterfactualStableShuffle
                    ? deck.Select(card => card.StableId).ToArray()
                    : []
            };
            Stopwatch stopwatch = Stopwatch.StartNew();
            DeckSimulationReport simulation = new DeckMonteCarloSimulator().Simulate(
                deck.Select(card => card.Card).ToArray(),
                variantOptions);
            stopwatch.Stop();
            baselineValue ??= simulation.TotalExpectedValue;
            decimal? deltaFromPrevious = previousValue.HasValue
                ? simulation.TotalExpectedValue - previousValue.Value
                : null;
            previousValue = simulation.TotalExpectedValue;
            decimal turnVarianceSum = Round(simulation.Turns.Sum(turn => turn.Variance));
            decimal covarianceContribution = Round(2m * simulation.TurnCovariances.Sum(covariance => covariance.Covariance));

            results.Add(new SimulationScenarioVariantResult(
                variant.Id,
                variant.Label,
                deck.Count,
                stopwatch.Elapsed.TotalMilliseconds,
                simulation.TotalExpectedValue,
                Round(simulation.TotalExpectedValue / options.Turns),
                simulation.TotalExpectedValue - baselineValue.Value,
                deltaFromPrevious,
                Round((simulation.TotalExpectedValue - baselineValue.Value) / options.Turns),
                deltaFromPrevious.HasValue ? Round(deltaFromPrevious.Value / options.Turns) : null,
                simulation.TotalVariance,
                turnVarianceSum,
                covarianceContribution,
                simulation.Turns,
                simulation.TurnCovariances,
                simulation.PlayedCards,
                simulation.CardValueCredits,
                simulation.CardValueCreditsByTurn,
                simulation.CardMoveChoices,
                simulation.CardTransformChoices,
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

    /// <summary>
    /// Benchmarks the same pure expected-value sampling path used by the in-game overlay. Unlike
    /// <see cref="Run"/>, this intentionally builds neither play traces nor attribution reports and
    /// records process allocation/GC plus low-overhead search-node telemetry for each variant.
    /// </summary>
    public PureEvScenarioBenchmarkReport RunPureEvBenchmark(
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

        List<ScenarioCardInstance> baseDeck = [];
        int nextStableId = 0;
        foreach (SimulationDeckCardSpec spec in scenario.Deck)
        {
            if (spec.Count <= 0)
            {
                continue;
            }

            SimulationCard card = BuildCard(spec, byTypeName, byModelId, calibration, layer);
            for (int i = 0; i < spec.Count; i++)
            {
                baseDeck.Add(new ScenarioCardInstance(card, nextStableId++));
            }
        }

        IReadOnlyList<SimulationScenarioVariant> variants = scenario.Variants.Count == 0
            ? [new SimulationScenarioVariant { Id = "base", Label = "Base" }]
            : scenario.Variants;
        List<PureEvScenarioVariantBenchmark> results = [];
        double? baselineMean = null;
        foreach (SimulationScenarioVariant variant in variants)
        {
            IReadOnlyList<ScenarioCardInstance> deck = ApplyVariant(
                baseDeck,
                variant,
                byTypeName,
                byModelId,
                calibration,
                layer);
            SearchBudgetTelemetryCollector budgetTelemetry = new();
            SearchBranchDiagnosticsCollector? branchDiagnostics = options.SearchBranchDiagnostics is null
                ? null
                : new SearchBranchDiagnosticsCollector();
            DeckSimulationOptions variantOptions = options with
            {
                CardLibrary = library,
                SearchBudgetTelemetry = budgetTelemetry,
                SearchBranchDiagnostics = branchDiagnostics,
                StartingInstanceIds = options.CounterfactualStableShuffle
                    ? deck.Select(card => card.StableId).ToArray()
                    : []
            };
            SimulationCard[] simulationDeck = deck.Select(card => card.Card).ToArray();

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            Stopwatch stopwatch = Stopwatch.StartNew();
            ExpectedValueSampleBatch samples = new DeckMonteCarloSimulator()
                .SimulateExpectedTotalSamples(simulationDeck, variantOptions, 0, options.Runs);
            stopwatch.Stop();
            TimeSpan cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);

            double meanTotalValue = samples.TotalValuesByRun.Average();
            baselineMean ??= meanTotalValue;
            results.Add(new PureEvScenarioVariantBenchmark(
                variant.Id,
                variant.Label,
                deck.Count,
                meanTotalValue,
                meanTotalValue - baselineMean.Value,
                stopwatch.Elapsed.TotalMilliseconds,
                (cpuAfter - cpuBefore).TotalMilliseconds,
                Math.Max(0, allocatedAfter - allocatedBefore),
                GC.CollectionCount(0) - gen0Before,
                GC.CollectionCount(1) - gen1Before,
                GC.CollectionCount(2) - gen2Before,
                budgetTelemetry.Snapshot(),
                branchDiagnostics?.Snapshot()));
        }

        return new PureEvScenarioBenchmarkReport(scenario.Name, layer, options, results);
    }

    private static SimulationCard BuildCard(
        SimulationDeckCardSpec spec,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId,
        ValueCalibration calibration,
        int layer)
    {
        SimulationCard baseCard = ResolveBaseCard(spec, byTypeName, byModelId, calibration, layer);
        return ApplyEnchantment(ApplyPatch(baseCard, spec.Patch, calibration, layer), spec);
    }

    private static SimulationCard ResolveBaseCard(
        SimulationDeckCardSpec spec,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId,
        ValueCalibration calibration,
        int layer)
    {
        string? modelId = spec.CloneModelId ?? spec.ModelId;
        if (spec.Upgrade > 0 && !string.IsNullOrWhiteSpace(modelId))
        {
            string upgradedModelId = $"{modelId}+{spec.Upgrade}";
            if (byModelId.TryGetValue(upgradedModelId, out SimulationCard? upgradedModelMatch))
            {
                return upgradedModelMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(modelId) && byModelId.TryGetValue(modelId, out SimulationCard? modelMatch))
        {
            return modelMatch;
        }

        string? typeName = spec.CloneTypeName ?? spec.TypeName;
        if (spec.Upgrade > 0 && !string.IsNullOrWhiteSpace(typeName))
        {
            string upgradedTypeName = $"{typeName}+{spec.Upgrade}";
            if (byTypeName.TryGetValue(upgradedTypeName, out SimulationCard? upgradedTypeMatch))
            {
                return upgradedTypeMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(typeName) && byTypeName.TryGetValue(typeName, out SimulationCard? typeMatch))
        {
            return typeMatch;
        }

        string customTypeName = spec.Patch?.TypeName ?? spec.TypeName ?? spec.DisplayName ?? "CustomCard";
        string cardType = spec.Patch?.CardType ?? "Skill";
        SimulationCard customCard = new()
        {
            ModelId = spec.Patch?.ModelId ?? $"DIY.{customTypeName.ToUpperInvariant()}",
            TypeName = customTypeName,
            FullTypeName = spec.Patch?.FullTypeName ?? $"DIY.{customTypeName}",
            UpgradeLevel = spec.Patch?.UpgradeLevel ?? spec.Upgrade,
            Cost = 0,
            CardType = cardType,
            Rarity = spec.Patch?.Rarity ?? "Custom",
            TargetType = spec.Patch?.TargetType ?? "Self",
            Layer = layer,
            BeamSetupValue = 0d,
            PlaySetupValue = 0d,
            DynamicSetups = CardBehaviorCatalog.ForCardTypeName(customTypeName).DynamicSetups,
            SearchAdmission = CardBehaviorCatalog.ForCardTypeName(customTypeName).SearchAdmission,
            PowerPlayPriority = CardBehaviorCatalog.ForCardTypeName(customTypeName).PowerPlayPriority,
            EnergyCost = 0,
            Confidence = 0.5,
            Warnings = ["DIY simulation card."]
        };
        return ApplyPatch(customCard, spec.Patch, calibration, layer);
    }

    private static IReadOnlyList<ScenarioCardInstance> ApplyVariant(
        IReadOnlyList<ScenarioCardInstance> baseDeck,
        SimulationScenarioVariant variant,
        IReadOnlyDictionary<string, SimulationCard> byTypeName,
        IReadOnlyDictionary<string, SimulationCard> byModelId,
        ValueCalibration calibration,
        int layer)
    {
        List<ScenarioCardInstance> deck = baseDeck.ToList();
        int nextStableId = baseDeck.Count == 0
            ? 0
            : baseDeck.Max(card => card.StableId) + 1;
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
                deck.Add(new ScenarioCardInstance(addedCard, nextStableId++));
            }
        }

        return deck
            .Select(instance =>
            {
                SimulationCard current = instance.Card;
                foreach (SimulationCardPatchRule rule in variant.CardPatches.Where(rule => Matches(instance.Card, rule)))
                {
                    current = ApplyPatch(current, rule.Patch, calibration, layer);
                }

                return instance with { Card = current };
            })
            .ToArray();
    }

    private static void RemoveCards(List<ScenarioCardInstance> deck, SimulationDeckCardRemoval removal)
    {
        int count = Math.Max(0, removal.Count);
        for (int i = 0; i < count; i++)
        {
            int index = deck.FindIndex(card => Matches(card.Card, removal));
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

    private sealed record ScenarioCardInstance(SimulationCard Card, int StableId);

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
        double intrinsicValue = (double?)patch.IntrinsicValue ?? (double?)calculatedIntrinsicValue ?? card.IntrinsicValue;
        double staticEstimatedValue = (double?)patch.StaticEstimatedValue ?? (double?)calculatedIntrinsicValue ?? card.StaticEstimatedValue;
        bool hasValuePatch = patch.Damage.HasValue || patch.Block.HasValue;
        double damageValue = hasValuePatch
            ? (double)CalculateDamageValue(patch, calibration, layer)
            : card.DamageValue;
        IReadOnlyList<CardActionFact> actions = patch.Actions
            ?? [.. card.Actions, .. patch.AddActions];
        string? cardType = patch.CardType ?? card.CardType;
        string typeName = patch.TypeName ?? card.TypeName;

        return card with
        {
            ModelId = patch.ModelId ?? card.ModelId,
            TypeName = typeName,
            FullTypeName = patch.FullTypeName ?? card.FullTypeName,
            Cost = patch.Cost ?? card.Cost,
            CardType = cardType,
            Rarity = patch.Rarity ?? card.Rarity,
            TargetType = patch.TargetType ?? card.TargetType,
            UpgradeLevel = patch.UpgradeLevel ?? card.UpgradeLevel,
            Layer = layer,
            StaticEstimatedValue = staticEstimatedValue,
            IntrinsicValue = intrinsicValue,
            DamageValue = damageValue,
            BeamSetupValue = card.BeamSetupValue,
            PlaySetupValue = card.PlaySetupValue,
            DynamicSetups = CardBehaviorCatalog.ForCardTypeName(typeName).DynamicSetups,
            SearchAdmission = CardBehaviorCatalog.ForCardTypeName(typeName).SearchAdmission,
            PowerPlayPriority = patch.PowerPlayPriority
                ?? CardBehaviorCatalog.ForCardTypeName(typeName).PowerPlayPriority,
            EnergyCost = patch.EnergyCost ?? patch.Cost ?? card.EnergyCost,
            StarCost = patch.StarCost ?? card.StarCost,
            Draw = patch.Draw ?? card.Draw,
            DrawNextTurn = patch.DrawNextTurn ?? card.DrawNextTurn,
            BlockNextTurn = patch.BlockNextTurn ?? card.BlockNextTurn,
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
            Actions = actions,
            Enchantment = card.Enchantment,
            Warnings = [.. card.Warnings, .. patch.AddWarnings]
        };
    }

    private static SimulationCard ApplyEnchantment(SimulationCard card, SimulationDeckCardSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.EnchantmentId))
        {
            return card;
        }

        SimulationEnchantment enchantment = new()
        {
            Id = spec.EnchantmentId,
            Amount = Math.Max(1, spec.EnchantmentAmount ?? 1)
        };
        return card with
        {
            Enchantment = enchantment
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
