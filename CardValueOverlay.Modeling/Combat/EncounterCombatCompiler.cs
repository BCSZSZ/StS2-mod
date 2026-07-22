using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Combat;

public sealed class EncounterCombatCompiler
{
    public IReadOnlyList<EncounterCombatDefinition> CompileFile(
        string path,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        EncounterOverrideCatalog overrides)
    {
        IReadOnlyList<EncounterPatternEntry> patterns = JsonSerializer.Deserialize<List<EncounterPatternEntry>>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"Encounter pattern file '{path}' is empty.");
        return Compile(patterns, monsters, overrides);
    }

    public IReadOnlyList<EncounterCombatDefinition> Compile(
        IReadOnlyList<EncounterPatternEntry> patterns,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        EncounterOverrideCatalog overrides)
    {
        List<EncounterCombatDefinition> result = [];
        foreach (EncounterPatternEntry pattern in patterns.OrderBy(pattern => pattern.ModelId, StringComparer.Ordinal))
        {
            foreach (int act in pattern.Acts.Select(reference => reference.ActNumber).Distinct().Order())
            {
                string stableEncounterId = $"{pattern.ModelId}:act{act}";
                if (overrides.TryGet(stableEncounterId, out EncounterOverrideEntry? entry))
                {
                    foreach (EncounterRealizationOverride realization in entry.Realizations.OrderBy(item => item.Id, StringComparer.Ordinal))
                    {
                        result.Add(CompileRealization(pattern, act, monsters, realization, entry.Source));
                    }
                    continue;
                }

                result.Add(CompileDefault(pattern, act, monsters));
            }
        }

        return result;
    }

    private static EncounterCombatDefinition CompileDefault(
        EncounterPatternEntry pattern,
        int act,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters)
    {
        List<string> unsupported = [];
        List<EncounterMonsterDefinition> compiledSlots = [];
        foreach (EncounterMonsterSlot slot in pattern.MonsterSlots.OrderBy(slot => slot.Position))
        {
            if (slot.PossibleMonsterTypeNames.Count != 1)
            {
                unsupported.Add($"Slot {slot.Position} has conditional monster selection without a sourced realization override.");
                continue;
            }
            CompileSlot(slot, slot.PossibleMonsterTypeNames[0], monsters, compiledSlots, unsupported);
        }
        return Build(pattern, act, compiledSlots, unsupported, "default", 1d);
    }

    private static EncounterCombatDefinition CompileRealization(
        EncounterPatternEntry pattern,
        int act,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        EncounterRealizationOverride realization,
        string source)
    {
        List<string> unsupported = [];
        List<EncounterMonsterDefinition> compiledSlots = [];
        foreach (EncounterMonsterSlot slot in pattern.MonsterSlots.OrderBy(slot => slot.Position))
        {
            string? typeName = realization.MonstersByPosition.GetValueOrDefault(slot.Position);
            if (typeName is null && slot.PossibleMonsterTypeNames.Count == 1)
            {
                typeName = slot.PossibleMonsterTypeNames[0];
            }
            if (typeName is null || !slot.PossibleMonsterTypeNames.Contains(typeName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Encounter override '{pattern.ModelId}:act{act}/{realization.Id}' has no allowed monster for slot {slot.Position} ({source}).");
            }
            CompileSlot(slot, typeName, monsters, compiledSlots, unsupported);
        }
        return Build(pattern, act, compiledSlots, unsupported, realization.Id, realization.Probability);
    }

    private static void CompileSlot(
        EncounterMonsterSlot slot,
        string typeName,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        List<EncounterMonsterDefinition> compiledSlots,
        List<string> unsupported)
    {
        if (!monsters.TryGetValue(typeName, out CombatMonsterDefinition? monster))
        {
            unsupported.Add($"Monster '{typeName}' has no move profile.");
            return;
        }
        if (!monster.IsSupported)
        {
            unsupported.Add($"Monster '{typeName}' is unsupported: {string.Join("; ", monster.UnsupportedReasons)}");
        }
        compiledSlots.Add(new EncounterMonsterDefinition(slot.Position, typeName, monster));
    }

    private static EncounterCombatDefinition Build(
        EncounterPatternEntry pattern,
        int act,
        IReadOnlyList<EncounterMonsterDefinition> compiledSlots,
        List<string> unsupported,
        string realizationId,
        double realizationProbability)
    {
        if (compiledSlots.Count != pattern.MonsterSlots.Count)
        {
            unsupported.Add("Not every encounter slot compiled to a concrete monster.");
        }
        return new EncounterCombatDefinition(
            pattern.ModelId,
            pattern.TypeName,
            act,
            pattern.Category.ToLowerInvariant(),
            compiledSlots,
            unsupported.Count == 0,
            unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            realizationId,
            realizationProbability);
    }
}
