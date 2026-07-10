using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.RunHistory;

public sealed class RunHistoryDeckExtractor
{
    public RunHistoryDeckExtractionReport Extract(RunHistoryDeckExtractionOptions options)
    {
        string? historyExportPath = string.IsNullOrWhiteSpace(options.HistoryExportPath)
            ? null
            : options.HistoryExportPath;
        string historyRoot = historyExportPath ?? ResolveHistoryRoot(options.HistoryRoot);
        if (historyExportPath is not null)
        {
            if (!File.Exists(historyExportPath))
            {
                throw new InvalidOperationException($"Run history export does not exist: {historyExportPath}");
            }
        }
        else if (!Directory.Exists(historyRoot))
        {
            throw new InvalidOperationException($"History root does not exist: {historyRoot}");
        }

        IReadOnlyDictionary<string, string> typeNamesByModelId = LoadCardTypeNames(options.CatalogPath);
        List<RunHistoryDeckResult> results = [];
        foreach (RunHistorySource source in EnumerateRunSources(historyRoot, historyExportPath, options.RunId))
        {
            JsonDocument runDocument;
            try
            {
                runDocument = JsonDocument.Parse(source.Json);
            }
            catch (Exception ex) when (string.IsNullOrWhiteSpace(options.RunId) || source.IsExport)
            {
                _ = ex;
                continue;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse run history source {source.Path}: {ex.Message}", ex);
            }

            using (runDocument)
            {
                JsonElement run = runDocument.RootElement;
                string runId = ResolveRunId(source, run);
                if (!string.IsNullOrWhiteSpace(options.RunId)
                    && !string.Equals(runId, options.RunId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ReadBool(run, "win"))
                {
                    continue;
                }

                if (ReadInt(run, "ascension") != options.Ascension)
                {
                    continue;
                }

                foreach (JsonElement player in ReadItems(run, "players"))
                {
                    if (!string.Equals(ReadString(player, "character"), options.Character, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    results.Add(ReconstructRunDeck(source, runId, run, options, typeNamesByModelId));
                }
            }
        }

        RunHistoryDeckResult[] ordered = results
            .OrderByDescending(item => item.StartTime)
            .ToArray();
        if (string.IsNullOrWhiteSpace(options.RunId) && options.Limit > 0)
        {
            ordered = ordered.Take(options.Limit).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(options.RunId) && ordered.Length == 0)
        {
            throw new InvalidOperationException(
                $"RunId '{options.RunId}' was not found or did not match character={options.Character}, ascension={options.Ascension}, win=true.");
        }

        return new RunHistoryDeckExtractionReport
        {
            GeneratedAt = DateTimeOffset.Now,
            HistoryRoot = historyRoot,
            CatalogPath = options.CatalogPath,
            Character = options.Character,
            Ascension = options.Ascension,
            Floor = options.Floor,
            IncludesFloorRewards = options.IncludeFloorRewards,
            Runs = ordered
        };
    }

    private static RunHistoryDeckResult ReconstructRunDeck(
        RunHistorySource source,
        string runId,
        JsonElement run,
        RunHistoryDeckExtractionOptions options,
        IReadOnlyDictionary<string, string> typeNamesByModelId)
    {
        List<DeckCardInstance> deck = NewStarterDeck(options.Character, options.Ascension);
        List<string> events = [];
        int floorNumber = 0;
        foreach (JsonElement act in ReadItems(run, "map_point_history"))
        {
            foreach (JsonElement node in AsItems(act))
            {
                floorNumber++;
                if (!options.IncludeFloorRewards && floorNumber >= options.Floor)
                {
                    break;
                }

                foreach (JsonElement stats in ReadItems(node, "player_stats"))
                {
                    ApplyPlayerStats(deck, stats, floorNumber, events);
                }

                if (options.IncludeFloorRewards && floorNumber >= options.Floor)
                {
                    break;
                }
            }

            if (floorNumber >= options.Floor)
            {
                break;
            }
        }

        IReadOnlyList<RunHistoryDeckCard> cards = deck
            .GroupBy(card => new { card.Id, card.Upgrade, card.EnchantmentId, card.EnchantmentAmount })
            .Select(group =>
            {
                string id = group.Key.Id;
                return new RunHistoryDeckCard
                {
                    Count = group.Count(),
                    Id = id,
                    TypeName = typeNamesByModelId.TryGetValue(id, out string? typeName) ? typeName : "",
                    Upgrade = group.Key.Upgrade,
                    EnchantmentId = group.Key.EnchantmentId,
                    EnchantmentAmount = group.Key.EnchantmentAmount
                };
            })
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .ThenBy(card => card.Upgrade)
            .ThenBy(card => card.EnchantmentId, StringComparer.Ordinal)
            .ThenBy(card => card.EnchantmentAmount)
            .ToArray();

        return new RunHistoryDeckResult
        {
            RunId = runId,
            StartTime = ReadLong(run, "start_time"),
            Build = ReadString(run, "build_id") ?? "",
            Seed = ReadString(run, "seed") ?? "",
            Path = source.Path,
            Character = options.Character,
            Ascension = options.Ascension,
            Floor = options.Floor,
            IncludesFloorRewards = options.IncludeFloorRewards,
            DeckCount = deck.Count,
            Events = events,
            Cards = cards
        };
    }

    private static void ApplyPlayerStats(
        List<DeckCardInstance> deck,
        JsonElement stats,
        int floorNumber,
        List<string> events)
    {
        foreach (JsonElement card in ReadItems(stats, "cards_removed"))
        {
            string? id = ReadCardId(card);
            if (!string.IsNullOrWhiteSpace(id))
            {
                events.Add($"F{floorNumber} remove {id}");
            }

            RemoveHistoryCard(deck, card);
        }

        foreach (JsonElement transform in ReadItems(stats, "cards_transformed"))
        {
            JsonElement? original = ReadProperty(transform, "original_card");
            JsonElement? final = ReadProperty(transform, "final_card");
            string? originalId = original.HasValue ? ReadCardId(original.Value) : null;
            string? finalId = final.HasValue ? ReadCardId(final.Value) : null;
            if (!string.IsNullOrWhiteSpace(originalId) || !string.IsNullOrWhiteSpace(finalId))
            {
                events.Add($"F{floorNumber} transform {originalId} -> {finalId}");
            }

            if (original.HasValue)
            {
                RemoveHistoryCard(deck, original.Value);
            }

            if (final.HasValue)
            {
                AddHistoryCard(deck, final.Value);
            }
        }

        foreach (JsonElement card in ReadItems(stats, "cards_gained"))
        {
            string? id = ReadCardId(card);
            if (!string.IsNullOrWhiteSpace(id))
            {
                events.Add($"F{floorNumber} gain {id}");
            }

            AddHistoryCard(deck, card);
        }

        foreach (JsonElement card in ReadItems(stats, "upgraded_cards"))
        {
            string? id = ReadCardId(card);
            if (!string.IsNullOrWhiteSpace(id))
            {
                events.Add($"F{floorNumber} upgrade {id}");
            }

            UpgradeHistoryCard(deck, card);
        }

        foreach (JsonElement enchantment in ReadItems(stats, "cards_enchanted"))
        {
            JsonElement? card = ReadProperty(enchantment, "card");
            string? id = card.HasValue ? ReadCardId(card.Value) : null;
            string? enchantmentId = ReadEventEnchantmentId(enchantment)
                ?? (card.HasValue ? ReadCardEnchantmentId(card.Value) : null);
            int? amount = ReadEventEnchantmentAmount(enchantment)
                ?? (card.HasValue ? ReadCardEnchantmentAmount(card.Value) : null);
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(enchantmentId))
            {
                events.Add($"F{floorNumber} enchant {id} {enchantmentId}:{amount ?? 1}");
            }

            if (card.HasValue)
            {
                EnchantHistoryCard(deck, card.Value, enchantmentId, amount);
            }
        }
    }

    private static List<DeckCardInstance> NewStarterDeck(string character, int ascension)
    {
        if (!string.Equals(character, "CHARACTER.REGENT", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only CHARACTER.REGENT starter deck reconstruction is implemented.");
        }

        List<DeckCardInstance> deck = [];
        AddStarterCard(deck, "CARD.STRIKE_REGENT", 4);
        AddStarterCard(deck, "CARD.DEFEND_REGENT", 4);
        AddStarterCard(deck, "CARD.FALLING_STAR", 1);
        AddStarterCard(deck, "CARD.VENERATE", 1);
        if (ascension >= 10)
        {
            AddStarterCard(deck, "CARD.ASCENDERS_BANE", 1);
        }

        return deck;
    }

    private static void AddStarterCard(List<DeckCardInstance> deck, string id, int count)
    {
        for (int i = 0; i < count; i++)
        {
            deck.Add(new DeckCardInstance(id, 0));
        }
    }

    private static void AddHistoryCard(List<DeckCardInstance> deck, JsonElement card)
    {
        string? id = ReadCardId(card);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        string? enchantmentId = ReadCardEnchantmentId(card);
        int? enchantmentAmount = NormalizeEnchantmentAmount(enchantmentId, ReadCardEnchantmentAmount(card));
        deck.Add(new DeckCardInstance(
            id,
            ReadCardUpgrade(card) ?? 0,
            enchantmentId,
            enchantmentAmount));
    }

    private static void RemoveHistoryCard(List<DeckCardInstance> deck, JsonElement card)
    {
        string? id = ReadCardId(card);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        int index = FindBestHistoryCardIndex(deck, card, preferUnenchanted: false);
        if (index >= 0)
        {
            deck.RemoveAt(index);
        }
    }

    private static void UpgradeHistoryCard(List<DeckCardInstance> deck, JsonElement card)
    {
        string? id = ReadCardId(card);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        int upgrade = ReadCardUpgrade(card) ?? 1;
        int index = FindBestHistoryCardIndex(deck, card, preferUnenchanted: false);
        if (index >= 0)
        {
            DeckCardInstance current = deck[index];
            string? enchantmentId = ReadCardEnchantmentId(card) ?? current.EnchantmentId;
            deck[index] = current with
            {
                Upgrade = Math.Max(current.Upgrade, upgrade),
                EnchantmentId = enchantmentId,
                EnchantmentAmount = NormalizeEnchantmentAmount(enchantmentId, ReadCardEnchantmentAmount(card) ?? current.EnchantmentAmount)
            };
        }
    }

    private static void EnchantHistoryCard(
        List<DeckCardInstance> deck,
        JsonElement card,
        string? enchantmentId,
        int? amount)
    {
        if (string.IsNullOrWhiteSpace(enchantmentId))
        {
            return;
        }

        int index = FindBestHistoryCardIndex(deck, card, preferUnenchanted: true);
        if (index < 0)
        {
            return;
        }

        DeckCardInstance current = deck[index];
        deck[index] = current with
        {
            EnchantmentId = enchantmentId,
            EnchantmentAmount = Math.Max(1, amount ?? 1)
        };
    }

    private static int FindBestHistoryCardIndex(
        List<DeckCardInstance> deck,
        JsonElement card,
        bool preferUnenchanted)
    {
        string? id = ReadCardId(card);
        if (string.IsNullOrWhiteSpace(id))
        {
            return -1;
        }

        int? upgrade = ReadCardUpgrade(card);
        string? enchantmentId = ReadCardEnchantmentId(card);
        int? enchantmentAmount = NormalizeEnchantmentAmount(enchantmentId, ReadCardEnchantmentAmount(card));
        int index = FindHistoryCardIndex(deck, id, upgrade, enchantmentId, enchantmentAmount, requireUnenchanted: false);
        if (index >= 0)
        {
            return index;
        }

        if (preferUnenchanted)
        {
            index = FindHistoryCardIndex(deck, id, upgrade, null, null, requireUnenchanted: true);
            if (index >= 0)
            {
                return index;
            }

            index = FindHistoryCardIndex(deck, id, null, null, null, requireUnenchanted: true);
            if (index >= 0)
            {
                return index;
            }
        }

        index = FindHistoryCardIndex(deck, id, upgrade, null, null, requireUnenchanted: false);
        return index >= 0
            ? index
            : FindHistoryCardIndex(deck, id, null, null, null, requireUnenchanted: false);
    }

    private static int FindHistoryCardIndex(
        List<DeckCardInstance> deck,
        string id,
        int? upgrade,
        string? enchantmentId,
        int? enchantmentAmount,
        bool requireUnenchanted)
    {
        for (int i = 0; i < deck.Count; i++)
        {
            DeckCardInstance item = deck[i];
            if (!string.Equals(item.Id, id, StringComparison.Ordinal))
            {
                continue;
            }

            if (upgrade.HasValue && item.Upgrade != upgrade.Value)
            {
                continue;
            }

            if (requireUnenchanted && item.EnchantmentId is not null)
            {
                continue;
            }

            if (!requireUnenchanted && enchantmentId is not null)
            {
                if (!string.Equals(item.EnchantmentId, enchantmentId, StringComparison.Ordinal)
                    || item.EnchantmentAmount != enchantmentAmount)
                {
                    continue;
                }
            }

            return i;
        }

        return -1;
    }

    private static string ResolveHistoryRoot(string? requestedRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedRoot))
        {
            return requestedRoot;
        }

        string? environmentRoot = Environment.GetEnvironmentVariable("STS2_RUN_HISTORY_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
        {
            return environmentRoot;
        }

        return @"C:\Program Files (x86)\Steam\userdata";
    }

    private static IReadOnlyDictionary<string, string> LoadCardTypeNames(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        IReadOnlyList<ModelCatalogEntry> entries =
            JsonSerializer.Deserialize<List<ModelCatalogEntry>>(File.ReadAllText(catalogPath), options)
            ?? [];

        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ModelId))
            .GroupBy(entry => entry.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().TypeName,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<RunHistorySource> EnumerateRunSources(
        string historyRoot,
        string? historyExportPath,
        string? runId)
    {
        if (!string.IsNullOrWhiteSpace(historyExportPath))
        {
            foreach (RunHistorySource source in EnumerateExportRunSources(historyExportPath))
            {
                yield return source;
            }

            yield break;
        }

        foreach (RunHistorySource source in EnumerateLocalRunSources(historyRoot, runId))
        {
            yield return source;
        }
    }

    private static IEnumerable<RunHistorySource> EnumerateLocalRunSources(string historyRoot, string? runId)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        IEnumerable<string> files = Directory.EnumerateFiles(historyRoot, "*.run", options);
        if (!string.IsNullOrWhiteSpace(runId))
        {
            files = files.Where(file => string.Equals(
                System.IO.Path.GetFileNameWithoutExtension(file),
                runId,
                StringComparison.OrdinalIgnoreCase));
        }

        foreach (string file in files)
        {
            yield return new RunHistorySource(
                file,
                System.IO.Path.GetFileNameWithoutExtension(file),
                File.ReadAllText(file),
                PreferSourceRunId: true,
                IsExport: false);
        }
    }

    private static IEnumerable<RunHistorySource> EnumerateExportRunSources(string historyExportPath)
    {
        using FileStream file = File.OpenRead(historyExportPath);
        using Stream stream = historyExportPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        using StreamReader reader = new(stream);
        long lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return new RunHistorySource(
                $"{historyExportPath}#{lineNumber.ToString(CultureInfo.InvariantCulture)}",
                lineNumber.ToString(CultureInfo.InvariantCulture),
                line,
                PreferSourceRunId: false,
                IsExport: true);
        }
    }

    private static string ResolveRunId(RunHistorySource source, JsonElement run)
    {
        if (source.PreferSourceRunId && !string.IsNullOrWhiteSpace(source.RunId))
        {
            return source.RunId;
        }

        string? hash = ReadString(run, "run_hash") ?? ReadString(run, "hash");
        if (!string.IsNullOrWhiteSpace(hash))
        {
            return hash;
        }

        long startTime = ReadLong(run, "start_time");
        if (startTime != 0)
        {
            return startTime.ToString(CultureInfo.InvariantCulture);
        }

        return source.RunId;
    }

    private static IEnumerable<JsonElement> ReadItems(JsonElement element, string propertyName)
    {
        JsonElement? value = ReadProperty(element, propertyName);
        return value.HasValue ? AsItems(value.Value) : [];
    }

    private static IEnumerable<JsonElement> AsItems(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
            : [element];
    }

    private static JsonElement? ReadProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadCardId(JsonElement card)
    {
        if (card.ValueKind == JsonValueKind.String)
        {
            return card.GetString();
        }

        return ReadString(card, "id");
    }

    private static int? ReadCardUpgrade(JsonElement card)
    {
        if (card.ValueKind == JsonValueKind.String)
        {
            return null;
        }

        return ReadInt(card, "current_upgrade_level") ?? ReadInt(card, "upgrade");
    }

    private static string? ReadCardEnchantmentId(JsonElement card)
    {
        if (card.ValueKind == JsonValueKind.String)
        {
            return null;
        }

        JsonElement? enchantment = ReadProperty(card, "enchantment");
        if (!enchantment.HasValue)
        {
            return null;
        }

        return enchantment.Value.ValueKind == JsonValueKind.String
            ? enchantment.Value.GetString()
            : ReadString(enchantment.Value, "id");
    }

    private static string? ReadEventEnchantmentId(JsonElement enchantmentEvent)
    {
        JsonElement? enchantment = ReadProperty(enchantmentEvent, "enchantment")
            ?? ReadProperty(enchantmentEvent, "enchantmentId")
            ?? ReadProperty(enchantmentEvent, "enchantment_id");
        if (!enchantment.HasValue)
        {
            return null;
        }

        return enchantment.Value.ValueKind == JsonValueKind.String
            ? enchantment.Value.GetString()
            : ReadString(enchantment.Value, "id");
    }

    private static int? ReadCardEnchantmentAmount(JsonElement card)
    {
        if (card.ValueKind == JsonValueKind.String)
        {
            return null;
        }

        JsonElement? enchantment = ReadProperty(card, "enchantment");
        if (!enchantment.HasValue || enchantment.Value.ValueKind == JsonValueKind.String)
        {
            return null;
        }

        return ReadInt(enchantment.Value, "amount");
    }

    private static int? ReadEventEnchantmentAmount(JsonElement enchantmentEvent)
    {
        int? amount = ReadInt(enchantmentEvent, "amount");
        if (amount.HasValue)
        {
            return Math.Max(1, amount.Value);
        }

        JsonElement? enchantment = ReadProperty(enchantmentEvent, "enchantment");
        if (!enchantment.HasValue || enchantment.Value.ValueKind == JsonValueKind.String)
        {
            return null;
        }

        amount = ReadInt(enchantment.Value, "amount");
        return amount.HasValue ? Math.Max(1, amount.Value) : null;
    }

    private static int? NormalizeEnchantmentAmount(string? enchantmentId, int? amount)
    {
        return string.IsNullOrWhiteSpace(enchantmentId)
            ? null
            : Math.Max(1, amount ?? 1);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        JsonElement? property = ReadProperty(element, propertyName);
        if (!property.HasValue)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.String)
        {
            return property.Value.GetString();
        }

        return property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? property.Value.ToString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        JsonElement? property = ReadProperty(element, propertyName);
        if (!property.HasValue)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int value))
        {
            return value;
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && int.TryParse(property.Value.GetString(), out int stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static long ReadLong(JsonElement element, string propertyName)
    {
        JsonElement? property = ReadProperty(element, propertyName);
        if (!property.HasValue)
        {
            return 0;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out long value))
        {
            return value;
        }

        return property.Value.ValueKind == JsonValueKind.String
            && long.TryParse(property.Value.GetString(), out long stringValue)
            ? stringValue
            : 0;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        JsonElement? property = ReadProperty(element, propertyName);
        if (!property.HasValue)
        {
            return false;
        }

        return property.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.Value.GetString(), out bool value) && value,
            _ => false
        };
    }

    private sealed record DeckCardInstance(
        string Id,
        int Upgrade,
        string? EnchantmentId = null,
        int? EnchantmentAmount = null);

    private sealed record RunHistorySource(
        string Path,
        string RunId,
        string Json,
        bool PreferSourceRunId,
        bool IsExport);
}
