using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Validation;

public sealed class MonsterMoveProfileValidator
{
    public IReadOnlyList<string> Validate(IReadOnlyList<MonsterMoveProfileEntry> entries)
    {
        List<string> errors = [];
        if (entries.Count == 0)
        {
            errors.Add("No monster move profiles were extracted.");
            return errors;
        }

        RequireMove(entries, "AxeRubyRaider", "SWING_1", "Expected AxeRubyRaider SWING_1 move.", errors);
        RequireEffect(entries, "AxeRubyRaider", "SWING_1", "attack", 5m, "Expected AxeRubyRaider SWING_1 to parse 5 attack.", errors);
        RequireEffect(entries, "AxeRubyRaider", "SWING_1", "block", 5m, "Expected AxeRubyRaider SWING_1 to parse 5 block.", errors);
        RequireEffect(entries, "AxeRubyRaider", "BIG_SWING", "attack", 12m, "Expected AxeRubyRaider BIG_SWING to parse 12 attack.", errors);
        RequireEffect(entries, "Axebot", "ONE_TWO_MOVE", "attack", 9m, "Expected Axebot ONE_TWO_MOVE to parse 9x2 attack.", errors);
        RequireHitCount(entries, "Axebot", "ONE_TWO_MOVE", 2m, "Expected Axebot ONE_TWO_MOVE to parse hit count 2.", errors);
        RequireEffect(entries, "Axebot", "BOOT_UP_MOVE", "block", 10m, "Expected Axebot BOOT_UP_MOVE to parse 10 block.", errors);
        RequireEffect(entries, "Axebot", "HAMMER_UPPERCUT_MOVE", "debuffWeak", 2m, "Expected Axebot HAMMER_UPPERCUT_MOVE to parse 2 Weak.", errors);
        RequireEffect(entries, "Axebot", "HAMMER_UPPERCUT_MOVE", "debuffFrail", 2m, "Expected Axebot HAMMER_UPPERCUT_MOVE to parse 2 Frail.", errors);

        return errors;
    }

    private static void RequireMove(
        IReadOnlyList<MonsterMoveProfileEntry> entries,
        string typeName,
        string stateId,
        string message,
        List<string> errors)
    {
        MonsterMoveProfileEntry? entry = entries.FirstOrDefault(item => item.TypeName == typeName);
        if (entry is null || !entry.Moves.Any(move => move.StateId == stateId))
        {
            errors.Add(message);
        }
    }

    private static void RequireEffect(
        IReadOnlyList<MonsterMoveProfileEntry> entries,
        string typeName,
        string stateId,
        string kind,
        decimal amount,
        string message,
        List<string> errors)
    {
        MonsterMoveStateEntry? move = FindMove(entries, typeName, stateId);
        if (move is null || !move.Effects.Any(effect => effect.Kind == kind && effect.Amount?.Value == amount))
        {
            errors.Add(message);
        }
    }

    private static void RequireHitCount(
        IReadOnlyList<MonsterMoveProfileEntry> entries,
        string typeName,
        string stateId,
        decimal hitCount,
        string message,
        List<string> errors)
    {
        MonsterMoveStateEntry? move = FindMove(entries, typeName, stateId);
        if (move is null || !move.Effects.Any(effect => effect.Kind == "attack" && effect.HitCount?.Value == hitCount))
        {
            errors.Add(message);
        }
    }

    private static MonsterMoveStateEntry? FindMove(
        IReadOnlyList<MonsterMoveProfileEntry> entries,
        string typeName,
        string stateId)
    {
        return entries.FirstOrDefault(item => item.TypeName == typeName)
            ?.Moves.FirstOrDefault(move => move.StateId == stateId);
    }
}
