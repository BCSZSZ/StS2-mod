using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CardValueOverlay.Modeling.RunHistory;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record SimulationDeckBuildOptions
{
    public string Name { get; init; } = "";

    public string InputPath { get; init; } = "";

    public string? RunId { get; init; }

    public string? Description { get; init; }

    public string? Source { get; init; }

    public IReadOnlyList<string> Assumptions { get; init; } = [];
}

public sealed class SimulationDeckDefinitionBuilder
{
    private static readonly Regex TextCardLinePattern = new(
        @"^\s*(?<count>\d+)\s+(?<modelId>CARD\.[A-Z0-9_]+(?:\+\d+)?)\s*(?<typeName>[A-Za-z][A-Za-z0-9_]*)?.*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SimulationDeckDefinition BuildFromFile(SimulationDeckBuildOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new InvalidOperationException("Deck name is required.");
        }

        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            throw new InvalidOperationException("Input path is required.");
        }

        if (!File.Exists(options.InputPath))
        {
            throw new InvalidOperationException($"Input path does not exist: {options.InputPath}");
        }

        string raw = File.ReadAllText(options.InputPath);
        DeckInputSelection selection = Path.GetExtension(options.InputPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ReadCardsFromJson(raw, options)
            : new DeckInputSelection(ReadCardsFromText(raw), null);

        IReadOnlyList<SimulationDeckCardSpec> normalizedCards = selection.Cards
            .Select(NormalizeCard)
            .Where(card => card.Count > 0)
            .ToArray();
        if (normalizedCards.Count == 0)
        {
            throw new InvalidOperationException($"No cards were found in {options.InputPath}.");
        }

        List<string> assumptions = [];
        if (!string.IsNullOrWhiteSpace(options.Source))
        {
            assumptions.Add($"Source: {options.Source}.");
        }

        if (!string.IsNullOrWhiteSpace(options.RunId))
        {
            assumptions.Add($"Run history id: {options.RunId}.");
        }

        AddRunHistoryAssumptions(assumptions, selection.SelectedRun);
        assumptions.Add("Generated from selected card-count information; card values are not patched in this deck file.");
        assumptions.AddRange(options.Assumptions.Where(assumption => !string.IsNullOrWhiteSpace(assumption)));

        return new SimulationDeckDefinition
        {
            Name = options.Name,
            Description = string.IsNullOrWhiteSpace(options.Description)
                ? "Simulation deck generated from selected StS2 card information."
                : options.Description,
            Cards = normalizedCards,
            Assumptions = assumptions
        };
    }

    public void WriteToFile(SimulationDeckDefinition deck, string outputPath)
    {
        string? parent = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(deck, options));
    }

    private static DeckInputSelection ReadCardsFromJson(string raw, SimulationDeckBuildOptions options)
    {
        JsonSerializerOptions serializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        using JsonDocument document = JsonDocument.Parse(raw);
        JsonElement root = document.RootElement;
        JsonElement? runsElement = ReadProperty(root, "runs");
        if (runsElement.HasValue)
        {
            RunHistoryDeckExtractionReport report =
                JsonSerializer.Deserialize<RunHistoryDeckExtractionReport>(raw, serializerOptions)
                ?? throw new InvalidOperationException($"Failed to read run-history extraction from {options.InputPath}.");
            RunHistoryDeckResult? selectedRun = report.Runs.FirstOrDefault(run =>
                string.IsNullOrWhiteSpace(options.RunId)
                || string.Equals(run.RunId, options.RunId, StringComparison.OrdinalIgnoreCase));
            if (selectedRun is null)
            {
                throw new InvalidOperationException($"RunId '{options.RunId}' was not found in {options.InputPath}.");
            }

            return new DeckInputSelection(selectedRun.Cards.Select(CardInput.FromRunHistoryCard).ToArray(), selectedRun);
        }

        JsonElement? cardsElement = ReadProperty(root, "cards");
        if (cardsElement.HasValue)
        {
            return new DeckInputSelection(ReadCardsFromJsonElement(cardsElement.Value), null);
        }

        return new DeckInputSelection(ReadCardsFromJsonElement(root), null);
    }

    private static IReadOnlyList<CardInput> ReadCardsFromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(CardInput.FromJson).ToArray();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return [CardInput.FromJson(element)];
        }

        throw new InvalidOperationException("JSON card input must be an object, array, or object with a cards array.");
    }

    private static IReadOnlyList<CardInput> ReadCardsFromText(string text)
    {
        List<CardInput> cards = [];
        foreach (string line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            Match match = TextCardLinePattern.Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            cards.Add(new CardInput
            {
                Count = int.Parse(match.Groups["count"].Value),
                ModelId = match.Groups["modelId"].Value,
                TypeName = match.Groups["typeName"].Success ? match.Groups["typeName"].Value : null
            });
        }

        if (cards.Count == 0)
        {
            throw new InvalidOperationException("No card rows were parsed from text input. Expected lines like: 4 CARD.DEFEND_REGENT DefendRegent");
        }

        return cards;
    }

    private static SimulationDeckCardSpec NormalizeCard(CardInput card)
    {
        int count = card.Count ?? 1;
        string? modelId = FirstNonWhiteSpace(card.ModelId, card.Id, card.Card);
        int? upgrade = card.Upgrade;
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            Match upgradedModelId = Regex.Match(modelId, @"^(CARD\.[A-Z0-9_]+)\+(\d+)$", RegexOptions.CultureInvariant);
            if (upgradedModelId.Success)
            {
                modelId = upgradedModelId.Groups[1].Value;
                upgrade = int.Parse(upgradedModelId.Groups[2].Value);
            }
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Card entry is missing modelId/id/Card.");
        }

        string typeName = FirstNonWhiteSpace(card.TypeName, card.TypeNamePascal)
            ?? ConvertModelIdToTypeName(modelId);
        string? notes = FirstNonWhiteSpace(card.Notes, card.NotesPascal);
        if (upgrade is > 0)
        {
            string upgradeNote = $"Input marks upgrade level {upgrade}; current scenario uses the base simulation card unless a scenario patch models this upgrade.";
            notes = string.IsNullOrWhiteSpace(notes) ? upgradeNote : $"{notes} {upgradeNote}";
        }

        return new SimulationDeckCardSpec
        {
            ModelId = modelId,
            TypeName = typeName,
            DisplayName = typeName,
            Count = count,
            Notes = notes
        };
    }

    private static void AddRunHistoryAssumptions(List<string> assumptions, RunHistoryDeckResult? selectedRun)
    {
        if (selectedRun is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedRun.Path))
        {
            assumptions.Add($"Deck source: {selectedRun.Path}.");
        }

        string scope = selectedRun.IncludesFloorRewards
            ? $"after applying floor {selectedRun.Floor} rewards/events"
            : $"before applying floor {selectedRun.Floor} rewards/events";
        assumptions.Add($"Run-history reconstruction scope: {scope}.");
    }

    private static string ConvertModelIdToTypeName(string modelId)
    {
        string name = modelId.StartsWith("CARD.", StringComparison.OrdinalIgnoreCase)
            ? modelId[5..]
            : modelId;
        return string.Concat(name
            .ToLowerInvariant()
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

    private sealed record DeckInputSelection(
        IReadOnlyList<CardInput> Cards,
        RunHistoryDeckResult? SelectedRun);

    private sealed record CardInput
    {
        public int? Count { get; init; }

        public string? ModelId { get; init; }

        public string? Id { get; init; }

        public string? Card { get; init; }

        public string? TypeName { get; init; }

        public string? TypeNamePascal { get; init; }

        public int? Upgrade { get; init; }

        public string? Notes { get; init; }

        public string? NotesPascal { get; init; }

        public static CardInput FromRunHistoryCard(RunHistoryDeckCard card)
        {
            return new CardInput
            {
                Count = card.Count,
                ModelId = card.Id,
                TypeName = card.TypeName,
                Upgrade = card.Upgrade
            };
        }

        public static CardInput FromJson(JsonElement element)
        {
            int? upgrade = ReadInt(element, "upgrade") ?? ReadInt(element, "Upgrade");
            string? card = ReadString(element, "Card");
            string? modelId = ReadString(element, "modelId");
            if (string.IsNullOrWhiteSpace(modelId))
            {
                modelId = ReadString(element, "ModelId");
            }

            return new CardInput
            {
                Count = ReadInt(element, "count") ?? ReadInt(element, "Count"),
                ModelId = modelId,
                Id = ReadString(element, "id") ?? ReadString(element, "Id"),
                Card = card,
                TypeName = ReadString(element, "typeName"),
                TypeNamePascal = ReadString(element, "TypeName"),
                Upgrade = upgrade,
                Notes = ReadString(element, "notes"),
                NotesPascal = ReadString(element, "Notes")
            };
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
    }
}
