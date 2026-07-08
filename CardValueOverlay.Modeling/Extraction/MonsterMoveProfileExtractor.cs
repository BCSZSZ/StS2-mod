namespace CardValueOverlay.Modeling.Extraction;

public sealed class MonsterMoveProfileExtractor
{
    private readonly GameDataExtractor _gameDataExtractor;
    private readonly IlSpyDecompiler _decompiler;
    private readonly MonsterMoveParser _parser;

    public MonsterMoveProfileExtractor(
        GameDataExtractor? gameDataExtractor = null,
        IlSpyDecompiler? decompiler = null,
        MonsterMoveParser? parser = null)
    {
        _gameDataExtractor = gameDataExtractor ?? new GameDataExtractor();
        _decompiler = decompiler ?? new IlSpyDecompiler();
        _parser = parser ?? new MonsterMoveParser();
    }

    public async Task<IReadOnlyList<MonsterMoveProfileEntry>> ExtractAsync(
        ModelingExtractionOptions options,
        bool refreshDecompile,
        CancellationToken cancellationToken = default)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionRunResult run = await _gameDataExtractor.ExtractAsync(options, cancellationToken);
        string sourceRoot = await _decompiler.EnsureProjectDecompiledAsync(paths, refreshDecompile, cancellationToken);

        List<MonsterMoveProfileEntry> entries = [];
        foreach (ModelCatalogEntry monster in run.Enemies)
        {
            string? sourcePath = FindSourcePath(sourceRoot, monster);
            if (sourcePath is null)
            {
                entries.Add(new MonsterMoveProfileEntry(
                    monster.ModelId,
                    monster.TypeName,
                    monster.FullTypeName,
                    null,
                    [],
                    null,
                    [$"Decompiled source file was not found for {monster.FullTypeName}."],
                    "ilspycmd decompiled C# monster move parser v1",
                    0.0));
                continue;
            }

            string source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            MonsterMoveProfileEntry parsed = _parser.Parse(monster, source);
            if (parsed.Moves.Count == 0)
            {
                string? baseSourcePath = FindDirectBaseMonsterSourcePath(sourceRoot, source);
                if (baseSourcePath is not null)
                {
                    string baseSource = await File.ReadAllTextAsync(baseSourcePath, cancellationToken);
                    MonsterMoveProfileEntry inherited = _parser.Parse(monster, source + Environment.NewLine + baseSource);
                    if (inherited.Moves.Count > 0)
                    {
                        parsed = inherited with
                        {
                            Unresolved = parsed.Unresolved
                                .Where(item => !item.Contains("No MoveState constructors", StringComparison.Ordinal))
                                .Append($"Move state machine was inherited from {Path.GetFileNameWithoutExtension(baseSourcePath)}.")
                                .ToArray(),
                            Confidence = Math.Min(0.75, inherited.Confidence)
                        };
                    }
                }
            }

            entries.Add(parsed);
        }

        return entries.OrderBy(entry => entry.TypeName, StringComparer.Ordinal).ToArray();
    }

    private static string? FindDirectBaseMonsterSourcePath(string sourceRoot, string source)
    {
        string? baseTypeName = TryParseDirectBaseTypeName(source);
        if (baseTypeName is null)
        {
            return null;
        }

        return Directory
            .EnumerateFiles(sourceRoot, $"{baseTypeName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Monsters{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryParseDirectBaseTypeName(string source)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            source,
            @"class\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*(?<base>[A-Za-z_][A-Za-z0-9_]*)");
        if (!match.Success)
        {
            return null;
        }

        string baseTypeName = match.Groups["base"].Value;
        return baseTypeName is "MonsterModel" or "object"
            ? null
            : baseTypeName;
    }

    private static string? FindSourcePath(string sourceRoot, ModelCatalogEntry monster)
    {
        string nestedPath = Path.Combine(sourceRoot, monster.FullTypeName.Replace('.', Path.DirectorySeparatorChar) + ".cs");
        if (File.Exists(nestedPath))
        {
            return nestedPath;
        }

        return Directory
            .EnumerateFiles(sourceRoot, $"{monster.TypeName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Monsters{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }
}
