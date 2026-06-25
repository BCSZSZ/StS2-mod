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
        RequireTerm(entries, "Adrenaline", "draw", 2m, "Expected Adrenaline to parse 2 draw.", errors);
        RequireTerm(entries, "Adrenaline", "energyGain", 1m, "Expected Adrenaline to parse 1 energy gain.", errors);
        RequireTerm(entries, "Bash", "debuffVulnerable", 2m, "Expected Bash to parse 2 Vulnerable.", errors);
        RequireTerm(entries, "Neutralize", "debuffWeak", 1m, "Expected Neutralize to parse 1 Weak.", errors);
        RequireParameter(entries, "Adrenaline", "keyword", "Exhaust", "Expected Adrenaline to parse Exhaust keyword.", errors);

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

    private static void RequireParameter(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        string typeName,
        string kind,
        string parameter,
        string message,
        List<string> errors)
    {
        CardEffectTermCatalogEntry? entry = entries.FirstOrDefault(item => item.TypeName == typeName);
        if (entry is null || !entry.Terms.Any(term => term.Kind == kind && term.Parameter == parameter))
        {
            errors.Add(message);
        }
    }
}
