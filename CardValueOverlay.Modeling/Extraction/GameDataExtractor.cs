using System.Text.Json;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class GameDataExtractor
{
    private const string CardPrefix = "MegaCrit.Sts2.Core.Models.Cards.";
    private const string MonsterPrefix = "MegaCrit.Sts2.Core.Models.Monsters.";
    private const string EncounterPrefix = "MegaCrit.Sts2.Core.Models.Encounters.";
    private const string IntentPrefix = "MegaCrit.Sts2.Core.MonsterMoves.Intents.";

    private readonly IlSpyTypeLister _typeLister;

    public GameDataExtractor(IlSpyTypeLister? typeLister = null)
    {
        _typeLister = typeLister ?? new IlSpyTypeLister();
    }

    public async Task<ExtractionRunResult> ExtractAsync(
        ModelingExtractionOptions options,
        CancellationToken cancellationToken = default)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        ExtractionValidationResult validation = ExtractionValidationResult.Validate(paths);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }

        IReadOnlyList<string> classes = await _typeLister.ListClassesAsync(paths, cancellationToken);

        IReadOnlyList<ModelCatalogEntry> cards = ExtractModels(classes, CardPrefix, "card", "CARD");
        IReadOnlyList<ModelCatalogEntry> enemies = ExtractModels(classes, MonsterPrefix, "enemy", "MONSTER");
        IReadOnlyList<ModelCatalogEntry> encounters = ExtractModels(classes, EncounterPrefix, "encounter", "ENCOUNTER");
        IReadOnlyList<IntentCatalogEntry> intents = ExtractIntents(classes);
        GameVersionInfo version = ReadVersion(paths);
        LocalizationCatalog localization = new(
            "SlayTheSpire2.pck",
            "pending",
            "Base localization is packed in SlayTheSpire2.pck or available through runtime GameInfo; v1 records the required tables but does not unpack PCK assets.",
            ["cards", "monsters", "intents", "keywords", "relics", "powers"]);

        List<UnresolvedExtractionItem> unresolved =
        [
            new(
                "localization",
                "warning",
                "Card and monster localized text is not extracted in offline v1.",
                "Add PCK localization extraction or a runtime exporter that serializes GameInfo.Objects records."),
            new(
                "card_effect_terms",
                "warning",
                "Card effects require per-card IL/body parsing and manual confidence scoring.",
                "Implement effect-term parser over decompiled card classes, then write low-confidence effects to data/manual-tags/card_effect_overrides.json."),
            new(
                "enemy_intents",
                "warning",
                "Monster move state machines are discovered by type but not fully normalized in v1.",
                "Parse MonsterModel.GenerateMoveStateMachine decompiled bodies for MoveState and AbstractIntent construction."),
            new(
                "runtime_exporter",
                "info",
                "Direct PowerShell assembly loading was unstable during exploration.",
                "If offline extraction is insufficient, implement a controlled C# exporter or in-game mod command rather than ad hoc script loading.")
        ];

        return new ExtractionRunResult(version, cards, enemies, encounters, intents, localization, unresolved);
    }

    private static IReadOnlyList<ModelCatalogEntry> ExtractModels(
        IEnumerable<string> classes,
        string prefix,
        string modelKind,
        string modelIdCategory)
    {
        return classes
            .Where(type => type.StartsWith(prefix, StringComparison.Ordinal))
            .Where(type => !type[prefix.Length..].Contains('.', StringComparison.Ordinal))
            .Select(type =>
            {
                string typeName = type[prefix.Length..];
                return new ModelCatalogEntry(
                    modelKind,
                    typeName,
                    type,
                    $"{modelIdCategory}.{Slugify(typeName)}",
                    "sts2.dll",
                    "ilspycmd class list",
                    0.65);
            })
            .OrderBy(entry => entry.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<IntentCatalogEntry> ExtractIntents(IEnumerable<string> classes)
    {
        return classes
            .Where(type => type.StartsWith(IntentPrefix, StringComparison.Ordinal))
            .Where(type => !type[IntentPrefix.Length..].Contains('.', StringComparison.Ordinal))
            .Where(type => type.EndsWith("Intent", StringComparison.Ordinal))
            .Select(type =>
            {
                string typeName = type[IntentPrefix.Length..];
                return new IntentCatalogEntry(
                    typeName,
                    type,
                    typeName.EndsWith("AttackIntent", StringComparison.Ordinal) ? "attack" : "nonAttack",
                    "sts2.dll",
                    "ilspycmd class list",
                    0.7);
            })
            .OrderBy(entry => entry.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static GameVersionInfo ReadVersion(ExtractionPaths paths)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paths.ReleaseInfoPath));
        JsonElement root = document.RootElement;
        FileInfo dll = new(paths.Sts2DllPath);
        FileInfo xml = new(paths.Sts2XmlPath);

        return new GameVersionInfo(
            TryGetString(root, "version"),
            TryGetString(root, "commit"),
            TryGetString(root, "branch"),
            TryGetString(root, "date"),
            TryGetInt(root, "main_assembly_hash"),
            paths.Sts2DllPath,
            dll.Length,
            dll.LastWriteTimeUtc,
            paths.Sts2XmlPath,
            xml.Length,
            xml.LastWriteTimeUtc,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string? TryGetString(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
    }

    private static int? TryGetInt(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed
            : null;
    }

    private static string Slugify(string typeName)
    {
        List<char> chars = [];
        for (int i = 0; i < typeName.Length; i++)
        {
            char current = typeName[i];
            if (i > 0
                && char.IsUpper(current)
                && (char.IsLower(typeName[i - 1])
                    || (i + 1 < typeName.Length && char.IsLower(typeName[i + 1]))))
            {
                chars.Add('_');
            }

            chars.Add(char.ToUpperInvariant(current));
        }

        return new string(chars.ToArray());
    }
}
