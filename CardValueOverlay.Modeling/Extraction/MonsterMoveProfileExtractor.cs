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
            entries.Add(_parser.Parse(monster, source));
        }

        return entries.OrderBy(entry => entry.TypeName, StringComparer.Ordinal).ToArray();
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
