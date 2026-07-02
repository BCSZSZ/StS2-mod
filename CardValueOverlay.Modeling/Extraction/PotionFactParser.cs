using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CardValueOverlay.Modeling.Extraction;

// Parses the decompiled StS2 potion sources into a PotionCatalogEntry per potion. Mirrors
// CardFactParser: declarative axes come from simple property/constructor regexes; numeric amounts
// come from CanonicalVars; effect tags come from an OnUse command-verb scan. Pool membership is read
// from the potion pool definition files (SharedPotionPool, the <Char>4Epoch lists, and the
// event/token/deprecated pools) so each potion is placed in exactly one generation pool.
public sealed class PotionFactParser
{
    private const string Provenance = "ilspycmd decompiled C# potion facts parser v1";

    private static readonly Regex ClassRegex = new(
        @"public\s+(?:sealed\s+)?class\s+(?<name>[A-Za-z0-9_]+)\s*:\s*PotionModel",
        RegexOptions.Compiled);

    private static readonly Regex NamespaceRegex = new(
        @"namespace\s+(?<ns>[A-Za-z0-9_.]+)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex RarityRegex = new(
        @"Rarity\s*=>\s*PotionRarity\.(?<value>[A-Za-z]+)",
        RegexOptions.Compiled);

    private static readonly Regex UsageRegex = new(
        @"Usage\s*=>\s*PotionUsage\.(?<value>[A-Za-z]+)",
        RegexOptions.Compiled);

    private static readonly Regex TargetRegex = new(
        @"TargetType\s*=>\s*TargetType\.(?<value>[A-Za-z]+)",
        RegexOptions.Compiled);

    // Fallback for potions whose TargetType is a conditional getter (e.g. FoulPotion: AllEnemies in
    // combat, TargetedNoCreature at the merchant). Capture the returned targets and prefer the combat one.
    private static readonly Regex ReturnTargetRegex = new(
        @"return\s+TargetType\.(?<value>[A-Za-z]+)",
        RegexOptions.Compiled);

    private static readonly Regex CanNotGenerateRegex = new(
        @"CanBeGeneratedInCombat\s*=>\s*false",
        RegexOptions.Compiled);

    private static readonly Regex PoolPotionRegex = new(
        @"Potion<(?<name>[A-Za-z0-9_]+)>",
        RegexOptions.Compiled);

    private static readonly Regex PowerVarRegex = new(
        @"new\s+PowerVar<(?<power>[A-Za-z0-9_]+Power)>\((?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex AppliedPowerRegex = new(
        @"PowerCmd\.Apply<(?<power>[A-Za-z0-9_]+Power)>",
        RegexOptions.Compiled);

    // Non-power CanonicalVars: kind -> regex capturing the numeric BaseValue.
    private static readonly (string Kind, Regex Regex)[] SimpleVarRegexes =
    [
        ("Damage", VarRegex("DamageVar")),
        ("Block", VarRegex("BlockVar")),
        ("Cards", VarRegex("CardsVar")),
        ("Energy", VarRegex("EnergyVar")),
        ("Stars", VarRegex("StarsVar")),
        ("Forge", VarRegex("ForgeVar")),
        ("Repeat", VarRegex("RepeatVar")),
        ("MaxHp", VarRegex("MaxHpVar")),
        ("Summon", VarRegex("SummonVar")),
        ("Gold", VarRegex("GoldVar")),
        ("Heal", VarRegex("HealVar")),
    ];

    private static Regex VarRegex(string varType) => new(
        $@"new\s+{varType}\((?:""[^""]*""\s*,\s*)?(?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    // Effect-tag verb scan. Each rule: a substring/verb present in the source implies a value channel.
    // NeedsNewChannel rules flag effects the simulator has no channel for yet.
    private static readonly (Regex Regex, string Tag, bool NeedsNewChannel)[] EffectRules =
    [
        (new(@"CreatureCmd\.Damage|new\s+DamageVar", RegexOptions.Compiled), "damage", false),
        (new(@"GainBlock|new\s+BlockVar", RegexOptions.Compiled), "block", false),
        (new(@"CardPileCmd\.Draw|DrawCards", RegexOptions.Compiled), "draw", false),
        (new(@"GainEnergy|new\s+EnergyVar", RegexOptions.Compiled), "energy", false),
        (new(@"GainStars|new\s+StarsVar", RegexOptions.Compiled), "star", false),
        (new(@"ForgeCmd\.Forge|new\s+ForgeVar", RegexOptions.Compiled), "forge", false),
        (new(@"ChooseACardScreen|AddGeneratedCard|\.CreateInHand|CardFactory\.", RegexOptions.Compiled), "cardGeneration", false),
        (new(@"AutoPlayFromDrawPile", RegexOptions.Compiled), "cardAutoPlay", false),
        (new(@"CreatureCmd\.Heal|\.Heal\(|new\s+HealVar", RegexOptions.Compiled), "heal", true),
        (new(@"GainMaxHp|new\s+MaxHpVar", RegexOptions.Compiled), "maxHp", true),
        (new(@"OrbCmd\.", RegexOptions.Compiled), "orb", true),
        (new(@"OstyCmd\.Summon|new\s+SummonVar", RegexOptions.Compiled), "summon", true),
        (new(@"GainGold|new\s+GoldVar", RegexOptions.Compiled), "gold", true),
        (new(@"PotionCmd\.TryToProcure|PotionFactory", RegexOptions.Compiled), "potionGeneration", true),
        (new(@"CardCmd\.(Exhaust|Discard|Upgrade|Transform)|SetToFree|CardPileCmd\.Shuffle|DiscardAndDraw", RegexOptions.Compiled), "cardManipulation", true),
    ];

    // Powers with an existing simulator value channel; any other applied power needs a new channel.
    private static readonly HashSet<string> VulnerableWeakPowers = new(StringComparer.Ordinal)
    {
        "VulnerablePower", "WeakPower"
    };

    private static readonly HashSet<string> ModeledPowers = new(StringComparer.Ordinal)
    {
        "StrengthPower", "DexterityPower"
    };

    private static readonly HashSet<string> RandomInCombatPools = new(StringComparer.Ordinal)
    {
        "Shared", "Regent", "Ironclad", "Silent", "Defect", "Necrobinder"
    };

    public IReadOnlyList<PotionCatalogEntry> Parse(
        string potionsDirectory,
        string potionPoolsDirectory,
        string epochsDirectory)
    {
        Dictionary<string, string> poolByType = BuildPoolMembership(potionPoolsDirectory, epochsDirectory);

        List<PotionCatalogEntry> entries = [];
        foreach (string file in Directory
            .EnumerateFiles(potionsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            string source = File.ReadAllText(file);
            Match classMatch = ClassRegex.Match(source);
            if (!classMatch.Success)
            {
                continue;
            }

            string typeName = classMatch.Groups["name"].Value;
            string ns = NamespaceRegex.Match(source) is { Success: true } m ? m.Groups["ns"].Value : "MegaCrit.Sts2.Core.Models.Potions";
            string locKey = Slugify(typeName);

            List<string> unresolved = [];
            string rarity = ReadOne(RarityRegex, source, "rarity", unresolved);
            string usage = ReadOne(UsageRegex, source, "usage", unresolved);
            string target = ResolveTargetType(source, unresolved);
            bool canBeGenerated = !CanNotGenerateRegex.IsMatch(source);
            string pool = poolByType.GetValueOrDefault(typeName, "Unknown");
            if (pool == "Unknown")
            {
                unresolved.Add("pool membership not found");
            }

            List<PotionVar> vars = ParseVars(source);
            (List<string> effectTags, List<string> needsNewChannel) = ClassifyEffects(source, target);

            bool inRandomPool = canBeGenerated
                && RandomInCombatPools.Contains(pool)
                && rarity is "Common" or "Uncommon" or "Rare";

            entries.Add(new PotionCatalogEntry(
                ModelId: "POTION." + locKey,
                TypeName: typeName,
                FullTypeName: $"{ns}.{typeName}",
                LocKey: locKey,
                Rarity: rarity,
                Usage: usage,
                TargetType: target,
                CanBeGeneratedInCombat: canBeGenerated,
                Pool: pool,
                InRandomInCombatPool: inRandomPool,
                Vars: vars,
                EffectTags: effectTags,
                NeedsNewChannel: needsNewChannel,
                Unresolved: unresolved,
                Provenance: Provenance,
                Confidence: unresolved.Count == 0 ? 0.85 : 0.6));
        }

        return entries.OrderBy(entry => entry.LocKey, StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, string> BuildPoolMembership(string potionPoolsDirectory, string epochsDirectory)
    {
        Dictionary<string, string> poolByType = new(StringComparer.Ordinal);

        AddPool(poolByType, Path.Combine(potionPoolsDirectory, "SharedPotionPool.cs"), "Shared");
        AddPool(poolByType, Path.Combine(potionPoolsDirectory, "EventPotionPool.cs"), "Event");
        AddPool(poolByType, Path.Combine(potionPoolsDirectory, "TokenPotionPool.cs"), "Token");
        AddPool(poolByType, Path.Combine(potionPoolsDirectory, "DeprecatedPotionPool.cs"), "Deprecated");

        foreach (string character in new[] { "Regent", "Ironclad", "Silent", "Defect", "Necrobinder" })
        {
            AddPool(poolByType, Path.Combine(epochsDirectory, $"{character}4Epoch.cs"), character);
        }

        return poolByType;
    }

    private static void AddPool(Dictionary<string, string> poolByType, string file, string pool)
    {
        if (!File.Exists(file))
        {
            return;
        }

        foreach (Match match in PoolPotionRegex.Matches(File.ReadAllText(file)))
        {
            string typeName = match.Groups["name"].Value;
            // First pool wins; character pools do not overlap the shared pool in the game data.
            poolByType.TryAdd(typeName, pool);
        }
    }

    private static string ReadOne(Regex regex, string source, string label, List<string> unresolved)
    {
        Match match = regex.Match(source);
        if (match.Success)
        {
            return match.Groups["value"].Value;
        }

        unresolved.Add($"{label} not found");
        return "Unknown";
    }

    private static string ResolveTargetType(string source, List<string> unresolved)
    {
        Match match = TargetRegex.Match(source);
        if (match.Success)
        {
            return match.Groups["value"].Value;
        }

        // Conditional getter: prefer the in-combat target over the out-of-combat one.
        List<string> returned = ReturnTargetRegex.Matches(source)
            .Select(returnMatch => returnMatch.Groups["value"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (returned.Count > 0)
        {
            return returned.FirstOrDefault(value => value != "TargetedNoCreature") ?? returned[0];
        }

        unresolved.Add("targetType not found");
        return "Unknown";
    }

    private static List<PotionVar> ParseVars(string source)
    {
        List<PotionVar> vars = [];
        foreach (Match match in PowerVarRegex.Matches(source))
        {
            vars.Add(new PotionVar(
                "Power",
                decimal.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture),
                match.Groups["power"].Value));
        }

        foreach ((string kind, Regex regex) in SimpleVarRegexes)
        {
            foreach (Match match in regex.Matches(source))
            {
                vars.Add(new PotionVar(
                    kind,
                    decimal.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture)));
            }
        }

        return vars;
    }

    private static (List<string> EffectTags, List<string> NeedsNewChannel) ClassifyEffects(string source, string target)
    {
        HashSet<string> tags = new(StringComparer.Ordinal);
        HashSet<string> needsNew = new(StringComparer.Ordinal);

        foreach ((Regex regex, string tag, bool needsNewChannel) in EffectRules)
        {
            if (regex.IsMatch(source))
            {
                if (needsNewChannel)
                {
                    needsNew.Add(tag);
                }
                else
                {
                    tags.Add(tag);
                }
            }
        }

        foreach (string powerName in PowerVarRegex.Matches(source)
            .Select(match => match.Groups["power"].Value)
            .Concat(AppliedPowerRegex.Matches(source).Select(match => match.Groups["power"].Value))
            .Distinct(StringComparer.Ordinal))
        {
            if (VulnerableWeakPowers.Contains(powerName))
            {
                tags.Add("vulnerableWeak");
            }
            else if (ModeledPowers.Contains(powerName))
            {
                tags.Add("powerInstall");
            }
            else
            {
                needsNew.Add("power:" + powerName.Replace("Power", string.Empty, StringComparison.Ordinal));
            }
        }

        if (tags.Contains("damage") && string.Equals(target, "AllEnemies", StringComparison.Ordinal))
        {
            tags.Add("aoe");
        }

        return (
            tags.Order(StringComparer.Ordinal).ToList(),
            needsNew.Order(StringComparer.Ordinal).ToList());
    }

    // PascalCase class name -> UPPER_SNAKE localization key (matches the game's Id.Entry slug).
    private static string Slugify(string pascalCase)
    {
        StringBuilder builder = new();
        for (int i = 0; i < pascalCase.Length; i++)
        {
            char c = pascalCase[i];
            if (i > 0 && char.IsUpper(c))
            {
                char prev = pascalCase[i - 1];
                bool afterLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                // New word boundary after an acronym / single-capital word (e.g. "...A|Bottle").
                bool startsWordAfterUpper = char.IsUpper(prev)
                    && i + 1 < pascalCase.Length
                    && char.IsLower(pascalCase[i + 1]);
                if (afterLowerOrDigit || startsWordAfterUpper)
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }
}
