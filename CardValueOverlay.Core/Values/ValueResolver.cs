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
        CardUpgradeState upgradeState,
        TrainingValueHorizon horizon,
        IReadOnlyDictionary<string, TrainingHorizonValues>? dynamicValues = null)
    {
        double? trainingValue = _cards.TryGetValue(cardKey, out CardValueEntry? entry)
            ? entry.ResolveTrainingValue(upgradeState, horizon)
            : null;
        double? dynamicValue = TryGetDynamic(dynamicValues, horizon, BuildCardDynamicKeys(cardKey, upgradeState));
        return new EffectiveValue<double>(trainingValue, dynamicValue);
    }

    public EffectiveValue<double> ResolveCommonParameter(
        string parameterKey,
        int layer,
        IReadOnlyDictionary<string, LayeredValueTable>? dynamicValues = null)
    {
        double? fixedLayerValue = _commonParameters.TryGetValue(parameterKey, out CommonParameterEntry? entry)
            ? entry.ResolveFixedLayerValue(layer)
            : null;
        double? dynamicValue = TryGetLayeredDynamic(dynamicValues, layer, parameterKey);
        return new EffectiveValue<double>(fixedLayerValue, dynamicValue);
    }

    private static double? TryGetDynamic(
        IReadOnlyDictionary<string, TrainingHorizonValues>? dynamicValues,
        TrainingValueHorizon horizon,
        params string[] keys)
    {
        if (dynamicValues is null)
        {
            return null;
        }

        foreach (string key in keys)
        {
            if (dynamicValues.TryGetValue(key, out TrainingHorizonValues? directValue))
            {
                return directValue.Resolve(horizon);
            }

            foreach ((string candidateKey, TrainingHorizonValues candidateValue) in dynamicValues)
            {
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateValue.Resolve(horizon);
                }
            }
        }

        return null;
    }

    private static double? TryGetLayeredDynamic(
        IReadOnlyDictionary<string, LayeredValueTable>? dynamicValues,
        int layer,
        params string[] keys)
    {
        if (dynamicValues is null)
        {
            return null;
        }

        foreach (string key in keys)
        {
            if (dynamicValues.TryGetValue(key, out LayeredValueTable? directValue))
            {
                return directValue.Resolve(layer);
            }

            foreach ((string candidateKey, LayeredValueTable candidateValue) in dynamicValues)
            {
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateValue.Resolve(layer);
                }
            }
        }

        return null;
    }

    private static string[] BuildCardDynamicKeys(string cardKey, CardUpgradeState upgradeState)
    {
        string stateKey = CardValueRequest.StateKey(upgradeState);
        if (upgradeState == CardUpgradeState.Upgraded)
        {
            return
            [
                $"card:{cardKey}:{stateKey}",
                $"{cardKey}:{stateKey}",
                $"{cardKey}+",
                cardKey
            ];
        }

        return
        [
            $"card:{cardKey}:{stateKey}",
            $"{cardKey}:{stateKey}",
            cardKey
        ];
    }

}
