using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    // Parses the decompiled potion sources into data/extracted/potion_facts.generated.json (the full
    // 64-potion roster with rarity/usage/target/pool/vars and effect tags). Reads the already-decompiled
    // Core sources under data/generated/decompiled; does not invoke ilspy.
    private static int ParsePotions(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string decompileRoot = GetOption(args, "--decompile-dir")
            ?? Path.Combine(outputRoot, "generated", "decompiled");

        if (!Directory.Exists(decompileRoot))
        {
            return Fail($"Missing decompiled sources at {decompileRoot}. Run parse-card-facts (or extract) first.");
        }

        string? potionsDir = Directory
            .EnumerateDirectories(decompileRoot, "Potions", SearchOption.AllDirectories)
            .FirstOrDefault(dir => dir.Replace('\\', '/')
                .EndsWith("Core/Models/Potions", StringComparison.OrdinalIgnoreCase));
        if (potionsDir is null)
        {
            return Fail($"Could not locate Core/Models/Potions under {decompileRoot}.");
        }

        string modelsDir = Directory.GetParent(potionsDir)!.FullName;
        string coreDir = Directory.GetParent(modelsDir)!.FullName;
        string poolsDir = Path.Combine(modelsDir, "PotionPools");
        string epochsDir = Path.Combine(coreDir, "Timeline", "Epochs");

        IReadOnlyList<PotionCatalogEntry> potions = new PotionFactParser().Parse(potionsDir, poolsDir, epochsDir);

        string extractedRoot = Path.Combine(outputRoot, "extracted");
        Directory.CreateDirectory(extractedRoot);
        string outputPath = Path.Combine(extractedRoot, "potion_facts.generated.json");
        JsonSerializerOptions writeOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(potions, writeOptions) + Environment.NewLine);

        int needsNew = potions.Count(potion => potion.NeedsNewChannel.Count > 0);
        int inRandomPool = potions.Count(potion => potion.InRandomInCombatPool);
        int unresolved = potions.Count(potion => potion.Unresolved.Count > 0);
        Console.WriteLine("potion facts parsed");
        Console.WriteLine($"potions: {potions.Count}");
        Console.WriteLine($"inRandomInCombatPool: {inRandomPool}");
        Console.WriteLine($"needNewChannel: {needsNew}");
        Console.WriteLine($"unresolved: {unresolved}");
        foreach (IGrouping<string, PotionCatalogEntry> group in potions
            .GroupBy(potion => potion.Rarity)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  rarity {group.Key}: {group.Count()}");
        }

        Console.WriteLine($"output: {outputPath}");
        return 0;
    }

    // Splits the extracted potion roster into a committable two-set architecture file:
    //   supported   = every OnUse effect maps to a channel the simulator already has
    //                 (NeedsNewChannel empty) and the potion has a modeled effect.
    //   unsupported = the potion needs a channel we do not model yet (heal / maxHp / orb / summon /
    //                 gold / potionGeneration / cardManipulation / an unsupported power), or has no
    //                 modeled effect at all - recorded with reasons.
    // This is the architecture stage only: potion VALUE is not computed here (potions are not yet
    // simulated). Mirrors write-generation-pools' supported/unsupported shape.
    private static int WritePotionPools(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string factsPath = GetOption(args, "--potion-facts")
            ?? Path.Combine(outputRoot, "extracted", "potion_facts.generated.json");
        string outputPath = GetOption(args, "--output-file")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_potion_pools.json");

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing potion facts at {factsPath}. Run parse-potions first.");
        }

        JsonSerializerOptions readOptions = new() { PropertyNameCaseInsensitive = true };
        IReadOnlyList<PotionCatalogEntry> potions =
            JsonSerializer.Deserialize<List<PotionCatalogEntry>>(File.ReadAllText(factsPath), readOptions)
            ?? throw new InvalidOperationException($"Failed to read potion facts from {factsPath}.");

        List<object> supported = [];
        List<object> unsupported = [];
        foreach (PotionCatalogEntry potion in potions.OrderBy(potion => potion.TypeName, StringComparer.Ordinal))
        {
            bool isSupported = potion.NeedsNewChannel.Count == 0 && potion.EffectTags.Count > 0;
            if (isSupported)
            {
                supported.Add(new
                {
                    typeName = potion.TypeName,
                    modelId = potion.ModelId,
                    rarity = potion.Rarity,
                    usage = potion.Usage,
                    target = potion.TargetType,
                    inRandomInCombatPool = potion.InRandomInCombatPool,
                    effectTags = potion.EffectTags
                });
            }
            else
            {
                IReadOnlyList<string> reasons = potion.NeedsNewChannel.Count > 0
                    ? potion.NeedsNewChannel
                    : ["noModeledEffect"];
                unsupported.Add(new
                {
                    typeName = potion.TypeName,
                    modelId = potion.ModelId,
                    rarity = potion.Rarity,
                    usage = potion.Usage,
                    target = potion.TargetType,
                    inRandomInCombatPool = potion.InRandomInCombatPool,
                    effectTags = potion.EffectTags,
                    needsNewChannel = reasons
                });
            }
        }

        var output = new
        {
            version = 1,
            note = "Architecture only (no potion valuation yet). 'supported' potions have every OnUse "
                + "effect mapped to a channel the card simulator already models (damage/block/draw/energy/"
                + "star/forge/vulnerableWeak/powerInstall/cardGeneration/cardAutoPlay/aoe). 'unsupported' "
                + "potions need a channel not modeled yet (heal/maxHp/orb/summon/gold/potionGeneration/"
                + "cardManipulation/unsupported power) or have no modeled effect; needsNewChannel lists why. "
                + "Regenerate with 'write-potion-pools' from data/extracted/potion_facts.generated.json.",
            supportedCount = supported.Count,
            unsupportedCount = unsupported.Count,
            supported,
            unsupported
        };

        JsonSerializerOptions writeOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        string? parent = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(output, writeOptions) + Environment.NewLine);
        Console.WriteLine("potion pools written");
        Console.WriteLine($"output: {outputPath}");
        Console.WriteLine($"supported: {supported.Count} / unsupported: {unsupported.Count} (total {potions.Count})");
        return 0;
    }
}
