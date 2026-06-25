namespace CardValueOverlay.Modeling.Extraction;

public sealed class EncounterPatternExtractor
{
    private readonly GameDataExtractor _gameDataExtractor;
    private readonly IlSpyDecompiler _decompiler;
    private readonly EncounterPatternParser _parser;

    public EncounterPatternExtractor(
        GameDataExtractor? gameDataExtractor = null,
        IlSpyDecompiler? decompiler = null,
        EncounterPatternParser? parser = null)
    {
        _gameDataExtractor = gameDataExtractor ?? new GameDataExtractor();
        _decompiler = decompiler ?? new IlSpyDecompiler();
        _parser = parser ?? new EncounterPatternParser();
    }

    public async Task<IReadOnlyList<EncounterPatternEntry>> ExtractAsync(
        ModelingExtractionOptions options,
        bool refreshDecompile,
        CancellationToken cancellationToken = default)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionRunResult run = await _gameDataExtractor.ExtractAsync(options, cancellationToken);
        string sourceRoot = await _decompiler.EnsureProjectDecompiledAsync(paths, refreshDecompile, cancellationToken);

        IReadOnlyDictionary<string, IReadOnlyList<EncounterActReference>> actsByEncounter =
            await ExtractActsByEncounterAsync(sourceRoot, cancellationToken);

        List<EncounterPatternEntry> entries = [];
        foreach (ModelCatalogEntry encounter in run.Encounters)
        {
            string? sourcePath = FindSourcePath(sourceRoot, encounter);
            IReadOnlyList<EncounterActReference> acts = actsByEncounter.TryGetValue(
                encounter.TypeName,
                out IReadOnlyList<EncounterActReference>? references)
                ? references
                : [];

            if (sourcePath is null)
            {
                entries.Add(new EncounterPatternEntry(
                    encounter.ModelId,
                    encounter.TypeName,
                    encounter.FullTypeName,
                    acts,
                    "Unknown",
                    false,
                    "Unknown",
                    [],
                    [],
                    [],
                    null,
                    false,
                    [$"Decompiled source file was not found for {encounter.FullTypeName}."],
                    "ilspycmd decompiled C# encounter pattern parser v1",
                    0.0));
                continue;
            }

            string source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            entries.Add(_parser.ParseEncounterSource(encounter, acts, source));
        }

        return entries.OrderBy(entry => entry.TypeName, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<EncounterActReference>>> ExtractActsByEncounterAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<EncounterActReference>> actsByEncounter = new(StringComparer.Ordinal);
        string actsRoot = Path.Combine(
            sourceRoot,
            "MegaCrit",
            "sts2",
            "Core",
            "Models",
            "Acts");

        if (!Directory.Exists(actsRoot))
        {
            return new Dictionary<string, IReadOnlyList<EncounterActReference>>(StringComparer.Ordinal);
        }

        foreach (string path in Directory.EnumerateFiles(actsRoot, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = await File.ReadAllTextAsync(path, cancellationToken);
            string fallbackTypeName = Path.GetFileNameWithoutExtension(path);
            EncounterActSourceEntry act = _parser.ParseActSource(fallbackTypeName, source);
            if (act.ActIndex < 0)
            {
                continue;
            }

            EncounterActReference reference = new(
                act.ActTypeName,
                act.ActIndex,
                act.ActNumber,
                act.IsDefault,
                act.NumberOfWeakEncounters,
                act.BaseNumberOfRooms);

            foreach (string encounterTypeName in act.EncounterTypeNames)
            {
                if (!actsByEncounter.TryGetValue(encounterTypeName, out List<EncounterActReference>? references))
                {
                    references = [];
                    actsByEncounter[encounterTypeName] = references;
                }

                references.Add(reference);
            }
        }

        return actsByEncounter.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EncounterActReference>)pair.Value
                .OrderBy(reference => reference.ActIndex)
                .ThenBy(reference => reference.ActTypeName, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static string? FindSourcePath(string sourceRoot, ModelCatalogEntry encounter)
    {
        string nestedPath = Path.Combine(sourceRoot, encounter.FullTypeName.Replace('.', Path.DirectorySeparatorChar) + ".cs");
        if (File.Exists(nestedPath))
        {
            return nestedPath;
        }

        return Directory
            .EnumerateFiles(sourceRoot, $"{encounter.TypeName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Encounters{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }
}
