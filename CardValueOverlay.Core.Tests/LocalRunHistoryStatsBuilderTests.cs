using System.Text.Json;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.History;

namespace CardValueOverlay.Core.Tests;

internal static class LocalRunHistoryStatsBuilderTests
{
    public static void RunAll()
    {
        UsesReferenceScopeAndMatchingChoiceDenominators();
    }

    private static void UsesReferenceScopeAndMatchingChoiceDenominators()
    {
        CardAdoptionCatalog reference = CreateReferenceCatalog();
        LocalHistoryStatsBuildResult result = LocalRunHistoryStatsBuilder.Build(
        [
            new("regent-1", RegentRunOne),
            new("regent-2", RegentRunTwo),
            new("silent-1", SilentRun),
            new("loss", RegentRunOne.Replace("\"win\": true", "\"win\": false")),
            new("old-build", RegentRunOne.Replace("v0.107.1", "v0.107.0")),
            new("broken", "{ definitely not json")
        ], reference);

        AssertEqual(5, result.ParsedRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(3, result.IncludedRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(4, result.OutcomeIncludedRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(2, result.FilteredRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, result.InvalidRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));

        CardAdoptionDisplayStats? regentA = result.CardAdoption.Resolve(
            "CARD.A",
            CardUpgradeState.Unupgraded,
            "CHARACTER.REGENT");
        AssertEqual(2, regentA?.SampleRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1d, regentA?.AppearanceProbability, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1.5d, regentA?.AvgCopiesWhenPresent, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(2, regentA?.OfferCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(0.5d, regentA?.PickRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));

        CardAdoptionDisplayStats? regentShop = result.CardAdoption.Resolve(
            "CARD.SHOP",
            CardUpgradeState.Unupgraded,
            "CHARACTER.REGENT");
        AssertEqual(1, regentShop?.ShopOfferCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1d, regentShop?.ShopBuyRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));

        CardAdoptionDisplayStats? regentColorless = result.CardAdoption.Resolve(
            "CARD.COLORLESS",
            CardUpgradeState.Unupgraded,
            "CHARACTER.REGENT");
        CardAdoptionDisplayStats? silentColorless = result.CardAdoption.Resolve(
            "CARD.COLORLESS",
            CardUpgradeState.Unupgraded,
            "CHARACTER.SILENT");
        AssertEqual(2, regentColorless?.SampleRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(0d, regentColorless?.PickRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, silentColorless?.SampleRuns, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1d, silentColorless?.PickRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));

        Ancient.AncientChoiceDisplayStats? regentAncient = result.AncientChoices.Resolve(
            "ANCIENT.TEST.pages.INITIAL.options.OPTION_A",
            "CHARACTER.REGENT");
        Ancient.AncientChoiceDisplayStats? silentAncient = result.AncientChoices.Resolve(
            "OPTION_A",
            "CHARACTER.SILENT");
        AssertEqual(2, regentAncient?.OfferCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, regentAncient?.PickCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(0.5d, regentAncient?.PickRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(2, regentAncient?.PickedRunCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, regentAncient?.PickedWinCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(0.5d, regentAncient?.PickedWinRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, silentAncient?.OfferCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, silentAncient?.PickCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1d, silentAncient?.PickRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, silentAncient?.PickedRunCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1, silentAncient?.PickedWinCount, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(1d, silentAncient?.PickedWinRate, nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
        AssertEqual(
            null,
            result.AncientChoices.Resolve("OPTION_A", null),
            nameof(UsesReferenceScopeAndMatchingChoiceDenominators));
    }

    private static CardAdoptionCatalog CreateReferenceCatalog()
    {
        object Variant(string group) => new
        {
            sampleRuns = 100,
            distributionGroup = group,
            copyDistributionEligible = true,
            plus0 = new { },
            plus1 = new { }
        };
        object RegentCard() => new
        {
            pools = new[] { "Regent" },
            variants = new Dictionary<string, object>
            {
                ["CHARACTER.REGENT"] = Variant("Regent")
            }
        };

        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            scope = new
            {
                filters = new
                {
                    ascension = 10,
                    win = "true",
                    players = 1,
                    game_mode = "standard",
                    build_ids = "v0.107.1,v0.108.0,v0.109.0",
                    characters = new[] { "CHARACTER.REGENT", "CHARACTER.SILENT" }
                }
            },
            totalRuns = 100,
            cards = new Dictionary<string, object>
            {
                ["CARD.A"] = RegentCard(),
                ["CARD.B"] = RegentCard(),
                ["CARD.SHOP"] = RegentCard(),
                ["CARD.COLORLESS"] = new
                {
                    pools = new[] { "Colorless" },
                    variants = new Dictionary<string, object>
                    {
                        ["CHARACTER.REGENT"] = Variant("Regent:Colorless"),
                        ["CHARACTER.SILENT"] = Variant("Silent:Colorless")
                    }
                }
            }
        });
        return CardAdoptionCatalog.LoadFromJson(json);
    }

    private const string RegentRunOne = """
    {
      "ascension": 10,
      "build_id": "v0.107.1",
      "game_mode": "standard",
      "win": true,
      "players": [{
        "id": 7,
        "character": "CHARACTER.REGENT",
        "deck": [
          {"id":"CARD.A","current_upgrade_level":0},
          {"id":"CARD.A","current_upgrade_level":0},
          {"id":"CARD.B","current_upgrade_level":1}
        ]
      }],
      "map_point_history": [[
        {
          "rooms": [{"room_type":"monster","model_id":"ENCOUNTER.TEST"}],
          "player_stats": [{
            "player_id": 7,
            "card_choices": [
              {"card":{"id":"CARD.A"},"was_picked":true},
              {"card":{"id":"CARD.B","current_upgrade_level":1},"was_picked":false},
              {"card":{"id":"CARD.COLORLESS"},"was_picked":false}
            ],
            "ancient_choice": [
              {"TextKey":"ANCIENT.TEST.pages.INITIAL.options.OPTION_A","was_chosen":true},
              {"title":{"key":"ANCIENT.TEST.pages.INITIAL.options.OPTION_B.title"},"was_chosen":false}
            ]
          }]
        },
        {
          "rooms": [{"room_type":"shop","model_id":"ROOM.MERCHANT"}],
          "player_stats": [{
            "player_id": 7,
            "card_choices": [
              {"card":{"id":"CARD.SHOP"},"was_picked":false},
              {"card":{"id":"CARD.B"},"was_picked":false}
            ],
            "cards_gained": [{"id":"CARD.SHOP"}]
          }]
        }
      ]]
    }
    """;

    private const string RegentRunTwo = """
    {
      "ascension": 10,
      "build_id": "v0.108.0",
      "game_mode": "standard",
      "win": true,
      "players": [{
        "id": 9,
        "character": "CHARACTER.REGENT",
        "deck": [{"id":"CARD.A"}]
      }],
      "map_point_history": [[{
        "rooms": [{"room_type":"elite"}],
        "player_stats": [{
          "player_id": 9,
          "card_choices": [
            {"card":{"id":"CARD.A"},"was_picked":false},
            {"card":{"id":"CARD.B","current_upgrade_level":1},"was_picked":true},
            {"card":{"id":"CARD.COLORLESS"},"was_picked":false}
          ],
          "ancient_choice": [
            {"text_key":"ANCIENT.TEST.pages.INITIAL.options.OPTION_A","was_chosen":false},
            {"textKey":"OPTION_B","was_chosen":true}
          ]
        }]
      }]]
    }
    """;

    private const string SilentRun = """
    {
      "ascension": 10,
      "build_id": "v0.109.0",
      "game_mode": "standard",
      "win": true,
      "players": [{
        "id": 11,
        "character": "CHARACTER.SILENT",
        "deck": [{"id":"CARD.COLORLESS"}]
      }],
      "map_point_history": [[{
        "rooms": [{"room_type":"boss"}],
        "player_stats": [{
          "player_id": 11,
          "card_choices": [
            {"card":{"id":"CARD.COLORLESS"},"was_picked":true},
            {"card":{"id":"CARD.X"},"was_picked":false},
            {"card":{"id":"CARD.Y"},"was_picked":false}
          ],
          "ancient_choice": [
            {"TextKey":"ANCIENT.TEST.pages.INITIAL.options.OPTION_A","was_chosen":true},
            {"TextKey":"ANCIENT.TEST.pages.INITIAL.options.OPTION_C","was_chosen":false}
          ]
        }]
      }]]
    }
    """;

    private static void AssertEqual<T>(T expected, T actual, string testName)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{testName} failed. Expected {expected}, got {actual}.");
        }
    }
}
