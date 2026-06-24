namespace CardValueOverlay.Core.Configuration;

public sealed record ConfigValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
