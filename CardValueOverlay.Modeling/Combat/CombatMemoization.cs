namespace CardValueOverlay.Modeling.Combat;

public sealed class CombatMemoization
{
    private readonly Dictionary<CombatStateKey, double> _values = [];

    public int Count => _values.Count;

    public bool TryGet(CombatStateKey key, out double value) => _values.TryGetValue(key, out value);

    public void Set(CombatStateKey key, double value) => _values[key] = value;

    public void Clear() => _values.Clear();
}
