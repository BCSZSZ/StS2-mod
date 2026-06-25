namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardEffectTermExtractor
{
    private readonly GameDataExtractor _gameDataExtractor;
    private readonly IlSpyDecompiler _decompiler;
    private readonly CardEffectParser _parser;

    public CardEffectTermExtractor(
        GameDataExtractor? gameDataExtractor = null,
        IlSpyDecompiler? decompiler = null,
        CardEffectParser? parser = null)
    {
        _gameDataExtractor = gameDataExtractor ?? new GameDataExtractor();
        _decompiler = decompiler ?? new IlSpyDecompiler();
        _parser = parser ?? new CardEffectParser();
    }

    public async Task<IReadOnlyList<CardEffectTermCatalogEntry>> ExtractAsync(
        ModelingExtractionOptions options,
        bool refreshDecompile,
        CancellationToken cancellationToken = default)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionRunResult run = await _gameDataExtractor.ExtractAsync(options, cancellationToken);
        string sourceRoot = await _decompiler.EnsureProjectDecompiledAsync(paths, refreshDecompile, cancellationToken);

        List<CardEffectTermCatalogEntry> entries = [];
        foreach (ModelCatalogEntry card in run.Cards)
        {
            string? sourcePath = FindSourcePath(sourceRoot, card);
            if (sourcePath is null)
            {
                entries.Add(new CardEffectTermCatalogEntry(
                    card.ModelId,
                    card.TypeName,
                    card.FullTypeName,
                    null,
                    null,
                    null,
                    null,
                    [],
                    [$"Decompiled source file was not found for {card.FullTypeName}."],
                    "ilspycmd decompiled C# parser v1",
                    0.0));
                continue;
            }

            string source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            entries.Add(_parser.Parse(card, source));
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
}
