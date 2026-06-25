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
        "forge"
    };

    public IReadOnlyList<SimulationCard> Build(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        ValueCalibration calibration,
        int layer)
    {
        IReadOnlyList<CardValueEstimate> estimates = new CardValueEstimator().Estimate(entries, calibration, layer);
        Dictionary<string, CardValueEstimate> estimatesByModelId = estimates.ToDictionary(
            estimate => estimate.ModelId,
            StringComparer.OrdinalIgnoreCase);

        return entries
            .Select(entry => BuildCard(entry, estimatesByModelId[entry.ModelId], layer))
            .OrderBy(card => card.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static SimulationCard BuildCard(
        CardEffectTermCatalogEntry entry,
        CardValueEstimate estimate,
        int layer)
    {
        List<string> warnings = [.. estimate.Warnings];
        int energyCost = entry.Cost.GetValueOrDefault(-1);
        bool unplayable = !entry.Cost.HasValue || entry.Cost.Value < 0 || HasKeyword(entry, "Unplayable");

        decimal intrinsicValue = estimate.Contributions
            .Where(contribution => !SimulatedResourceKinds.Contains(contribution.TermKind))
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
            EnergyCost = energyCost,
            StarCost = SumTermAmount(entry, "starCost"),
            Draw = SumTermAmount(entry, "draw"),
            DrawNextTurn = SumTermAmount(entry, "drawNextTurn"),
            EnergyGain = SumTermAmount(entry, "energyGain"),
            EnergyNextTurn = SumTermAmount(entry, "energyNextTurn"),
            StarGain = SumTermAmount(entry, "starGain"),
            StarNextTurn = SumTermAmount(entry, "starNextTurn"),
            Forge = SumTermAmount(entry, "forge"),
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
}
