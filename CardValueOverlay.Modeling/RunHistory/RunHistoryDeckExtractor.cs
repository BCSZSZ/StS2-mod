using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.RunHistory;

public sealed class RunHistoryDeckExtractor
{
    public RunHistoryDeckExtractionReport Extract(RunHistoryDeckExtractionOptions options)
    {
        string historyRoot = ResolveHistoryRoot(options.HistoryRoot);
        if (!Directory.Exists(historyRoot))
        {
            throw new InvalidOperationException($"History root does not exist: {historyRoot}");
        }

        IReadOnlyDictionary<string, string> typeNamesByModelId = LoadCardTypeNames(options.CatalogPath);
        List<RunHistoryDeckResult> results = [];
        foreach (string runPath in EnumerateRunFiles(historyRoot, options.RunId))
        {
            JsonDocument runDocument;
            try
            {
                runDocument = JsonDocument.Parse(File.ReadAllText(runPath));
            }
            catch (Exception ex) when (string.IsNullOrWhiteSpace(options.RunId))
            {
                _ = ex;
                continue;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse run history file {runPath}: {ex.Message}", ex);
            }

            using (runDocument)
            {
                JsonElement run = runDocument.RootElement;
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

                    results.Add(ReconstructRunDeck(runPath, run, options, typeNamesByModelId));
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
        string runPath,
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
            .GroupBy(card => new { card.Id, card.Upgrade })
            .Select(group =>
            {
                string id = group.Key.Id;
                return new RunHistoryDeckCard
                {
                    Count = group.Count(),
                    Id = id,
                    TypeName = typeNamesByModelId.TryGetValue(id, out string? typeName) ? typeName : "",
                    Upgrade = group.Key.Upgrade
                };
            })
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .ThenBy(card => card.Upgrade)
            .ToArray();

        return new RunHistoryDeckResult
        {
            RunId = System.IO.Path.GetFileNameWithoutExtension(runPath),
            StartTime = ReadLong(run, "start_time"),
            Build = ReadString(run, "build_id") ?? "",
            Seed = ReadString(run, "seed") ?? "",
            Path = runPath,
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

        deck.Add(new DeckCardInstance(id, ReadCardUpgrade(card) ?? 0));
    }

    private static void RemoveHistoryCard(List<DeckCardInstance> deck, JsonElement card)
    {
        string? id = ReadCardId(card);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        int index = deck.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
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
        int index = deck.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (index >= 0)
        {
            DeckCardInstance current = deck[index];
            deck[index] = current with { Upgrade = Math.Max(current.Upgrade, upgrade) };
        }
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

    private static IEnumerable<string> EnumerateRunFiles(string historyRoot, string? runId)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        IEnumerable<string> files = Directory.EnumerateFiles(historyRoot, "*.run", options);
        if (string.IsNullOrWhiteSpace(runId))
        {
            return files;
        }

        return files.Where(file => string.Equals(
            System.IO.Path.GetFileNameWithoutExtension(file),
            runId,
            StringComparison.OrdinalIgnoreCase));
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

    private sealed record DeckCardInstance(string Id, int Upgrade);
}
