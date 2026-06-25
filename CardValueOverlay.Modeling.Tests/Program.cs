using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Simulation;
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
            CardEffectParserParsesStarsNextTurnResourcesAndForge();
            CardEffectParserParsesDebuffPowers();
            CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints();
            EncounterPatternParserParsesActAndMonsterSlots();
            CardValueEstimatorUsesCalibration();
            SimulationCardLibraryBuilderUsesParsedResources();
            DeckMonteCarloSimulatorUsesStarsAndForge();
            SimulationScenarioRunnerBuildsDiyCardsAndVariants();
            MonsterMoveParserParsesAttackBlockCycle();
            MonsterMoveParserParsesMultiHitAndDebuffs();
            EnemyExpectationEstimatorAveragesMonsterMoves();
            EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands();
            DefenseCalibrationEstimatorSummarizesEnemyPressure();
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

    private static void CardEffectParserParsesStarsNextTurnResourcesAndForge()
    {
        const string source = """
        public sealed class TestResourceCard : CardModel
        {
            public override int CanonicalStarCost => 2;

            protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
            {
                new DamageVar(6m),
                new CardsVar(1),
                new EnergyVar(2),
                new StarsVar(1),
                new PowerVar<StarNextTurnPower>(3m),
                new ForgeVar(5)
            };

            public TestResourceCard() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
            {
            }

            protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
            {
                await CardPileCmd.Draw(choiceContext, base.DynamicVars.Cards.BaseValue, base.Owner);
                await PowerCmd.Apply<DrawCardsNextTurnPower>(choiceContext, base.Owner.Creature, base.DynamicVars.Cards.BaseValue, base.Owner.Creature, this);
                await PlayerCmd.GainEnergy(base.DynamicVars.Energy.IntValue, base.Owner);
                await PowerCmd.Apply<EnergyNextTurnPower>(choiceContext, base.Owner.Creature, base.DynamicVars.Energy.BaseValue, base.Owner.Creature, this);
                await PlayerCmd.GainStars(base.DynamicVars.Stars.BaseValue, base.Owner);
                await PowerCmd.Apply<StarNextTurnPower>(choiceContext, base.Owner.Creature, base.DynamicVars["StarNextTurnPower"].BaseValue, base.Owner.Creature, this);
                await ForgeCmd.Forge(base.DynamicVars.Forge.IntValue, base.Owner, this);
            }

            protected override void OnUpgrade()
            {
                base.DynamicVars.Cards.UpgradeValueBy(1m);
                base.DynamicVars.Energy.UpgradeValueBy(1m);
                base.DynamicVars.Stars.UpgradeValueBy(1m);
                base.DynamicVars["StarNextTurnPower"].UpgradeValueBy(1m);
                base.DynamicVars.Forge.UpgradeValueBy(2m);
            }
        }
        """;

        CardEffectTermCatalogEntry parsed = new CardEffectParser().Parse(MakeCard("TestResourceCard"), source);

        AssertEqual((decimal?)2m, parsed.Terms.Single(term => term.Kind == "starCost").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)1m, parsed.Terms.Single(term => term.Kind == "draw").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)1m, parsed.Terms.Single(term => term.Kind == "drawNextTurn").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)2m, parsed.Terms.Single(term => term.Kind == "energyGain").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)2m, parsed.Terms.Single(term => term.Kind == "energyNextTurn").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)1m, parsed.Terms.Single(term => term.Kind == "starGain").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)3m, parsed.Terms.Single(term => term.Kind == "starNextTurn").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)5m, parsed.Terms.Single(term => term.Kind == "forge").Amount, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)2m, parsed.Terms.Single(term => term.Kind == "forge").UpgradeDelta, nameof(CardEffectParserParsesStarsNextTurnResourcesAndForge));
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

    private static void CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints()
    {
        const string poolSource = """
        public sealed class IroncladCardPool : CardPoolModel
        {
            public override string Title => "ironclad";

            protected override CardModel[] GenerateAllCards()
            {
                return new CardModel[2]
                {
                    ModelDb.Card<Aggression>(),
                    ModelDb.Card<DemonicShield>()
                };
            }
        }
        """;
        const string multiplayerSource = """
        public sealed class DemonicShield : CardModel
        {
            public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
        }
        """;
        const string singleplayerSource = """
        public sealed class WellLaidPlans : CardModel
        {
            public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.SingleplayerOnly;
        }
        """;

        CardPoolMembershipParser parser = new();
        CardPoolSourceEntry pool = parser.ParsePoolSource("IroncladCardPool", poolSource);

        AssertEqual("Ironclad", pool.PoolName, nameof(CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints));
        AssertTrue(pool.CardTypeNames.Contains("Aggression"), nameof(CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints));
        AssertTrue(pool.CardTypeNames.Contains("DemonicShield"), nameof(CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints));
        AssertEqual("MultiplayerOnly", parser.ParseMultiplayerConstraint(multiplayerSource), nameof(CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints));
        AssertEqual("SingleplayerOnly", parser.ParseMultiplayerConstraint(singleplayerSource), nameof(CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints));
        AssertEqual("None", parser.ParseMultiplayerConstraint("public sealed class StrikeIronclad : CardModel {}"), nameof(CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints));
    }

    private static void EncounterPatternParserParsesActAndMonsterSlots()
    {
        const string actSource = """
        public sealed class Overgrowth : ActModel
        {
            protected override int NumberOfWeakEncounters => 3;
            protected override int BaseNumberOfRooms => 15;
            public override int Index => 0;
            public override bool IsDefault => true;

            public override IEnumerable<EncounterModel> GenerateAllEncounters()
            {
                return new EncounterModel[2]
                {
                    ModelDb.Encounter<ChompersNormal>(),
                    ModelDb.Encounter<BowlbugsWeak>()
                };
            }
        }
        """;
        const string fixedEncounterSource = """
        public sealed class ChompersNormal : EncounterModel
        {
            public override RoomType RoomType => RoomType.Monster;
            public override IEnumerable<MonsterModel> AllPossibleMonsters => new Single(ModelDb.Monster<Chomper>());

            protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
            {
                Chomper chomper = (Chomper)ModelDb.Monster<Chomper>().ToMutable();
                return new (MonsterModel, string)[2]
                {
                    (ModelDb.Monster<Chomper>().ToMutable(), "front"),
                    (chomper, "back")
                };
            }
        }
        """;
        const string randomEncounterSource = """
        public sealed class BowlbugsWeak : EncounterModel
        {
            public override RoomType RoomType => RoomType.Monster;
            public override bool IsWeak => true;
            private static MonsterModel[] Bugs => new MonsterModel[2]
            {
                ModelDb.Monster<BowlbugEgg>(),
                ModelDb.Monster<BowlbugNectar>()
            };
            public override IEnumerable<MonsterModel> AllPossibleMonsters => Bugs.Concat(new Single(ModelDb.Monster<BowlbugRock>()));

            protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
            {
                return new (MonsterModel, string)[2]
                {
                    (ModelDb.Monster<BowlbugRock>().ToMutable(), "odd"),
                    (base.Rng.NextItem(Bugs).ToMutable(), "even")
                };
            }
        }
        """;

        EncounterPatternParser parser = new();
        EncounterActSourceEntry act = parser.ParseActSource("Overgrowth", actSource);
        AssertEqual("Overgrowth", act.ActTypeName, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertEqual(1, act.ActNumber, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertTrue(act.EncounterTypeNames.Contains("ChompersNormal"), nameof(EncounterPatternParserParsesActAndMonsterSlots));

        EncounterActReference reference = new(
            act.ActTypeName,
            act.ActIndex,
            act.ActNumber,
            act.IsDefault,
            act.NumberOfWeakEncounters,
            act.BaseNumberOfRooms);
        EncounterPatternEntry fixedPattern = parser.ParseEncounterSource(
            MakeEncounter("ChompersNormal"),
            [reference],
            fixedEncounterSource);
        AssertEqual("Normal", fixedPattern.Category, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertEqual(2, fixedPattern.FixedMonsterCount, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertEqual(2, fixedPattern.MonsterSlots.Count(slot => slot.MonsterTypeName == "Chomper"), nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertEqual(1, fixedPattern.Acts.Single().ActNumber, nameof(EncounterPatternParserParsesActAndMonsterSlots));

        EncounterPatternEntry randomPattern = parser.ParseEncounterSource(
            MakeEncounter("BowlbugsWeak"),
            [reference],
            randomEncounterSource);
        AssertEqual("Weak", randomPattern.Category, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertEqual(2, randomPattern.FixedMonsterCount, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertTrue(randomPattern.HasConditionalMonsterSelection, nameof(EncounterPatternParserParsesActAndMonsterSlots));
        EncounterMonsterSlot randomSlot = randomPattern.MonsterSlots.Single(slot => slot.SlotName == "even");
        AssertTrue(randomSlot.PossibleMonsterTypeNames.Contains("BowlbugEgg"), nameof(EncounterPatternParserParsesActAndMonsterSlots));
        AssertTrue(randomSlot.PossibleMonsterTypeNames.Contains("BowlbugNectar"), nameof(EncounterPatternParserParsesActAndMonsterSlots));
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
        AssertEqual(6m, strike.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(9m, strike.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(3m, strike.SmithValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate defend = estimator.Estimate(
            MakeEffectEntry(
                "DefendIronclad",
                1,
                "Skill",
                "Self",
                [new CardEffectTerm("block", 5m, 3m, null, "Self", null, "test", 0.9)]),
            calibration,
            layer: 1);
        AssertEqual(6m, defend.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(9.6m, defend.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));

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
        AssertEqual(22m, adrenaline.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(30m, adrenaline.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate neutralize = estimator.Estimate(
            MakeEffectEntry(
                "Neutralize",
                0,
                "Attack",
                "AnyEnemy",
                [new CardEffectTerm("debuffWeak", 1m, 1m, null, "AnyEnemy", "power:Weak", "test", 0.9)]),
            calibration,
            layer: 1);
        AssertEqual(2.4m, neutralize.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(3.6m, neutralize.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(1.2m, neutralize.SmithValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate bash = estimator.Estimate(
            MakeEffectEntry(
                "Bash",
                2,
                "Attack",
                "AnyEnemy",
                [new CardEffectTerm("debuffVulnerable", 2m, 1m, null, "AnyEnemy", "power:Vulnerable", "test", 0.9)]),
            calibration,
            layer: 40);
        AssertEqual(21m, bash.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(26.6m, bash.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(5.6m, bash.SmithValue, nameof(CardValueEstimatorUsesCalibration));
    }

    private static void SimulationCardLibraryBuilderUsesParsedResources()
    {
        CardEffectTermCatalogEntry entry = MakeEffectEntry(
            "ResourceCard",
            1,
            "Skill",
            "Self",
            [
                new CardEffectTerm("damage", 6m, null, null, "AnyEnemy", null, "test", 0.9),
                new CardEffectTerm("draw", 1m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("drawNextTurn", 1m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("energyGain", 2m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("energyNextTurn", 1m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("starCost", 2m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("starGain", 1m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("starNextTurn", 3m, null, null, "Self", null, "test", 0.9),
                new CardEffectTerm("forge", 5m, null, null, "Self", null, "test", 0.9)
            ]);

        SimulationCard card = new SimulationCardLibraryBuilder()
            .Build([entry], MakeCalibration(), layer: 1)
            .Single();

        AssertEqual(6m, card.IntrinsicValue, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.Draw, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.DrawNextTurn, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(2, card.EnergyGain, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.EnergyNextTurn, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(2, card.StarCost, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.StarGain, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(3, card.StarNextTurn, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(5, card.Forge, nameof(SimulationCardLibraryBuilderUsesParsedResources));
    }

    private static void DeckMonteCarloSimulatorUsesStarsAndForge()
    {
        SimulationCard starGain = MakeSimulationCard("Glow", value: 0m) with
        {
            StarGain = 1
        };
        SimulationCard starSpend = MakeSimulationCard("FallingStar", value: 10m) with
        {
            StarCost = 1
        };
        DeckSimulationReport starReport = new DeckMonteCarloSimulator().Simulate(
            [starGain, starSpend],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 2, BaseEnergy = 3, Seed = 1 });

        AssertEqual(10m, starReport.Turns.Single().ExpectedValue, nameof(DeckMonteCarloSimulatorUsesStarsAndForge));

        SimulationCard forge = MakeSimulationCard("Forge", value: 0m) with
        {
            Forge = 5
        };
        DeckSimulationReport forgeReport = new DeckMonteCarloSimulator().Simulate(
            [forge],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 1, BaseEnergy = 3, Seed = 1 });

        AssertEqual(15m, forgeReport.Turns.Single().ExpectedValue, nameof(DeckMonteCarloSimulatorUsesStarsAndForge));
    }

    private static void SimulationScenarioRunnerBuildsDiyCardsAndVariants()
    {
        SimulationCard reflect = MakeSimulationCard("Reflect", value: 0m) with
        {
            EnergyCost = 1,
            StarCost = 3
        };
        SimulationScenario scenario = new()
        {
            Name = "test_diy_scenario",
            Deck =
            [
                new SimulationDeckCardSpec
                {
                    CloneTypeName = "Reflect",
                    Count = 1,
                    Patch = new SimulationCardPatch
                    {
                        ModelId = "DIY.REFLECT_20_20",
                        TypeName = "DiyReflect20_20",
                        Damage = 20m,
                        Block = 20m
                    }
                }
            ],
            Variants =
            [
                new SimulationScenarioVariant
                {
                    Id = "base",
                    Label = "Base"
                },
                new SimulationScenarioVariant
                {
                    Id = "gain_energy",
                    Label = "Gain Energy",
                    CardPatches =
                    [
                        new SimulationCardPatchRule
                        {
                            MatchTypeName = "DiyReflect20_20",
                            Patch = new SimulationCardPatch
                            {
                                EnergyGain = 1
                            }
                        }
                    ]
                }
            ]
        };

        SimulationScenarioReport report = new SimulationScenarioRunner().Run(
            scenario,
            [reflect],
            MakeCalibration(),
            layer: 1,
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 1, BaseStars = 3, Seed = 1 });

        AssertEqual("DiyReflect20_20", report.Deck.Single().TypeName, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
        AssertEqual(44m, report.Results[0].TotalExpectedValue, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
        AssertEqual(2, report.Results.Count, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
    }

    private static SimulationCard MakeSimulationCard(string name, decimal value)
    {
        return new SimulationCard
        {
            ModelId = $"CARD.{name.ToUpperInvariant()}",
            TypeName = name,
            FullTypeName = $"MegaCrit.Sts2.Core.Models.Cards.{name}",
            Cost = 0,
            CardType = "Skill",
            Rarity = "Common",
            TargetType = "Self",
            Layer = 1,
            StaticEstimatedValue = value,
            IntrinsicValue = value,
            EnergyCost = 0,
            Confidence = 1.0,
            Warnings = []
        };
    }

    private static void MonsterMoveParserParsesAttackBlockCycle()
    {
        const string source = """
        public sealed class AxeRubyRaider : MonsterModel
        {
            private int SwingDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);
            private int SwingBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);
            private int BigSwingDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 13, 12);

            protected override MonsterMoveStateMachine GenerateMoveStateMachine()
            {
                MoveState moveState = new MoveState("SWING_1", SwingMove, new SingleAttackIntent(SwingDamage), new DefendIntent());
                MoveState moveState2 = new MoveState("SWING_2", SwingMove, new SingleAttackIntent(SwingDamage), new DefendIntent());
                MoveState moveState3 = new MoveState("BIG_SWING", BigSwingMove, new SingleAttackIntent(BigSwingDamage));
                moveState.FollowUpState = moveState2;
                moveState2.FollowUpState = moveState3;
                moveState3.FollowUpState = moveState;
                return new MonsterMoveStateMachine(list, moveState);
            }

            private async Task SwingMove(IReadOnlyList<Creature> targets)
            {
                await DamageCmd.Attack(SwingDamage).FromMonster(this).Execute(null);
                await CreatureCmd.GainBlock(base.Creature, SwingBlock, ValueProp.Move, null);
            }

            private async Task BigSwingMove(IReadOnlyList<Creature> targets)
            {
                await DamageCmd.Attack(BigSwingDamage).FromMonster(this).Execute(null);
            }
        }
        """;

        MonsterMoveProfileEntry parsed = new MonsterMoveParser().Parse(MakeMonster("AxeRubyRaider"), source);
        MonsterMoveStateEntry swing = parsed.Moves.Single(move => move.StateId == "SWING_1");
        MonsterMoveStateEntry bigSwing = parsed.Moves.Single(move => move.StateId == "BIG_SWING");

        AssertEqual("SWING_1", parsed.InitialStateId, nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertTrue(swing.Intents.Contains("SingleAttackIntent"), nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertTrue(swing.Intents.Contains("DefendIntent"), nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertEqual((decimal?)5m, swing.Effects.Single(effect => effect.Kind == "attack").Amount?.Value, nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertEqual((decimal?)6m, swing.Effects.Single(effect => effect.Kind == "attack").Amount?.AscensionValue, nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertEqual((decimal?)5m, swing.Effects.Single(effect => effect.Kind == "block").Amount?.Value, nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertEqual((decimal?)12m, bigSwing.Effects.Single(effect => effect.Kind == "attack").Amount?.Value, nameof(MonsterMoveParserParsesAttackBlockCycle));
        AssertTrue(swing.FollowUpStateIds.Contains("SWING_2"), nameof(MonsterMoveParserParsesAttackBlockCycle));
    }

    private static void MonsterMoveParserParsesMultiHitAndDebuffs()
    {
        const string source = """
        public sealed class Axebot : MonsterModel
        {
            private int OneTwoDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 9);
            private int HammerUppercutDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 14, 12);

            protected override MonsterMoveStateMachine GenerateMoveStateMachine()
            {
                MoveState moveState = new MoveState("ONE_TWO_MOVE", OneTwoMove, new MultiAttackIntent(OneTwoDamage, 2));
                MoveState moveState2 = new MoveState("HAMMER_UPPERCUT_MOVE", HammerUppercutMove, new SingleAttackIntent(HammerUppercutDamage), new DebuffIntent());
                return new MonsterMoveStateMachine(list, moveState);
            }

            private async Task OneTwoMove(IReadOnlyList<Creature> targets)
            {
                await DamageCmd.Attack(OneTwoDamage).WithHitCount(2).FromMonster(this).Execute(null);
            }

            private async Task HammerUppercutMove(IReadOnlyList<Creature> targets)
            {
                await DamageCmd.Attack(HammerUppercutDamage).FromMonster(this).Execute(null);
                await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), targets, 2m, base.Creature, null);
                await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), targets, 2m, base.Creature, null);
            }
        }
        """;

        MonsterMoveProfileEntry parsed = new MonsterMoveParser().Parse(MakeMonster("Axebot"), source);
        MonsterMoveStateEntry oneTwo = parsed.Moves.Single(move => move.StateId == "ONE_TWO_MOVE");
        MonsterMoveStateEntry uppercut = parsed.Moves.Single(move => move.StateId == "HAMMER_UPPERCUT_MOVE");

        MonsterMoveEffectTerm attack = oneTwo.Effects.Single(effect => effect.Kind == "attack");
        AssertEqual((decimal?)9m, attack.Amount?.Value, nameof(MonsterMoveParserParsesMultiHitAndDebuffs));
        AssertEqual((decimal?)2m, attack.HitCount?.Value, nameof(MonsterMoveParserParsesMultiHitAndDebuffs));
        AssertEqual((decimal?)2m, uppercut.Effects.Single(effect => effect.Kind == "debuffWeak").Amount?.Value, nameof(MonsterMoveParserParsesMultiHitAndDebuffs));
        AssertEqual((decimal?)2m, uppercut.Effects.Single(effect => effect.Kind == "debuffFrail").Amount?.Value, nameof(MonsterMoveParserParsesMultiHitAndDebuffs));
    }

    private static void EnemyExpectationEstimatorAveragesMonsterMoves()
    {
        MonsterMoveProfileEntry profile = new(
            "MONSTER.TEST",
            "TestMonster",
            "MegaCrit.Sts2.Core.Models.Monsters.TestMonster",
            new MonsterHpRange(Number(20m, 21m), Number(22m, 23m)),
            [
                new MonsterMoveStateEntry(
                    "SWING",
                    "SwingMove",
                    ["SingleAttackIntent", "DefendIntent"],
                    [
                        new MonsterMoveEffectTerm("attack", Number(5m, 6m), Number(1m), "player", null, "test", 0.9),
                        new MonsterMoveEffectTerm("block", Number(5m, 6m), null, "self", null, "test", 0.8)
                    ],
                    ["BIG_SWING"],
                    [],
                    0.8),
                new MonsterMoveStateEntry(
                    "BIG_SWING",
                    "BigSwingMove",
                    ["SingleAttackIntent"],
                    [
                        new MonsterMoveEffectTerm("attack", Number(12m, 13m), Number(1m), "player", null, "test", 0.9),
                        new MonsterMoveEffectTerm("debuffWeak", Number(2m), null, "player", "power:Weak", "test", 0.7),
                        new MonsterMoveEffectTerm("debuffFrail", Number(2m), null, "player", "power:Frail", "test", 0.7)
                    ],
                    ["SWING"],
                    [],
                    0.7)
            ],
            "SWING",
            [],
            "test",
            0.8);

        EnemyExpectationProfile expectation = new EnemyExpectationEstimator().Estimate(profile);

        AssertEqual((decimal?)20m, expectation.MinHp, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual((decimal?)23m, expectation.AscensionMaxHp, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual(8.5m, expectation.AverageDamagePerMove, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual((decimal?)9.5m, expectation.AscensionAverageDamagePerMove, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual(2.5m, expectation.AverageBlockPerMove, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual((decimal?)3m, expectation.AscensionAverageBlockPerMove, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual(1m, expectation.ExpectedWeakPerMove, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual(1m, expectation.ExpectedFrailPerMove, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
        AssertEqual(1m, expectation.AttackMoveRate, nameof(EnemyExpectationEstimatorAveragesMonsterMoves));
    }

    private static void EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands()
    {
        MonsterMoveProfileEntry cycleMonster = new(
            "MONSTER.CYCLE",
            "CycleMonster",
            "MegaCrit.Sts2.Core.Models.Monsters.CycleMonster",
            null,
            [
                new MonsterMoveStateEntry(
                    "SWING",
                    "SwingMove",
                    ["SingleAttackIntent"],
                    [new MonsterMoveEffectTerm("attack", Number(5m, 6m), Number(1m), "player", null, "test", 0.9)],
                    ["BIG"],
                    [],
                    0.9),
                new MonsterMoveStateEntry(
                    "BIG",
                    "BigMove",
                    ["SingleAttackIntent"],
                    [new MonsterMoveEffectTerm("attack", Number(10m, 12m), Number(1m), "player", null, "test", 0.9)],
                    ["SWING"],
                    [],
                    0.9)
            ],
            "SWING",
            [],
            "test",
            0.9);
        MonsterMoveProfileEntry steadyMonster = new(
            "MONSTER.STEADY",
            "SteadyMonster",
            "MegaCrit.Sts2.Core.Models.Monsters.SteadyMonster",
            null,
            [
                new MonsterMoveStateEntry(
                    "HIT",
                    "HitMove",
                    ["SingleAttackIntent"],
                    [new MonsterMoveEffectTerm("attack", Number(2m, 3m), Number(1m), "player", null, "test", 0.9)],
                    ["HIT"],
                    [],
                    0.9)
            ],
            "HIT",
            [],
            "test",
            0.9);

        EncounterActReference act1 = new("Overgrowth", 0, 1, true, 3, 15);
        EncounterActReference alternateAct1 = new("Underdocks", 0, 1, false, 3, 15);
        EncounterActReference act2 = new("Hive", 1, 2, true, 2, 14);
        EncounterActReference act3 = new("Glory", 2, 3, true, 2, 13);
        IReadOnlyList<EncounterPatternEntry> encounters =
        [
            MakeEncounterPattern("Act1Weak", "Weak", [act1], ["CycleMonster"]),
            MakeEncounterPattern("Act1AlternateWeak", "Weak", [alternateAct1], ["SteadyMonster"]),
            MakeEncounterPattern("Act1Normal", "Normal", [act1], ["SteadyMonster"]),
            MakeEncounterPattern("Act1Elite", "Elite", [act1], ["CycleMonster", "SteadyMonster"]),
            MakeEncounterPattern("Act1Boss", "Boss", [act1], ["CycleMonster", "CycleMonster"]),
            MakeEncounterPattern("Act2Weak", "Weak", [act2], ["SteadyMonster"]),
            MakeEncounterPattern("Act2Boss", "Boss", [act2], ["CycleMonster"]),
            MakeEncounterPattern("Act3Weak", "Weak", [act3], ["SteadyMonster"]),
            MakeEncounterPattern("Act3Boss", "Boss", [act3], ["CycleMonster"])
        ];

        EncounterWeightedEnemyPressureReport report = new EncounterWeightedEnemyPressureEstimator()
            .Estimate([cycleMonster, steadyMonster], encounters, turnCount: 8);

        EncounterDamageProfile cycleEncounter = report.Encounters.Single(encounter => encounter.TypeName == "Act1Weak");
        AssertEqual(24m, cycleEncounter.OpeningDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(8m, cycleEncounter.OpeningDamagePerTurn, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(48m, cycleEncounter.SustainDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(9.6m, cycleEncounter.SustainDamagePerTurn, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(12m, cycleEncounter.PeakDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(1.6m, cycleEncounter.ScalingDeltaPerTurn, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(8.4m, cycleEncounter.WeightedPressure, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertTrue(cycleEncounter.TurnDamages.SequenceEqual([6m, 12m, 6m, 12m, 6m, 12m, 6m, 12m]), nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));

        EncounterLayerPressureSegment act1Weak = report.LayerSegments.Single(segment => segment.ActNumber == 1 && segment.SegmentKind == "weak");
        EncounterLayerPressureSegment act1Hard = report.LayerSegments.Single(segment => segment.ActNumber == 1 && segment.SegmentKind == "normal+elite");
        EncounterLayerPressureSegment act1Boss = report.LayerSegments.Single(segment => segment.ActNumber == 1 && segment.SegmentKind == "boss");
        EncounterLayerPressureSegment act1Ancient = report.LayerSegments.Single(segment => segment.ActNumber == 1 && segment.SegmentKind == "ancient/noncombat");
        EncounterLayerPressureSegment act2Weak = report.LayerSegments.Single(segment => segment.ActNumber == 2 && segment.SegmentKind == "weak");
        EncounterLayerPressureSegment act3Weak = report.LayerSegments.Single(segment => segment.ActNumber == 3 && segment.SegmentKind == "weak");
        EncounterLayerPressureSegment act3Boss = report.LayerSegments.Single(segment => segment.ActNumber == 3 && segment.SegmentKind == "boss");

        AssertEqual(1, act1Weak.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(5, act1Weak.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(16.5m, act1Weak.AverageOpeningDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(31.5m, act1Weak.AverageSustainDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(7.5m, act1Weak.AveragePeakDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(5.7m, act1Weak.AverageWeightedPressure, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertTrue(act1Weak.EncounterTypeNames.Contains("Act1AlternateWeak"), nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(6, act1Hard.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(15, act1Hard.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(21m, act1Hard.AverageOpeningDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(16, act1Boss.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(16, act1Boss.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(48m, act1Boss.AverageOpeningDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(18.88m, act1Boss.AverageWeightedPressure, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(17, act1Ancient.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(17, act1Ancient.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(0m, act1Ancient.AverageOpeningDamage, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(18, act2Weak.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(20, act2Weak.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(34, act3Weak.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(36, act3Weak.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(47, act3Boss.StartLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(48, act3Boss.EndLayer, nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
        AssertEqual(0, report.LayerSegments.Count(segment => segment.ActNumber == 3 && segment.SegmentKind == "ancient/noncombat"), nameof(EncounterWeightedEnemyPressureEstimatorUsesFirstThreeTurnsAndLayerBands));
    }

    private static void DefenseCalibrationEstimatorSummarizesEnemyPressure()
    {
        ValueCalibration calibration = MakeCalibration();
        EnemyExpectationProfile first = MakeEnemyExpectation("FirstEnemy", 10m, 12m, 0.5m, 1m, 0.5m, 0.25m, 0m, 0.9, []);
        EnemyExpectationProfile second = MakeEnemyExpectation("SecondEnemy", 20m, 24m, 1m, 3m, 1.5m, 0.75m, 2m, 0.6, ["conditional move needs review"]);

        DefenseCalibrationReport report = new DefenseCalibrationEstimator().Estimate([first, second], calibration);

        AssertEqual(2, report.EnemyCount, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1, report.NeedsReviewCount, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(18m, report.AverageDamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(18m, report.AscensionAverageDamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(18m, report.MedianDamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(21m, report.P75DamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(22.8m, report.P90DamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(24m, report.MaxDamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(0.75m, report.AverageAttackMoveRate, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(2m, report.AverageWeakPerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1m, report.AverageVulnerablePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(0.5m, report.AverageFrailPerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1m, report.AverageStrengthGainPerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertTrue(report.Warnings.Count == 1, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));

        FightDefenseExpectation normal = report.FightExpectations.Single(item => item.FightType == "normal");
        AssertEqual(4m, normal.ExpectedTurns, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(72m, normal.ExpectedDamage, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(72m, normal.AscensionExpectedDamage, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(8m, normal.ExpectedWeak, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));

        LayerDefensePressure firstLayer = report.LayerPressures.Single(item => item.Layer == 1);
        AssertEqual("manualDefensePressure", firstLayer.PressureSource, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1m, firstLayer.AscensionMix, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(8m, firstLayer.EffectiveDamagePerMove, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1.2m, firstLayer.CurrentBlockToDamage, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1m, firstLayer.DamageUnitValue, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(1.2m, firstLayer.CandidateValuePerBlock, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
        AssertEqual(6.667m, firstLayer.RequiredBlockPerMoveAtCurrentConversion, nameof(DefenseCalibrationEstimatorSummarizesEnemyPressure));
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

    private static ModelCatalogEntry MakeMonster(string typeName)
    {
        return new ModelCatalogEntry(
            "enemy",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Monsters.{typeName}",
            $"MONSTER.{typeName.ToUpperInvariant()}",
            "sts2.dll",
            "test",
            1.0);
    }

    private static ModelCatalogEntry MakeEncounter(string typeName)
    {
        return new ModelCatalogEntry(
            "encounter",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Encounters.{typeName}",
            $"ENCOUNTER.{typeName.ToUpperInvariant()}",
            "sts2.dll",
            "test",
            1.0);
    }

    private static EncounterPatternEntry MakeEncounterPattern(
        string typeName,
        string category,
        IReadOnlyList<EncounterActReference> acts,
        IReadOnlyList<string> monsterTypeNames)
    {
        return new EncounterPatternEntry(
            $"ENCOUNTER.{typeName.ToUpperInvariant()}",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Encounters.{typeName}",
            acts,
            category == "Boss" ? "Boss" : category == "Elite" ? "Elite" : "Monster",
            category == "Weak",
            category,
            [],
            monsterTypeNames
                .Select((monster, index) => new EncounterMonsterSlot(index + 1, null, monster, [monster], "test", 1.0))
                .ToArray(),
            monsterTypeNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            monsterTypeNames.Count,
            false,
            [],
            "test",
            1.0);
    }

    private static MonsterMoveNumeric Number(decimal value, decimal? ascensionValue = null)
    {
        return new MonsterMoveNumeric(
            value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value,
            ascensionValue,
            ascensionValue.HasValue ? "DeadlyEnemies" : null,
            0.95);
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

    private static EnemyExpectationProfile MakeEnemyExpectation(
        string typeName,
        decimal damage,
        decimal ascensionDamage,
        decimal attackRate,
        decimal weak,
        decimal vulnerable,
        decimal frail,
        decimal strengthGain,
        double confidence,
        IReadOnlyList<string> warnings)
    {
        return new EnemyExpectationProfile(
            $"MONSTER.{typeName.ToUpperInvariant()}",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Monsters.{typeName}",
            20m,
            30m,
            22m,
            32m,
            damage,
            ascensionDamage,
            damage,
            attackRate,
            0m,
            0m,
            weak,
            vulnerable,
            frail,
            strengthGain,
            1,
            1,
            confidence,
            [],
            warnings,
            "test");
    }

    private static ValueCalibration MakeCalibration()
    {
        return new ValueCalibration
        {
            LayerBreakpoints = [1, 20, 40],
            BaselineCardValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["cost0"] = 7m,
                ["cost1"] = 15m,
                ["cost2"] = 23m
            },
            DamageUnitValue = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = 1m
            },
            BlockToDamage = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = 1.2m,
                ["20"] = 1.6m,
                ["40"] = 2m
            },
            DefensePressure = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = 8m,
                ["20"] = 15m,
                ["40"] = 24m
            },
            ExpectedCombatTurns = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["normal"] = 4m,
                ["elite"] = 5.5m,
                ["boss"] = 9m
            },
            ResourceValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["draw"] = 7m,
                ["energy"] = 8m,
                ["nextTurnEnergyMultiplier"] = 0.75m,
                ["selfHpLossPenalty"] = 1m
            },
            PowerValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["generic"] = 1.6m
            },
            DebuffStackMultipliers = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = 1m,
                ["2"] = 1.5m,
                ["3"] = 1.9m
            },
            WeakValueParameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["damageReduction"] = 0.25m
            },
            VulnerableValueParameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["basePressure"] = 8m,
                ["baseValue"] = 5m,
                ["pressureGrowthMultiplier"] = 0.9m
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
