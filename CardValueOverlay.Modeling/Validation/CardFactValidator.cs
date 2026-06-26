using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Validation;

public sealed class CardFactValidator
{
    public IReadOnlyList<string> Validate(IReadOnlyList<CardFactCatalogEntry> entries)
    {
        List<string> errors = [];

        if (entries.Count == 0)
        {
            errors.Add("No card fact entries were extracted.");
            return errors;
        }

        RequireAction(entries, "StrikeIronclad", "damage", 6m, "Expected StrikeIronclad to parse 6 damage.", errors);
        RequireAction(entries, "DefendIronclad", "block", 5m, "Expected DefendIronclad to parse 5 block.", errors);
        RequireAction(entries, "PerfectedStrike", "damage", 6m, "Expected PerfectedStrike to parse 6 base damage.", errors);
        RequireAction(entries, "PerfectedStrike", "scalingDamagePerCardTag", 2m, "Expected PerfectedStrike to parse Strike-count scaling damage.", errors);
        RequireAction(entries, "Adrenaline", "draw", 2m, "Expected Adrenaline to parse 2 draw.", errors);
        RequireAction(entries, "Adrenaline", "energyGain", 1m, "Expected Adrenaline to parse 1 energy gain.", errors);
        RequireAction(entries, "Bash", "debuffVulnerable", 2m, "Expected Bash to parse 2 Vulnerable.", errors);
        RequireAction(entries, "Neutralize", "debuffWeak", 1m, "Expected Neutralize to parse 1 Weak.", errors);
        RequireKeyword(entries, "Adrenaline", "Exhaust", "Expected Adrenaline to parse Exhaust keyword.", errors);

        return errors;
    }

    private static void RequireAction(
        IReadOnlyList<CardFactCatalogEntry> entries,
        string typeName,
        string kind,
        decimal amount,
        string message,
        List<string> errors)
    {
        CardFactCatalogEntry? entry = entries.FirstOrDefault(item => item.TypeName == typeName);
        if (entry is null || !entry.Actions.Any(action => action.Kind == kind && action.Amount == amount))
        {
            errors.Add(message);
        }
    }

    private static void RequireKeyword(
        IReadOnlyList<CardFactCatalogEntry> entries,
        string typeName,
        string keyword,
        string message,
        List<string> errors)
    {
        CardFactCatalogEntry? entry = entries.FirstOrDefault(item => item.TypeName == typeName);
        if (entry is null || !entry.Keywords.Contains(keyword, StringComparer.Ordinal))
        {
            errors.Add(message);
        }
    }
}
