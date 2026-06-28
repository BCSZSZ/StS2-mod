namespace CardValueOverlay.Core.Values;

public readonly record struct EffectiveValue<T>(T? Training, T? Dynamic)
    where T : struct
{
    public T? Value => Dynamic ?? Training;

    public ValueSource Source => Dynamic.HasValue
        ? ValueSource.Dynamic
        : Training.HasValue
            ? ValueSource.Training
            : ValueSource.None;

    public bool HasValue => Value.HasValue;
}
