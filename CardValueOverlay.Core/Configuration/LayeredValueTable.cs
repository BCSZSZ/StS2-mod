namespace CardValueOverlay.Core.Configuration;

public sealed class LayeredValueTable : Dictionary<int, double?>
{
    public LayeredValueTable()
    {
    }

    public LayeredValueTable(IDictionary<int, double?> values)
        : base(values)
    {
    }

    public double? Resolve(int layer)
    {
        if (Count == 0)
        {
            return null;
        }

        int normalizedLayer = Math.Max(1, layer);
        int? bestLayer = null;

        foreach (int candidateLayer in Keys)
        {
            if (candidateLayer <= normalizedLayer
                && (!bestLayer.HasValue || candidateLayer > bestLayer.Value))
            {
                bestLayer = candidateLayer;
            }
        }

        return bestLayer.HasValue ? this[bestLayer.Value] : null;
    }

    public bool HasAnyValue => Values.Any(value => value.HasValue);

    public bool HasBaseLayer => ContainsKey(1);
}
