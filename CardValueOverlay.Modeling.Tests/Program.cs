using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Estimation;
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
            CardEffectParserParsesDrawEnergyAndKeyword();
            CardEffectParserParsesDebuffPowers();
            CardValueEstimatorUsesCalibration();
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
                _ = c.Tags.Contains(CardTag.Strike);
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

    private static void CardEffectParserParsesDrawEnergyAndKeyword()
    {
        const string source = """
        public sealed class Adrenaline : Card
        {
            protected override IEnumerable<CardKeyword> CanonicalKeywords => new Single(CardKeyword.Exhaust);

            public Adrenaline() : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
            {
                DynamicVars.Energy.UpgradeValueBy(1m);
                _ = new EnergyVar(1);
                _ = new CardsVar(2);
                await PlayerCmd.GainEnergy(base.DynamicVars.Energy.IntValue, base.Owner);
                await CardPileCmd.Draw(choiceContext, base.DynamicVars.Cards.BaseValue, base.Owner);
            }
        }
        """;

        CardEffectTermCatalogEntry parsed = new CardEffectParser().Parse(MakeCard("Adrenaline"), source);
        CardEffectTerm draw = parsed.Terms.Single(item => item.Kind == "draw");
        CardEffectTerm energy = parsed.Terms.Single(item => item.Kind == "energyGain");
        CardEffectTerm keyword = parsed.Terms.Single(item => item.Kind == "keyword");

        AssertEqual((decimal?)2m, draw.Amount, nameof(CardEffectParserParsesDrawEnergyAndKeyword));
        AssertEqual((decimal?)1m, energy.Amount, nameof(CardEffectParserParsesDrawEnergyAndKeyword));
        AssertEqual((decimal?)1m, energy.UpgradeDelta, nameof(CardEffectParserParsesDrawEnergyAndKeyword));
        AssertEqual("Exhaust", keyword.Parameter, nameof(CardEffectParserParsesDrawEnergyAndKeyword));
    }

    private static void CardEffectParserParsesDebuffPowers()
    {
        const string source = """
        public sealed class Bash : Card
        {
            public Bash() : base(2, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
            {
                DynamicVars.Vulnerable.UpgradeValueBy(1m);
                _ = new DamageVar(8m);
                _ = new PowerVar<VulnerablePower>(2m);
                await PowerCmd.Apply<VulnerablePower>(choiceContext, cardPlay.Target, base.DynamicVars.Vulnerable.BaseValue, base.Owner.Creature, this);
            }
        }
        """;

        CardEffectTermCatalogEntry parsed = new CardEffectParser().Parse(MakeCard("Bash"), source);
        CardEffectTerm vulnerable = parsed.Terms.Single(item => item.Kind == "debuffVulnerable");

        AssertEqual((decimal?)2m, vulnerable.Amount, nameof(CardEffectParserParsesDebuffPowers));
        AssertEqual((decimal?)1m, vulnerable.UpgradeDelta, nameof(CardEffectParserParsesDebuffPowers));
        AssertEqual("power:Vulnerable", vulnerable.Parameter, nameof(CardEffectParserParsesDebuffPowers));

        const string weakSource = """
        public sealed class Neutralize : Card
        {
            public Neutralize() : base(0, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
            {
                DynamicVars.Weak.UpgradeValueBy(1m);
                _ = new DamageVar(3m);
                _ = new PowerVar<WeakPower>(1m);
                await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
            }
        }
        """;

        CardEffectTermCatalogEntry weakParsed = new CardEffectParser().Parse(MakeCard("Neutralize"), weakSource);
        CardEffectTerm weak = weakParsed.Terms.Single(item => item.Kind == "debuffWeak");

        AssertEqual((decimal?)1m, weak.Amount, nameof(CardEffectParserParsesDebuffPowers));
        AssertEqual((decimal?)1m, weak.UpgradeDelta, nameof(CardEffectParserParsesDebuffPowers));
        AssertEqual("power:Weak", weak.Parameter, nameof(CardEffectParserParsesDebuffPowers));
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

    private static void CardValueEstimatorUsesCalibration()
    {
        ValueCalibration calibration = MakeCalibration();
        CardValueEstimator estimator = new();

        CardValueEstimate strike = estimator.Estimate(
            MakeEffectEntry(
                "StrikeIronclad",
                1,
                "Attack",
                "AnyEnemy",
                [new CardEffectTerm("damage", 6m, 3m, null, "AnyEnemy", null, "test", 0.9)]),
            calibration,
            layer: 1);
        AssertEqual(15m, strike.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(22.5m, strike.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(7.5m, strike.SmithValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate defend = estimator.Estimate(
            MakeEffectEntry(
                "DefendIronclad",
                1,
                "Skill",
                "Self",
                [new CardEffectTerm("block", 5m, 3m, null, "Self", null, "test", 0.9)]),
            calibration,
            layer: 1);
        AssertEqual(15m, defend.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(24m, defend.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate adrenaline = estimator.Estimate(
            MakeEffectEntry(
                "Adrenaline",
                0,
                "Skill",
                "Self",
                [
                    new CardEffectTerm("draw", 2m, null, null, "Self", null, "test", 0.9),
                    new CardEffectTerm("energyGain", 1m, 1m, null, "Self", null, "test", 0.9),
                    new CardEffectTerm("keyword", null, null, null, "Self", "Exhaust", "test", 0.7)
                ]),
            calibration,
            layer: 1);
        AssertEqual(26.666m, adrenaline.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(36.666m, adrenaline.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
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

    private static CardEffectTermCatalogEntry MakeEffectEntry(
        string typeName,
        int cost,
        string cardType,
        string targetType,
        IReadOnlyList<CardEffectTerm> terms)
    {
        return new CardEffectTermCatalogEntry(
            $"CARD.{typeName.ToUpperInvariant()}",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Cards.{typeName}",
            cost,
            cardType,
            "Common",
            targetType,
            terms,
            [],
            "test",
            0.9);
    }

    private static ValueCalibration MakeCalibration()
    {
        return new ValueCalibration
        {
            BaselineCardValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["cost0"] = 7m,
                ["cost1"] = 15m,
                ["cost2"] = 23m
            },
            DamageUnitValue = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = 2.5m
            },
            BlockToDamage = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = 1.2m
            },
            ResourceValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["draw"] = 8.333m,
                ["energy"] = 10m,
                ["nextTurnEnergyMultiplier"] = 0.75m,
                ["selfHpLossPenalty"] = 2.5m
            },
            PowerValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Vulnerable"] = 4m,
                ["Weak"] = 3.5m,
                ["generic"] = 4m
            },
            KeywordValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Exhaust"] = 0m,
                ["generic"] = 0m
            },
            ScalingAssumptions = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["generic"] = 1m
            },
            TargetingPenalties = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["randomTargetMultiplier"] = 0.85m,
                ["enemyCountAssumption"] = 2.25m
            }
        };
    }
}
