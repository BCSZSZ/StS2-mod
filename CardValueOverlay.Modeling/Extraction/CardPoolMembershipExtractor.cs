namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardPoolMembershipExtractor
{
    private readonly GameDataExtractor _gameDataExtractor;
    private readonly IlSpyDecompiler _decompiler;
    private readonly CardPoolMembershipParser _parser;

    public CardPoolMembershipExtractor(
        GameDataExtractor? gameDataExtractor = null,
        IlSpyDecompiler? decompiler = null,
        CardPoolMembershipParser? parser = null)
    {
        _gameDataExtractor = gameDataExtractor ?? new GameDataExtractor();
        _decompiler = decompiler ?? new IlSpyDecompiler();
        _parser = parser ?? new CardPoolMembershipParser();
    }

    public async Task<IReadOnlyList<CardPoolMembershipEntry>> ExtractAsync(
        ModelingExtractionOptions options,
        bool refreshDecompile,
        CancellationToken cancellationToken = default)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionRunResult run = await _gameDataExtractor.ExtractAsync(options, cancellationToken);
        string sourceRoot = await _decompiler.EnsureProjectDecompiledAsync(paths, refreshDecompile, cancellationToken);

        IReadOnlyDictionary<string, IReadOnlyList<string>> poolsByCard = await ExtractPoolsByCardAsync(sourceRoot, cancellationToken);
        List<CardPoolMembershipEntry> entries = [];
        foreach (ModelCatalogEntry card in run.Cards)
        {
            string? cardSourcePath = FindCardSourcePath(sourceRoot, card);
            string multiplayerConstraint = "None";
            List<string> warnings = [];
            double confidence = 0.95;

            if (cardSourcePath is null)
            {
                warnings.Add($"Decompiled card source file was not found for {card.FullTypeName}.");
                confidence = 0.5;
            }
            else
            {
                string cardSource = await File.ReadAllTextAsync(cardSourcePath, cancellationToken);
                multiplayerConstraint = _parser.ParseMultiplayerConstraint(cardSource);
            }

            IReadOnlyList<string> pools = poolsByCard.TryGetValue(card.TypeName, out IReadOnlyList<string>? matchedPools)
                ? matchedPools
                : [];

            if (pools.Count == 0)
            {
                warnings.Add("Card was not found in any parsed CardPool GenerateAllCards list.");
                confidence = Math.Min(confidence, 0.65);
            }

            entries.Add(new CardPoolMembershipEntry(
                card.ModelId,
                card.TypeName,
                card.FullTypeName,
                pools,
                multiplayerConstraint,
                string.Equals(multiplayerConstraint, "MultiplayerOnly", StringComparison.Ordinal),
                string.Equals(multiplayerConstraint, "SingleplayerOnly", StringComparison.Ordinal),
                warnings,
                "ilspycmd decompiled CardPool and CardModel parser v1",
                confidence));
        }

        return entries.OrderBy(entry => entry.TypeName, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ExtractPoolsByCardAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        Dictionary<string, SortedSet<string>> poolsByCard = new(StringComparer.Ordinal);
        string cardPoolsRoot = Path.Combine(
            sourceRoot,
            "MegaCrit",
            "sts2",
            "Core",
            "Models",
            "CardPools");

        if (!Directory.Exists(cardPoolsRoot))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        foreach (string path in Directory.EnumerateFiles(cardPoolsRoot, "*CardPool.cs", SearchOption.TopDirectoryOnly))
        {
            string source = await File.ReadAllTextAsync(path, cancellationToken);
            string fallbackPoolTypeName = Path.GetFileNameWithoutExtension(path);
            CardPoolSourceEntry pool = _parser.ParsePoolSource(fallbackPoolTypeName, source);
            foreach (string cardTypeName in pool.CardTypeNames)
            {
                if (!poolsByCard.TryGetValue(cardTypeName, out SortedSet<string>? pools))
                {
                    pools = new SortedSet<string>(StringComparer.Ordinal);
                    poolsByCard[cardTypeName] = pools;
                }

                pools.Add(pool.PoolName);
            }
        }

        return poolsByCard.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static string? FindCardSourcePath(string sourceRoot, ModelCatalogEntry card)
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
