using System.Text.RegularExpressions;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class EncounterPatternParser
{
    private static readonly Regex ClassNamePattern = new(
        @"class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?:ActModel|EncounterModel)",
        RegexOptions.Compiled);

    private static readonly Regex EncounterPattern = new(
        @"ModelDb\.Encounter<(?<type>[A-Za-z_][A-Za-z0-9_]*)>\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex MonsterPattern = new(
        @"ModelDb\.Monster<(?<type>[A-Za-z_][A-Za-z0-9_]*)>\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex RoomTypePattern = new(
        @"RoomType\s*=>\s*RoomType\.(?<room>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex IsWeakPattern = new(
        @"IsWeak\s*=>\s*true",
        RegexOptions.Compiled);

    private static readonly Regex IsDebugPattern = new(
        @"IsDebugEncounter\s*=>\s*true",
        RegexOptions.Compiled);

    private static readonly Regex TagPattern = new(
        @"EncounterTag\.(?<tag>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex DirectMonsterSlotPattern = new(
        @"\(\s*ModelDb\.Monster<(?<type>[A-Za-z_][A-Za-z0-9_]*)>\s*\(\)\.ToMutable\(\)\s*,\s*(?<slot>[^)\r\n]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex LocalMonsterVariablePattern = new(
        @"(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\([^)]*\)\s*ModelDb\.Monster<(?<type>[A-Za-z_][A-Za-z0-9_]*)>\s*\(\)\.ToMutable\(\)",
        RegexOptions.Compiled);

    private static readonly Regex VariableMonsterSlotPattern = new(
        @"\(\s*(?<var>[a-z_][A-Za-z0-9_]*)\s*,\s*(?<slot>[^)\r\n]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex DirectRandomSlotPattern = new(
        @"\(\s*base\.Rng\.NextItem\((?<collection>[A-Za-z_][A-Za-z0-9_]*)\)\.ToMutable\(\)\s*,\s*(?<slot>[^)\r\n]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex RandomVariableSlotPattern = new(
        @"\(\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\.ToMutable\(\)\s*,\s*(?<slot>[^)\r\n]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex SwitchRandomSlotPattern = new(
        @"\(\(\s*[A-Za-z_][A-Za-z0-9_]*\s+switch\s*\{[\s\S]*?\}\)\.ToMutable\(\)\s*,\s*(?<slot>[^)\r\n]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex DirectMonsterAddPattern = new(
        @"\.Add\(ModelDb\.Monster<(?<type>[A-Za-z_][A-Za-z0-9_]*)>\s*\(\)\)",
        RegexOptions.Compiled);

    private static readonly Regex LoopPattern = new(
        @"for\s*\([^;]+;(?<condition>[^;]+);[^)]*\)\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex LoopCountPattern = new(
        @"<\s*(?<count>[0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex LocalCollectionFromKeysPattern = new(
        @"List<MonsterModel>\s+(?<local>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<collection>[A-Za-z_][A-Za-z0-9_]*)\.Keys",
        RegexOptions.Compiled);

    private static readonly Regex LocalRandomMonsterPattern = new(
        @"(?<monster>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*base\.Rng\.NextItem\((?<source>[A-Za-z_][A-Za-z0-9_]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex LocalListFromCollectionPattern = new(
        @"List<MonsterModel>\s+(?<local>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<collection>[A-Za-z_][A-Za-z0-9_]*)\.ToList\(\)",
        RegexOptions.Compiled);

    private static readonly Regex RandomVariableAddPattern = new(
        @"\.Add\(\(\s*(?<monster>[A-Za-z_][A-Za-z0-9_]*)\.ToMutable\(\)\s*,\s*(?<slot>[^)\r\n]+)\)\)",
        RegexOptions.Compiled);

    private static readonly Regex MonsterCollectionDeclarationPattern = new(
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=>|=)\s*new\s+(?:MonsterModel\s*\[[^\]]*\]|Dictionary<MonsterModel,\s*int>)",
        RegexOptions.Compiled);

    public EncounterActSourceEntry ParseActSource(string fallbackActTypeName, string source)
    {
        string actTypeName = ParseClassName(source, fallbackActTypeName);
        int actIndex = ParseIntExpressionProperty(source, "Index") ?? -1;
        int weakEncounters = ParseIntExpressionProperty(source, "NumberOfWeakEncounters") ?? 0;
        int rooms = ParseIntExpressionProperty(source, "BaseNumberOfRooms") ?? 0;
        bool isDefault = ParseBoolExpressionProperty(source, "IsDefault") ?? false;

        IReadOnlyList<string> encounters = ExtractBracedBlockAfter(source, "GenerateAllEncounters")
            is { } body
            ? EncounterPattern.Matches(body)
                .Select(match => match.Groups["type"].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        return new EncounterActSourceEntry(
            actTypeName,
            actIndex,
            actIndex >= 0 ? actIndex + 1 : -1,
            isDefault,
            weakEncounters,
            rooms,
            encounters);
    }

    public EncounterPatternEntry ParseEncounterSource(
        ModelCatalogEntry encounter,
        IReadOnlyList<EncounterActReference> acts,
        string source)
    {
        string roomType = RoomTypePattern.Match(source) is { Success: true } roomMatch
            ? roomMatch.Groups["room"].Value
            : "Unknown";
        bool isWeak = IsWeakPattern.IsMatch(source);
        bool isDebug = IsDebugPattern.IsMatch(source);
        IReadOnlyList<string> tags = TagPattern.Matches(source)
            .Select(match => match.Groups["tag"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<string> possibleMonsters = MonsterPattern.Matches(source)
            .Select(match => match.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        List<string> warnings = [];
        string category = Categorize(encounter.TypeName, roomType, isWeak, isDebug);
        string? generateMonstersBody = ExtractBracedBlockAfter(source, "GenerateMonsters");
        IReadOnlyList<EncounterMonsterSlot> slots = generateMonstersBody is null
            ? []
            : ParseMonsterSlots(generateMonstersBody, source, possibleMonsters, warnings);

        if (generateMonstersBody is null)
        {
            warnings.Add("GenerateMonsters body was not parsed.");
        }
        else if (slots.Count == 0 && generateMonstersBody.Contains("ToMutable()", StringComparison.Ordinal))
        {
            warnings.Add("GenerateMonsters contains monster construction, but no supported v1 slot pattern was parsed.");
        }

        bool hasVariableMonsterCount = warnings.Any(
            warning => warning.Contains("variable monster count", StringComparison.Ordinal));
        bool hasConditionalMonsterSelection = hasVariableMonsterCount
            || slots.Any(slot => slot.MonsterTypeName is null || slot.PossibleMonsterTypeNames.Count > 1);
        if (hasConditionalMonsterSelection)
        {
            warnings.Add("Encounter has conditional or random monster selection; use possibleMonsterTypeNames for review.");
        }

        if (generateMonstersBody is not null
            && generateMonstersBody.Contains("base.Rng.", StringComparison.Ordinal)
            && !hasConditionalMonsterSelection)
        {
            warnings.Add("GenerateMonsters uses RNG for non-composition setup, such as initial move or state.");
        }

        if (acts.Count == 0 && category is not ("Event" or "Debug"))
        {
            warnings.Add("Encounter was not found in any parsed Act.GenerateAllEncounters list.");
        }

        double confidence = 0.95;
        if (generateMonstersBody is null || slots.Count == 0)
        {
            confidence = Math.Min(confidence, 0.35);
        }

        if (hasConditionalMonsterSelection)
        {
            confidence = Math.Min(confidence, 0.65);
        }

        if (warnings.Count > 0)
        {
            confidence = Math.Min(confidence, 0.8);
        }

        return new EncounterPatternEntry(
            encounter.ModelId,
            encounter.TypeName,
            encounter.FullTypeName,
            acts,
            roomType,
            isWeak,
            category,
            tags,
            slots,
            possibleMonsters,
            slots.Count > 0 && !hasVariableMonsterCount ? slots.Count : null,
            hasConditionalMonsterSelection,
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "ilspycmd decompiled C# encounter pattern parser v1",
            Math.Round(confidence, 3));
    }

    private static IReadOnlyList<EncounterMonsterSlot> ParseMonsterSlots(
        string body,
        string fullSource,
        IReadOnlyList<string> allPossibleMonsters,
        List<string> warnings)
    {
        IReadOnlyDictionary<string, string> variableTypes = ParseLocalMonsterVariables(body);
        IReadOnlyDictionary<string, IReadOnlyList<string>> collections = ParseMonsterCollections(fullSource);
        IReadOnlyDictionary<string, IReadOnlyList<string>> randomVariablePossibleMonsters =
            ParseRandomMonsterVariables(body, collections, allPossibleMonsters);
        IReadOnlyList<(int Start, int End)> loopRandomRanges = FindLoopRandomRanges(body);
        List<SlotCandidate> candidates = [];

        foreach (Match match in DirectMonsterSlotPattern.Matches(body))
        {
            string typeName = match.Groups["type"].Value;
            candidates.Add(new SlotCandidate(
                match.Index,
                NormalizeSlot(match.Groups["slot"].Value),
                typeName,
                [typeName],
                "GenerateMonsters direct ModelDb.Monster slot",
                0.9));
        }

        foreach (Match match in DirectRandomSlotPattern.Matches(body))
        {
            string collectionName = match.Groups["collection"].Value;
            IReadOnlyList<string> possible = collections.TryGetValue(collectionName, out IReadOnlyList<string>? collection)
                ? collection
                : allPossibleMonsters;
            candidates.Add(new SlotCandidate(
                match.Index,
                NormalizeSlot(match.Groups["slot"].Value),
                null,
                possible,
                $"GenerateMonsters random NextItem({collectionName}) slot",
                0.65));
        }

        foreach (Match match in RandomVariableSlotPattern.Matches(body))
        {
            if (loopRandomRanges.Any(range => match.Index >= range.Start && match.Index <= range.End))
            {
                continue;
            }

            string variableName = match.Groups["var"].Value;
            if (!randomVariablePossibleMonsters.TryGetValue(variableName, out IReadOnlyList<string>? possible))
            {
                continue;
            }

            candidates.Add(new SlotCandidate(
                match.Index,
                NormalizeSlot(match.Groups["slot"].Value),
                null,
                possible,
                $"GenerateMonsters random variable slot {variableName}",
                0.6));
        }

        foreach (Match match in SwitchRandomSlotPattern.Matches(body))
        {
            candidates.Add(new SlotCandidate(
                match.Index,
                NormalizeSlot(match.Groups["slot"].Value),
                null,
                allPossibleMonsters,
                "GenerateMonsters switch-selected monster slot",
                0.55));
        }

        foreach (Match match in DirectMonsterAddPattern.Matches(body))
        {
            string typeName = match.Groups["type"].Value;
            candidates.Add(new SlotCandidate(
                match.Index,
                null,
                typeName,
                [typeName],
                "GenerateMonsters list Add(ModelDb.Monster) slot",
                0.7));
        }

        foreach (Match match in VariableMonsterSlotPattern.Matches(body))
        {
            string variableName = match.Groups["var"].Value;
            if (!variableTypes.TryGetValue(variableName, out string? typeName))
            {
                continue;
            }

            candidates.Add(new SlotCandidate(
                match.Index,
                NormalizeSlot(match.Groups["slot"].Value),
                typeName,
                [typeName],
                $"GenerateMonsters local variable slot {variableName}",
                0.85));
        }

        AddLoopRandomSlots(body, collections, allPossibleMonsters, candidates, warnings);
        AddSwitchListProjectionSlots(body, allPossibleMonsters, candidates, warnings);

        return candidates
            .OrderBy(candidate => candidate.SourceIndex)
            .Select((candidate, index) => new EncounterMonsterSlot(
                index + 1,
                candidate.SlotName,
                candidate.MonsterTypeName,
                candidate.PossibleMonsterTypeNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                candidate.Source,
                candidate.Confidence))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseRandomMonsterVariables(
        string body,
        IReadOnlyDictionary<string, IReadOnlyList<string>> collections,
        IReadOnlyList<string> allPossibleMonsters)
    {
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach (Match match in LocalListFromCollectionPattern.Matches(body))
        {
            aliases[match.Groups["local"].Value] = match.Groups["collection"].Value;
        }

        Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.Ordinal);
        foreach (Match match in LocalRandomMonsterPattern.Matches(body))
        {
            string source = match.Groups["source"].Value;
            string collectionName = aliases.TryGetValue(source, out string? alias) ? alias : source;
            result[match.Groups["monster"].Value] = collections.TryGetValue(collectionName, out IReadOnlyList<string>? collection)
                ? collection
                : allPossibleMonsters;
        }

        return result;
    }

    private static void AddSwitchListProjectionSlots(
        string body,
        IReadOnlyList<string> allPossibleMonsters,
        List<SlotCandidate> candidates,
        List<string> warnings)
    {
        if (!body.Contains("switch", StringComparison.Ordinal)
            || !body.Contains(".Select((MonsterModel", StringComparison.Ordinal))
        {
            return;
        }

        HashSet<string> fixedMonsters = candidates
            .Where(candidate => candidate.MonsterTypeName is not null)
            .Select(candidate => candidate.MonsterTypeName!)
            .ToHashSet(StringComparer.Ordinal);
        string[] possibleVariableMonsters = allPossibleMonsters
            .Where(monster => !fixedMonsters.Contains(monster))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (possibleVariableMonsters.Length == 0)
        {
            possibleVariableMonsters = allPossibleMonsters.Order(StringComparer.Ordinal).ToArray();
        }

        candidates.Add(new SlotCandidate(
            body.IndexOf("switch", StringComparison.Ordinal),
            null,
            null,
            possibleVariableMonsters,
            "GenerateMonsters switch/list projection variable-count slot",
            0.35));
        warnings.Add("Switch/list based variable monster count requires manual review.");
    }

    private static void AddLoopRandomSlots(
        string body,
        IReadOnlyDictionary<string, IReadOnlyList<string>> collections,
        IReadOnlyList<string> allPossibleMonsters,
        List<SlotCandidate> candidates,
        List<string> warnings)
    {
        foreach (Match loopMatch in LoopPattern.Matches(body))
        {
            int openBrace = body.IndexOf('{', loopMatch.Index + loopMatch.Length - 1);
            int closeBrace = openBrace >= 0 ? FindMatchingDelimiter(body, openBrace, '{', '}') : -1;
            if (openBrace < 0 || closeBrace < 0)
            {
                continue;
            }

            string loopBody = body[(openBrace + 1)..closeBrace];
            Match countMatch = LoopCountPattern.Match(loopMatch.Groups["condition"].Value);
            Match addMatch = RandomVariableAddPattern.Match(loopBody);
            Match randomMatch = LocalRandomMonsterPattern.Match(loopBody);
            if (!countMatch.Success || !addMatch.Success || !randomMatch.Success)
            {
                continue;
            }

            if (!int.TryParse(countMatch.Groups["count"].Value, out int count) || count <= 0)
            {
                continue;
            }

            string randomSource = randomMatch.Groups["source"].Value;
            string collectionName = ResolveCollectionName(loopBody, randomSource);
            IReadOnlyList<string> possible = collections.TryGetValue(collectionName, out IReadOnlyList<string>? collection)
                ? collection
                : allPossibleMonsters;
            string? slot = NormalizeSlot(addMatch.Groups["slot"].Value);

            for (int i = 0; i < count; i++)
            {
                candidates.Add(new SlotCandidate(
                    loopMatch.Index + i,
                    slot?.Replace("i", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
                    null,
                    possible,
                    $"GenerateMonsters loop random NextItem({collectionName}) slot",
                    0.55));
            }

            warnings.Add($"Loop-based random monster selection was approximated as {count} conditional slots.");
        }
    }

    private static IReadOnlyList<(int Start, int End)> FindLoopRandomRanges(string body)
    {
        List<(int Start, int End)> ranges = [];
        foreach (Match loopMatch in LoopPattern.Matches(body))
        {
            int openBrace = body.IndexOf('{', loopMatch.Index + loopMatch.Length - 1);
            int closeBrace = openBrace >= 0 ? FindMatchingDelimiter(body, openBrace, '{', '}') : -1;
            if (openBrace < 0 || closeBrace < 0)
            {
                continue;
            }

            string loopBody = body[(openBrace + 1)..closeBrace];
            if (RandomVariableAddPattern.IsMatch(loopBody))
            {
                ranges.Add((openBrace, closeBrace));
            }
        }

        return ranges;
    }

    private static string ResolveCollectionName(string loopBody, string localSource)
    {
        foreach (Match match in LocalCollectionFromKeysPattern.Matches(loopBody))
        {
            if (string.Equals(match.Groups["local"].Value, localSource, StringComparison.Ordinal))
            {
                return match.Groups["collection"].Value;
            }
        }

        return localSource;
    }

    private static IReadOnlyDictionary<string, string> ParseLocalMonsterVariables(string body)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (Match match in LocalMonsterVariablePattern.Matches(body))
        {
            result[match.Groups["var"].Value] = match.Groups["type"].Value;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseMonsterCollections(string source)
    {
        Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.Ordinal);
        foreach (Match match in MonsterCollectionDeclarationPattern.Matches(source))
        {
            int openBrace = source.IndexOf('{', match.Index + match.Length);
            int closeBrace = openBrace >= 0 ? FindMatchingDelimiter(source, openBrace, '{', '}') : -1;
            if (openBrace < 0 || closeBrace < 0)
            {
                continue;
            }

            string body = source[openBrace..(closeBrace + 1)];
            string[] monsters = MonsterPattern.Matches(body)
                .Select(monsterMatch => monsterMatch.Groups["type"].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (monsters.Length > 0)
            {
                result[match.Groups["name"].Value] = monsters;
            }
        }

        return result;
    }

    private static int? ParseIntExpressionProperty(string source, string propertyName)
    {
        Match match = Regex.Match(
            source,
            $@"{Regex.Escape(propertyName)}\s*=>\s*(?<value>-?[0-9]+)",
            RegexOptions.Compiled);
        return match.Success && int.TryParse(match.Groups["value"].Value, out int value) ? value : null;
    }

    private static bool? ParseBoolExpressionProperty(string source, string propertyName)
    {
        Match match = Regex.Match(
            source,
            $@"{Regex.Escape(propertyName)}\s*=>\s*(?<value>true|false)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        return match.Success && bool.TryParse(match.Groups["value"].Value, out bool value) ? value : null;
    }

    private static string ParseClassName(string source, string fallback)
    {
        Match match = ClassNamePattern.Match(source);
        return match.Success ? match.Groups["name"].Value : fallback;
    }

    private static string Categorize(string typeName, string roomType, bool isWeak, bool isDebug)
    {
        if (isDebug)
        {
            return "Debug";
        }

        if (typeName.EndsWith("EventEncounter", StringComparison.Ordinal))
        {
            return "Event";
        }

        return roomType switch
        {
            "Boss" => "Boss",
            "Elite" => "Elite",
            "Monster" when isWeak => "Weak",
            "Monster" => "Normal",
            _ => roomType
        };
    }

    private static string? NormalizeSlot(string value)
    {
        value = value.Trim().TrimEnd(',');
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    private static string? ExtractBracedBlockAfter(string source, string token)
    {
        int tokenIndex = source.IndexOf(token, StringComparison.Ordinal);
        if (tokenIndex < 0)
        {
            return null;
        }

        int openBrace = source.IndexOf('{', tokenIndex);
        if (openBrace < 0)
        {
            return null;
        }

        int closeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
        return closeBrace < 0 ? null : source[(openBrace + 1)..closeBrace];
    }

    private static int FindMatchingDelimiter(string source, int openIndex, char open, char close)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIndex; i < source.Length; i++)
        {
            char current = source[i];
            if (current == '"' && (i == 0 || source[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (current == open)
            {
                depth++;
            }
            else if (current == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private sealed record SlotCandidate(
        int SourceIndex,
        string? SlotName,
        string? MonsterTypeName,
        IReadOnlyList<string> PossibleMonsterTypeNames,
        string Source,
        double Confidence);
}
