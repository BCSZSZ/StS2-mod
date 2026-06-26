using System.Globalization;
using System.Text.RegularExpressions;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardFactParser
{
    private static readonly Regex ConstructorRegex = new(
        @":\s*base\((?<cost>[^,]+),\s*CardType\.(?<type>[A-Za-z0-9_]+),\s*CardRarity\.(?<rarity>[A-Za-z0-9_]+),\s*TargetType\.(?<target>[A-Za-z0-9_]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex DamageVarRegex = new(
        @"new\s+DamageVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex BlockVarRegex = new(
        @"new\s+BlockVar\((?:(?<quote>"")(?<name>[^""]+)\k<quote>\s*,\s*)?(?<amount>[0-9]+(?:\.[0-9]+)?)m?",
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

    private static readonly Regex StarsVarRegex = new(
        @"new\s+StarsVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex ForgeVarRegex = new(
        @"new\s+ForgeVar\((?<amount>[0-9]+(?:\.[0-9]+)?)m?",
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

    private static readonly Regex GenericDynamicVarRegex = new(
        @"new\s+DynamicVar\(""(?<name>[^""]+)""\s*,\s*(?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex DrawLiteralRegex = new(
        @"CardPileCmd\.Draw\([^,]+,\s*(?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex DrawNextTurnLiteralRegex = new(
        @"PowerCmd\.Apply<DrawCardsNextTurnPower>\([^;]*?,\s*(?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex StarNextTurnLiteralRegex = new(
        @"PowerCmd\.Apply<StarNextTurnPower>\([^;]*?,\s*(?<amount>[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex StarNextTurnStarsVarApplyRegex = new(
        @"PowerCmd\.Apply<StarNextTurnPower>\([^;]*DynamicVars\.Stars\.",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex StarNextTurnPowerVarApplyRegex = new(
        @"PowerCmd\.Apply<StarNextTurnPower>\([^;]*DynamicVars\[""StarNextTurnPower""\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CanonicalStarCostRegex = new(
        @"CanonicalStarCost\s*=>\s*(?<amount>[0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex AppliedPowerRegex = new(
        @"PowerCmd\.Apply<(?<power>[A-Za-z0-9_]+Power)>",
        RegexOptions.Compiled);

    private static readonly Regex AppliedPowerCallRegex = new(
        @"PowerCmd\.Apply<(?<power>[A-Za-z0-9_]+Power)>\((?<args>[^;]*)\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex UpgradeRegex = new(
        @"DynamicVars(?:\.(?<name>[A-Za-z0-9_]+)|\[""(?<indexName>[^""]+)""\])\.UpgradeValueBy\((?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex EnergyCostUpgradeRegex = new(
        @"EnergyCost\.UpgradeBy\((?<amount>-?[0-9]+(?:\.[0-9]+)?)m?",
        RegexOptions.Compiled);

    private static readonly Regex RemoveKeywordRegex = new(
        @"RemoveKeyword\(CardKeyword\.(?<keyword>[A-Za-z0-9_]+)\)",
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

    private static readonly Regex IsUpgradedBranchRegex = new(
        @"if\s*\(\s*base\.IsUpgraded\s*\)\s*\{(?<body>.*?)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex UpgradeGeneratedCardsRegex = new(
        @"CardCmd\.Upgrade\((?<target>[^,\)]+)",
        RegexOptions.Compiled);

    private static readonly Regex TransformRegex = new(
        @"CardCmd\.TransformTo<(?<card>[A-Za-z0-9_]+)>",
        RegexOptions.Compiled);

    private static readonly Regex RandomTransformRegex = new(
        @"CardCmd\.(?:TransformToRandom|Transform)\(",
        RegexOptions.Compiled);

    private static readonly Regex DiscardRegex = new(
        @"CardCmd\.Discard\(",
        RegexOptions.Compiled);

    private static readonly Regex ExhaustRegex = new(
        @"CardCmd\.Exhaust\(",
        RegexOptions.Compiled);

    private static readonly Regex GeneratedCardRegex = new(
        @"AddGeneratedCardToCombat\([^;]*?CreateCard<(?<card>[A-Za-z0-9_]+)>[^;]*?PileType\.(?<pile>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SelectPileRegex = new(
        @"CardSelectCmd\.FromCombatPile\((?<args>[^;]*?PileType\.(?<pile>[A-Za-z0-9_]+)[^;]*)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SelectHandRegex = new(
        @"CardSelectCmd\.FromHand(?:ForDiscard)?\((?<args>[^;]*)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ChooseCardRegex = new(
        @"CardSelectCmd\.FromChooseACardScreen\(",
        RegexOptions.Compiled);

    private static readonly Regex CardPoolGenerationRegex = new(
        @"CardFactory\.GetDistinctForCombat\([^;]*?CardPool<(?<pool>[A-Za-z0-9_]+CardPool)>[^;]*?,\s*(?<count>[0-9]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex AddGeneratedSelectionRegex = new(
        @"AddGeneratedCardToCombat\((?<card>[A-Za-z0-9_]+)[^;]*?PileType\.(?<pile>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MoveToPileRegex = new(
        @"CardPileCmd\.Add\([^;]*?PileType\.(?<pile>[A-Za-z0-9_]+)(?:,\s*CardPilePosition\.(?<position>[A-Za-z0-9_]+))?",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex LocalDynamicVarAssignmentRegex = new(
        @"(?:int|decimal|float|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<expr>[^;]*DynamicVars(?:\.(?<dot>[A-Za-z0-9_]+)|\[""(?<index>[^""]+)""\])[^;]*);",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> ExtractAppliedPowerTypeNames(string source)
    {
        return AppliedPowerRegex.Matches(source)
            .Select(match => match.Groups["power"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public CardFactCatalogEntry Parse(
        ModelCatalogEntry card,
        string source,
        string sourceFile = "source",
        IReadOnlyDictionary<string, string>? relatedPowerSources = null,
        IReadOnlyDictionary<string, string>? relatedPowerSourceFiles = null)
    {
        List<CardActionFact> actions = [];
        List<CardRawOperation> rawOperations = [];
        List<string> unresolved = [];

        CardHeader header = ParseHeader(source);
        IReadOnlyList<DynamicVarFact> dynamicVars = ParseDynamicVarFacts(source, sourceFile);
        IReadOnlyList<UpgradeOperationFact> upgradeOperations = ParseUpgradeOperationFacts(source, sourceFile, rawOperations);
        int? hitCount = ParseHitCount(source);
        bool isXCost = source.Contains("ResolveEnergyXValue()", StringComparison.Ordinal)
            || source.Contains("HasEnergyCostX", StringComparison.Ordinal);

        AddDamageActions(source, sourceFile, header.TargetType, hitCount, isXCost, actions);
        AddBlockActions(source, sourceFile, header.TargetType, actions);
        AddScalingActions(source, sourceFile, header.TargetType, actions);
        AddResourceActions(source, sourceFile, header.TargetType, dynamicVars, actions);
        AddForgeActions(source, sourceFile, header.TargetType, actions);
        AddPowerActions(source, sourceFile, header.TargetType, dynamicVars, actions);

        IReadOnlyList<string> keywords = ParseCanonicalKeywords(source);
        IReadOnlyList<string> tags = ParseTags(source);

        AddUnsupportedCardOperationFacts(source, sourceFile, header.TargetType, isXCost, dynamicVars, actions, rawOperations);
        AddPersistentPowerTriggerFacts(relatedPowerSources, relatedPowerSourceFiles, actions);

        if (actions.Count == 0 && rawOperations.Count == 0 && keywords.Count == 0 && tags.Count == 0)
        {
            unresolved.Add("No card facts or raw operations were parsed from the decompiled card body.");
        }

        double confidence = Confidence(actions, rawOperations, unresolved);
        return new CardFactCatalogEntry(
            card.ModelId,
            card.TypeName,
            card.FullTypeName,
            header.Cost,
            header.CardType,
            header.Rarity,
            header.TargetType,
            keywords,
            tags,
            dynamicVars,
            upgradeOperations,
            actions.OrderBy(action => action.Evidence.Line ?? int.MaxValue).ThenBy(action => action.Kind, StringComparer.Ordinal).ToArray(),
            rawOperations.OrderBy(operation => operation.Evidence.Line ?? int.MaxValue).ThenBy(operation => operation.Kind, StringComparer.Ordinal).ToArray(),
            unresolved,
            "ilspycmd decompiled C# card facts parser v1",
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

    private static IReadOnlyList<DynamicVarFact> ParseDynamicVarFacts(string source, string sourceFile)
    {
        List<DynamicVarFact> facts = [];
        AddDynamicVarFacts(source, sourceFile, DamageVarRegex, facts, "Damage", "Damage", null, 0.85);
        AddDynamicVarFacts(source, sourceFile, BlockVarRegex, facts, "Block", "Block", null, 0.85);
        AddDynamicVarFacts(source, sourceFile, CalculationBaseRegex, facts, "CalculationBase", "CalculationBase", null, 0.75);
        AddDynamicVarFacts(source, sourceFile, ExtraDamageRegex, facts, "ExtraDamage", "ExtraDamage", null, 0.75);
        AddDynamicVarFacts(source, sourceFile, CardsVarRegex, facts, "Cards", "Cards", null, 0.82);
        AddDynamicVarFacts(source, sourceFile, StarsVarRegex, facts, "Stars", "Stars", null, 0.82);
        AddDynamicVarFacts(source, sourceFile, ForgeVarRegex, facts, "Forge", "Forge", null, 0.82);
        AddDynamicVarFacts(source, sourceFile, HpLossVarRegex, facts, "HpLoss", "HpLoss", null, 0.75);

        foreach (Match match in EnergyVarRegex.Matches(source))
        {
            string name = match.Groups["name"].Success && !string.IsNullOrWhiteSpace(match.Groups["name"].Value)
                ? match.Groups["name"].Value
                : "Energy";
            string factName = match.Groups["name"].Success && !string.IsNullOrWhiteSpace(match.Groups["name"].Value)
                ? match.Groups["name"].Value
                : name;
            facts.Add(new DynamicVarFact(
                factName,
                "Energy",
                ParseDecimal(match.Groups["amount"].Value),
                null,
                Evidence(source, sourceFile, match, 0.82)));
        }

        foreach (Match match in PowerVarRegex.Matches(source))
        {
            string power = match.Groups["power"].Value;
            string name = ToDynamicVarKey(power);
            facts.Add(new DynamicVarFact(
                name,
                "Power",
                ParseDecimal(match.Groups["amount"].Value),
                $"power:{name}",
                Evidence(source, sourceFile, match, 0.78)));
        }

        foreach (Match match in GenericDynamicVarRegex.Matches(source))
        {
            string name = match.Groups["name"].Value;
            facts.Add(new DynamicVarFact(
                name,
                "Dynamic",
                ParseDecimal(match.Groups["amount"].Value),
                null,
                Evidence(source, sourceFile, match, 0.8)));
        }

        return facts
            .OrderBy(fact => fact.Evidence.Line ?? int.MaxValue)
            .ThenBy(fact => fact.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddDynamicVarFacts(
        string source,
        string sourceFile,
        Regex regex,
        List<DynamicVarFact> facts,
        string name,
        string kind,
        string? parameter,
        double confidence)
    {
        foreach (Match match in regex.Matches(source))
        {
            string factName = match.Groups["name"].Success && !string.IsNullOrWhiteSpace(match.Groups["name"].Value)
                ? match.Groups["name"].Value
                : name;
            facts.Add(new DynamicVarFact(
                factName,
                kind,
                ParseDecimal(match.Groups["amount"].Value),
                parameter,
                Evidence(source, sourceFile, match, confidence)));
        }
    }

    private static IReadOnlyList<UpgradeOperationFact> ParseUpgradeOperationFacts(
        string source,
        string sourceFile,
        List<CardRawOperation> rawOperations)
    {
        List<UpgradeOperationFact> operations = [];

        foreach (Match match in UpgradeRegex.Matches(source))
        {
            string name = match.Groups["name"].Success
                ? match.Groups["name"].Value
                : match.Groups["indexName"].Value;
            operations.Add(new UpgradeOperationFact(
                "upgradeDynamicVar",
                name,
                ParseDecimal(match.Groups["amount"].Value),
                null,
                null,
                Evidence(source, sourceFile, match, 0.9)));
        }

        foreach (Match match in EnergyCostUpgradeRegex.Matches(source))
        {
            operations.Add(new UpgradeOperationFact(
                "upgradeCost",
                "EnergyCost",
                ParseDecimal(match.Groups["amount"].Value),
                null,
                null,
                Evidence(source, sourceFile, match, 0.9)));
        }

        foreach (Match match in AddKeywordRegex.Matches(source))
        {
            operations.Add(new UpgradeOperationFact(
                "addKeyword",
                match.Groups["keyword"].Value,
                null,
                null,
                null,
                Evidence(source, sourceFile, match, 0.85)));
        }

        foreach (Match match in RemoveKeywordRegex.Matches(source))
        {
            operations.Add(new UpgradeOperationFact(
                "removeKeyword",
                match.Groups["keyword"].Value,
                null,
                null,
                null,
                Evidence(source, sourceFile, match, 0.85)));
        }

        foreach (Match branch in IsUpgradedBranchRegex.Matches(source))
        {
            rawOperations.Add(Raw("isUpgradedBranch", source, sourceFile, branch, "condition:base.IsUpgraded"));
            foreach (Match upgrade in UpgradeGeneratedCardsRegex.Matches(branch.Groups["body"].Value))
            {
                int absoluteIndex = branch.Groups["body"].Index + upgrade.Index;
                Match absoluteMatch = Regex.Match(source[absoluteIndex..], Regex.Escape(upgrade.Value), RegexOptions.Compiled);
                Match evidenceMatch = absoluteMatch.Success
                    ? Regex.Match(source, Regex.Escape(upgrade.Value), RegexOptions.Compiled)
                    : branch;
                operations.Add(new UpgradeOperationFact(
                    "upgradeGeneratedCards",
                    "generatedCards",
                    null,
                    $"target:{upgrade.Groups["target"].Value.Trim()}",
                    "base.IsUpgraded",
                    Evidence(source, sourceFile, evidenceMatch.Success ? evidenceMatch : branch, 0.75)));
            }
        }

        return operations
            .OrderBy(operation => operation.Evidence.Line ?? int.MaxValue)
            .ThenBy(operation => operation.Kind, StringComparer.Ordinal)
            .ToArray();
    }

    private static int? ParseHitCount(string source)
    {
        Match match = HitCountRegex.Match(source);
        return match.Success && int.TryParse(match.Groups["count"].Value, out int hitCount)
            ? hitCount
            : null;
    }

    private static void AddDamageActions(
        string source,
        string sourceFile,
        string? targetType,
        int? hitCount,
        bool isXCost,
        List<CardActionFact> actions)
    {
        string kind = isXCost ? "xCostDamage" : "damage";
        foreach (Match match in DamageVarRegex.Matches(source))
        {
            actions.Add(Action(
                kind,
                source,
                sourceFile,
                match,
                ParseDecimal(match.Groups["amount"].Value),
                "Damage",
                isXCost ? null : hitCount,
                targetType,
                isXCost ? "energyX" : null,
                isXCost ? "DamageVar + ResolveEnergyXValue" : "DamageVar",
                isXCost ? 0.65 : 0.85));
        }

        foreach (Match match in CalculationBaseRegex.Matches(source))
        {
            actions.Add(Action(
                kind,
                source,
                sourceFile,
                match,
                ParseDecimal(match.Groups["amount"].Value),
                "CalculationBase",
                isXCost ? null : hitCount,
                targetType,
                isXCost ? "energyX" : "calculationBase",
                isXCost ? "CalculationBaseVar + ResolveEnergyXValue" : "CalculationBaseVar",
                isXCost ? 0.6 : 0.75));
        }
    }

    private static void AddBlockActions(
        string source,
        string sourceFile,
        string? targetType,
        List<CardActionFact> actions)
    {
        foreach (Match match in BlockVarRegex.Matches(source).Where(IsDefaultBlockVar))
        {
            actions.Add(Action(
                "block",
                source,
                sourceFile,
                match,
                ParseDecimal(match.Groups["amount"].Value),
                "Block",
                null,
                targetType,
                null,
                "BlockVar",
                0.85));
        }
    }

    private static void AddScalingActions(
        string source,
        string sourceFile,
        string? targetType,
        List<CardActionFact> actions)
    {
        Match extraDamage = ExtraDamageRegex.Match(source);
        if (!extraDamage.Success)
        {
            return;
        }

        Match tag = TagContainsRegex.Match(source);
        string kind = tag.Success ? "scalingDamagePerCardTag" : "scalingDamage";
        string parameter = tag.Success ? $"cardTag:{tag.Groups["tag"].Value}" : "calculatedMultiplier";
        actions.Add(Action(
            kind,
            source,
            sourceFile,
            extraDamage,
            ParseDecimal(extraDamage.Groups["amount"].Value),
            "ExtraDamage",
            null,
            targetType,
            parameter,
            tag.Success ? "ExtraDamageVar + CardTag multiplier" : "ExtraDamageVar + CalculatedDamageVar",
            tag.Success ? 0.75 : 0.5));
    }

    private static void AddResourceActions(
        string source,
        string sourceFile,
        string? targetType,
        IReadOnlyList<DynamicVarFact> dynamicVars,
        List<CardActionFact> actions)
    {
        Dictionary<string, DynamicVarFact> varsByName = dynamicVars
            .GroupBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> localVarMap = ParseLocalDynamicVarAssignments(source);

        if (source.Contains("CardPileCmd.Draw(", StringComparison.Ordinal))
        {
            foreach (Match match in CardsVarRegex.Matches(source))
            {
                actions.Add(Action("draw", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Cards", null, targetType, null, "CardsVar + CardPileCmd.Draw", 0.82));
            }

            foreach (Match match in DrawLiteralRegex.Matches(source))
            {
                actions.Add(Action("draw", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), null, null, targetType, "literal", "CardPileCmd.Draw literal", 0.72));
            }
        }

        if (source.Contains("PowerCmd.Apply<DrawCardsNextTurnPower>", StringComparison.Ordinal))
        {
            foreach (Match match in CardsVarRegex.Matches(source))
            {
                actions.Add(Action("drawNextTurn", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Cards", null, targetType, null, "CardsVar + DrawCardsNextTurnPower", 0.78));
            }

            foreach (Match match in DrawNextTurnLiteralRegex.Matches(source))
            {
                actions.Add(Action("drawNextTurn", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), null, null, targetType, "literal", "DrawCardsNextTurnPower literal", 0.72));
            }
        }

        if (source.Contains("PlayerCmd.GainEnergy(", StringComparison.Ordinal))
        {
            foreach (Match match in EnergyVarRegex.Matches(source).Where(IsDefaultDynamicVar))
            {
                actions.Add(Action("energyGain", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Energy", null, targetType, null, "EnergyVar + PlayerCmd.GainEnergy", 0.82));
            }
        }

        if (source.Contains("PlayerCmd.GainStars(", StringComparison.Ordinal))
        {
            foreach (Match match in StarsVarRegex.Matches(source))
            {
                actions.Add(Action("starGain", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Stars", null, targetType, null, "StarsVar + PlayerCmd.GainStars", 0.82));
            }
        }

        if (source.Contains("PowerCmd.Apply<StarNextTurnPower>", StringComparison.Ordinal))
        {
            if (StarNextTurnStarsVarApplyRegex.IsMatch(source))
            {
                foreach (Match match in StarsVarRegex.Matches(source))
                {
                    actions.Add(Action("starNextTurn", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Stars", null, targetType, null, "StarsVar + StarNextTurnPower", 0.78));
                }
            }

            if (StarNextTurnPowerVarApplyRegex.IsMatch(source))
            {
                foreach (Match match in PowerVarRegex.Matches(source).Where(match => string.Equals(match.Groups["power"].Value, "StarNextTurnPower", StringComparison.Ordinal)))
                {
                    actions.Add(Action("starNextTurn", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "StarNextTurnPower", null, targetType, null, "PowerVar<StarNextTurnPower> + StarNextTurnPower", 0.78));
                }
            }

            foreach (Match match in StarNextTurnLiteralRegex.Matches(source))
            {
                actions.Add(Action("starNextTurn", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), null, null, targetType, "literal", "StarNextTurnPower literal", 0.72));
            }
        }

        foreach (Match match in CanonicalStarCostRegex.Matches(source))
        {
            actions.Add(Action("starCost", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), null, null, targetType, null, "CanonicalStarCost", 0.92));
        }

        if (source.Contains("PowerCmd.Apply<EnergyNextTurnPower>", StringComparison.Ordinal))
        {
            foreach (Match match in EnergyVarRegex.Matches(source).Where(IsDefaultDynamicVar))
            {
                actions.Add(Action("energyNextTurn", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Energy", null, targetType, null, "EnergyVar + EnergyNextTurnPower", 0.78));
            }
        }

        foreach (Match match in AppliedPowerCallRegex.Matches(source)
            .Where(match => match.Groups["power"].Value == "BlockNextTurnPower"))
        {
            string? amountExpression = GetApplyAmountExpression(match.Groups["args"].Value);
            string dynamicVarName = ResolveDynamicVarName(amountExpression, localVarMap) ?? "BlockNextTurn";
            DynamicVarFact? dynamicVar = ResolveDynamicVarFact(dynamicVarName, varsByName);
            decimal? amount = ParseLiteralAmount(amountExpression) ?? dynamicVar?.Amount;
            actions.Add(Action(
                "blockNextTurn",
                source,
                sourceFile,
                match,
                amount,
                dynamicVarName,
                null,
                targetType,
                "power:BlockNextTurn",
                amount.HasValue ? "BlockVar + BlockNextTurnPower" : "PowerCmd.Apply<BlockNextTurnPower>",
                amount.HasValue ? 0.78 : 0.5));
        }

        foreach (Match match in HpLossVarRegex.Matches(source))
        {
            actions.Add(Action("hpLoss", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "HpLoss", null, "Self", null, "HpLossVar", 0.75));
        }
    }

    private static void AddForgeActions(
        string source,
        string sourceFile,
        string? targetType,
        List<CardActionFact> actions)
    {
        if (!source.Contains("ForgeCmd.Forge(", StringComparison.Ordinal))
        {
            return;
        }

        foreach (Match match in ForgeVarRegex.Matches(source))
        {
            actions.Add(Action("forge", source, sourceFile, match, ParseDecimal(match.Groups["amount"].Value), "Forge", null, targetType, null, "ForgeVar + ForgeCmd.Forge", 0.82));
        }

        if (!ForgeVarRegex.IsMatch(source) && source.Contains("CalculatedForge", StringComparison.Ordinal))
        {
            Match match = Regex.Match(source, "CalculatedForge", RegexOptions.Compiled);
            actions.Add(Action("forge", source, sourceFile, match, 0m, null, null, targetType, "calculatedForge", "CalculatedForge + ForgeCmd.Forge", 0.45));
        }
    }

    private static void AddPowerActions(
        string source,
        string sourceFile,
        string? targetType,
        IReadOnlyList<DynamicVarFact> dynamicVars,
        List<CardActionFact> actions)
    {
        Dictionary<string, DynamicVarFact> varsByName = dynamicVars
            .GroupBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> localVarMap = ParseLocalDynamicVarAssignments(source);
        HashSet<string> handledPowers = [];

        foreach (Match match in AppliedPowerCallRegex.Matches(source))
        {
            string power = match.Groups["power"].Value;
            if (power is "StarNextTurnPower" or "DrawCardsNextTurnPower" or "EnergyNextTurnPower" or "BlockNextTurnPower")
            {
                continue;
            }

            string defaultVarName = ToDynamicVarKey(power);
            string? amountExpression = GetApplyAmountExpression(match.Groups["args"].Value);
            string? dynamicVarName = ResolveDynamicVarName(amountExpression, localVarMap) ?? defaultVarName;
            DynamicVarFact? dynamicVar = ResolveDynamicVarFact(dynamicVarName, varsByName);
            decimal? amount = ParseLiteralAmount(amountExpression) ?? dynamicVar?.Amount;
            string kind = ToPowerActionKind(power);

            actions.Add(Action(
                kind,
                source,
                sourceFile,
                match,
                amount,
                dynamicVarName,
                null,
                targetType,
                dynamicVarName is null ? $"power:{defaultVarName}" : $"power:{defaultVarName};var:{dynamicVarName}",
                dynamicVar is null && amount is null ? $"PowerCmd.Apply<{power}>" : $"DynamicVar + PowerCmd.Apply<{power}>",
                dynamicVar is null && amount is null ? 0.5 : 0.78));
            handledPowers.Add(power);
        }

        foreach (Match match in AppliedPowerRegex.Matches(source))
        {
            string power = match.Groups["power"].Value;
            if (power is "StarNextTurnPower" or "DrawCardsNextTurnPower" or "EnergyNextTurnPower" or "BlockNextTurnPower")
            {
                continue;
            }

            if (handledPowers.Contains(power))
            {
                continue;
            }

            string dynamicVarKey = ToDynamicVarKey(power);
            actions.Add(Action(
                ToPowerActionKind(power),
                source,
                sourceFile,
                match,
                null,
                null,
                null,
                targetType,
                $"power:{dynamicVarKey}",
                $"PowerCmd.Apply<{power}>",
                0.5));
        }
    }

    private static string? GetApplyAmountExpression(string args)
    {
        IReadOnlyList<string> parts = SplitArguments(args);
        return parts.Count >= 3 ? parts[2].Trim() : null;
    }

    private static IReadOnlyList<string> SplitArguments(string args)
    {
        List<string> parts = [];
        int start = 0;
        int depth = 0;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c is '(' or '[' or '<')
            {
                depth++;
            }
            else if (c is ')' or ']' or '>')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(args[start..i]);
                start = i + 1;
            }
        }

        parts.Add(args[start..]);
        return parts;
    }

    private static Dictionary<string, string> ParseLocalDynamicVarAssignments(string source)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (Match match in LocalDynamicVarAssignmentRegex.Matches(source))
        {
            string varName = match.Groups["dot"].Success
                ? match.Groups["dot"].Value
                : match.Groups["index"].Value;
            map[match.Groups["name"].Value] = varName;
        }

        return map;
    }

    private static string? ResolveDynamicVarName(string? expression, IReadOnlyDictionary<string, string> localVarMap)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        string trimmed = expression.Trim();
        if (localVarMap.TryGetValue(trimmed, out string? localVarName))
        {
            return localVarName;
        }

        Match match = Regex.Match(trimmed, @"DynamicVars(?:\.(?<dot>[A-Za-z0-9_]+)|\[""(?<index>[^""]+)""\])", RegexOptions.Compiled);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["dot"].Success ? match.Groups["dot"].Value : match.Groups["index"].Value;
    }

    private static DynamicVarFact? ResolveDynamicVarFact(
        string? dynamicVarName,
        IReadOnlyDictionary<string, DynamicVarFact> varsByName)
    {
        if (dynamicVarName is null)
        {
            return null;
        }

        if (varsByName.TryGetValue(dynamicVarName, out DynamicVarFact? fact))
        {
            return fact;
        }

        string normalized = ToDynamicVarKey(dynamicVarName);
        return varsByName.TryGetValue(normalized, out DynamicVarFact? normalizedFact)
            ? normalizedFact
            : null;
    }

    private static decimal? ParseLiteralAmount(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        string trimmed = expression.Trim().TrimEnd('m', 'M');
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    private static IReadOnlyList<string> ParseCanonicalKeywords(string source)
    {
        return CanonicalKeywordsRegex.Matches(source)
            .SelectMany(match => CardKeywordRegex.Matches(match.Groups["body"].Value)
                .Select(keywordMatch => keywordMatch.Groups["keyword"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseTags(string source)
    {
        return CardTagRegex.Matches(source)
            .Select(match => match.Groups["tag"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddUnsupportedCardOperationFacts(
        string source,
        string sourceFile,
        string? targetType,
        bool isXCost,
        IReadOnlyList<DynamicVarFact> dynamicVars,
        List<CardActionFact> actions,
        List<CardRawOperation> rawOperations)
    {
        Dictionary<string, DynamicVarFact> varsByName = dynamicVars
            .GroupBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        SelectionContext? selection = null;
        foreach (Match match in SelectPileRegex.Matches(source))
        {
            string pile = match.Groups["pile"].Value;
            SelectionAmount selectionAmount = ResolveSelectionAmount(match.Groups["args"].Value, varsByName);
            selection = new SelectionContext(NormalizePileName(pile), selectionAmount.Amount, selectionAmount.DynamicVarName);
            string parameter = $"from:{selection.FromPile}";
            actions.Add(Action("selectCards", source, sourceFile, match, selection.Amount, selection.DynamicVarName, null, targetType, parameter, "CardSelectCmd.FromCombatPile", 0.65));
            rawOperations.Add(Raw("selectCards", source, sourceFile, match, parameter));
        }

        foreach (Match match in SelectHandRegex.Matches(source))
        {
            SelectionAmount selectionAmount = ResolveSelectionAmount(match.Groups["args"].Value, varsByName);
            selection = new SelectionContext("Hand", selectionAmount.Amount, selectionAmount.DynamicVarName);
            string parameter = $"from:{selection.FromPile}";
            actions.Add(Action("selectCards", source, sourceFile, match, selection.Amount, selection.DynamicVarName, null, targetType, parameter, "CardSelectCmd.FromHand", 0.72));
            rawOperations.Add(Raw("selectCards", source, sourceFile, match, parameter));
        }

        foreach (Match match in ChooseCardRegex.Matches(source))
        {
            actions.Add(Action("selectCards", source, sourceFile, match, null, null, null, targetType, "screen:chooseACard", "CardSelectCmd.FromChooseACardScreen", 0.65));
            rawOperations.Add(Raw("selectCards", source, sourceFile, match, "screen:chooseACard"));
        }

        foreach (Match match in TransformRegex.Matches(source))
        {
            string card = match.Groups["card"].Value;
            string parameter = selection is null
                ? $"card:{card}"
                : $"from:{selection.FromPile};card:{card}";
            actions.Add(Action("transformCard", source, sourceFile, match, selection?.Amount ?? 1m, selection?.DynamicVarName, null, targetType, parameter, "CardCmd.TransformTo", 0.75));
            rawOperations.Add(Raw("transformCard", source, sourceFile, match, parameter));
        }

        foreach (Match match in RandomTransformRegex.Matches(source))
        {
            string parameter = selection is null
                ? "card:SIM.TRANSFORMED_CARD"
                : $"from:{selection.FromPile};card:SIM.TRANSFORMED_CARD";
            actions.Add(Action("transformCard", source, sourceFile, match, selection?.Amount ?? 1m, selection?.DynamicVarName, null, targetType, parameter, "CardCmd.Transform", 0.55));
            rawOperations.Add(Raw("transformCard", source, sourceFile, match, parameter));
        }

        foreach (Match match in CardPoolGenerationRegex.Matches(source))
        {
            string pool = match.Groups["pool"].Value;
            string count = match.Groups["count"].Value;
            actions.Add(Action("createCardChoices", source, sourceFile, match, ParseDecimal(count), null, null, targetType, $"pool:{pool};count:{count}", "CardFactory.GetDistinctForCombat", 0.7));
            rawOperations.Add(Raw("createCardChoices", source, sourceFile, match, $"pool:{pool};count:{count}"));
        }

        foreach (Match match in GeneratedCardRegex.Matches(source))
        {
            string card = match.Groups["card"].Value;
            string pile = match.Groups["pile"].Value;
            actions.Add(Action("createCard", source, sourceFile, match, 1m, null, null, targetType, $"card:{card};pile:{pile}", "CardPileCmd.AddGeneratedCardToCombat", 0.75));
            rawOperations.Add(Raw("createCard", source, sourceFile, match, $"card:{card};pile:{pile}"));
        }

        foreach (Match match in AddGeneratedSelectionRegex.Matches(source))
        {
            string card = match.Groups["card"].Value;
            string pile = match.Groups["pile"].Value;
            actions.Add(Action("createCard", source, sourceFile, match, 1m, null, null, targetType, $"source:{card};pile:{pile}", "CardPileCmd.AddGeneratedCardToCombat", 0.7));
            rawOperations.Add(Raw("createCard", source, sourceFile, match, $"source:{card};pile:{pile}"));
        }

        foreach (Match match in MoveToPileRegex.Matches(source))
        {
            string pile = NormalizePileName(match.Groups["pile"].Value);
            string? position = match.Groups["position"].Success ? match.Groups["position"].Value : null;
            string parameter = selection is null
                ? position is null ? $"to:{pile}" : $"to:{pile};position:{position}"
                : position is null ? $"from:{selection.FromPile};to:{pile}" : $"from:{selection.FromPile};to:{pile};position:{position}";
            actions.Add(Action("moveCardBetweenPiles", source, sourceFile, match, selection?.Amount, selection?.DynamicVarName, null, targetType, parameter, "CardPileCmd.Add", 0.68));
            rawOperations.Add(Raw("moveCardBetweenPiles", source, sourceFile, match, parameter));
        }

        foreach (Match match in DiscardRegex.Matches(source))
        {
            string parameter = selection is null
                ? "from:Hand;to:Discard"
                : $"from:{selection.FromPile};to:Discard";
            actions.Add(Action("moveCardBetweenPiles", source, sourceFile, match, selection?.Amount ?? 1m, selection?.DynamicVarName, null, targetType, parameter, "CardCmd.Discard", 0.65));
            rawOperations.Add(Raw("moveCardBetweenPiles", source, sourceFile, match, parameter));
        }

        foreach (Match match in ExhaustRegex.Matches(source))
        {
            string parameter = selection is null
                ? "from:Hand;to:Exhaust"
                : $"from:{selection.FromPile};to:Exhaust";
            actions.Add(Action("moveCardBetweenPiles", source, sourceFile, match, selection?.Amount ?? 1m, selection?.DynamicVarName, null, targetType, parameter, "CardCmd.Exhaust", 0.65));
            rawOperations.Add(Raw("moveCardBetweenPiles", source, sourceFile, match, parameter));
        }

        if (isXCost)
        {
            Match match = Regex.Match(source, "ResolveEnergyXValue\\(\\)|HasEnergyCostX", RegexOptions.Compiled);
            if (match.Success)
            {
                rawOperations.Add(Raw("xCost", source, sourceFile, match, "energyX"));
            }
        }
    }

    private static SelectionAmount ResolveSelectionAmount(
        string expression,
        IReadOnlyDictionary<string, DynamicVarFact> varsByName)
    {
        string? dynamicVarName = ResolveDynamicVarName(expression, new Dictionary<string, string>(StringComparer.Ordinal));
        DynamicVarFact? dynamicVar = ResolveDynamicVarFact(dynamicVarName, varsByName);
        if (dynamicVar is not null)
        {
            return new SelectionAmount(dynamicVar.Amount, dynamicVarName);
        }

        Match countMatch = Regex.Match(
            expression,
            @"CardSelectorPrefs\([^,]+,\s*(?<amount>-?[0-9]+)",
            RegexOptions.Singleline);
        if (countMatch.Success)
        {
            return new SelectionAmount(ParseDecimal(countMatch.Groups["amount"].Value), null);
        }

        return new SelectionAmount(1m, null);
    }

    private static string NormalizePileName(string pile)
    {
        return pile switch
        {
            "Draw" or "DrawPile" => "Draw",
            "Discard" or "DiscardPile" => "Discard",
            "Exhaust" or "ExhaustPile" => "Exhaust",
            _ => pile
        };
    }

    private static void AddPersistentPowerTriggerFacts(
        IReadOnlyDictionary<string, string>? relatedPowerSources,
        IReadOnlyDictionary<string, string>? relatedPowerSourceFiles,
        List<CardActionFact> actions)
    {
        if (relatedPowerSources is null)
        {
            return;
        }

        foreach ((string powerName, string powerSource) in relatedPowerSources)
        {
            string dynamicVarKey = ToDynamicVarKey(powerName);
            CardActionFact? appliedPower = actions.FirstOrDefault(action => IsPowerActionFor(action, dynamicVarKey));
            string sourceFile = relatedPowerSourceFiles is not null && relatedPowerSourceFiles.TryGetValue(powerName, out string? file)
                ? file
                : $"{powerName}.cs";

            if (powerSource.Contains("AfterStarsSpent", StringComparison.Ordinal)
                && powerSource.Contains("GainBlock", StringComparison.Ordinal))
            {
                Match match = Regex.Match(powerSource, "AfterStarsSpent|GainBlock", RegexOptions.Compiled);
                actions.Add(PersistentPowerTrigger(
                    appliedPower,
                    "Self",
                    "AfterStarsSpent:gainBlockPerStarSpent",
                    $"{powerName}.AfterStarsSpent",
                    powerSource,
                    sourceFile,
                    match,
                    0.75));
            }

            if (powerSource.Contains("AfterCardPlayed", StringComparison.Ordinal)
                && powerSource.Contains("StarsSpent", StringComparison.Ordinal)
                && powerSource.Contains("DealDamageToAllEnemies", StringComparison.Ordinal))
            {
                Match match = Regex.Match(powerSource, "AfterCardPlayed|StarsSpent|DealDamageToAllEnemies", RegexOptions.Compiled);
                actions.Add(PersistentPowerTrigger(
                    appliedPower,
                    "AllEnemies",
                    "AfterCardPlayed:damageAllEnemiesOnStarSpent",
                    $"{powerName}.AfterCardPlayed",
                    powerSource,
                    sourceFile,
                    match,
                    0.75));
            }

            if (powerSource.Contains("AfterStarsGained", StringComparison.Ordinal)
                && powerSource.Contains("DealDamageToAllEnemies", StringComparison.Ordinal))
            {
                Match match = Regex.Match(powerSource, "AfterStarsGained|DealDamageToAllEnemies", RegexOptions.Compiled);
                actions.Add(PersistentPowerTrigger(
                    appliedPower,
                    "AllEnemies",
                    "AfterStarsGained:damageAllEnemiesOnStarGained",
                    $"{powerName}.AfterStarsGained",
                    powerSource,
                    sourceFile,
                    match,
                    0.75));
            }
        }
    }

    private static bool IsPowerActionFor(CardActionFact action, string dynamicVarKey)
    {
        const string prefix = "power:";
        if (action.Kind != "power" || action.Parameter is null || !action.Parameter.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string powerKey = action.Parameter[prefix.Length..];
        int separator = powerKey.IndexOf(';', StringComparison.Ordinal);
        if (separator >= 0)
        {
            powerKey = powerKey[..separator];
        }

        return string.Equals(powerKey, dynamicVarKey, StringComparison.Ordinal);
    }

    private static CardActionFact PersistentPowerTrigger(
        CardActionFact? appliedPower,
        string targetType,
        string parameter,
        string source,
        string powerSource,
        string sourceFile,
        Match match,
        double confidence)
    {
        return new CardActionFact(
            "persistentPowerTrigger",
            appliedPower?.Amount,
            appliedPower?.DynamicVarName,
            null,
            targetType,
            parameter,
            source,
            Evidence(powerSource, sourceFile, match, confidence),
            confidence);
    }

    private static CardActionFact Action(
        string kind,
        string source,
        string sourceFile,
        Match match,
        decimal? amount,
        string? dynamicVarName,
        int? hitCount,
        string? targetType,
        string? parameter,
        string actionSource,
        double confidence)
    {
        return new CardActionFact(
            kind,
            amount,
            dynamicVarName,
            hitCount,
            targetType,
            parameter,
            actionSource,
            Evidence(source, sourceFile, match, confidence),
            confidence);
    }

    private static CardRawOperation Raw(
        string kind,
        string source,
        string sourceFile,
        Match match,
        string? parameter)
    {
        return new CardRawOperation(
            kind,
            SingleLine(match.Value),
            parameter,
            Evidence(source, sourceFile, match, 0.65));
    }

    private static SourceEvidence Evidence(string source, string sourceFile, Match match, double confidence)
    {
        return new SourceEvidence(
            sourceFile,
            MethodAt(source, match.Index),
            LineNumber(source, match.Index),
            SingleLine(match.Value),
            confidence);
    }

    private static string? MethodAt(string source, int index)
    {
        string prefix = source[..Math.Clamp(index, 0, source.Length)];
        MatchCollection matches = Regex.Matches(
            prefix,
            @"(?:public|protected|private)\s+(?:override\s+)?(?:async\s+)?(?:Task|void|int|bool|IEnumerable<[^>]+>|[A-Za-z0-9_<>?]+)\s+(?<name>[A-Za-z0-9_]+)\s*\(",
            RegexOptions.Singleline);
        return matches.Count == 0 ? null : matches[^1].Groups["name"].Value;
    }

    private static int LineNumber(string source, int index)
    {
        int line = 1;
        int end = Math.Clamp(index, 0, source.Length);
        for (int i = 0; i < end; i++)
        {
            if (source[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string SingleLine(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ");
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

    private static bool IsDefaultBlockVar(Match match)
    {
        return !match.Groups["name"].Success
            || string.IsNullOrWhiteSpace(match.Groups["name"].Value)
            || string.Equals(match.Groups["name"].Value, "Block", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToDynamicVarKey(string power)
    {
        return power.EndsWith("Power", StringComparison.Ordinal)
            ? power[..^"Power".Length]
            : power;
    }

    private static string ToPowerActionKind(string power)
    {
        return power switch
        {
            "VulnerablePower" => "debuffVulnerable",
            "WeakPower" => "debuffWeak",
            "PoisonPower" => "debuffPoison",
            _ => "power"
        };
    }

    private static double Confidence(
        IReadOnlyList<CardActionFact> actions,
        IReadOnlyList<CardRawOperation> rawOperations,
        IReadOnlyList<string> unresolved)
    {
        if (actions.Count == 0 && rawOperations.Count == 0)
        {
            return unresolved.Count > 0 ? 0.2 : 0.7;
        }

        double confidence = actions.Count > 0
            ? actions.Min(action => action.Confidence)
            : rawOperations.Min(operation => operation.Evidence.Confidence);
        if (rawOperations.Count > 0)
        {
            confidence = Math.Min(confidence, 0.65);
        }

        if (unresolved.Count > 0)
        {
            confidence = Math.Min(confidence, 0.4);
        }

        return Math.Round(confidence, 3);
    }

    private sealed record SelectionAmount(decimal? Amount, string? DynamicVarName);

    private sealed record SelectionContext(string FromPile, decimal? Amount, string? DynamicVarName);

    private sealed record CardHeader(int? Cost, string? CardType, string? Rarity, string? TargetType);
}
