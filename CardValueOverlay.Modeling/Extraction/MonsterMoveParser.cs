using System.Globalization;
using System.Text.RegularExpressions;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class MonsterMoveParser
{
    private static readonly Regex NumericSymbolRegex = new(
        @"(?:public|private|protected)\s+(?:static\s+)?(?:override\s+)?(?:const\s+)?(?:int|decimal)\s+(?<name>[A-Za-z0-9_]+)\s*(?:=>|=)\s*(?<expr>[^;\r\n]+);",
        RegexOptions.Compiled);

    private static readonly Regex LocalAscensionPropertyRegex = new(
        @"(?:public|private|protected)\s+(?:static\s+)?(?:override\s+)?(?:const\s+)?(?:int|decimal)\s+(?<name>[A-Za-z0-9_]+)\s*\{\s*get\s*\{\s*(?:int|decimal)\s+[A-Za-z0-9_]+\s*=\s*(?<expr>AscensionHelper\.GetValueIfAscension\([^;]+);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex FollowUpRegex = new(
        @"(?<from>[A-Za-z0-9_]+)\.FollowUpState\s*=\s*(?<to>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex BranchTargetRegex = new(
        @"(?<branch>[A-Za-z0-9_]+)\.Add(?:State|Branch)\((?<state>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex InitialStateRegex = new(
        @"return\s+new\s+MonsterMoveStateMachine\([^,]+,\s*(?<initial>[A-Za-z0-9_]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex MethodRegex = new(
        @"(?:private|protected|public)\s+(?:override\s+)?(?:async\s+)?Task\s+(?<name>[A-Za-z0-9_]+)\s*\([^)]*\)\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex AttackRegex = new(
        @"DamageCmd\.Attack\((?<amount>[^)]+)\)(?<chain>[^;]*?)\.Execute",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex HitCountRegex = new(
        @"\.WithHitCount\((?<count>[^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex GainBlockRegex = new(
        @"CreatureCmd\.GainBlock\([^,]+,\s*(?<amount>[^,]+)",
        RegexOptions.Compiled);

    private static readonly Regex PowerRegex = new(
        @"PowerCmd\.Apply<(?<power>[A-Za-z0-9_]+Power)>\([^,]+,\s*(?<target>[^,]+),\s*(?<amount>[^,]+)",
        RegexOptions.Compiled);

    public MonsterMoveProfileEntry Parse(ModelCatalogEntry monster, string source)
    {
        Dictionary<string, MonsterMoveNumeric> symbols = ParseNumericSymbols(source);
        Dictionary<string, string> methodBodies = ParseMethodBodies(source);
        Dictionary<string, string> moveVariableToStateId = [];
        List<string> unresolved = [];
        List<MonsterMoveStateEntry> moves = [];

        foreach (MoveStateCall call in ParseMoveStateCalls(source))
        {
            if (call.VariableName is not null)
            {
                moveVariableToStateId[call.VariableName] = call.StateId;
            }

            List<string> warnings = [];
            IReadOnlyList<string> intents = ParseIntents(call.Arguments.Skip(2));
            List<MonsterMoveEffectTerm> effects = [];

            string? moveMethod = call.Arguments.Count > 1 ? NormalizeIdentifier(call.Arguments[1]) : null;
            if (moveMethod is not null && methodBodies.TryGetValue(moveMethod, out string? body))
            {
                effects.AddRange(ParseMethodEffects(body, symbols));
            }
            else if (moveMethod is not null && !moveMethod.Contains("=>", StringComparison.Ordinal))
            {
                warnings.Add($"Move method body was not found for {moveMethod}.");
            }

            AddIntentFallbackEffects(call.Arguments.Skip(2), symbols, effects);

            if (effects.Count == 0 && intents.Count == 0)
            {
                warnings.Add("No supported v1 intents or effects were parsed for this move state.");
            }

            double confidence = effects.Count > 0
                ? effects.Min(effect => effect.Confidence)
                : intents.Count > 0 ? 0.65 : 0.2;

            moves.Add(new MonsterMoveStateEntry(
                call.StateId,
                moveMethod,
                intents,
                effects,
                [],
                warnings,
                confidence));
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> followUps = ParseFollowUps(source, moveVariableToStateId);
        moves = moves
            .Select(move => move with
            {
                FollowUpStateIds = followUps.TryGetValue(move.StateId, out IReadOnlyList<string>? ids) ? ids : []
            })
            .OrderBy(move => move.StateId, StringComparer.Ordinal)
            .ToList();

        InitialStateParseResult initialState = ParseInitialStateId(source, moveVariableToStateId);
        unresolved.AddRange(initialState.Warnings);
        if (moves.Count == 0)
        {
            unresolved.Add("No MoveState constructors were parsed from GenerateMoveStateMachine.");
        }

        MonsterHpRange hpRange = new(
            symbols.GetValueOrDefault("MinInitialHp"),
            symbols.GetValueOrDefault("MaxInitialHp"));

        double profileConfidence = moves.Count == 0 ? 0.1 : moves.Min(move => move.Confidence);
        if (unresolved.Count > 0)
        {
            profileConfidence = Math.Min(profileConfidence, 0.4);
        }

        return new MonsterMoveProfileEntry(
            monster.ModelId,
            monster.TypeName,
            monster.FullTypeName,
            hpRange,
            moves,
            initialState.StateId,
            unresolved,
            "ilspycmd decompiled C# monster move parser v1",
            profileConfidence);
    }

    private static Dictionary<string, MonsterMoveNumeric> ParseNumericSymbols(string source)
    {
        Dictionary<string, MonsterMoveNumeric> symbols = new(StringComparer.Ordinal);
        foreach (Match match in NumericSymbolRegex.Matches(source))
        {
            string name = match.Groups["name"].Value;
            string expression = match.Groups["expr"].Value.Trim();
            symbols[name] = ParseNumeric(expression, symbols);
        }

        foreach (Match match in LocalAscensionPropertyRegex.Matches(source))
        {
            string name = match.Groups["name"].Value;
            if (symbols.ContainsKey(name))
            {
                continue;
            }

            MonsterMoveNumeric parsed = ParseNumeric(match.Groups["expr"].Value.Trim(), symbols);
            symbols[name] = parsed with
            {
                Expression = name,
                Confidence = Math.Min(parsed.Confidence, 0.65)
            };
        }

        return symbols;
    }

    private static List<MoveStateCall> ParseMoveStateCalls(string source)
    {
        List<MoveStateCall> calls = [];
        foreach (ConstructorCall call in FindConstructorCalls(source, "new MoveState"))
        {
            IReadOnlyList<string> args = SplitTopLevelArguments(call.Arguments);
            if (args.Count < 2)
            {
                continue;
            }

            string? stateId = Unquote(args[0]);
            if (stateId is null)
            {
                continue;
            }

            string? variableName = FindAssignedVariableName(source, call.StartIndex);
            calls.Add(new MoveStateCall(variableName, stateId, args, call.StartIndex));
        }

        return calls;
    }

    private static IReadOnlyList<string> ParseIntents(IEnumerable<string> args)
    {
        return args
            .Select(arg => Regex.Match(arg, @"new\s+(?<intent>[A-Za-z0-9_]+Intent)\b"))
            .Where(match => match.Success)
            .Select(match => match.Groups["intent"].Value)
            .ToArray();
    }

    private static List<MonsterMoveEffectTerm> ParseMethodEffects(
        string body,
        IReadOnlyDictionary<string, MonsterMoveNumeric> symbols)
    {
        List<MonsterMoveEffectTerm> effects = [];

        foreach (Match match in AttackRegex.Matches(body))
        {
            string amountExpression = match.Groups["amount"].Value.Trim();
            Match hitCount = HitCountRegex.Match(match.Groups["chain"].Value);
            effects.Add(new MonsterMoveEffectTerm(
                "attack",
                ParseNumeric(amountExpression, symbols),
                hitCount.Success ? ParseNumeric(hitCount.Groups["count"].Value.Trim(), symbols) : Literal(1m),
                "player",
                null,
                "DamageCmd.Attack",
                0.85));
        }

        foreach (Match match in GainBlockRegex.Matches(body))
        {
            effects.Add(new MonsterMoveEffectTerm(
                "block",
                ParseNumeric(match.Groups["amount"].Value.Trim(), symbols),
                null,
                "self",
                null,
                "CreatureCmd.GainBlock",
                0.8));
        }

        foreach (Match match in PowerRegex.Matches(body))
        {
            string power = match.Groups["power"].Value;
            effects.Add(new MonsterMoveEffectTerm(
                ToPowerKind(power),
                ParseNumeric(match.Groups["amount"].Value.Trim(), symbols),
                null,
                ParsePowerTarget(match.Groups["target"].Value),
                $"power:{ToPowerKey(power)}",
                $"PowerCmd.Apply<{power}>",
                0.72));
        }

        return effects;
    }

    private static void AddIntentFallbackEffects(
        IEnumerable<string> args,
        IReadOnlyDictionary<string, MonsterMoveNumeric> symbols,
        List<MonsterMoveEffectTerm> effects)
    {
        foreach (string arg in args)
        {
            Match singleAttack = Regex.Match(arg, @"new\s+SingleAttackIntent\((?<amount>[^)]+)\)");
            if (singleAttack.Success && !effects.Any(effect => effect.Kind == "attack"))
            {
                effects.Add(new MonsterMoveEffectTerm(
                    "attack",
                    ParseNumeric(singleAttack.Groups["amount"].Value.Trim(), symbols),
                    Literal(1m),
                    "player",
                    null,
                    "SingleAttackIntent",
                    0.65));
            }

            Match multiAttack = Regex.Match(arg, @"new\s+MultiAttackIntent\((?<amount>[^,]+),\s*(?<count>[^)]+)\)");
            if (multiAttack.Success && !effects.Any(effect => effect.Kind == "attack"))
            {
                effects.Add(new MonsterMoveEffectTerm(
                    "attack",
                    ParseNumeric(multiAttack.Groups["amount"].Value.Trim(), symbols),
                    ParseNumeric(multiAttack.Groups["count"].Value.Trim(), symbols),
                    "player",
                    null,
                    "MultiAttackIntent",
                    0.65));
            }

            Match status = Regex.Match(arg, @"new\s+StatusIntent\((?<amount>[^)]+)\)");
            if (status.Success)
            {
                effects.Add(new MonsterMoveEffectTerm(
                    "status",
                    ParseNumeric(status.Groups["amount"].Value.Trim(), symbols),
                    null,
                    "player",
                    null,
                    "StatusIntent",
                    0.55));
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseFollowUps(
        string source,
        IReadOnlyDictionary<string, string> moveVariableToStateId)
    {
        Dictionary<string, List<string>> result = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> branchTargets = ParseBranchTargets(source, moveVariableToStateId);

        foreach (Match match in FollowUpRegex.Matches(source))
        {
            string fromVar = match.Groups["from"].Value;
            string toVar = match.Groups["to"].Value;
            if (!moveVariableToStateId.TryGetValue(fromVar, out string? fromState))
            {
                continue;
            }

            if (moveVariableToStateId.TryGetValue(toVar, out string? toState))
            {
                AddFollowUp(result, fromState, toState);
            }
            else if (branchTargets.TryGetValue(toVar, out List<string>? targets))
            {
                AddFollowUps(result, fromState, targets);
            }
        }

        foreach (MoveStateCall call in ParseMoveStateCalls(source))
        {
            foreach (string fromVar in FindInlineFollowUpAssignmentSources(source, call.StartIndex))
            {
                if (moveVariableToStateId.TryGetValue(fromVar, out string? fromState))
                {
                    AddFollowUp(result, fromState, call.StateId);
                }
            }
        }

        foreach (BranchStateCall call in ParseBranchStateCalls(source))
        {
            if (call.VariableName is null || !branchTargets.TryGetValue(call.VariableName, out List<string>? targets))
            {
                continue;
            }

            foreach (string fromVar in FindInlineFollowUpAssignmentSources(source, call.StartIndex))
            {
                if (moveVariableToStateId.TryGetValue(fromVar, out string? fromState))
                {
                    AddFollowUps(result, fromState, targets);
                }
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, List<string>> ParseBranchTargets(
        string source,
        IReadOnlyDictionary<string, string> moveVariableToStateId)
    {
        Dictionary<string, List<string>> result = new(StringComparer.Ordinal);
        foreach (Match match in BranchTargetRegex.Matches(source))
        {
            string branchVar = match.Groups["branch"].Value;
            string stateVar = match.Groups["state"].Value;
            if (!moveVariableToStateId.TryGetValue(stateVar, out string? stateId))
            {
                continue;
            }

            if (!result.TryGetValue(branchVar, out List<string>? targets))
            {
                targets = [];
                result[branchVar] = targets;
            }

            targets.Add(stateId);
        }

        return result;
    }

    private static IReadOnlyList<BranchStateCall> ParseBranchStateCalls(string source)
    {
        List<BranchStateCall> calls = [];
        foreach (ConstructorCall call in FindConstructorCalls(source, "new RandomBranchState"))
        {
            calls.Add(new BranchStateCall(FindAssignedVariableName(source, call.StartIndex), call.StartIndex));
        }

        foreach (ConstructorCall call in FindConstructorCalls(source, "new ConditionalBranchState"))
        {
            calls.Add(new BranchStateCall(FindAssignedVariableName(source, call.StartIndex), call.StartIndex));
        }

        return calls;
    }

    private static IReadOnlyList<string> FindInlineFollowUpAssignmentSources(string source, int constructorStartIndex)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, constructorStartIndex - 1));
        string prefix = source[(lineStart + 1)..constructorStartIndex];
        return Regex.Matches(prefix, @"(?<var>[A-Za-z_][A-Za-z0-9_]*)\.FollowUpState\s*=")
            .Select(match => match.Groups["var"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddFollowUps(
        Dictionary<string, List<string>> result,
        string fromState,
        IEnumerable<string> toStates)
    {
        foreach (string toState in toStates)
        {
            AddFollowUp(result, fromState, toState);
        }
    }

    private static void AddFollowUp(
        Dictionary<string, List<string>> result,
        string fromState,
        string toState)
    {
        if (!result.TryGetValue(fromState, out List<string>? ids))
        {
            ids = [];
            result[fromState] = ids;
        }

        ids.Add(toState);
    }

    private static InitialStateParseResult ParseInitialStateId(
        string source,
        IReadOnlyDictionary<string, string> moveVariableToStateId)
    {
        List<string> stateIds = [];
        foreach (Match match in InitialStateRegex.Matches(source))
        {
            if (moveVariableToStateId.TryGetValue(match.Groups["initial"].Value, out string? stateId))
            {
                stateIds.Add(stateId);
            }
        }

        string[] distinct = stateIds.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 1)
        {
            return new InitialStateParseResult(distinct[0], []);
        }

        if (distinct.Length > 1)
        {
            return new InitialStateParseResult(
                null,
                [$"Initial move state is conditional or ambiguous: {string.Join(", ", distinct)}."]);
        }

        return new InitialStateParseResult(null, ["Initial move state was not parsed."]);
    }

    private static Dictionary<string, string> ParseMethodBodies(string source)
    {
        Dictionary<string, string> bodies = new(StringComparer.Ordinal);
        foreach (Match match in MethodRegex.Matches(source))
        {
            int openBrace = match.Index + match.Length - 1;
            int closeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
            if (closeBrace < 0)
            {
                continue;
            }

            bodies[match.Groups["name"].Value] = source[(openBrace + 1)..closeBrace];
        }

        return bodies;
    }

    private static MonsterMoveNumeric ParseNumeric(
        string expression,
        IReadOnlyDictionary<string, MonsterMoveNumeric> symbols)
    {
        expression = expression.Trim();
        if (symbols.TryGetValue(expression, out MonsterMoveNumeric? symbolValue))
        {
            return symbolValue;
        }

        Match ascension = Regex.Match(
            expression,
            @"AscensionHelper\.GetValueIfAscension\(AscensionLevel\.(?<level>[A-Za-z0-9_]+),\s*(?<asc>[-0-9.]+)m?,\s*(?<normal>[-0-9.]+)m?\)");
        if (ascension.Success)
        {
            return new MonsterMoveNumeric(
                expression,
                ParseDecimal(ascension.Groups["normal"].Value),
                ParseDecimal(ascension.Groups["asc"].Value),
                ascension.Groups["level"].Value,
                0.9);
        }

        if (TryParseLiteral(expression, out decimal literal))
        {
            return Literal(literal, expression);
        }

        return new MonsterMoveNumeric(expression, null, null, null, 0.25);
    }

    private static MonsterMoveNumeric Literal(decimal value, string? expression = null)
    {
        return new MonsterMoveNumeric(
            expression ?? value.ToString(CultureInfo.InvariantCulture),
            value,
            null,
            null,
            0.95);
    }

    private static bool TryParseLiteral(string expression, out decimal value)
    {
        expression = expression.Trim();
        if (expression.EndsWith('m') || expression.EndsWith('f'))
        {
            expression = expression[..^1];
        }

        return decimal.TryParse(expression, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<ConstructorCall> FindConstructorCalls(string source, string token)
    {
        List<ConstructorCall> calls = [];
        int searchIndex = 0;
        while (searchIndex < source.Length)
        {
            int tokenIndex = source.IndexOf(token, searchIndex, StringComparison.Ordinal);
            if (tokenIndex < 0)
            {
                break;
            }

            int openParen = source.IndexOf('(', tokenIndex + token.Length);
            if (openParen < 0)
            {
                break;
            }

            int closeParen = FindMatchingDelimiter(source, openParen, '(', ')');
            if (closeParen < 0)
            {
                searchIndex = openParen + 1;
                continue;
            }

            calls.Add(new ConstructorCall(tokenIndex, source[(openParen + 1)..closeParen]));
            searchIndex = closeParen + 1;
        }

        return calls;
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

    private static IReadOnlyList<string> SplitTopLevelArguments(string args)
    {
        List<string> result = [];
        int depth = 0;
        bool inString = false;
        int start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            char current = args[i];
            if (current == '"' && (i == 0 || args[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (current is '(' or '[' or '{')
            {
                depth++;
            }
            else if (current is ')' or ']' or '}')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                result.Add(args[start..i].Trim());
                start = i + 1;
            }
        }

        result.Add(args[start..].Trim());
        return result;
    }

    private static string? FindAssignedVariableName(string source, int constructorStartIndex)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, constructorStartIndex - 1));
        string prefix = source[(lineStart + 1)..constructorStartIndex];

        Match declaration = Regex.Match(prefix, @"MoveState\s+(?<var>[A-Za-z0-9_]+)\s*=");
        if (declaration.Success)
        {
            return declaration.Groups["var"].Value;
        }

        Match stateDeclaration = Regex.Match(prefix, @"(?:RandomBranchState|ConditionalBranchState)\s+(?<var>[A-Za-z0-9_]+)\s*=");
        if (stateDeclaration.Success)
        {
            return stateDeclaration.Groups["var"].Value;
        }

        Match assignment = Regex.Match(prefix, @"(?<!\.)\b(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=");
        if (assignment.Success)
        {
            return assignment.Groups["var"].Value;
        }

        return null;
    }

    private static string? Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : null;
    }

    private static string? NormalizeIdentifier(string value)
    {
        value = value.Trim();
        if (value.Contains("=>", StringComparison.Ordinal))
        {
            return value;
        }

        Match match = Regex.Match(value, @"^[A-Za-z_][A-Za-z0-9_]*$");
        return match.Success ? value : null;
    }

    private static string ParsePowerTarget(string target)
    {
        return target.Contains("base.Creature", StringComparison.Ordinal)
            ? "self"
            : "player";
    }

    private static string ToPowerKind(string power)
    {
        return power switch
        {
            "WeakPower" => "debuffWeak",
            "VulnerablePower" => "debuffVulnerable",
            "FrailPower" => "debuffFrail",
            "StrengthPower" => "buffStrength",
            _ => "power"
        };
    }

    private static string ToPowerKey(string power)
    {
        return power.EndsWith("Power", StringComparison.Ordinal)
            ? power[..^"Power".Length]
            : power;
    }

    private sealed record ConstructorCall(int StartIndex, string Arguments);

    private sealed record MoveStateCall(string? VariableName, string StateId, IReadOnlyList<string> Arguments, int StartIndex);

    private sealed record BranchStateCall(string? VariableName, int StartIndex);

    private sealed record InitialStateParseResult(string? StateId, IReadOnlyList<string> Warnings);
}
