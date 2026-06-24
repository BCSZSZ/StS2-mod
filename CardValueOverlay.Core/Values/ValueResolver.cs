using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Values;

public sealed class ValueResolver
{
    private readonly Dictionary<string, CardValueEntry> _cards;
    private readonly Dictionary<string, CommonParameterEntry> _commonParameters;

    public ValueResolver(CardValueConfig config)
    {
        _cards = new Dictionary<string, CardValueEntry>(config.Cards, StringComparer.OrdinalIgnoreCase);
        _commonParameters = new Dictionary<string, CommonParameterEntry>(config.CommonParameters, StringComparer.OrdinalIgnoreCase);
    }

    public EffectiveValue<double> ResolveCardValue(
        string cardKey,
        IReadOnlyDictionary<string, double?>? dynamicValues = null)
    {
        double? manual = _cards.TryGetValue(cardKey, out CardValueEntry? entry) ? entry.ManualValue : null;
        double? dynamicValue = TryGetDynamic(dynamicValues, cardKey);
        return new EffectiveValue<double>(manual, dynamicValue);
    }

    public EffectiveValue<double> ResolveCommonParameter(
        string parameterKey,
        IReadOnlyDictionary<string, double?>? dynamicValues = null)
    {
        double? fixedValue = _commonParameters.TryGetValue(parameterKey, out CommonParameterEntry? entry)
            ? entry.FixedValue
            : null;
        double? dynamicValue = TryGetDynamic(dynamicValues, parameterKey);
        return new EffectiveValue<double>(fixedValue, dynamicValue);
    }

    private static double? TryGetDynamic(IReadOnlyDictionary<string, double?>? dynamicValues, string key)
    {
        if (dynamicValues is null)
        {
            return null;
        }

        if (dynamicValues.TryGetValue(key, out double? directValue))
        {
            return directValue;
        }

        foreach ((string candidateKey, double? candidateValue) in dynamicValues)
        {
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return candidateValue;
            }
        }

        return null;
    }
}
