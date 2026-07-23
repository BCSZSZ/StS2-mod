using System.Text.Json;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Ancient;

namespace CardValueOverlay.Core.History;

public sealed record LocalRunHistorySource(string Id, string Json);

public sealed record LocalHistoryStatsBuildResult(
    CardAdoptionCatalog CardAdoption,
    AncientChoiceCatalog AncientChoices,
    int ParsedRuns,
    int IncludedRuns,
    int OutcomeIncludedRuns,
    int FilteredRuns,
    int InvalidRuns);

/// <summary>
/// Builds the two runtime history catalogs from local .run JSON. The filters,
/// card-form rules, room rules, and denominators mirror fetch_spire_codex_runs.py.
/// </summary>
public static class LocalRunHistoryStatsBuilder
{
    private const string AllGroupKey = "all";
    private const string RoomFullOfCheese = "EVENT.ROOM_FULL_OF_CHEESE";

    public static LocalHistoryStatsBuildResult Build(
        IEnumerable<LocalRunHistorySource> sources,
        CardAdoptionCatalog referenceCatalog)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(referenceCatalog);

        Dictionary<string, SummaryGroup> groups = new(StringComparer.OrdinalIgnoreCase)
        {
            [AllGroupKey] = new()
        };
        int parsedRuns = 0;
        int includedRuns = 0;
        int outcomeIncludedRuns = 0;
        int filteredRuns = 0;
        int invalidRuns = 0;

        foreach (LocalRunHistorySource source in sources)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(source.Json);
                parsedRuns++;
                JsonElement run = document.RootElement;
                if (run.ValueKind != JsonValueKind.Object
                    || !MatchesScope(run, referenceCatalog.Scope.Filters, includeWin: false)
                    || !TryReadEligiblePlayer(
                        run,
                        referenceCatalog.Scope.Filters,
                        out JsonElement player,
                        out string character))
                {
                    filteredRuns++;
                    continue;
                }

                JsonElement? playerId = TryGetProperty(player, "id", out JsonElement id)
                    ? id
                    : null;
                List<List<AncientObservation>> ancientScreens = ReadAncientChoiceScreens(run, playerId);
                SummaryGroup characterGroup = groups.TryGetValue(character, out SummaryGroup? existing)
                    ? existing
                    : groups[character] = new SummaryGroup();
                bool won = ReadBool(run, "win");
                UpdateAncientOutcome(groups[AllGroupKey], ancientScreens, won);
                UpdateAncientOutcome(characterGroup, ancientScreens, won);
                outcomeIncludedRuns++;

                if (!MatchesScope(run, referenceCatalog.Scope.Filters, includeWin: true)
                    || !TryGetProperty(player, "deck", out JsonElement deck)
                    || deck.ValueKind != JsonValueKind.Array
                    || deck.GetArrayLength() == 0)
                {
                    filteredRuns++;
                    continue;
                }

                (List<CardObservation> rewardOffers, List<CardObservation> shopOffers) =
                    ReadCardOfferChoices(run, playerId);
                UpdateGroup(groups[AllGroupKey], deck, rewardOffers, shopOffers, ancientScreens);
                UpdateGroup(characterGroup, deck, rewardOffers, shopOffers, ancientScreens);
                includedRuns++;
            }
            catch (JsonException)
            {
                invalidRuns++;
            }
            catch (InvalidOperationException)
            {
                invalidRuns++;
            }
            catch (FormatException)
            {
                invalidRuns++;
            }
        }

        SummaryGroup allGroup = groups[AllGroupKey];
        Dictionary<string, CardAdoptionEntry> cards = BuildCardEntries(referenceCatalog, groups);
        Dictionary<string, AncientChoiceCharacterStats> ancientCharacters = groups
            .Where(item => !item.Key.Equals(AllGroupKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                item => item.Key,
                item => AncientChoiceCharacterStats.Create(
                    item.Value.TotalRuns,
                    item.Value.TotalAncientChoiceScreens,
                    item.Value.OutcomeTotalRuns,
                    item.Value.OutcomeWins,
                    item.Value.OutcomeAncientChoiceScreens,
                    item.Value.AncientChoices.ToDictionary(
                        choice => choice.Key,
                        choice => new AncientChoiceEntry
                        {
                            OfferCount = choice.Value.OfferCount,
                            PickCount = choice.Value.PickCount,
                            PickRate = Ratio(choice.Value.PickCount, choice.Value.OfferCount),
                            PickedRunCount = choice.Value.PickedRunCount,
                            PickedWinCount = choice.Value.PickedWinCount,
                            PickedWinRate = Ratio(choice.Value.PickedWinCount, choice.Value.PickedRunCount)
                        },
                        StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        return new LocalHistoryStatsBuildResult(
            CardAdoptionCatalog.Create(allGroup.TotalRuns, referenceCatalog.Scope, cards),
            AncientChoiceCatalog.Create(ancientCharacters),
            parsedRuns,
            includedRuns,
            outcomeIncludedRuns,
            filteredRuns,
            invalidRuns);
    }

    private static Dictionary<string, CardAdoptionEntry> BuildCardEntries(
        CardAdoptionCatalog referenceCatalog,
        IReadOnlyDictionary<string, SummaryGroup> groups)
    {
        Dictionary<string, CardAdoptionEntry> cards = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string cardKey, CardAdoptionEntry referenceEntry) in referenceCatalog.Cards)
        {
            Dictionary<string, CardAdoptionVariant> variants = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string variantKey, CardAdoptionVariant referenceVariant) in referenceEntry.Variants)
            {
                groups.TryGetValue(variantKey, out SummaryGroup? group);
                group ??= variantKey.Equals(AllGroupKey, StringComparison.OrdinalIgnoreCase)
                    ? groups[AllGroupKey]
                    : null;
                CardAccumulator accumulator = group is not null
                    && group.Cards.TryGetValue(cardKey, out CardAccumulator? existingAccumulator)
                        ? existingAccumulator
                        : new CardAccumulator();
                int sampleRuns = group?.TotalRuns ?? 0;

                variants[variantKey] = new CardAdoptionVariant
                {
                    SampleRuns = sampleRuns,
                    DistributionGroup = referenceVariant.DistributionGroup,
                    CopyDistributionEligible = referenceVariant.CopyDistributionEligible,
                    TotalRunsWith = accumulator.TotalRunsWith,
                    TotalCopies = accumulator.TotalCopies,
                    AvgCopiesWhenPresent = Ratio(accumulator.TotalCopies, accumulator.TotalRunsWith) ?? 0d,
                    Plus0 = BuildForm(accumulator.Plus0, sampleRuns),
                    Plus1 = BuildForm(accumulator.Plus1, sampleRuns)
                };
            }

            cards[cardKey] = new CardAdoptionEntry
            {
                Pools = referenceEntry.Pools,
                Variants = variants
            };
        }

        return cards;
    }

    private static CardAdoptionFormStats BuildForm(FormAccumulator form, int sampleRuns)
    {
        return new CardAdoptionFormStats
        {
            FinalRunCount = form.FinalRunCount,
            AppearanceProbability = Ratio(form.FinalRunCount, sampleRuns) ?? 0d,
            OfferCount = form.OfferCount,
            PickCount = form.PickCount,
            PickRate = Ratio(form.PickCount, form.OfferCount),
            ShopOfferCount = form.ShopOfferCount,
            ShopBuyCount = form.ShopBuyCount,
            ShopBuyRate = Ratio(form.ShopBuyCount, form.ShopOfferCount)
        };
    }

    private static bool MatchesScope(
        JsonElement run,
        CardAdoptionScopeFilters filters,
        bool includeWin)
    {
        if (filters.Ascension is int wantedAscension
            && ReadInt(run, "ascension", -1) != wantedAscension)
        {
            return false;
        }

        if (includeWin && !string.IsNullOrWhiteSpace(filters.Win))
        {
            bool wantedWin = filters.Win.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (ReadBool(run, "win") != wantedWin)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.GameMode)
            && !ReadString(run, "game_mode").Equals(filters.GameMode, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryGetProperty(run, "players", out JsonElement players)
            || players.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (filters.Players is int wantedPlayers && players.GetArrayLength() != wantedPlayers)
        {
            return false;
        }

        string buildId = ReadString(run, "build_id");
        if (!string.IsNullOrWhiteSpace(filters.BuildId)
            && !buildId.Equals(filters.BuildId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filters.BuildIds))
        {
            HashSet<string> allowedBuilds = filters.BuildIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
            if (!allowedBuilds.Contains(buildId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadEligiblePlayer(
        JsonElement run,
        CardAdoptionScopeFilters filters,
        out JsonElement player,
        out string character)
    {
        player = default;
        character = "";
        JsonElement players = run.GetProperty("players");
        if (players.GetArrayLength() != 1)
        {
            return false;
        }

        player = players[0];
        if (player.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        character = ReadString(player, "character");
        if (!string.IsNullOrWhiteSpace(filters.Character)
            && !character.Equals(filters.Character, StringComparison.Ordinal))
        {
            return false;
        }

        if (filters.Characters.Count > 0
            && !filters.Characters.Contains(character, StringComparer.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static void UpdateAncientOutcome(
        SummaryGroup group,
        IReadOnlyList<List<AncientObservation>> ancientScreens,
        bool won)
    {
        group.OutcomeTotalRuns++;
        if (won)
        {
            group.OutcomeWins++;
        }

        HashSet<string> pickedChoices = new(StringComparer.OrdinalIgnoreCase);
        foreach (List<AncientObservation> screen in ancientScreens)
        {
            if (screen.Count == 0)
            {
                continue;
            }

            group.OutcomeAncientChoiceScreens++;
            foreach (AncientObservation observation in screen.Where(choice => choice.WasSelected))
            {
                pickedChoices.Add(observation.TextKey);
            }
        }

        foreach (string textKey in pickedChoices)
        {
            AncientAccumulator choice = group.Ancient(textKey);
            choice.PickedRunCount++;
            if (won)
            {
                choice.PickedWinCount++;
            }
        }
    }

    private static void UpdateGroup(
        SummaryGroup group,
        JsonElement deck,
        IReadOnlyList<CardObservation> rewardOffers,
        IReadOnlyList<CardObservation> shopOffers,
        IReadOnlyList<List<AncientObservation>> ancientScreens)
    {
        group.TotalRuns++;
        Dictionary<string, (int Plus0, int Plus1)> copiesByCard = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement card in deck.EnumerateArray())
        {
            if (!TryReadCardIdentity(card, out CardFormKey identity))
            {
                continue;
            }

            (int plus0, int plus1) = copiesByCard.GetValueOrDefault(identity.CardKey);
            copiesByCard[identity.CardKey] = identity.IsUpgraded
                ? (plus0, plus1 + 1)
                : (plus0 + 1, plus1);
        }

        foreach ((string cardKey, (int plus0, int plus1)) in copiesByCard)
        {
            CardAccumulator card = group.Card(cardKey);
            card.TotalRunsWith++;
            card.TotalCopies += plus0 + plus1;
            if (plus0 > 0)
            {
                card.Plus0.FinalRunCount++;
            }
            if (plus1 > 0)
            {
                card.Plus1.FinalRunCount++;
            }
        }

        foreach (CardObservation offer in rewardOffers)
        {
            FormAccumulator form = group.Card(offer.Identity.CardKey).Form(offer.Identity.IsUpgraded);
            form.OfferCount++;
            if (offer.WasSelected)
            {
                form.PickCount++;
            }
        }

        foreach (CardObservation offer in shopOffers)
        {
            FormAccumulator form = group.Card(offer.Identity.CardKey).Form(offer.Identity.IsUpgraded);
            form.ShopOfferCount++;
            if (offer.WasSelected)
            {
                form.ShopBuyCount++;
            }
        }

        foreach (List<AncientObservation> screen in ancientScreens)
        {
            if (screen.Count == 0)
            {
                continue;
            }

            group.TotalAncientChoiceScreens++;
            foreach (AncientObservation observation in screen)
            {
                AncientAccumulator choice = group.Ancient(observation.TextKey);
                choice.OfferCount++;
                if (observation.WasSelected)
                {
                    choice.PickCount++;
                }
            }
        }
    }

    private static (List<CardObservation> Reward, List<CardObservation> Shop) ReadCardOfferChoices(
        JsonElement run,
        JsonElement? playerId)
    {
        List<CardObservation> rewardOffers = [];
        List<CardObservation> shopOffers = [];
        foreach (JsonElement node in EnumerateMapNodes(run))
        {
            HashSet<string> roomTypes = [];
            HashSet<string> roomModelIds = [];
            if (TryGetProperty(node, "rooms", out JsonElement rooms)
                && rooms.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement room in rooms.EnumerateArray())
                {
                    if (room.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    roomTypes.Add(ReadString(room, "room_type"));
                    roomModelIds.Add(ReadString(room, "model_id"));
                }
            }

            foreach (JsonElement stats in EnumeratePlayerStats(node, playerId))
            {
                if (roomTypes.Overlaps(["monster", "elite", "boss"]))
                {
                    rewardOffers.AddRange(ReadObservedCardChoices(stats, new HashSet<int> { 3, 4 }, 1));
                }
                if (roomTypes.Contains("shop"))
                {
                    shopOffers.AddRange(ReadShopCardChoices(stats));
                }
                if (roomModelIds.Contains(RoomFullOfCheese))
                {
                    rewardOffers.AddRange(ReadObservedCardChoices(stats, new HashSet<int> { 8 }, 2));
                }
            }
        }

        return (rewardOffers, shopOffers);
    }

    private static List<CardObservation> ReadObservedCardChoices(
        JsonElement stats,
        IReadOnlySet<int> allowedCounts,
        int maxPicked)
    {
        List<CardObservation> choices = ReadChoices(stats);
        if (!allowedCounts.Contains(choices.Count)
            || choices.Count(choice => choice.WasSelected) > maxPicked)
        {
            return [];
        }
        return choices;
    }

    private static List<CardObservation> ReadShopCardChoices(JsonElement stats)
    {
        List<CardObservation> offers = ReadChoices(stats);
        Dictionary<CardFormKey, int> alreadyPicked = [];
        foreach (CardObservation offer in offers.Where(offer => offer.WasSelected))
        {
            alreadyPicked[offer.Identity] = alreadyPicked.GetValueOrDefault(offer.Identity) + 1;
        }

        if (!TryGetProperty(stats, "cards_gained", out JsonElement cardsGained)
            || cardsGained.ValueKind != JsonValueKind.Array)
        {
            return offers;
        }

        foreach (JsonElement gained in cardsGained.EnumerateArray())
        {
            if (!TryReadCardIdentity(gained, out CardFormKey identity))
            {
                continue;
            }
            int counted = alreadyPicked.GetValueOrDefault(identity);
            if (counted > 0)
            {
                alreadyPicked[identity] = counted - 1;
                continue;
            }

            int matchingOffer = offers.FindIndex(offer =>
                offer.Identity == identity && !offer.WasSelected);
            if (matchingOffer >= 0)
            {
                offers[matchingOffer] = new CardObservation(identity, true);
            }
            else
            {
                offers.Add(new CardObservation(identity, true));
            }
        }

        return offers;
    }

    private static List<CardObservation> ReadChoices(JsonElement stats)
    {
        List<CardObservation> choices = [];
        if (!TryGetProperty(stats, "card_choices", out JsonElement rawChoices)
            || rawChoices.ValueKind != JsonValueKind.Array)
        {
            return choices;
        }

        foreach (JsonElement choice in rawChoices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            JsonElement rawCard = TryGetProperty(choice, "card", out JsonElement card)
                ? card
                : choice;
            if (TryReadCardIdentity(rawCard, out CardFormKey identity))
            {
                choices.Add(new CardObservation(identity, ReadBool(choice, "was_picked")));
            }
        }
        return choices;
    }

    private static List<List<AncientObservation>> ReadAncientChoiceScreens(
        JsonElement run,
        JsonElement? playerId)
    {
        List<List<AncientObservation>> screens = [];
        foreach (JsonElement node in EnumerateMapNodes(run))
        {
            foreach (JsonElement stats in EnumeratePlayerStats(node, playerId))
            {
                JsonElement rawChoices = default;
                bool found = false;
                foreach (string key in new[] { "ancient_choice", "ancient_choices", "ancientChoices" })
                {
                    if (TryGetProperty(stats, key, out JsonElement candidate)
                        && candidate.ValueKind == JsonValueKind.Array
                        && candidate.GetArrayLength() > 0)
                    {
                        rawChoices = candidate;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    continue;
                }

                List<AncientObservation> choices = [];
                foreach (JsonElement choice in rawChoices.EnumerateArray())
                {
                    if (choice.ValueKind != JsonValueKind.Object
                        || !TryReadAncientChoiceKey(choice, out string textKey))
                    {
                        continue;
                    }
                    choices.Add(new AncientObservation(textKey, ReadBool(choice, "was_chosen")));
                }
                if (choices.Count > 0)
                {
                    screens.Add(choices);
                }
            }
        }
        return screens;
    }

    private static bool TryReadAncientChoiceKey(JsonElement choice, out string textKey)
    {
        textKey = ReadString(choice, "TextKey");
        if (textKey.Length == 0)
        {
            textKey = ReadString(choice, "textKey");
        }
        if (textKey.Length == 0)
        {
            textKey = ReadString(choice, "text_key");
        }
        if (textKey.Length == 0
            && TryGetProperty(choice, "title", out JsonElement title)
            && title.ValueKind == JsonValueKind.Object)
        {
            textKey = ReadString(title, "key");
        }

        textKey = AncientChoiceCatalog.NormalizeTextKey(textKey);
        return textKey.Length > 0;
    }

    private static IEnumerable<JsonElement> EnumerateMapNodes(JsonElement run)
    {
        if (!TryGetProperty(run, "map_point_history", out JsonElement acts)
            || acts.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (JsonElement act in acts.EnumerateArray())
        {
            if (act.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (JsonElement node in act.EnumerateArray())
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    yield return node;
                }
            }
        }
    }

    private static IEnumerable<JsonElement> EnumeratePlayerStats(JsonElement node, JsonElement? playerId)
    {
        if (!TryGetProperty(node, "player_stats", out JsonElement playerStats)
            || playerStats.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (JsonElement stats in playerStats.EnumerateArray())
        {
            if (stats.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (playerId is JsonElement wanted
                && wanted.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                && TryGetProperty(stats, "player_id", out JsonElement statsPlayerId)
                && statsPlayerId.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                && !statsPlayerId.ToString().Equals(wanted.ToString(), StringComparison.Ordinal))
            {
                continue;
            }
            yield return stats;
        }
    }

    private static bool TryReadCardIdentity(JsonElement card, out CardFormKey identity)
    {
        identity = default;
        string value;
        int upgradeLevel = 0;
        if (card.ValueKind == JsonValueKind.String)
        {
            value = card.GetString() ?? "";
        }
        else if (card.ValueKind == JsonValueKind.Object)
        {
            value = ReadString(card, "id");
            upgradeLevel = ReadInt(card, "current_upgrade_level", 0);
        }
        else
        {
            return false;
        }

        if (value.Length == 0)
        {
            return false;
        }
        if (value.EndsWith("+1", StringComparison.Ordinal))
        {
            value = value[..^2];
            upgradeLevel = Math.Max(upgradeLevel, 1);
        }
        if (!value.StartsWith("CARD.", StringComparison.OrdinalIgnoreCase))
        {
            value = $"CARD.{value}";
        }
        identity = new CardFormKey(value, upgradeLevel > 0);
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }
        value = default;
        return false;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
    {
        return TryGetProperty(element, name, out JsonElement value)
            && value.TryGetInt32(out int result)
                ? result
                : fallback;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static double? Ratio(int numerator, int denominator)
    {
        return denominator > 0 ? (double)numerator / denominator : null;
    }

    private readonly record struct CardFormKey(string CardKey, bool IsUpgraded);
    private readonly record struct CardObservation(CardFormKey Identity, bool WasSelected);
    private readonly record struct AncientObservation(string TextKey, bool WasSelected);

    private sealed class SummaryGroup
    {
        public int TotalRuns { get; set; }
        public int TotalAncientChoiceScreens { get; set; }
        public int OutcomeTotalRuns { get; set; }
        public int OutcomeWins { get; set; }
        public int OutcomeAncientChoiceScreens { get; set; }
        public Dictionary<string, CardAccumulator> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, AncientAccumulator> AncientChoices { get; } = new(StringComparer.OrdinalIgnoreCase);

        public CardAccumulator Card(string cardKey)
        {
            return Cards.TryGetValue(cardKey, out CardAccumulator? card)
                ? card
                : Cards[cardKey] = new CardAccumulator();
        }

        public AncientAccumulator Ancient(string textKey)
        {
            string normalized = AncientChoiceCatalog.NormalizeTextKey(textKey);
            return AncientChoices.TryGetValue(normalized, out AncientAccumulator? choice)
                ? choice
                : AncientChoices[normalized] = new AncientAccumulator();
        }
    }

    private sealed class CardAccumulator
    {
        public int TotalRunsWith { get; set; }
        public int TotalCopies { get; set; }
        public FormAccumulator Plus0 { get; } = new();
        public FormAccumulator Plus1 { get; } = new();

        public FormAccumulator Form(bool upgraded) => upgraded ? Plus1 : Plus0;
    }

    private sealed class FormAccumulator
    {
        public int FinalRunCount { get; set; }
        public int OfferCount { get; set; }
        public int PickCount { get; set; }
        public int ShopOfferCount { get; set; }
        public int ShopBuyCount { get; set; }
    }

    private sealed class AncientAccumulator
    {
        public int OfferCount { get; set; }
        public int PickCount { get; set; }
        public int PickedRunCount { get; set; }
        public int PickedWinCount { get; set; }
    }
}
