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
}
