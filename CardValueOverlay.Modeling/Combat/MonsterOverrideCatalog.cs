using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat;

public sealed record MonsterMoveOverrideFile(
    int SchemaVersion,
    IReadOnlyDictionary<string, MonsterMoveOverride> Monsters,
    IReadOnlyList<string> Notes);

public sealed record MonsterMoveOverride(
    string Source,
    IReadOnlyDictionary<string, MonsterStateOverride> States);

public sealed record MonsterStateOverride(
    IReadOnlyList<MonsterTransitionOverride> FollowUps);

public sealed record MonsterTransitionOverride(
    string StateId,
    double Probability,
    string Source);

public sealed class MonsterOverrideCatalog
{
    private readonly MonsterMoveOverrideFile _file;

    private MonsterOverrideCatalog(MonsterMoveOverrideFile file, string contentHash)
    {
        _file = file;
        ContentHash = contentHash;
        if (file.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Monster move override schemaVersion must be 1.");
        }
    }

    public string ContentHash { get; }

    public static MonsterOverrideCatalog Load(string path)
    {
        MonsterMoveOverrideFile file = JsonSerializer.Deserialize<MonsterMoveOverrideFile>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"Monster override file '{path}' is empty.");
        return new MonsterOverrideCatalog(file, CombatJson.Sha256File(path));
    }

    public bool TryGetTransitions(
        string monsterTypeName,
        string stateId,
        out IReadOnlyList<MonsterIntentTransition> transitions,
        out string? error)
    {
        transitions = [];
        error = null;
        if (!_file.Monsters.TryGetValue(monsterTypeName, out MonsterMoveOverride? monster) ||
            !monster.States.TryGetValue(stateId, out MonsterStateOverride? state))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(monster.Source) || state.FollowUps.Count == 0 ||
            state.FollowUps.Any(item => string.IsNullOrWhiteSpace(item.Source) || item.Probability <= 0))
        {
            error = $"Override {monsterTypeName}/{stateId} lacks sourced positive transition probabilities.";
            return false;
        }

        double sum = state.FollowUps.Sum(item => item.Probability);
        if (Math.Abs(sum - 1d) > 1e-9)
        {
            error = $"Override {monsterTypeName}/{stateId} transition probabilities sum to {sum:R}, not 1.";
            return false;
        }

        transitions = state.FollowUps
            .Select(item => new MonsterIntentTransition(item.StateId, item.Probability, item.Source))
            .OrderBy(item => item.StateId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }
}
