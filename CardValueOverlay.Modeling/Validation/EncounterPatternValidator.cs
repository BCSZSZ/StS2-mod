using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Validation;

public sealed class EncounterPatternValidator
{
    public IReadOnlyList<string> Validate(IReadOnlyList<EncounterPatternEntry> entries)
    {
        List<string> errors = [];
        if (entries.Count == 0)
        {
            errors.Add("No encounter patterns were extracted.");
            return errors;
        }

        RequireEncounter(entries, "ChompersNormal", "Normal", 2, errors);
        RequireMonsterCount(entries, "ChompersNormal", "Chomper", 2, errors);
        RequireEncounter(entries, "TheKinBoss", "Boss", 3, errors);
        RequireMonsterCount(entries, "TheKinBoss", "KinFollower", 2, errors);
        RequireMonsterCount(entries, "TheKinBoss", "KinPriest", 1, errors);
        RequireEncounter(entries, "BowlbugsNormal", "Normal", 3, errors);
        RequireEncounter(entries, "RubyRaidersNormal", "Normal", 3, errors);
        RequireEncounter(entries, "BowlbugsWeak", "Weak", 2, errors);
        RequirePossibleMonster(entries, "BowlbugsWeak", "BowlbugEgg", errors);
        RequirePossibleMonster(entries, "BowlbugsWeak", "BowlbugNectar", errors);
        RequirePossibleMonster(entries, "BowlbugsWeak", "BowlbugRock", errors);

        return errors;
    }

    private static void RequireEncounter(
        IReadOnlyList<EncounterPatternEntry> entries,
        string typeName,
        string category,
        int monsterCount,
        List<string> errors)
    {
        EncounterPatternEntry? entry = entries.FirstOrDefault(item => item.TypeName == typeName);
        if (entry is null)
        {
            errors.Add($"Expected encounter pattern for {typeName}.");
            return;
        }

        if (entry.Category != category)
        {
            errors.Add($"Expected {typeName} category {category}, got {entry.Category}.");
        }

        if (entry.FixedMonsterCount != monsterCount)
        {
            errors.Add($"Expected {typeName} monster count {monsterCount}, got {entry.FixedMonsterCount}.");
        }
    }

    private static void RequireMonsterCount(
        IReadOnlyList<EncounterPatternEntry> entries,
        string encounterTypeName,
        string monsterTypeName,
        int count,
        List<string> errors)
    {
        EncounterPatternEntry? entry = entries.FirstOrDefault(item => item.TypeName == encounterTypeName);
        int actual = entry?.MonsterSlots.Count(slot => slot.MonsterTypeName == monsterTypeName) ?? 0;
        if (actual != count)
        {
            errors.Add($"Expected {encounterTypeName} to include {count} {monsterTypeName}, got {actual}.");
        }
    }

    private static void RequirePossibleMonster(
        IReadOnlyList<EncounterPatternEntry> entries,
        string encounterTypeName,
        string monsterTypeName,
        List<string> errors)
    {
        EncounterPatternEntry? entry = entries.FirstOrDefault(item => item.TypeName == encounterTypeName);
        if (entry is null || !entry.PossibleMonsterTypeNames.Contains(monsterTypeName))
        {
            errors.Add($"Expected {encounterTypeName} possible monsters to include {monsterTypeName}.");
        }
    }
}
