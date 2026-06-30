namespace CardValueOverlay.Core.Configuration;

public static class CardValueGenerationMethods
{
    public const string MonteCarlo = "monteCarlo";

    public const string Estimate = "estimate";

    public static bool IsKnown(string? method)
    {
        return string.Equals(method, MonteCarlo, StringComparison.Ordinal)
            || string.Equals(method, Estimate, StringComparison.Ordinal);
    }
}
