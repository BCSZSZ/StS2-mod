namespace CardValueOverlay.Modeling.Simulation;

public sealed class PinnedModelIdSearchCardScorer(
    IReadOnlyCollection<string> pinnedModelIds,
    double scoreBoost,
    ISearchCardScorer? inner = null) : ISearchCardScorer
{
    private readonly HashSet<string> _pinnedModelIds = new(pinnedModelIds, StringComparer.OrdinalIgnoreCase);

    public double Score(SearchCardScoringContext context)
    {
        double score = inner?.Score(context) ?? HeuristicScore(context);
        return _pinnedModelIds.Contains(context.CardModelId)
            ? score + scoreBoost
            : score;
    }

    private static double HeuristicScore(SearchCardScoringContext context)
    {
        return context.Features.TryGetValue("card.heuristicScore", out double score)
            ? (double)score
            : 0d;
    }
}
