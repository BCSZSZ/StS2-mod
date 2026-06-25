using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Validation;

public sealed class GeneratedDataValidator
{
    public IReadOnlyList<string> Validate(ExtractionRunResult result)
    {
        List<string> errors = [];

        RequireAny(result.Cards, "No card catalog entries were extracted.", errors);
        RequireAny(result.Enemies, "No enemy catalog entries were extracted.", errors);
        RequireAny(result.Encounters, "No encounter catalog entries were extracted.", errors);
        RequireAny(result.Intents, "No intent catalog entries were extracted.", errors);

        RequireContains(result.Cards, "StrikeIronclad", "Expected StrikeIronclad in card catalog.", errors);
        RequireContains(result.Cards, "DefendIronclad", "Expected DefendIronclad in card catalog.", errors);
        RequireContains(result.Cards, "PerfectedStrike", "Expected PerfectedStrike in card catalog.", errors);
        RequireContains(result.Enemies, "Chomper", "Expected Chomper in enemy catalog.", errors);
        RequireContains(result.Encounters, "ChompersNormal", "Expected ChompersNormal in encounter catalog.", errors);
        RequireUnique(result.Cards, entry => entry.ModelId, "Duplicate card model id", errors);
        RequireUnique(result.Enemies, entry => entry.ModelId, "Duplicate enemy model id", errors);
        RequireUnique(result.Encounters, entry => entry.ModelId, "Duplicate encounter model id", errors);

        return errors;
    }

    private static void RequireAny<T>(IReadOnlyCollection<T> values, string message, List<string> errors)
    {
        if (values.Count == 0)
        {
            errors.Add(message);
        }
    }

    private static void RequireContains(
        IReadOnlyList<ModelCatalogEntry> values,
        string typeName,
        string message,
        List<string> errors)
    {
        if (!values.Any(entry => string.Equals(entry.TypeName, typeName, StringComparison.Ordinal)))
        {
            errors.Add(message);
        }
    }

    private static void RequireUnique<T>(
        IReadOnlyList<T> values,
        Func<T, string> keySelector,
        string message,
        List<string> errors)
    {
        foreach (IGrouping<string, T> group in values.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                errors.Add($"{message}: {group.Key}");
            }
        }
    }
}
