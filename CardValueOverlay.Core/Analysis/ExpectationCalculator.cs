using CardValueOverlay.Core.Values;

namespace CardValueOverlay.Core.Analysis;

public static class ExpectationCalculator
{
    public static AverageExpectationResult CalculateAverage(
        IEnumerable<string> cardKeys,
        ValueResolver resolver,
        IReadOnlyDictionary<string, double?>? dynamicCardValues = null)
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

            requestedCount++;
            EffectiveValue<double> value = resolver.ResolveCardValue(key, dynamicCardValues);
            if (value.Value is double resolved)
            {
                valuedCount++;
                sum += resolved;
            }
            else
            {
                missingKeys.Add(key);
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
