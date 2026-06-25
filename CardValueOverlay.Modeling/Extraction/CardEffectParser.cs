using System.Globalization;
using System.Text.RegularExpressions;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardEffectParser
{
    private static readonly Regex ConstructorRegex = new(
        @":\s*base\((?<cost>[^,]+),\s*CardType\.(?<type>[A-Za-z0-9_]+),\s*CardRarity\.(?<rarity>[A-Za-z0-9_]+),\s*TargetType\.(?<target>[A-Za-z0-9_]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex DamageVarRegex = new(
        @"new\s+DamageVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex BlockVarRegex = new(
        @"new\s+BlockVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex CalculationBaseRegex = new(
        @"new\s+CalculationBaseVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex ExtraDamageRegex = new(
        @"new\s+ExtraDamageVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex CardsVarRegex = new(
        @"new\s+CardsVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex EnergyVarRegex = new(
        @"new\s+EnergyVar\((?:(?<quote>"")(?<name>[^""]+)\k<quote>\s*,\s*)?(?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex HpLossVarRegex = new(
        @"new\s+HpLossVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex PowerVarRegex = new(
        @"new\s+PowerVar<(?<power>[A-Za-z0-9_]+Power)>\((?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex DrawLiteralRegex = new(
        @"CardPileCmd\.Draw\([^,]+,\s*(?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex AppliedPowerRegex = new(
        @"PowerCmd\.Apply<(?<power>[A-Za-z0-9_]+Power)>",
        RegexOptions.Compiled);

    private static readonly Regex UpgradeRegex = new(
        @"DynamicVars\.(?<name>[A-Za-z0-9_]+)\.UpgradeValueBy\((?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex HitCountRegex = new(
        @"\.WithHitCount\((?<count>[0-9]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex CardTagRegex = new(
        @"CardTag\.(?<tag>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex TagContainsRegex = new(
        @"Tags\.Contains\(CardTag\.(?<tag>[A-Za-z0-9_]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex CanonicalKeywordsRegex = new(
        @"CanonicalKeywords\s*=>\s*(?<body>.*?);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CardKeywordRegex = new(
        @"CardKeyword\.(?<keyword>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex AddKeywordRegex = new(
        @"AddKeyword\(CardKeyword\.(?<keyword>[A-Za-z0-9_]+)\)",
        RegexOptions.Compiled);

    public CardEffectTermCatalogEntry Parse(ModelCatalogEntry card, string source)
    {
        List<CardEffectTerm> terms = [];
        List<string> unresolved = [];

        CardHeader header = ParseHeader(source);
        Dictionary<string, decimal> upgradeDeltas = ParseUpgradeDeltas(source);
        int? hitCount = ParseHitCount(source);

        AddDamageTerms(source, header.TargetType, hitCount, upgradeDeltas, terms);
        AddBlockTerms(source, header.TargetType, upgradeDeltas, terms);
        AddScalingTerms(source, header.TargetType, upgradeDeltas, terms);
        AddResourceTerms(source, header.TargetType, upgradeDeltas, terms);
        AddPowerTerms(source, header.TargetType, upgradeDeltas, terms);
        AddKeywordTerms(source, header.TargetType, terms);

        if (terms.Count == 0)
        {
            unresolved.Add("No supported v1 effect terms were parsed from the decompiled card body.");
        }

        double confidence = terms.Count == 0 ? 0.2 : terms.Min(term => term.Confidence);
        if (unresolved.Count > 0)
        {
            confidence = Math.Min(confidence, 0.4);
        }

        return new CardEffectTermCatalogEntry(
            card.ModelId,
            card.TypeName,
            card.FullTypeName,
            header.Cost,
            header.CardType,
            header.Rarity,
            header.TargetType,
            terms,
            unresolved,
            "ilspycmd decompiled C# parser v1",
            confidence);
    }

    private static CardHeader ParseHeader(string source)
    {
        Match match = ConstructorRegex.Match(source);
        if (!match.Success)
        {
            return new CardHeader(null, null, null, null);
        }

        int? cost = int.TryParse(match.Groups["cost"].Value.Trim(), out int parsedCost)
            ? parsedCost
            : null;

        return new CardHeader(
            cost,
            match.Groups["type"].Value,
            match.Groups["rarity"].Value,
            match.Groups["target"].Value);
    }

    private static Dictionary<string, decimal> ParseUpgradeDeltas(string source)
    {
        Dictionary<string, decimal> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UpgradeRegex.Matches(source))
        {
            if (decimal.TryParse(match.Groups["amount"].Value, CultureInfo.InvariantCulture, out decimal amount))
            {
                values[match.Groups["name"].Value] = amount;
            }
        }

        return values;
    }

    private static int? ParseHitCount(string source)
    {
        Match match = HitCountRegex.Match(source);
        return match.Success && int.TryParse(match.Groups["count"].Value, out int hitCount)
            ? hitCount
            : null;
    }

    private static void AddDamageTerms(
        string source,
        string? targetType,
        int? hitCount,
        IReadOnlyDictionary<string, decimal> upgradeDeltas,
        List<CardEffectTerm> terms)
    {
        foreach (Match match in DamageVarRegex.Matches(source))
        {
            terms.Add(new CardEffectTerm(
                "damage",
                ParseDecimal(match.Groups["amount"].Value),
                TryGetUpgrade(upgradeDeltas, "Damage"),
                hitCount,
                targetType,
                null,
                "DamageVar",
                0.85));
        }

        foreach (Match match in CalculationBaseRegex.Matches(source))
        {
            terms.Add(new CardEffectTerm(
                "damage",
                ParseDecimal(match.Groups["amount"].Value),
                TryGetUpgrade(upgradeDeltas, "CalculationBase"),
                hitCount,
                targetType,
                "calculationBase",
                "CalculationBaseVar",
                0.75));
        }
    }

    private static void AddBlockTerms(
        string source,
        string? targetType,
        IReadOnlyDictionary<string, decimal> upgradeDeltas,
        List<CardEffectTerm> terms)
    {
        foreach (Match match in BlockVarRegex.Matches(source))
        {
            terms.Add(new CardEffectTerm(
                "block",
                ParseDecimal(match.Groups["amount"].Value),
                TryGetUpgrade(upgradeDeltas, "Block"),
                null,
                targetType,
                null,
                "BlockVar",
                0.85));
        }
    }

    private static void AddScalingTerms(
        string source,
        string? targetType,
        IReadOnlyDictionary<string, decimal> upgradeDeltas,
        List<CardEffectTerm> terms)
    {
        Match extraDamage = ExtraDamageRegex.Match(source);
        if (!extraDamage.Success)
        {
            return;
        }

        Match tag = TagContainsRegex.Match(source);
        if (!tag.Success)
        {
            terms.Add(new CardEffectTerm(
                "scalingDamage",
                ParseDecimal(extraDamage.Groups["amount"].Value),
                TryGetUpgrade(upgradeDeltas, "ExtraDamage"),
                null,
                targetType,
                "calculatedMultiplier",
                "ExtraDamageVar + CalculatedDamageVar",
                0.5));
            return;
        }

        terms.Add(new CardEffectTerm(
            "scalingDamagePerCardTag",
            ParseDecimal(extraDamage.Groups["amount"].Value),
            TryGetUpgrade(upgradeDeltas, "ExtraDamage"),
            null,
            targetType,
            $"cardTag:{tag.Groups["tag"].Value}",
            "ExtraDamageVar + CardTag multiplier",
            0.75));
    }

    private static void AddResourceTerms(
        string source,
        string? targetType,
        IReadOnlyDictionary<string, decimal> upgradeDeltas,
        List<CardEffectTerm> terms)
    {
        if (source.Contains("CardPileCmd.Draw(", StringComparison.Ordinal))
        {
            foreach (Match match in CardsVarRegex.Matches(source))
            {
                terms.Add(new CardEffectTerm(
                    "draw",
                    ParseDecimal(match.Groups["amount"].Value),
                    TryGetUpgrade(upgradeDeltas, "Cards"),
                    null,
                    targetType,
                    null,
                    "CardsVar + CardPileCmd.Draw",
                    0.82));
            }

            foreach (Match match in DrawLiteralRegex.Matches(source))
            {
                terms.Add(new CardEffectTerm(
                    "draw",
                    ParseDecimal(match.Groups["amount"].Value),
                    null,
                    null,
                    targetType,
                    "literal",
                    "CardPileCmd.Draw literal",
                    0.72));
            }
        }

        if (source.Contains("PlayerCmd.GainEnergy(", StringComparison.Ordinal))
        {
            foreach (Match match in EnergyVarRegex.Matches(source).Where(IsDefaultDynamicVar))
            {
                terms.Add(new CardEffectTerm(
                    "energyGain",
                    ParseDecimal(match.Groups["amount"].Value),
                    TryGetUpgrade(upgradeDeltas, "Energy"),
                    null,
                    targetType,
                    null,
                    "EnergyVar + PlayerCmd.GainEnergy",
                    0.82));
            }
        }

        if (source.Contains("PowerCmd.Apply<EnergyNextTurnPower>", StringComparison.Ordinal))
        {
            foreach (Match match in EnergyVarRegex.Matches(source).Where(IsDefaultDynamicVar))
            {
                terms.Add(new CardEffectTerm(
                    "energyNextTurn",
                    ParseDecimal(match.Groups["amount"].Value),
                    TryGetUpgrade(upgradeDeltas, "Energy"),
                    null,
                    targetType,
                    null,
                    "EnergyVar + EnergyNextTurnPower",
                    0.78));
            }
        }

        foreach (Match match in HpLossVarRegex.Matches(source))
        {
            terms.Add(new CardEffectTerm(
                "hpLoss",
                ParseDecimal(match.Groups["amount"].Value),
                TryGetUpgrade(upgradeDeltas, "HpLoss"),
                null,
                "Self",
                null,
                "HpLossVar",
                0.75));
        }
    }

    private static void AddPowerTerms(
        string source,
        string? targetType,
        IReadOnlyDictionary<string, decimal> upgradeDeltas,
        List<CardEffectTerm> terms)
    {
        HashSet<string> appliedPowers = AppliedPowerRegex.Matches(source)
            .Select(match => match.Groups["power"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (Match match in PowerVarRegex.Matches(source))
        {
            string power = match.Groups["power"].Value;
            if (!appliedPowers.Contains(power))
            {
                continue;
            }

            string dynamicVarKey = ToDynamicVarKey(power);
            terms.Add(new CardEffectTerm(
                ToPowerTermKind(power),
                ParseDecimal(match.Groups["amount"].Value),
                TryGetUpgrade(upgradeDeltas, dynamicVarKey),
                null,
                targetType,
                $"power:{dynamicVarKey}",
                $"PowerVar<{power}> + PowerCmd.Apply",
                0.78));
        }
    }

    private static void AddKeywordTerms(
        string source,
        string? targetType,
        List<CardEffectTerm> terms)
    {
        HashSet<string> canonicalKeywords = CanonicalKeywordsRegex.Matches(source)
            .SelectMany(match => CardKeywordRegex.Matches(match.Groups["body"].Value)
                .Select(keywordMatch => keywordMatch.Groups["keyword"].Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (string keyword in canonicalKeywords.Order(StringComparer.Ordinal))
        {
            terms.Add(new CardEffectTerm(
                "keyword",
                null,
                null,
                null,
                targetType,
                keyword,
                "CanonicalKeywords",
                0.7));
        }

        foreach (string keyword in AddKeywordRegex.Matches(source)
            .Select(match => match.Groups["keyword"].Value)
            .Where(keyword => !canonicalKeywords.Contains(keyword))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            terms.Add(new CardEffectTerm(
                "keywordOnUpgrade",
                null,
                null,
                null,
                targetType,
                keyword,
                "AddKeyword",
                0.55));
        }
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static decimal? TryGetUpgrade(IReadOnlyDictionary<string, decimal> values, string key)
    {
        return values.TryGetValue(key, out decimal value) ? value : null;
    }

    private static bool IsDefaultDynamicVar(Match match)
    {
        return !match.Groups["name"].Success
            || string.IsNullOrWhiteSpace(match.Groups["name"].Value)
            || string.Equals(match.Groups["name"].Value, "Energy", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToDynamicVarKey(string power)
    {
        return power.EndsWith("Power", StringComparison.Ordinal)
            ? power[..^"Power".Length]
            : power;
    }

    private static string ToPowerTermKind(string power)
    {
        return power switch
        {
            "VulnerablePower" => "debuffVulnerable",
            "WeakPower" => "debuffWeak",
            "PoisonPower" => "debuffPoison",
            _ => "power"
        };
    }

    private sealed record CardHeader(int? Cost, string? CardType, string? Rarity, string? TargetType);
}
