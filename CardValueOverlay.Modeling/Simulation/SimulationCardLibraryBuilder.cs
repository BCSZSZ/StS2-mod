using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class SimulationCardLibraryBuilder
{
    private static readonly HashSet<string> SimulatedResourceKinds = new(StringComparer.Ordinal)
    {
        "draw",
        "drawNextTurn",
        "energyGain",
        "energyNextTurn",
        "starGain",
        "starNextTurn",
        "starCost",
        "forge",
        "debuffVulnerable"
    };

    public IReadOnlyList<SimulationCard> Build(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        ValueCalibration calibration,
        int layer)
    {
        CardValueEstimator estimator = new();
        IReadOnlyList<CardValueEstimate> estimates = estimator.Estimate(entries, calibration, layer);
        IReadOnlyList<CardValueEstimate> weakLayerEstimates = estimator.Estimate(entries, calibration, WeakEstimateLayer(calibration, layer));
        Dictionary<string, CardValueEstimate> estimatesByModelId = estimates.ToDictionary(
            estimate => estimate.ModelId,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CardValueEstimate> weakLayerEstimatesByModelId = weakLayerEstimates.ToDictionary(
            estimate => estimate.ModelId,
            StringComparer.OrdinalIgnoreCase);

        return entries
            .Select(entry => BuildCard(
                entry,
                estimatesByModelId[entry.ModelId],
                weakLayerEstimatesByModelId[entry.ModelId],
                layer))
            .OrderBy(card => card.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static SimulationCard BuildCard(
        CardEffectTermCatalogEntry entry,
        CardValueEstimate estimate,
        CardValueEstimate weakLayerEstimate,
        int layer)
    {
        List<string> warnings = [.. estimate.Warnings];
        int energyCost = entry.Cost.GetValueOrDefault(-1);
        bool unplayable = !entry.Cost.HasValue || entry.Cost.Value < 0 || HasKeyword(entry, "Unplayable");

        decimal intrinsicValue = estimate.Contributions
            .Where(contribution => !SimulatedResourceKinds.Contains(contribution.TermKind))
            .Where(contribution => !string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Where(contribution => !IsRuntimeSimulatedRetainContribution(contribution))
            .Sum(contribution => contribution.BaseValue);
        intrinsicValue += weakLayerEstimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "debuffWeak", StringComparison.Ordinal))
            .Sum(contribution => contribution.BaseValue);

        SimulationCard card = new()
        {
            ModelId = entry.ModelId,
            TypeName = entry.TypeName,
            FullTypeName = entry.FullTypeName,
            Cost = entry.Cost,
            CardType = entry.CardType,
            Rarity = entry.Rarity,
            TargetType = entry.TargetType,
            Layer = layer,
            StaticEstimatedValue = estimate.EstimatedValue,
            IntrinsicValue = intrinsicValue,
            DamageValue = DamageValue(estimate),
            EnergyCost = energyCost,
            StarCost = SumTermAmount(entry, "starCost"),
            Draw = SumTermAmount(entry, "draw"),
            DrawNextTurn = SumTermAmount(entry, "drawNextTurn"),
            EnergyGain = SumTermAmount(entry, "energyGain"),
            EnergyNextTurn = SumTermAmount(entry, "energyNextTurn"),
            StarGain = SumTermAmount(entry, "starGain"),
            StarNextTurn = SumTermAmount(entry, "starNextTurn"),
            Forge = SumTermAmount(entry, "forge"),
            Vulnerable = SumTermAmount(entry, "debuffVulnerable"),
            Exhausts = HasKeyword(entry, "Exhaust"),
            Unplayable = unplayable,
            Ethereal = HasKeyword(entry, "Ethereal"),
            Retain = HasKeyword(entry, "Retain"),
            Innate = HasKeyword(entry, "Innate"),
            Confidence = estimate.Confidence,
            Warnings = warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
        return card;
    }

    private static decimal DamageValue(CardValueEstimate estimate)
    {
        return estimate.Contributions
            .Where(contribution => string.Equals(contribution.TermKind, "damage", StringComparison.Ordinal))
            .Sum(contribution => contribution.BaseValue);
    }

    private static int WeakEstimateLayer(ValueCalibration calibration, int fallbackLayer)
    {
        return calibration.LayerBreakpoints
            .Order()
            .Skip(1)
            .FirstOrDefault(fallbackLayer);
    }

    private static int SumTermAmount(CardEffectTermCatalogEntry entry, string kind)
    {
        decimal amount = entry.Terms
            .Where(term => string.Equals(term.Kind, kind, StringComparison.Ordinal))
            .Sum(term => term.Amount ?? 0m);

        return amount <= 0m ? 0 : (int)Math.Round(amount, MidpointRounding.AwayFromZero);
    }

    private static bool HasKeyword(CardEffectTermCatalogEntry entry, string keyword)
    {
        return entry.Terms.Any(term =>
            (term.Kind is "keyword" or "keywordOnUpgrade")
            && string.Equals(term.Parameter, keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRuntimeSimulatedRetainContribution(CardValueContribution contribution)
    {
        return contribution.TermKind is "keyword" or "keywordOnUpgrade"
            && string.Equals(contribution.Parameter, "Retain", StringComparison.OrdinalIgnoreCase);
    }
}
