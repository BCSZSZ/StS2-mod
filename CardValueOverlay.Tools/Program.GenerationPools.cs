using System.Text.Json;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    // One managed generated-card pool: its id, the "subject" every candidate card must satisfy
    // (current-hero/Regent/Colorless plus any type/cost filter), and an optional self-exclusion.
    private sealed record GenerationPoolSpec(
        string PoolId,
        Func<SimulationCard, bool> Subject,
        string? ExcludeTypeName,
        string Description);

    // A same-subject card that is NOT placed in the active pool, with the reason(s). Record-only.
    private sealed record UnsupportedPoolEntry(string TypeName, string ModelId, IReadOnlyList<string> Reasons);

    // Cards that override CardModel.CanBeGeneratedInCombat => false, so the game NEVER produces them
    // from random in-combat generation (grep 'CanBeGeneratedInCombat => false' under
    // data/generated/decompiled/.../Core/Models/Cards/). HiddenGem is the "gem"; Alchemize (gives a
    // potion), HandOfGreed (gold) and Royalties are the other Regent/Colorless-relevant ones.
    private static readonly HashSet<string> NonGeneratableInCombat = new(StringComparer.Ordinal)
    {
        "Alchemize", "Disintegration", "Feed", "FranticEscape", "HandOfGreed", "HiddenGem",
        "MindRot", "NeowsFury", "NotYet", "Royalties", "Sloth", "Soot", "TheHunt", "WasteAway"
    };

    // CardFactory.FilterForCombat also drops these rarities from random generation
    // (data/generated/decompiled/.../Core/Factories/CardFactory.cs FilterForCombat).
    private static readonly HashSet<string> NonGeneratableRarities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Basic", "Ancient", "Event"
    };

    // Regenerates data/manual-tags/simulation_generated_card_pools.json from the authoritative
    // simulation card library. Each managed pool is filled with every faithfully simulatable card
    // matching its subject; same-subject cards the simulator cannot model are written to a
    // record-only 'unsupportedPools' dictionary (ignored by the loader). Unmanaged pools such as
    // entropy.sunStrike are preserved verbatim.
    private static int WriteGenerationPools(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        int layer = GetIntOption(args, "--layer") ?? 1;
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string setupPrioritiesPath = GetOption(args, "--setup-priorities")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
        string autoPlayEffectsPath = GetOption(args, "--card-autoplay-effects")
            ?? Path.Combine(outputRoot, "manual-tags", "card_autoplay_effects.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(membershipsPath))
        {
            return Fail($"Missing card pool memberships at {membershipsPath}. Run parse-card-pools first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog existing = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        SimulationSetupPriorityCatalog setupPriorities = LoadOptionalSimulationSetupPriorities(setupPrioritiesPath, jsonOptions);
        IReadOnlyList<AutoPlayEffectEntry> autoPlayEffects = LoadOptionalAutoPlayEffects(autoPlayEffectsPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);

        IReadOnlyList<SimulationCard> cards = new SimulationCardLibraryBuilder().Build(
            entries,
            calibration,
            layer,
            includeUpgrades: false,
            memberships,
            setupPriorities,
            autoPlayEffects);

        HashSet<string> multiplayerOnly = memberships
            .Where(entry => entry.IsMultiplayerOnly)
            .Select(entry => entry.ModelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        static bool InPool(SimulationCard card, string pool) =>
            card.Pools.Contains(pool, StringComparer.OrdinalIgnoreCase);

        GenerationPoolSpec[] specs =
        [
            new("calamity.regent.attack", card => InPool(card, "Regent") && card.IsAttack, null,
                "Calamity: current-hero (Regent) Attacks."),
            new("spectrumShift.colorless", card => InPool(card, "Colorless"), null,
                "SpectrumShift: Colorless card pool."),
            new("bundleOfJoy.colorless", card => InPool(card, "Colorless"), null,
                "BundleOfJoy: Colorless card pool."),
            new("manifestAuthority.colorless", card => InPool(card, "Colorless"), null,
                "ManifestAuthority: Colorless card pool."),
            new("quasar.colorless", card => InPool(card, "Colorless"), null,
                "Quasar: Colorless Discover pool."),
            new("jackOfAllTrades.colorless", card => InPool(card, "Colorless"), "JackOfAllTrades",
                "JackOfAllTrades: Colorless card pool (excludes itself)."),
            new("discovery.regent", card => InPool(card, "Regent"), null,
                "Discovery: current-hero (Regent) Discover pool."),
            new("jackpot.regent.zeroCost", card => InPool(card, "Regent") && card.Cost == 0, null,
                "Jackpot: current-hero (Regent) 0-cost pool."),
        ];

        SortedDictionary<string, string[]> pools = new(StringComparer.Ordinal);
        SortedDictionary<string, List<UnsupportedPoolEntry>> unsupportedPools = new(StringComparer.Ordinal);

        foreach (GenerationPoolSpec spec in specs)
        {
            List<string> simulatable = [];
            List<UnsupportedPoolEntry> notSimulatable = [];
            IEnumerable<SimulationCard> subject = cards
                .Where(card => spec.ExcludeTypeName is null
                    || !string.Equals(card.TypeName, spec.ExcludeTypeName, StringComparison.OrdinalIgnoreCase))
                .Where(spec.Subject)
                .OrderBy(card => card.TypeName, StringComparer.Ordinal);
            foreach (SimulationCard card in subject)
            {
                List<string> reasons = PoolExclusionReasons(card, multiplayerOnly);
                if (reasons.Count == 0)
                {
                    simulatable.Add(card.TypeName);
                }
                else
                {
                    notSimulatable.Add(new UnsupportedPoolEntry(card.TypeName, card.ModelId, reasons));
                }
            }

            pools[spec.PoolId] = [.. simulatable];
            unsupportedPools[spec.PoolId] = notSimulatable;
        }

        // Preserve any existing pool ids this command does not manage (e.g. entropy.sunStrike).
        HashSet<string> managed = specs.Select(spec => spec.PoolId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string[]> existingPool in existing.Pools)
        {
            if (!managed.Contains(existingPool.Key))
            {
                pools[existingPool.Key] = existingPool.Value;
            }
        }

        var output = new
        {
            version = 2,
            note = "Generated by 'write-generation-pools'. 'pools' holds only faithfully simulatable cards "
                + "matching each generator's subject (Regent / current-hero / Colorless). 'unsupportedPools' "
                + "records same-subject cards the simulator cannot faithfully model (unsupported action, "
                + "unplayable, multiplayer-only, ally-target) with reasons; it is RECORD-ONLY and ignored by the "
                + "loader (GeneratedCardPoolCatalog reads only 'version' and 'pools'). Unmanaged pools such as "
                + "entropy.sunStrike are preserved as-is.",
            pools,
            unsupportedPools
        };

        JsonSerializerOptions writeOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        File.WriteAllText(generatedCardPoolsPath, JsonSerializer.Serialize(output, writeOptions) + Environment.NewLine);

        Console.WriteLine("generation pools written");
        Console.WriteLine($"output: {generatedCardPoolsPath}");
        Console.WriteLine($"libraryCards: {cards.Count}");
        foreach (GenerationPoolSpec spec in specs)
        {
            Console.WriteLine(
                $"  {spec.PoolId}: {pools[spec.PoolId].Length} simulatable / {unsupportedPools[spec.PoolId].Count} recorded");
        }

        return 0;
    }

    private static List<string> PoolExclusionReasons(SimulationCard card, HashSet<string> multiplayerOnly)
    {
        List<string> reasons = [];

        // Game generation-eligibility gate (CardFactory.FilterForCombat): a card is never produced by
        // random in-combat generation if it opts out via CanBeGeneratedInCombat or is Basic/Ancient/Event.
        if (NonGeneratableInCombat.Contains(card.TypeName))
        {
            reasons.Add("notGeneratableInCombat");
        }

        if (card.Rarity is not null && NonGeneratableRarities.Contains(card.Rarity))
        {
            reasons.Add($"rarity:{card.Rarity}");
        }

        if (!card.IsPlayable)
        {
            reasons.Add("unplayable");
        }

        if (multiplayerOnly.Contains(card.ModelId))
        {
            reasons.Add("multiplayerOnly");
        }

        if (card.TargetType is not null && card.TargetType.Contains("Ally", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("allyTarget");
        }

        reasons.AddRange(card.Warnings.Where(IsPoolBlockingWarning));
        return reasons;
    }

    private static bool IsPoolBlockingWarning(string warning)
    {
        return warning.StartsWith("Unsupported simulation action", StringComparison.Ordinal)
            || warning.Contains("Generic calculated damage scaling requires manual review", StringComparison.Ordinal);
    }
}
