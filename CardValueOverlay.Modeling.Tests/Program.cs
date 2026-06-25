using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Validation;

namespace CardValueOverlay.Modeling.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            SlugModelIdsAreStable();
            ExtractionValidationReportsMissingFiles();
            CardEffectParserParsesStrike();
            CardEffectParserParsesDefend();
            CardEffectParserParsesPerfectedStrikeScaling();
            await RealExtractionFindsKnownModels();
            Console.WriteLine("All modeling tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void SlugModelIdsAreStable()
    {
        ModelCatalogEntry entry = new(
            "card",
            "PerfectedStrike",
            "MegaCrit.Sts2.Core.Models.Cards.PerfectedStrike",
            "CARD.PERFECTED_STRIKE",
            "sts2.dll",
            "test",
            1.0);

        AssertEqual("CARD.PERFECTED_STRIKE", entry.ModelId, nameof(SlugModelIdsAreStable));
    }

    private static void ExtractionValidationReportsMissingFiles()
    {
        ModelingExtractionOptions options = new()
        {
            GameRoot = "Z:/missing-game",
            Sts2DataDir = "Z:/missing-game/data",
            IlSpyPath = "Z:/missing/ilspycmd.exe"
        };
        ExtractionValidationResult result = ExtractionValidationResult.Validate(ExtractionPaths.FromOptions(options));
        AssertTrue(!result.IsValid, nameof(ExtractionValidationReportsMissingFiles));
        AssertTrue(result.Errors.Count >= 4, nameof(ExtractionValidationReportsMissingFiles));
    }

    private static void CardEffectParserParsesStrike()
    {
        const string source = """
        public sealed class StrikeIronclad : Card
        {
            public StrikeIronclad() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
            {
                DynamicVars.Damage.UpgradeValueBy(3m);
                _ = new DamageVar(6m);
            }
        }
        """;

        CardEffectTermCatalogEntry parsed = new CardEffectParser().Parse(MakeCard("StrikeIronclad"), source);
        CardEffectTerm term = parsed.Terms.Single(item => item.Kind == "damage");

        AssertEqual((int?)1, parsed.Cost, nameof(CardEffectParserParsesStrike));
        AssertEqual("Attack", parsed.CardType, nameof(CardEffectParserParsesStrike));
        AssertEqual("Basic", parsed.Rarity, nameof(CardEffectParserParsesStrike));
        AssertEqual("AnyEnemy", parsed.TargetType, nameof(CardEffectParserParsesStrike));
        AssertEqual((decimal?)6m, term.Amount, nameof(CardEffectParserParsesStrike));
        AssertEqual((decimal?)3m, term.UpgradeDelta, nameof(CardEffectParserParsesStrike));
    }

    private static void CardEffectParserParsesDefend()
    {
        const string source = """
        public sealed class DefendIronclad : Card
        {
            public DefendIronclad() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
            {
                DynamicVars.Block.UpgradeValueBy(3m);
                _ = new BlockVar(5m);
            }
        }
        """;

        CardEffectTermCatalogEntry parsed = new CardEffectParser().Parse(MakeCard("DefendIronclad"), source);
        CardEffectTerm term = parsed.Terms.Single(item => item.Kind == "block");

        AssertEqual((int?)1, parsed.Cost, nameof(CardEffectParserParsesDefend));
        AssertEqual("Skill", parsed.CardType, nameof(CardEffectParserParsesDefend));
        AssertEqual("Self", parsed.TargetType, nameof(CardEffectParserParsesDefend));
        AssertEqual((decimal?)5m, term.Amount, nameof(CardEffectParserParsesDefend));
        AssertEqual((decimal?)3m, term.UpgradeDelta, nameof(CardEffectParserParsesDefend));
    }

    private static void CardEffectParserParsesPerfectedStrikeScaling()
    {
        const string source = """
        public sealed class PerfectedStrike : Card
        {
            public PerfectedStrike() : base(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
            {
                DynamicVars.ExtraDamage.UpgradeValueBy(1m);
                _ = new CalculationBaseVar(6m);
                _ = new ExtraDamageVar(2m);
                _ = CardTag.Strike;
            }
        }
        """;

        CardEffectTermCatalogEntry parsed = new CardEffectParser().Parse(MakeCard("PerfectedStrike"), source);
        CardEffectTerm damage = parsed.Terms.Single(item => item.Kind == "damage");
        CardEffectTerm scaling = parsed.Terms.Single(item => item.Kind == "scalingDamagePerCardTag");

        AssertEqual((int?)2, parsed.Cost, nameof(CardEffectParserParsesPerfectedStrikeScaling));
        AssertEqual("Attack", parsed.CardType, nameof(CardEffectParserParsesPerfectedStrikeScaling));
        AssertEqual("Common", parsed.Rarity, nameof(CardEffectParserParsesPerfectedStrikeScaling));
        AssertEqual((decimal?)6m, damage.Amount, nameof(CardEffectParserParsesPerfectedStrikeScaling));
        AssertEqual((decimal?)2m, scaling.Amount, nameof(CardEffectParserParsesPerfectedStrikeScaling));
        AssertEqual((decimal?)1m, scaling.UpgradeDelta, nameof(CardEffectParserParsesPerfectedStrikeScaling));
        AssertEqual("cardTag:Strike", scaling.Parameter, nameof(CardEffectParserParsesPerfectedStrikeScaling));
    }

    private static async Task RealExtractionFindsKnownModels()
    {
        ModelingExtractionOptions options = new()
        {
            OutputRoot = Path.Combine(Path.GetTempPath(), "CardValueOverlay.Modeling.Tests")
        };

        GameDataExtractor extractor = new();
        ExtractionRunResult result = await extractor.ExtractAsync(options);
        IReadOnlyList<string> errors = new GeneratedDataValidator().Validate(result);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        AssertTrue(result.Cards.Count > 100, nameof(RealExtractionFindsKnownModels));
        AssertTrue(result.Enemies.Count > 20, nameof(RealExtractionFindsKnownModels));
        AssertTrue(result.Encounters.Count > 20, nameof(RealExtractionFindsKnownModels));
        AssertTrue(result.Intents.Count > 5, nameof(RealExtractionFindsKnownModels));
        ModelCatalogEntry iAmInvincible = result.Cards.Single(entry => entry.TypeName == "IAmInvincible");
        AssertEqual("CARD.I_AM_INVINCIBLE", iAmInvincible.ModelId, nameof(RealExtractionFindsKnownModels));
    }

    private static void AssertTrue(bool condition, string testName)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{testName} failed.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string testName)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{testName} failed. Expected {expected}, got {actual}.");
        }
    }

    private static ModelCatalogEntry MakeCard(string typeName)
    {
        return new ModelCatalogEntry(
            "card",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Cards.{typeName}",
            $"CARD.{typeName.ToUpperInvariant()}",
            "sts2.dll",
            "test",
            1.0);
    }
}
