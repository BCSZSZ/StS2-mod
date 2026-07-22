using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Combat;

public sealed class MonsterIntentCompiler
{
    public IReadOnlyDictionary<string, CombatMonsterDefinition> CompileFile(
        string path,
        MonsterOverrideCatalog overrides)
    {
        IReadOnlyList<MonsterMoveProfileEntry> profiles = JsonSerializer.Deserialize<List<MonsterMoveProfileEntry>>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"Monster profile file '{path}' is empty.");
        return Compile(profiles, overrides, CombatJson.Sha256File(path));
    }

    public IReadOnlyDictionary<string, CombatMonsterDefinition> Compile(
        IReadOnlyList<MonsterMoveProfileEntry> profiles,
        MonsterOverrideCatalog overrides,
        string sourceHash = "in-memory")
    {
        return profiles.ToDictionary(
            profile => profile.TypeName,
            profile => Compile(profile, overrides, sourceHash),
            StringComparer.Ordinal);
    }

    public CombatMonsterDefinition Compile(
        MonsterMoveProfileEntry profile,
        MonsterOverrideCatalog overrides,
        string sourceHash = "in-memory")
    {
        List<string> unsupported = [.. profile.Unresolved];
        int? minHp = ResolveA10(profile.HpRange?.Min);
        int? maxHp = ResolveA10(profile.HpRange?.Max);
        if (!minHp.HasValue || !maxHp.HasValue || minHp <= 0 || maxHp < minHp)
        {
            unsupported.Add("A10 HP range is unresolved.");
        }

        if (string.IsNullOrWhiteSpace(profile.InitialStateId))
        {
            unsupported.Add("Initial intent state is unresolved.");
        }

        Dictionary<string, MonsterIntentDefinition> intents = new(StringComparer.Ordinal);
        foreach (MonsterMoveStateEntry move in profile.Moves)
        {
            List<MonsterIntentEffect> effects = [];
            for (int index = 0; index < move.Effects.Count; index++)
            {
                MonsterMoveEffectTerm term = move.Effects[index];
                int? amount = ResolveA10(term.Amount);
                int? hitCount = term.HitCount is null ? 1 : ResolveA10(term.HitCount);
                MonsterIntentEffectKind? kind = ParseKind(term.Kind);
                if (!amount.HasValue || !hitCount.HasValue || hitCount <= 0 || !kind.HasValue)
                {
                    unsupported.Add($"{move.StateId}: unsupported or unresolved effect {term.Kind}/{term.Amount?.Expression ?? "<null>"}.");
                    continue;
                }

                effects.Add(new MonsterIntentEffect(
                    kind.Value,
                    amount.Value,
                    hitCount.Value,
                    term.Target ?? string.Empty,
                    term.Source,
                    index));
            }

            IReadOnlyList<MonsterIntentTransition> transitions;
            if (move.FollowUpStateIds.Count == 1)
            {
                transitions = [new MonsterIntentTransition(move.FollowUpStateIds[0], 1d, "parsed deterministic FollowUpState")];
            }
            else if (overrides.TryGetTransitions(profile.TypeName, move.StateId, out transitions, out string? overrideError))
            {
                if (overrideError is not null)
                {
                    unsupported.Add(overrideError);
                }
            }
            else
            {
                unsupported.Add(move.FollowUpStateIds.Count == 0
                    ? $"{move.StateId}: no follow-up transition was parsed."
                    : $"{move.StateId}: {move.FollowUpStateIds.Count} follow-ups lack sourced probabilities.");
                transitions = [];
            }

            intents[move.StateId] = new MonsterIntentDefinition(move.StateId, effects, transitions);
        }

        foreach (MonsterIntentDefinition intent in intents.Values)
        {
            foreach (MonsterIntentTransition transition in intent.Transitions)
            {
                if (!intents.ContainsKey(transition.StateId))
                {
                    unsupported.Add($"{intent.StateId}: transition target '{transition.StateId}' is missing.");
                }
            }
        }

        return new CombatMonsterDefinition(
            profile.ModelId,
            profile.TypeName,
            minHp ?? 0,
            maxHp ?? 0,
            profile.InitialStateId ?? string.Empty,
            intents,
            unsupported.Count == 0,
            unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            sourceHash);
    }

    private static int? ResolveA10(MonsterMoveNumeric? numeric)
    {
        decimal? value = numeric?.AscensionValue ?? numeric?.Value;
        return value.HasValue && decimal.Truncate(value.Value) == value.Value && value.Value <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static MonsterIntentEffectKind? ParseKind(string kind) => kind switch
    {
        "attack" => MonsterIntentEffectKind.Attack,
        "block" => MonsterIntentEffectKind.Block,
        "debuffWeak" => MonsterIntentEffectKind.ApplyWeak,
        "debuffVulnerable" => MonsterIntentEffectKind.ApplyVulnerable,
        "debuffFrail" => MonsterIntentEffectKind.ApplyFrail,
        "buffStrength" => MonsterIntentEffectKind.GainStrength,
        "heal" => MonsterIntentEffectKind.HealSelf,
        _ => null
    };
}
