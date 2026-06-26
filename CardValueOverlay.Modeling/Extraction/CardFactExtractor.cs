namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardFactExtractor
{
    private readonly GameDataExtractor _gameDataExtractor;
    private readonly IlSpyDecompiler _decompiler;
    private readonly CardFactParser _parser;

    public CardFactExtractor(
        GameDataExtractor? gameDataExtractor = null,
        IlSpyDecompiler? decompiler = null,
        CardFactParser? parser = null)
    {
        _gameDataExtractor = gameDataExtractor ?? new GameDataExtractor();
        _decompiler = decompiler ?? new IlSpyDecompiler();
        _parser = parser ?? new CardFactParser();
    }

    public async Task<IReadOnlyList<CardFactCatalogEntry>> ExtractAsync(
        ModelingExtractionOptions options,
        bool refreshDecompile,
        CancellationToken cancellationToken = default)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionRunResult run = await _gameDataExtractor.ExtractAsync(options, cancellationToken);
        string sourceRoot = await _decompiler.EnsureProjectDecompiledAsync(paths, refreshDecompile, cancellationToken);

        List<CardFactCatalogEntry> entries = [];
        foreach (ModelCatalogEntry card in run.Cards)
        {
            string? sourcePath = FindSourcePath(sourceRoot, card);
            if (sourcePath is null)
            {
                entries.Add(new CardFactCatalogEntry(
                    card.ModelId,
                    card.TypeName,
                    card.FullTypeName,
                    null,
                    null,
                    null,
                    null,
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    [$"Decompiled source file was not found for {card.FullTypeName}."],
                    "ilspycmd decompiled C# card facts parser v1",
                    0.0));
                continue;
            }

            string source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            IReadOnlyList<string> powerNames = CardFactParser.ExtractAppliedPowerTypeNames(source);
            Dictionary<string, string> powerSources = new(StringComparer.Ordinal);
            Dictionary<string, string> powerSourceFiles = new(StringComparer.Ordinal);
            foreach (string powerName in powerNames)
            {
                string? powerPath = FindPowerSourcePath(sourceRoot, powerName);
                if (powerPath is null)
                {
                    continue;
                }

                powerSources[powerName] = await File.ReadAllTextAsync(powerPath, cancellationToken);
                powerSourceFiles[powerName] = powerPath;
            }

            entries.Add(_parser.Parse(card, source, sourcePath, powerSources, powerSourceFiles));
        }

        return entries.OrderBy(entry => entry.TypeName, StringComparer.Ordinal).ToArray();
    }

    private static string? FindSourcePath(string sourceRoot, ModelCatalogEntry card)
    {
        string nestedPath = Path.Combine(sourceRoot, card.FullTypeName.Replace('.', Path.DirectorySeparatorChar) + ".cs");
        if (File.Exists(nestedPath))
        {
            return nestedPath;
        }

        return Directory
            .EnumerateFiles(sourceRoot, $"{card.TypeName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Cards{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindPowerSourcePath(string sourceRoot, string powerName)
    {
        return Directory
            .EnumerateFiles(sourceRoot, $"{powerName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Powers{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }
}
