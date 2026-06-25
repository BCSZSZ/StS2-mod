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

    private static readonly Regex UpgradeRegex = new(
        @"DynamicVars\.(?<name>[A-Za-z0-9_]+)\.UpgradeValueBy\((?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex HitCountRegex = new(
        @"\.WithHitCount\((?<count>[0-9]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex CardTagRegex = new(
        @"CardTag\.(?<tag>[A-Za-z0-9_]+)",
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

        string? tag = CardTagRegex.Matches(source)
            .Select(match => match.Groups["tag"].Value)
            .FirstOrDefault(value => !string.Equals(value, "Strike", StringComparison.OrdinalIgnoreCase))
            ?? CardTagRegex.Matches(source)
                .Select(match => match.Groups["tag"].Value)
                .FirstOrDefault();

        terms.Add(new CardEffectTerm(
            "scalingDamagePerCardTag",
            ParseDecimal(extraDamage.Groups["amount"].Value),
            TryGetUpgrade(upgradeDeltas, "ExtraDamage"),
            null,
            targetType,
            tag is null ? "cardTag" : $"cardTag:{tag}",
            "ExtraDamageVar + CardTag multiplier",
            tag is null ? 0.55 : 0.75));
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static decimal? TryGetUpgrade(IReadOnlyDictionary<string, decimal> values, string key)
    {
        return values.TryGetValue(key, out decimal value) ? value : null;
    }

    private sealed record CardHeader(int? Cost, string? CardType, string? Rarity, string? TargetType);
}
