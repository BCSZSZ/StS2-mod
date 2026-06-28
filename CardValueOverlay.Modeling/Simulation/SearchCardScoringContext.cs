namespace CardValueOverlay.Modeling.Simulation;

public sealed record SearchCardScoringContext(
    string CardModelId,
    string CardTypeName,
    IReadOnlyDictionary<string, double> Features);

public interface ISearchCardScorer
{
    decimal Score(SearchCardScoringContext context);
}
