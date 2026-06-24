namespace CardValueOverlay.Core.Values;

public readonly record struct EffectiveValue<T>(T? Manual, T? Dynamic)
    where T : struct
{
    public T? Value => Dynamic ?? Manual;

    public ValueSource Source => Dynamic.HasValue
        ? ValueSource.Dynamic
        : Manual.HasValue
            ? ValueSource.Manual
            : ValueSource.None;

    public bool HasValue => Value.HasValue;
}
