using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed class CombatBaselineCache
{
    private readonly string _root;

    public CombatBaselineCache(string root = "data/generated/combat_aware/baselines")
    {
        _root = root;
    }

    public string BuildKey(
        CompiledCombatDeck deck,
        CombatSample sample,
        int horizon,
        CombatSimulationOptions options,
        string hpHash,
        string combatModelHash)
    {
        string semantic = string.Join('|',
            deck.StableKey,
            sample.SampleId,
            sample.RunKey,
            sample.EncounterId,
            sample.PlayerHp,
            sample.PlayerMaxHp,
            horizon,
            options.SemanticsVersion,
            options.Ascension,
            options.HandSize,
            options.MaxHandSize,
            options.EnergyPerTurn,
            options.InitialStars,
            options.SolveMode,
            options.MaximumCanonicalStates,
            options.MaximumChanceBranches,
            options.ExactOutcomeLimit,
            options.MaximumDecisionDepth,
            options.SparseChanceSamples,
            options.EnableMemoization,
            hpHash,
            combatModelHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semantic))).ToLowerInvariant();
    }

    public bool TryRead(string key, out CombatContextResult result)
    {
        string path = Path.Combine(_root, $"{key}.json");
        if (!File.Exists(path))
        {
            result = null!;
            return false;
        }

        result = JsonSerializer.Deserialize<CombatContextResult>(File.ReadAllText(path), CombatJson.Options)
            ?? throw new InvalidOperationException($"Combat baseline cache '{path}' is empty.");
        return true;
    }

    public void Write(string key, CombatContextResult result)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, $"{key}.json");
        string stagingPath = path + $".{Environment.ProcessId}.tmp";
        File.WriteAllText(stagingPath, JsonSerializer.Serialize(result, CombatJson.Options));
        File.Move(stagingPath, path, overwrite: true);
    }
}
