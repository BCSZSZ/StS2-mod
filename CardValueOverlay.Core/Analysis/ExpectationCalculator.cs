using CardValueOverlay.Core.Values;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Analysis;

public static class ExpectationCalculator
{
    public static AverageExpectationResult CalculateAverage(
        IEnumerable<string> cardKeys,
        ValueResolver resolver,
        TrainingValueHorizon horizon = TrainingValueHorizon.Midline,
        IReadOnlyDictionary<string, TrainingHorizonValues>? dynamicCardValues = null)
    {
        return Calculate(
            cardKeys,
            (request) => resolver.ResolveCardValue(request.CardKey, request.UpgradeState, horizon, dynamicCardValues));
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
