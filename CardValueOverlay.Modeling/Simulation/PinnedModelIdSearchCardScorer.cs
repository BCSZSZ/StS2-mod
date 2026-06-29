namespace CardValueOverlay.Modeling.Simulation;

public sealed class PinnedModelIdSearchCardScorer(
    IReadOnlyCollection<string> pinnedModelIds,
    decimal scoreBoost,
    ISearchCardScorer? inner = null) : ISearchCardScorer
{
    private readonly HashSet<string> _pinnedModelIds = new(pinnedModelIds, StringComparer.OrdinalIgnoreCase);

    public decimal Score(SearchCardScoringContext context)
    {
        decimal score = inner?.Score(context) ?? HeuristicScore(context);
        return _pinnedModelIds.Contains(context.CardModelId)
            ? score + scoreBoost
            : score;
    }

    private static decimal HeuristicScore(SearchCardScoringContext context)
    {
        return context.Features.TryGetValue("card.heuristicScore", out double score)
            ? (decimal)score
            : 0m;
    }
}
