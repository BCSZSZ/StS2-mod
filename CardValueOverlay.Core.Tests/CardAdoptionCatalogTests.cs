using System.Text.Json;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.Core.Tests;

internal static class CardAdoptionCatalogTests
{
    public static void RunAll()
    {
        UsesCharacterSampleDenominatorAndDisplayedPickForm();
        BandsUseEmpiricalQuartiles();
        BandsStayWithinDistributionGroup();
        CopyBandsUseGeneratedEligibility();
        ColorlessStatsUseCurrentCharacterVariant();
        RejectsSupersededSchema();
    }

    private static void UsesCharacterSampleDenominatorAndDisplayedPickForm()
    {
        CardAdoptionCatalog catalog = LoadCatalog(500, new()
        {
            ["CARD.TEST"] = Card(
                ("CHARACTER.REGENT", Variant(
                    sampleRuns: 100,
                    totalRunsWith: 40,
                    avgCopies: 1.5,
                    distributionGroup: "Regent",
                    plus0PickRate: 0.25,
                    plus1PickRate: 0.75,
                    plus0ShopBuyRate: 0.4,
                    plus1ShopBuyRate: 0.2)))
        });

        CardAdoptionDisplayStats? plus0 = catalog.Resolve(
            "test",
            CardUpgradeState.Unupgraded,
            "CHARACTER.REGENT");
        CardAdoptionDisplayStats? plus1 = catalog.Resolve(
            "CARD.TEST",
            CardUpgradeState.Upgraded,
            "REGENT");

        AssertEqual(0.4, plus0?.AppearanceProbability, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
        AssertEqual(0.25, plus0?.PickRate, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
        AssertEqual(0.4, plus0?.ShopBuyRate, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
        AssertEqual(0.4, plus1?.AppearanceProbability, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
        AssertEqual(0.75, plus1?.PickRate, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
        AssertEqual(0.2, plus1?.ShopBuyRate, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
        AssertEqual(1.5, plus0?.AvgCopiesWhenPresent, nameof(UsesCharacterSampleDenominatorAndDisplayedPickForm));
    }

    private static void BandsUseEmpiricalQuartiles()
    {
        CardAdoptionCatalog catalog = LoadCatalog(100, new()
        {
            ["CARD.ZERO"] = Card(("CHARACTER.REGENT", Variant(100, 0, 0, "Regent", plus0PickRate: null))),
            ["CARD.LOW"] = Card(("CHARACTER.REGENT", Variant(100, 30, 1.0, "Regent", plus0PickRate: 0.1))),
            ["CARD.LOWER_MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 40, 1.1, "Regent", plus0PickRate: 0.2))),
            ["CARD.MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 50, 1.2, "Regent", plus0PickRate: 0.3))),
            ["CARD.UPPER_MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 60, 1.3, "Regent", plus0PickRate: 0.4))),
            ["CARD.HIGH"] = Card(("CHARACTER.REGENT", Variant(100, 70, 1.4, "Regent", plus0PickRate: 0.5)))
        });

        CardAdoptionDisplayStats? zero = Resolve(catalog, "CARD.ZERO", "REGENT");
        CardAdoptionDisplayStats? low = Resolve(catalog, "CARD.LOW", "REGENT");
        CardAdoptionDisplayStats? middle = Resolve(catalog, "CARD.MIDDLE", "REGENT");
        CardAdoptionDisplayStats? high = Resolve(catalog, "CARD.HIGH", "REGENT");

        AssertEqual(CardAdoptionStatBand.Unknown, zero?.AppearanceBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Low, low?.AppearanceBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Middle, middle?.AppearanceBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.High, high?.AppearanceBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Low, low?.PickRateBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Middle, middle?.PickRateBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.High, high?.PickRateBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Low, low?.AvgCopiesWhenPresentBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Middle, middle?.AvgCopiesWhenPresentBand, nameof(BandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.High, high?.AvgCopiesWhenPresentBand, nameof(BandsUseEmpiricalQuartiles));
    }

    private static void BandsStayWithinDistributionGroup()
    {
        CardAdoptionCatalog catalog = LoadCatalog(100, new()
        {
            ["CARD.LOW"] = Card(("CHARACTER.REGENT", Variant(100, 30, 1.0, "Regent", plus0PickRate: 0.1))),
            ["CARD.MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 40, 1.1, "Regent", plus0PickRate: 0.2))),
            ["CARD.HIGH"] = Card(("CHARACTER.REGENT", Variant(100, 50, 1.2, "Regent", plus0PickRate: 0.3))),
            ["CARD.OFF_POOL"] = Card(("CHARACTER.SILENT", Variant(100, 100, 3.0, "Silent", plus0PickRate: 1.0)))
        });

        CardAdoptionDisplayStats? low = Resolve(catalog, "CARD.LOW", "REGENT");
        CardAdoptionDisplayStats? high = Resolve(catalog, "CARD.HIGH", "REGENT");
        CardAdoptionDisplayStats? offPool = Resolve(catalog, "CARD.OFF_POOL", "SILENT");

        AssertEqual(CardAdoptionStatBand.Low, low?.AppearanceBand, nameof(BandsStayWithinDistributionGroup));
        AssertEqual(CardAdoptionStatBand.High, high?.AppearanceBand, nameof(BandsStayWithinDistributionGroup));
        AssertEqual(CardAdoptionStatBand.Unknown, offPool?.AppearanceBand, nameof(BandsStayWithinDistributionGroup));
    }

    private static void CopyBandsUseGeneratedEligibility()
    {
        CardAdoptionCatalog catalog = LoadCatalog(100, new()
        {
            ["CARD.STARTER"] = Card(("CHARACTER.REGENT", Variant(100, 80, 5.0, "Regent", copyEligible: false))),
            ["CARD.LOW_SAMPLE"] = Card(("CHARACTER.REGENT", Variant(100, 5, 10.0, "Regent"))),
            ["CARD.COPY_LOW"] = Card(("CHARACTER.REGENT", Variant(100, 30, 1.0, "Regent"))),
            ["CARD.COPY_LOWER_MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 40, 1.1, "Regent"))),
            ["CARD.COPY_MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 50, 1.2, "Regent"))),
            ["CARD.COPY_UPPER_MIDDLE"] = Card(("CHARACTER.REGENT", Variant(100, 60, 1.3, "Regent"))),
            ["CARD.COPY_HIGH"] = Card(("CHARACTER.REGENT", Variant(100, 70, 1.4, "Regent")))
        });

        AssertEqual(
            CardAdoptionStatBand.Unknown,
            Resolve(catalog, "CARD.STARTER", "REGENT")?.AvgCopiesWhenPresentBand,
            nameof(CopyBandsUseGeneratedEligibility));
        AssertEqual(
            CardAdoptionStatBand.Unknown,
            Resolve(catalog, "CARD.LOW_SAMPLE", "REGENT")?.AvgCopiesWhenPresentBand,
            nameof(CopyBandsUseGeneratedEligibility));
        AssertEqual(
            CardAdoptionStatBand.Low,
            Resolve(catalog, "CARD.COPY_LOW", "REGENT")?.AvgCopiesWhenPresentBand,
            nameof(CopyBandsUseGeneratedEligibility));
        AssertEqual(
            CardAdoptionStatBand.Middle,
            Resolve(catalog, "CARD.COPY_MIDDLE", "REGENT")?.AvgCopiesWhenPresentBand,
            nameof(CopyBandsUseGeneratedEligibility));
        AssertEqual(
            CardAdoptionStatBand.High,
            Resolve(catalog, "CARD.COPY_HIGH", "REGENT")?.AvgCopiesWhenPresentBand,
            nameof(CopyBandsUseGeneratedEligibility));
    }

    private static void ColorlessStatsUseCurrentCharacterVariant()
    {
        CardAdoptionCatalog catalog = LoadCatalog(200, new()
        {
            ["CARD.COLOR_LOW"] = ColorlessCard(regentRunsWith: 10, silentRunsWith: 90),
            ["CARD.COLOR_MIDDLE"] = ColorlessCard(regentRunsWith: 20, silentRunsWith: 80),
            ["CARD.COLOR_HIGH"] = ColorlessCard(regentRunsWith: 30, silentRunsWith: 70)
        });

        CardAdoptionDisplayStats? regent = Resolve(catalog, "CARD.COLOR_LOW", "REGENT");
        CardAdoptionDisplayStats? silent = Resolve(catalog, "CARD.COLOR_LOW", "SILENT");
        CardAdoptionDisplayStats? noCharacter = catalog.Resolve(
            "CARD.COLOR_LOW",
            CardUpgradeState.Unupgraded,
            null);

        AssertEqual(0.1, regent?.AppearanceProbability, nameof(ColorlessStatsUseCurrentCharacterVariant));
        AssertEqual(0.9, silent?.AppearanceProbability, nameof(ColorlessStatsUseCurrentCharacterVariant));
        AssertEqual(CardAdoptionStatBand.Low, regent?.AppearanceBand, nameof(ColorlessStatsUseCurrentCharacterVariant));
        AssertEqual(CardAdoptionStatBand.High, silent?.AppearanceBand, nameof(ColorlessStatsUseCurrentCharacterVariant));
        AssertEqual(null, noCharacter, nameof(ColorlessStatsUseCurrentCharacterVariant));
    }

    private static void RejectsSupersededSchema()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "totalRuns": 1,
          "cards": {}
        }
        """;

        AssertThrows<InvalidDataException>(
            () => CardAdoptionCatalog.LoadFromJson(json),
            nameof(RejectsSupersededSchema));
    }

    private static CardAdoptionEntry ColorlessCard(int regentRunsWith, int silentRunsWith)
    {
        return Card(
            ("CHARACTER.REGENT", Variant(100, regentRunsWith, 1.0, "Regent:Colorless", plus0PickRate: regentRunsWith / 100d)),
            ("CHARACTER.SILENT", Variant(100, silentRunsWith, 1.0, "Silent:Colorless", plus0PickRate: silentRunsWith / 100d)));
    }

    private static CardAdoptionCatalog LoadCatalog(
        int totalRuns,
        Dictionary<string, CardAdoptionEntry> cards)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            totalRuns,
            cards
        });
        return CardAdoptionCatalog.LoadFromJson(json);
    }

    private static CardAdoptionEntry Card(
        params (string Key, CardAdoptionVariant Variant)[] variants)
    {
        return new CardAdoptionEntry
        {
            Pools = [],
            Variants = variants.ToDictionary(item => item.Key, item => item.Variant)
        };
    }

    private static CardAdoptionVariant Variant(
        int sampleRuns,
        int totalRunsWith,
        double avgCopies,
        string distributionGroup,
        bool copyEligible = true,
        double? plus0PickRate = 0.2,
        double? plus1PickRate = null,
        double? plus0ShopBuyRate = 0.2,
        double? plus1ShopBuyRate = null)
    {
        return new CardAdoptionVariant
        {
            SampleRuns = sampleRuns,
            DistributionGroup = distributionGroup,
            CopyDistributionEligible = copyEligible,
            TotalRunsWith = totalRunsWith,
            TotalCopies = (int)Math.Round(totalRunsWith * avgCopies),
            AvgCopiesWhenPresent = avgCopies,
            Plus0 = Form(plus0PickRate, plus0ShopBuyRate),
            Plus1 = Form(plus1PickRate, plus1ShopBuyRate)
        };
    }

    private static CardAdoptionFormStats Form(double? pickRate, double? shopBuyRate)
    {
        return new CardAdoptionFormStats
        {
            PickRate = pickRate,
            ShopBuyRate = shopBuyRate
        };
    }

    private static CardAdoptionDisplayStats? Resolve(
        CardAdoptionCatalog catalog,
        string cardKey,
        string characterKey)
    {
        return catalog.Resolve(cardKey, CardUpgradeState.Unupgraded, characterKey);
    }

    private static void AssertEqual<T>(T expected, T actual, string testName)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{testName} failed. Expected {expected}, got {actual}.");
        }
    }

    private static void AssertThrows<TException>(Action action, string testName)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{testName} failed. Expected {typeof(TException).Name}.");
    }
}
