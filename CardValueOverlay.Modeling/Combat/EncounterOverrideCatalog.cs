using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat;

public sealed record EncounterRealizationOverride(
    string Id,
    double Probability,
    IReadOnlyDictionary<int, string> MonstersByPosition);

public sealed record EncounterOverrideEntry(
    IReadOnlyList<EncounterRealizationOverride> Realizations,
    string Source,
    string Reason,
    double Confidence);

public sealed record EncounterOverrideFile(
    int SchemaVersion,
    IReadOnlyDictionary<string, EncounterOverrideEntry> Encounters,
    IReadOnlyList<string> Notes);

public sealed class EncounterOverrideCatalog
{
    private readonly IReadOnlyDictionary<string, EncounterOverrideEntry> _entries;

    private EncounterOverrideCatalog(IReadOnlyDictionary<string, EncounterOverrideEntry> entries)
    {
        _entries = entries;
    }

    public static EncounterOverrideCatalog Load(string path)
    {
        EncounterOverrideFile file = JsonSerializer.Deserialize<EncounterOverrideFile>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"Encounter override file '{path}' is empty.");
        return Create(file);
    }

    public static EncounterOverrideCatalog Create(EncounterOverrideFile file)
    {
        if (file.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Encounter override schemaVersion must be 1.");
        }

        foreach ((string stableEncounterId, EncounterOverrideEntry entry) in file.Encounters)
        {
            if (string.IsNullOrWhiteSpace(stableEncounterId) || entry.Realizations.Count == 0 ||
                string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Reason) ||
                entry.Confidence <= 0 || entry.Confidence > 1)
            {
                throw new InvalidOperationException($"Encounter override '{stableEncounterId}' lacks sourced review metadata or realizations.");
            }

            if (entry.Realizations.Select(realization => realization.Id).Distinct(StringComparer.Ordinal).Count() != entry.Realizations.Count ||
                entry.Realizations.Any(realization => string.IsNullOrWhiteSpace(realization.Id) ||
                    realization.Probability <= 0 || realization.MonstersByPosition.Count == 0))
            {
                throw new InvalidOperationException($"Encounter override '{stableEncounterId}' has an invalid realization.");
            }

            double mass = entry.Realizations.Sum(realization => realization.Probability);
            if (Math.Abs(mass - 1d) > 1e-9)
            {
                throw new InvalidOperationException($"Encounter override '{stableEncounterId}' probabilities sum to {mass:R}, not 1.");
            }
        }

        return new EncounterOverrideCatalog(file.Encounters);
    }

    public bool TryGet(string stableEncounterId, out EncounterOverrideEntry entry) =>
        _entries.TryGetValue(stableEncounterId, out entry!);
}
