using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Validation;

public sealed class CardEffectTermValidator
{
    public IReadOnlyList<string> Validate(IReadOnlyList<CardEffectTermCatalogEntry> entries)
    {
        List<string> errors = [];

        if (entries.Count == 0)
        {
            errors.Add("No card effect entries were extracted.");
            return errors;
        }

        RequireTerm(entries, "StrikeIronclad", "damage", 6m, "Expected StrikeIronclad to parse 6 damage.", errors);
        RequireTerm(entries, "DefendIronclad", "block", 5m, "Expected DefendIronclad to parse 5 block.", errors);
        RequireTerm(entries, "PerfectedStrike", "damage", 6m, "Expected PerfectedStrike to parse 6 base damage.", errors);
        RequireTerm(entries, "PerfectedStrike", "scalingDamagePerCardTag", 2m, "Expected PerfectedStrike to parse Strike-count scaling damage.", errors);

        return errors;
    }

    private static void RequireTerm(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        string typeName,
        string kind,
        decimal amount,
        string message,
        List<string> errors)
    {
        CardEffectTermCatalogEntry? entry = entries.FirstOrDefault(item => item.TypeName == typeName);
        if (entry is null || !entry.Terms.Any(term => term.Kind == kind && term.Amount == amount))
        {
            errors.Add(message);
        }
    }
}
