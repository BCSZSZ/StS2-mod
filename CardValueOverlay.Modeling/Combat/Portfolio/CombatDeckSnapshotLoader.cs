using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record CombatDeckCardReference(
    string ModelId,
    string TypeName,
    int Upgrade,
    int Count);

public sealed record CombatDeckSnapshot(
    string RunId,
    string Group,
    int Floor,
    int Ascension,
    IReadOnlyList<CombatDeckCardReference> Cards)
{
    public string SnapshotId => $"{RunId}:{Group}:{Floor}";
}

public sealed record CompiledCombatDeck(
    string DeckId,
    string Group,
    IReadOnlyList<int> CardDefinitionIds,
    bool IsSupported,
    IReadOnlyList<string> UnsupportedReasons)
{
    public string StableKey => $"{DeckId}:{string.Join(',', CardDefinitionIds.Order())}";
}

public sealed class CombatDeckSnapshotLoader
{
    public IReadOnlyList<CombatDeckSnapshot> Load(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("decks", out JsonElement decksElement) || decksElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Deck source '{path}' has no decks array.");
        }

        List<CombatDeckSnapshot> snapshots = [];
        foreach (JsonElement deck in decksElement.EnumerateArray())
        {
            List<CombatDeckCardReference> cards = [];
            foreach (JsonElement card in deck.GetProperty("cards").EnumerateArray())
            {
                string modelId = card.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty;
                string typeName = card.TryGetProperty("typeName", out JsonElement type) ? type.GetString() ?? string.Empty : string.Empty;
                int upgrade = ReadInt(card, "upgrade", 0);
                int count = ReadInt(card, "count", 1);
                cards.Add(new CombatDeckCardReference(modelId, typeName, upgrade, count));
            }

            snapshots.Add(new CombatDeckSnapshot(
                deck.GetProperty("runId").GetString() ?? throw new InvalidOperationException("Deck runId is null."),
                deck.GetProperty("group").GetString() ?? throw new InvalidOperationException("Deck group is null."),
                ReadInt(deck, "floor", 0),
                ReadInt(deck, "ascension", 10),
                cards));
        }
        return snapshots;
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;
    }

    public CompiledCombatDeck CompileDeck(CombatDeckSnapshot snapshot, CombatCardCatalog catalog)
    {
        List<int> cards = [];
        List<string> unsupported = [];
        foreach (CombatDeckCardReference reference in snapshot.Cards)
        {
            string upgradedKey = $"{reference.ModelId}+{reference.Upgrade}";
            if (!catalog.TryGet(upgradedKey, out CombatCardDefinition? card) &&
                !catalog.TryGet(reference.TypeName, out card))
            {
                unsupported.Add($"Card '{reference.ModelId}/{reference.TypeName}+{reference.Upgrade}' was not compiled.");
                continue;
            }

            if (card.UpgradeLevel != reference.Upgrade)
            {
                CombatCardDefinition? exact = catalog.Cards.FirstOrDefault(candidate =>
                    string.Equals(candidate.ModelId, reference.ModelId, StringComparison.OrdinalIgnoreCase) &&
                    candidate.UpgradeLevel == reference.Upgrade);
                if (exact is not null) card = exact;
            }

            if (!card.IsSupported)
            {
                unsupported.Add($"Card '{card.StableKey}' unsupported: {string.Join("; ", card.UnsupportedReasons)}");
            }

            cards.AddRange(Enumerable.Repeat(card.DefinitionId, reference.Count));
        }

        return new CompiledCombatDeck(
            snapshot.SnapshotId,
            snapshot.Group,
            cards,
            unsupported.Count == 0,
            unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
