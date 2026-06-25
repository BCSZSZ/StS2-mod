using System.Globalization;
using System.Text.Json;

namespace CardValueOverlay.Modeling.Estimation;

public sealed class ValueCalibration
{
    public int SchemaVersion { get; init; } = 1;

    public int[] LayerBreakpoints { get; init; } = [];

    public Dictionary<string, decimal> BaselineCardValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> BlockToDamage { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> ExpectedCombatTurns { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> EnergyDrawExchange { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> TargetingPenalties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> DamageUnitValue { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> ResourceValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> PowerValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> KeywordValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> ScalingAssumptions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static ValueCalibration Load(string path)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<ValueCalibration>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"Failed to load value calibration from {path}");
    }

    public decimal GetLayeredValue(IReadOnlyDictionary<string, decimal> values, int layer, string label)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException($"Calibration table '{label}' is empty.");
        }

        List<(int Layer, decimal Value)> points = values
            .Select(pair => (Layer: ParseLayer(pair.Key, label), pair.Value))
            .OrderBy(pair => pair.Layer)
            .ToList();

        if (layer <= points[0].Layer)
        {
            return points[0].Value;
        }

        if (layer >= points[^1].Layer)
        {
            return points[^1].Value;
        }

        for (int i = 1; i < points.Count; i++)
        {
            (int rightLayer, decimal rightValue) = points[i];
            (int leftLayer, decimal leftValue) = points[i - 1];
            if (layer > rightLayer)
            {
                continue;
            }

            decimal ratio = (decimal)(layer - leftLayer) / (rightLayer - leftLayer);
            return leftValue + ((rightValue - leftValue) * ratio);
        }

        return points[^1].Value;
    }

    public decimal GetNamedValue(IReadOnlyDictionary<string, decimal> values, string key, decimal fallback)
    {
        return values.TryGetValue(key, out decimal value) ? value : fallback;
    }

    private static int ParseLayer(string key, string label)
    {
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int layer))
        {
            return layer;
        }

        throw new InvalidOperationException($"Calibration table '{label}' has non-integer layer key '{key}'.");
    }
}
