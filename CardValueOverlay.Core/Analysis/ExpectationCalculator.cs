using CardValueOverlay.Core.Values;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Analysis;

public static class ExpectationCalculator
{
    public static AverageExpectationResult CalculateAverage(
        IEnumerable<string> cardKeys,
        ValueResolver resolver,
        int layer = 1,
        IReadOnlyDictionary<string, LayeredValueTable>? dynamicCardValues = null)
    {
        return Calculate(
            cardKeys,
            (request) => resolver.ResolveCardValue(request.CardKey, request.UpgradeState, layer, dynamicCardValues));
    }

    public static AverageExpectationResult CalculateSmithAverage(
        IEnumerable<string> cardKeys,
        ValueResolver resolver,
        int layer = 1,
        IReadOnlyDictionary<string, LayeredValueTable>? dynamicSmithValues = null)
    {
        return Calculate(
            cardKeys,
            (request) => resolver.ResolveSmithValue(request.CardKey, request.UpgradeState, layer, dynamicSmithValues));
    }

    private static AverageExpectationResult Calculate(
        IEnumerable<string> cardKeys,
        Func<CardValueRequest, EffectiveValue<double>> resolve)
    {
        int requestedCount = 0;
        int valuedCount = 0;
        double sum = 0;
        List<string> missingKeys = [];

        foreach (string rawKey in cardKeys)
        {
            string key = rawKey.Trim();
            if (key.Length == 0)
            {
                continue;
            }

            CardValueRequest request = CardValueRequest.Parse(key);
            requestedCount++;
            EffectiveValue<double> value = resolve(request);
            if (value.Value is double resolved)
            {
                valuedCount++;
                sum += resolved;
            }
            else
            {
                missingKeys.Add(request.DisplayKey);
            }
        }

        return new AverageExpectationResult(
            requestedCount,
            valuedCount,
            missingKeys.Count,
            valuedCount == 0 ? null : sum / valuedCount,
            missingKeys);
    }
}
