using System.Text.Json;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.RunHistory;
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
            ExtractionPathsUsesActiveProfilePaths();
            CardFactParserParsesStrike();
            CardFactParserParsesDefend();
            CardFactParserParsesPerfectedStrikeScaling();
            CardFactParserParsesDrawEnergyAndKeyword();
            CardFactParserParsesStarsNextTurnResourcesAndForge();
            CardFactParserParsesDebuffPowers();
            CardFactParserParsesPersistentPowerTriggers();
            CardFactParserParsesGlimmerPutBackAndTransformTargets();
            CardFactParserPreservesComplexRawOperations();
            CardFactParserParsesComplexUpgradeFacts();
            CardFormBuilderBuildsUpgradedFormsFromFacts();
            CardPoolMembershipParserParsesPoolsAndMultiplayerConstraints();
            EncounterPatternParserParsesActAndMonsterSlots();
            CardValueEstimatorUsesCalibration();
            CardValueEstimatorSuppressesSimulatorManagedWarnings();
            SimulationCardLibraryBuilderUsesParsedResources();
            SimulationCardLibraryBuilderSeparatesDynamicVulnerableFromEstimatedWeak();
            SimulationCardLibraryBuilderTreatsRetainAsRuntimeBehavior();
            SimulationCardLibraryBuilderUsesPersistentPowerFacts();
            SimulationCardLibraryBuilderTreatsCardObjectActionsAsRuntimeBehavior();
            DeckMonteCarloSimulatorUsesStarsAndForge();
            DeckMonteCarloSimulatorIgnoresStartingSovereignBladeTokens();
            DeckMonteCarloSimulatorCreditsForgeToSource();
            DeckMonteCarloSimulatorShufflesDiscardForInTurnDraw();
            DeckMonteCarloSimulatorAppliesVulnerableDynamically();
            DeckMonteCarloSimulatorCreditsPersistentPowers();
            DeckMonteCarloSimulatorMovesCardObjectsByValue();
            DeckMonteCarloSimulatorTransformsLowestValueCardObjects();
            SimulationScenarioRunnerBuildsDiyCardsAndVariants();
            RunHistoryDeckExtractorReconstructsRegentA10FloorDeck();
            SimulationDeckDefinitionBuilderUsesRunHistoryOutput();
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

    private static void ExtractionPathsUsesActiveProfilePaths()
    {
        const string profileName = "test-profile";
        const string expectedGameRoot = "C:/games/test-profile/Slay the Spire 2";
        const string expectedDataDir = "C:/games/test-profile/Slay the Spire 2/data_sts2_windows_x86_64";
        const string expectedIlSpyPath = "C:/tools/test-profile/ilspycmd.exe";
        string? oldProfile = Environment.GetEnvironmentVariable("STS2_MOD_PROFILE");
        string? oldProfileValue = Environment.GetEnvironmentVariable(profileName);
        string? oldIlSpyPath = Environment.GetEnvironmentVariable("ILSPYCMD_PATH");
        string? oldLiaoIlSpyPath = Environment.GetEnvironmentVariable("LIAO_ILSPYCMD");

        try
        {
            Environment.SetEnvironmentVariable("STS2_MOD_PROFILE", profileName);
            Environment.SetEnvironmentVariable(
                profileName,
                $$"""
                {
                    "sts2Path": "{{expectedGameRoot}}",
                    "sts2DataDir": "{{expectedDataDir}}",
                    "ilspycmdPath": "{{expectedIlSpyPath}}"
                }
                """);
            Environment.SetEnvironmentVariable("ILSPYCMD_PATH", null);
            Environment.SetEnvironmentVariable("LIAO_ILSPYCMD", null);

            ModelingExtractionOptions options = new();
            ExtractionPaths paths = ExtractionPaths.FromOptions(options);

            AssertEqual(expectedGameRoot, options.GameRoot, nameof(ExtractionPathsUsesActiveProfilePaths));
            AssertEqual(expectedDataDir, options.Sts2DataDir, nameof(ExtractionPathsUsesActiveProfilePaths));
            AssertEqual(
                Path.GetFullPath(expectedGameRoot.Replace('/', Path.DirectorySeparatorChar)),
                paths.GameRoot,
                nameof(ExtractionPathsUsesActiveProfilePaths));
            AssertEqual(
                Path.GetFullPath(expectedDataDir.Replace('/', Path.DirectorySeparatorChar)),
                paths.Sts2DataDir,
                nameof(ExtractionPathsUsesActiveProfilePaths));
            AssertEqual(
                Path.GetFullPath(expectedIlSpyPath.Replace('/', Path.DirectorySeparatorChar)),
                paths.IlSpyPath,
                nameof(ExtractionPathsUsesActiveProfilePaths));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STS2_MOD_PROFILE", oldProfile);
            Environment.SetEnvironmentVariable(profileName, oldProfileValue);
            Environment.SetEnvironmentVariable("ILSPYCMD_PATH", oldIlSpyPath);
            Environment.SetEnvironmentVariable("LIAO_ILSPYCMD", oldLiaoIlSpyPath);
        }
    }

    private static void CardFactParserParsesStrike()
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

        CardFactCatalogEntry parsed = new CardFactParser().Parse(MakeCard("StrikeIronclad"), source);
        CardActionFact term = parsed.Actions.Single(item => item.Kind == "damage");

        AssertEqual((int?)1, parsed.Cost, nameof(CardFactParserParsesStrike));
        AssertEqual("Attack", parsed.CardType, nameof(CardFactParserParsesStrike));
        AssertEqual("Basic", parsed.Rarity, nameof(CardFactParserParsesStrike));
        AssertEqual("AnyEnemy", parsed.TargetType, nameof(CardFactParserParsesStrike));
        AssertEqual((decimal?)6m, term.Amount, nameof(CardFactParserParsesStrike));
        AssertEqual("Damage", term.DynamicVarName, nameof(CardFactParserParsesStrike));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Damage", 3m, nameof(CardFactParserParsesStrike));
    }

    private static void CardFactParserParsesDefend()
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

        CardFactCatalogEntry parsed = new CardFactParser().Parse(MakeCard("DefendIronclad"), source);
        CardActionFact term = parsed.Actions.Single(item => item.Kind == "block");

        AssertEqual((int?)1, parsed.Cost, nameof(CardFactParserParsesDefend));
        AssertEqual("Skill", parsed.CardType, nameof(CardFactParserParsesDefend));
        AssertEqual("Self", parsed.TargetType, nameof(CardFactParserParsesDefend));
        AssertEqual((decimal?)5m, term.Amount, nameof(CardFactParserParsesDefend));
        AssertEqual("Block", term.DynamicVarName, nameof(CardFactParserParsesDefend));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Block", 3m, nameof(CardFactParserParsesDefend));
    }

    private static void CardFactParserParsesPerfectedStrikeScaling()
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

        CardFactCatalogEntry parsed = new CardFactParser().Parse(MakeCard("PerfectedStrike"), source);
        CardActionFact damage = parsed.Actions.Single(item => item.Kind == "damage");
        CardActionFact scaling = parsed.Actions.Single(item => item.Kind == "scalingDamagePerCardTag");

        AssertEqual((int?)2, parsed.Cost, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertEqual("Attack", parsed.CardType, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertEqual("Common", parsed.Rarity, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertEqual((decimal?)6m, damage.Amount, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertEqual((decimal?)2m, scaling.Amount, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertEqual("ExtraDamage", scaling.DynamicVarName, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "ExtraDamage", 1m, nameof(CardFactParserParsesPerfectedStrikeScaling));
        AssertEqual("cardTag:Strike", scaling.Parameter, nameof(CardFactParserParsesPerfectedStrikeScaling));
    }

    private static void CardFactParserParsesDrawEnergyAndKeyword()
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

        CardFactCatalogEntry parsed = new CardFactParser().Parse(MakeCard("Adrenaline"), source);
        CardActionFact draw = parsed.Actions.Single(item => item.Kind == "draw");
        CardActionFact energy = parsed.Actions.Single(item => item.Kind == "energyGain");

        AssertEqual((decimal?)2m, draw.Amount, nameof(CardFactParserParsesDrawEnergyAndKeyword));
        AssertEqual((decimal?)1m, energy.Amount, nameof(CardFactParserParsesDrawEnergyAndKeyword));
        AssertEqual("Energy", energy.DynamicVarName, nameof(CardFactParserParsesDrawEnergyAndKeyword));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Energy", 1m, nameof(CardFactParserParsesDrawEnergyAndKeyword));
        AssertTrue(parsed.Keywords.Contains("Exhaust"), nameof(CardFactParserParsesDrawEnergyAndKeyword));
    }

    private static void CardFactParserParsesStarsNextTurnResourcesAndForge()
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

        CardFactCatalogEntry parsed = new CardFactParser().Parse(MakeCard("TestResourceCard"), source);

        AssertEqual((decimal?)2m, parsed.Actions.Single(term => term.Kind == "starCost").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)1m, parsed.Actions.Single(term => term.Kind == "draw").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)1m, parsed.Actions.Single(term => term.Kind == "drawNextTurn").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)2m, parsed.Actions.Single(term => term.Kind == "energyGain").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)2m, parsed.Actions.Single(term => term.Kind == "energyNextTurn").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)1m, parsed.Actions.Single(term => term.Kind == "starGain").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)3m, parsed.Actions.Single(term => term.Kind == "starNextTurn").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertEqual((decimal?)5m, parsed.Actions.Single(term => term.Kind == "forge").Amount, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Cards", 1m, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Energy", 1m, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Stars", 1m, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "StarNextTurnPower", 1m, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Forge", 2m, nameof(CardFactParserParsesStarsNextTurnResourcesAndForge));
    }

    private static void CardFactParserParsesDebuffPowers()
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

        CardFactCatalogEntry parsed = new CardFactParser().Parse(MakeCard("Bash"), source);
        CardActionFact vulnerable = parsed.Actions.Single(item => item.Kind == "debuffVulnerable");

        AssertEqual((decimal?)2m, vulnerable.Amount, nameof(CardFactParserParsesDebuffPowers));
        AssertEqual("Vulnerable", vulnerable.DynamicVarName, nameof(CardFactParserParsesDebuffPowers));
        AssertUpgradeOperation(parsed, "upgradeDynamicVar", "Vulnerable", 1m, nameof(CardFactParserParsesDebuffPowers));
        AssertEqual("power:Vulnerable;var:Vulnerable", vulnerable.Parameter, nameof(CardFactParserParsesDebuffPowers));

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

        CardFactCatalogEntry weakParsed = new CardFactParser().Parse(MakeCard("Neutralize"), weakSource);
        CardActionFact weak = weakParsed.Actions.Single(item => item.Kind == "debuffWeak");

        AssertEqual((decimal?)1m, weak.Amount, nameof(CardFactParserParsesDebuffPowers));
        AssertEqual("Weak", weak.DynamicVarName, nameof(CardFactParserParsesDebuffPowers));
        AssertUpgradeOperation(weakParsed, "upgradeDynamicVar", "Weak", 1m, nameof(CardFactParserParsesDebuffPowers));
        AssertEqual("power:Weak;var:Weak", weak.Parameter, nameof(CardFactParserParsesDebuffPowers));
    }

    private static void CardFactParserParsesPersistentPowerTriggers()
    {
        CardFactParser parser = new();
        CardFormBuilder formBuilder = new();

        CardFactCatalogEntry childOfTheStars = parser.Parse(
            MakeCard("ChildOfTheStars"),
            """
            public sealed class ChildOfTheStars : Card
            {
                public ChildOfTheStars() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
                {
                    _ = new DynamicVar("BlockForStars", 2m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    await PowerCmd.Apply<ChildOfTheStarsPower>(choiceContext, base.Owner.Creature, base.DynamicVars["BlockForStars"].BaseValue, base.Owner.Creature, this);
                }

                protected override void OnUpgrade()
                {
                    base.DynamicVars["BlockForStars"].UpgradeValueBy(1m);
                }
            }
            """,
            relatedPowerSources: new Dictionary<string, string>
            {
                ["ChildOfTheStarsPower"] = """
                public sealed class ChildOfTheStarsPower : Power
                {
                    protected override async Task AfterStarsSpent(int amount)
                    {
                        await PlayerCmd.GainBlock(base.Owner, base.Amount * amount);
                    }
                }
                """
            });

        CardActionFact childTrigger = childOfTheStars.Actions.Single(action =>
            action.Kind == "persistentPowerTrigger"
            && action.Parameter == "AfterStarsSpent:gainBlockPerStarSpent");
        AssertEqual((decimal?)2m, childTrigger.Amount, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertEqual("BlockForStars", childTrigger.DynamicVarName, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertEqual("Self", childTrigger.TargetType, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertUpgradeOperation(childOfTheStars, "upgradeDynamicVar", "BlockForStars", 1m, nameof(CardFactParserParsesPersistentPowerTriggers));
        CardForm childUpgrade = formBuilder.Build(childOfTheStars, 1);
        AssertEqual(
            (decimal?)3m,
            childUpgrade.Actions.Single(action => action.Parameter == "AfterStarsSpent:gainBlockPerStarSpent").Amount,
            nameof(CardFactParserParsesPersistentPowerTriggers));

        CardFactCatalogEntry blackHole = parser.Parse(
            MakeCard("BlackHole"),
            """
            public sealed class BlackHole : Card
            {
                public BlackHole() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
                {
                    _ = new PowerVar<BlackHolePower>(3m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    await PowerCmd.Apply<BlackHolePower>(choiceContext, base.Owner.Creature, base.DynamicVars["BlackHolePower"].BaseValue, base.Owner.Creature, this);
                }

                protected override void OnUpgrade()
                {
                    base.DynamicVars["BlackHolePower"].UpgradeValueBy(1m);
                }
            }
            """,
            relatedPowerSources: new Dictionary<string, string>
            {
                ["BlackHolePower"] = """
                public sealed class BlackHolePower : Power
                {
                    protected override async Task AfterCardPlayed(CardPlay cardPlay)
                    {
                        if (cardPlay.Resources.StarsSpent > 0 && cardPlay.IsLastInSeries)
                        {
                            await CreatureCmd.DealDamageToAllEnemies(base.Amount);
                        }
                    }

                    protected override async Task AfterStarsGained(int amount)
                    {
                        if (amount > 0)
                        {
                            await CreatureCmd.DealDamageToAllEnemies(base.Amount);
                        }
                    }
                }
                """
            });

        IReadOnlyList<CardActionFact> blackHoleTriggers = blackHole.Actions
            .Where(action => action.Kind == "persistentPowerTrigger")
            .ToArray();
        AssertEqual(2, blackHoleTriggers.Count, nameof(CardFactParserParsesPersistentPowerTriggers));
        CardActionFact starSpentTrigger = blackHoleTriggers.Single(action => action.Parameter == "AfterCardPlayed:damageAllEnemiesOnStarSpent");
        CardActionFact starGainedTrigger = blackHoleTriggers.Single(action => action.Parameter == "AfterStarsGained:damageAllEnemiesOnStarGained");
        AssertEqual((decimal?)3m, starSpentTrigger.Amount, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertEqual((decimal?)3m, starGainedTrigger.Amount, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertEqual("BlackHolePower", starSpentTrigger.DynamicVarName, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertEqual("AllEnemies", starSpentTrigger.TargetType, nameof(CardFactParserParsesPersistentPowerTriggers));
        AssertUpgradeOperation(blackHole, "upgradeDynamicVar", "BlackHolePower", 1m, nameof(CardFactParserParsesPersistentPowerTriggers));
        CardForm blackHoleUpgrade = formBuilder.Build(blackHole, 1);
        AssertEqual(
            (decimal?)4m,
            blackHoleUpgrade.Actions.Single(action => action.Parameter == "AfterStarsGained:damageAllEnemiesOnStarGained").Amount,
            nameof(CardFactParserParsesPersistentPowerTriggers));
    }

    private static void CardFactParserParsesGlimmerPutBackAndTransformTargets()
    {
        CardFactParser parser = new();
        CardFactCatalogEntry glimmer = parser.Parse(
            MakeCard("Glimmer"),
            """
            public sealed class Glimmer : Card
            {
                public Glimmer() : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
                {
                    _ = new CardsVar(3m);
                    _ = new DynamicVar("PutBack", 1m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    await CardPileCmd.Draw(choiceContext, base.Owner, base.DynamicVars.Cards.BaseValue);
                    await CardPileCmd.Add(await CardSelectCmd.FromHand(prefs: new CardSelectorPrefs(base.SelectionScreenPrompt, base.DynamicVars["PutBack"].IntValue), context: choiceContext, player: base.Owner, filter: null, source: this), PileType.Draw, CardPilePosition.Top);
                }

                protected override void OnUpgrade()
                {
                    base.DynamicVars.Cards.UpgradeValueBy(1m);
                }
            }
            """);

        CardActionFact draw = glimmer.Actions.Single(action => action.Kind == "draw");
        CardActionFact select = glimmer.Actions.Single(action => action.Kind == "selectCards");
        CardActionFact move = glimmer.Actions.Single(action => action.Kind == "moveCardBetweenPiles");
        AssertEqual((decimal?)3m, draw.Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("Cards", draw.DynamicVarName, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual((decimal?)1m, select.Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("PutBack", select.DynamicVarName, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("from:Hand", select.Parameter, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual((decimal?)1m, move.Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("PutBack", move.DynamicVarName, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("from:Hand;to:Draw;position:Top", move.Parameter, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertUpgradeOperation(glimmer, "upgradeDynamicVar", "Cards", 1m, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        CardForm glimmerUpgrade = new CardFormBuilder().Build(glimmer, 1);
        AssertEqual((decimal?)4m, glimmerUpgrade.Actions.Single(action => action.Kind == "draw").Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual((decimal?)1m, glimmerUpgrade.Actions.Single(action => action.Kind == "moveCardBetweenPiles").Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));

        CardFactCatalogEntry charge = parser.Parse(
            MakeCard("Charge"),
            """
            public sealed class Charge : Card
            {
                public Charge() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
                {
                    _ = new CardsVar(2m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    List<CardModel> selection = (await CardSelectCmd.FromCombatPile(choiceContext, PileType.Draw.GetPile(base.Owner), base.Owner, new CardSelectorPrefs(base.SelectionScreenPrompt, base.DynamicVars.Cards.IntValue))).ToList();
                    foreach (CardModel item in selection)
                    {
                        await CardCmd.TransformTo<MinionDiveBomb>(item);
                    }
                }
            }
            """);
        CardActionFact chargeSelect = charge.Actions.Single(action => action.Kind == "selectCards");
        CardActionFact chargeTransform = charge.Actions.Single(action => action.Kind == "transformCard");
        AssertEqual((decimal?)2m, chargeSelect.Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("Cards", chargeSelect.DynamicVarName, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("from:Draw", chargeSelect.Parameter, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual((decimal?)2m, chargeTransform.Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("Cards", chargeTransform.DynamicVarName, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("from:Draw;card:MinionDiveBomb", chargeTransform.Parameter, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));

        CardFactCatalogEntry randomTransform = parser.Parse(
            MakeCard("Begone"),
            """
            public sealed class Begone : Card
            {
                public Begone() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) {}

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    CardModel cardModel = (await CardSelectCmd.FromHand(prefs: new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), context: choiceContext, player: base.Owner, filter: null, source: this)).FirstOrDefault();
                    await CardCmd.Transform(cardModel, CardCmd.CreateCard<SolarStrike>());
                }
            }
            """);
        CardActionFact transform = randomTransform.Actions.Single(action => action.Kind == "transformCard");
        AssertEqual((decimal?)1m, transform.Amount, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
        AssertEqual("from:Hand;card:SIM.TRANSFORMED_CARD", transform.Parameter, nameof(CardFactParserParsesGlimmerPutBackAndTransformTargets));
    }

    private static void CardFactParserPreservesComplexRawOperations()
    {
        CardFactCatalogEntry charge = new CardFactParser().Parse(
            MakeCard("Charge"),
            """
            public sealed class Charge : Card
            {
                public Charge() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) {}

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    CardSelectResult result = await CardSelectCmd.FromCombatPile(choiceContext, base.Owner, PileType.DiscardPile);
                    await CardPileCmd.Add(choiceContext, result.Cards[0], base.Owner, PileType.Hand, CardPilePosition.Top);
                }
            }
            """);
        AssertTrue(charge.Actions.Any(action => action.Kind == "selectCards"), nameof(CardFactParserPreservesComplexRawOperations));
        AssertTrue(charge.RawOperations.Any(operation => operation.Kind == "moveCardBetweenPiles"), nameof(CardFactParserPreservesComplexRawOperations));
        AssertTrue(charge.Unresolved.Count == 0, nameof(CardFactParserPreservesComplexRawOperations));

        CardFactCatalogEntry collisionCourse = new CardFactParser().Parse(
            MakeCard("CollisionCourse"),
            """
            public sealed class CollisionCourse : Card
            {
                public bool HasEnergyCostX => true;

                public CollisionCourse() : base(-1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
                {
                    _ = new DamageVar(5m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    int spent = ResolveEnergyXValue();
                }
            }
            """);
        AssertTrue(collisionCourse.Actions.Any(action => action.Kind == "xCostDamage"), nameof(CardFactParserPreservesComplexRawOperations));
        AssertTrue(collisionCourse.RawOperations.Any(operation => operation.Kind == "xCost"), nameof(CardFactParserPreservesComplexRawOperations));

        CardFactCatalogEntry neowsFury = new CardFactParser().Parse(
            MakeCard("NeowsFury"),
            """
            public sealed class NeowsFury : Card
            {
                public NeowsFury() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self) {}

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    await CardPileCmd.AddGeneratedCardToCombat(choiceContext, CardCmd.CreateCard<StrikeRegent>(), PileType.DrawPile);
                }
            }
            """);
        AssertTrue(neowsFury.Actions.Any(action => action.Kind == "createCard"), nameof(CardFactParserPreservesComplexRawOperations));
        AssertTrue(neowsFury.RawOperations.Any(operation => operation.Parameter == "card:StrikeRegent;pile:DrawPile"), nameof(CardFactParserPreservesComplexRawOperations));

        CardFactCatalogEntry volley = new CardFactParser().Parse(
            MakeCard("Volley"),
            """
            public sealed class Volley : Card
            {
                public Volley() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
                {
                    _ = new DamageVar(3m).WithHitCount(3);
                }
            }
            """);
        AssertEqual(3, volley.Actions.Single(action => action.Kind == "damage").HitCount, nameof(CardFactParserPreservesComplexRawOperations));

        CardFactCatalogEntry refineBlade = new CardFactParser().Parse(
            MakeCard("RefineBlade"),
            """
            public sealed class RefineBlade : Card
            {
                public RefineBlade() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
                {
                    _ = new ForgeVar(4m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    await CardCmd.TransformTo<SolarStrike>(choiceContext, cardPlay.Card);
                    await ForgeCmd.Forge(base.DynamicVars.Forge.IntValue, base.Owner, this);
                }
            }
            """);
        AssertTrue(refineBlade.Actions.Any(action => action.Kind == "transformCard"), nameof(CardFactParserPreservesComplexRawOperations));
        AssertEqual((decimal?)4m, refineBlade.Actions.Single(action => action.Kind == "forge").Amount, nameof(CardFactParserPreservesComplexRawOperations));

        CardFactCatalogEntry childOfTheStars = new CardFactParser().Parse(
            MakeCard("ChildOfTheStars"),
            """
            public sealed class ChildOfTheStars : Card
            {
                public ChildOfTheStars() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
                {
                    _ = new PowerVar<ChildOfTheStarsPower>(2m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    await PowerCmd.Apply<ChildOfTheStarsPower>(choiceContext, base.Owner.Creature, base.DynamicVars.ChildOfTheStars.BaseValue, base.Owner.Creature, this);
                }
            }
            """,
            relatedPowerSources: new Dictionary<string, string>
            {
                ["ChildOfTheStarsPower"] = "protected override async Task AfterStarsSpent() { await PlayerCmd.GainBlock(owner, 1); }"
            });
        AssertTrue(childOfTheStars.Actions.Any(action => action.Kind == "persistentPowerTrigger"), nameof(CardFactParserPreservesComplexRawOperations));

        CardFactCatalogEntry cosmicIndifference = new CardFactParser().Parse(
            MakeCard("CosmicIndifference"),
            """
            public sealed class CosmicIndifference : Card
            {
                protected override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal };

                public CosmicIndifference() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
                {
                    _ = new BlockVar(7m);
                    _ = CardTag.Star;
                }
            }
            """);
        AssertTrue(cosmicIndifference.Keywords.Contains("Ethereal"), nameof(CardFactParserPreservesComplexRawOperations));
        AssertTrue(cosmicIndifference.Tags.Contains("Star"), nameof(CardFactParserPreservesComplexRawOperations));
    }

    private static void CardFactParserParsesComplexUpgradeFacts()
    {
        CardFactCatalogEntry quasar = new CardFactParser().Parse(
            MakeCard("Quasar"),
            """
            public sealed class Quasar : Card
            {
                public Quasar() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self) {}

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    List<Card> cards = CardFactory.GetDistinctForCombat(choiceContext.Rng, CardPool<ColorlessCardPool>(), 3);
                    if (base.IsUpgraded)
                    {
                        CardCmd.Upgrade(cards, choiceContext);
                    }

                    CardSelectResult result = await CardSelectCmd.FromChooseACardScreen(choiceContext, cards);
                    Card selected = result.Cards[0];
                    await CardPileCmd.AddGeneratedCardToCombat(selected, PileType.Hand);
                }
            }
            """);
        AssertTrue(quasar.Actions.Any(action => action.Kind == "createCardChoices"), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertTrue(quasar.Actions.Any(action => action.Kind == "selectCards"), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertTrue(quasar.Actions.Any(action => action.Kind == "createCard"), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertTrue(quasar.RawOperations.Any(operation => operation.Kind == "isUpgradedBranch"), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertTrue(
            quasar.UpgradeOperations.Any(operation =>
                operation.Kind == "upgradeGeneratedCards"
                && operation.Condition == "base.IsUpgraded"
                && operation.Parameter == "target:cards"),
            nameof(CardFactParserParsesComplexUpgradeFacts));

        CardFactCatalogEntry voidForm = new CardFactParser().Parse(
            MakeCard("VoidForm"),
            """
            public sealed class VoidForm : Card
            {
                protected override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal };

                public VoidForm() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) {}

                protected override void OnUpgrade()
                {
                    RemoveKeyword(CardKeyword.Ethereal);
                }
            }
            """);
        AssertTrue(voidForm.Keywords.Contains("Ethereal"), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertUpgradeOperation(voidForm, "removeKeyword", "Ethereal", null, nameof(CardFactParserParsesComplexUpgradeFacts));

        CardFactCatalogEntry orbit = new CardFactParser().Parse(
            MakeCard("Orbit"),
            """
            public sealed class Orbit : Card
            {
                public Orbit() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self) {}

                protected override void OnUpgrade()
                {
                    EnergyCost.UpgradeBy(-1);
                }
            }
            """);
        AssertEqual((int?)2, orbit.Cost, nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertUpgradeOperation(orbit, "upgradeCost", "EnergyCost", -1m, nameof(CardFactParserParsesComplexUpgradeFacts));

        CardFactCatalogEntry shockwave = new CardFactParser().Parse(
            MakeCard("Shockwave"),
            """
            public sealed class Shockwave : Card
            {
                public Shockwave() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
                {
                    _ = new DynamicVar("Power", 3m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    int amount = base.DynamicVars["Power"].IntValue;
                    await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, amount, base.Owner.Creature, this);
                    await PowerCmd.Apply<VulnerablePower>(choiceContext, cardPlay.Target, amount, base.Owner.Creature, this);
                }

                protected override void OnUpgrade()
                {
                    base.DynamicVars["Power"].UpgradeValueBy(2m);
                }
            }
            """);
        AssertEqual((decimal?)3m, shockwave.DynamicVars.Single(fact => fact.Name == "Power").Amount, nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertEqual((decimal?)3m, shockwave.Actions.Single(action => action.Kind == "debuffWeak").Amount, nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertEqual((decimal?)3m, shockwave.Actions.Single(action => action.Kind == "debuffVulnerable").Amount, nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertEqual("Power", shockwave.Actions.Single(action => action.Kind == "debuffWeak").DynamicVarName, nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertUpgradeOperation(shockwave, "upgradeDynamicVar", "Power", 2m, nameof(CardFactParserParsesComplexUpgradeFacts));

        string json = JsonSerializer.Serialize(new[] { quasar, voidForm, orbit, shockwave });
        AssertTrue(!json.Contains(string.Concat("Upgrade", "Delta"), StringComparison.Ordinal), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertTrue(!json.Contains(string.Concat("Is", "Simulatable"), StringComparison.Ordinal), nameof(CardFactParserParsesComplexUpgradeFacts));
        AssertTrue(!json.Contains(string.Concat("Is", "Value", "Estimatable"), StringComparison.Ordinal), nameof(CardFactParserParsesComplexUpgradeFacts));
    }

    private static void CardFormBuilderBuildsUpgradedFormsFromFacts()
    {
        CardFactParser parser = new();
        CardFormBuilder builder = new();

        CardFactCatalogEntry shockwave = parser.Parse(
            MakeCard("Shockwave"),
            """
            public sealed class Shockwave : Card
            {
                public Shockwave() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
                {
                    _ = new DynamicVar("Power", 3m);
                }

                protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
                {
                    int amount = base.DynamicVars["Power"].IntValue;
                    await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, amount, base.Owner.Creature, this);
                    await PowerCmd.Apply<VulnerablePower>(choiceContext, cardPlay.Target, amount, base.Owner.Creature, this);
                }

                protected override void OnUpgrade()
                {
                    base.DynamicVars["Power"].UpgradeValueBy(2m);
                }
            }
            """);
        CardForm shockwaveBase = builder.Build(shockwave, 0);
        CardForm shockwaveUpgrade = builder.Build(shockwave, 1);
        AssertEqual((decimal?)3m, shockwaveBase.Actions.Single(action => action.Kind == "debuffWeak").Amount, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));
        AssertEqual((decimal?)5m, shockwaveUpgrade.Actions.Single(action => action.Kind == "debuffWeak").Amount, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));
        AssertEqual((decimal?)3m, shockwaveBase.Actions.Single(action => action.Kind == "debuffVulnerable").Amount, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));
        AssertEqual((decimal?)5m, shockwaveUpgrade.Actions.Single(action => action.Kind == "debuffVulnerable").Amount, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));

        CardFactCatalogEntry orbit = parser.Parse(
            MakeCard("Orbit"),
            """
            public sealed class Orbit : Card
            {
                public Orbit() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self) {}
                protected override void OnUpgrade() { EnergyCost.UpgradeBy(-1); }
            }
            """);
        AssertEqual((int?)1, builder.Build(orbit, 1).Cost, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));

        CardFactCatalogEntry voidForm = parser.Parse(
            MakeCard("VoidForm"),
            """
            public sealed class VoidForm : Card
            {
                protected override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal };
                public VoidForm() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) {}
                protected override void OnUpgrade() { RemoveKeyword(CardKeyword.Ethereal); }
            }
            """);
        AssertTrue(!builder.Build(voidForm, 1).Keywords.Contains("Ethereal"), nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));

        CardFactCatalogEntry fallingStar = parser.Parse(
            MakeCard("FallingStar"),
            """
            public sealed class FallingStar : Card
            {
                public FallingStar() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
                {
                    DynamicVars.Damage.UpgradeValueBy(3m);
                    DynamicVars.Weak.UpgradeValueBy(1m);
                    _ = new DamageVar(6m);
                    _ = new PowerVar<WeakPower>(1m);
                    await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
                }
            }
            """);
        CardForm fallingStarPlus = builder.Build(fallingStar, 1);
        AssertEqual((decimal?)9m, fallingStarPlus.Actions.Single(action => action.Kind == "damage").Amount, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));
        AssertEqual((decimal?)2m, fallingStarPlus.Actions.Single(action => action.Kind == "debuffWeak").Amount, nameof(CardFormBuilderBuildsUpgradedFormsFromFacts));
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
            MakeFactEntry(
                "StrikeIronclad",
                1,
                "Attack",
                "AnyEnemy",
                [MakeAction("damage", 6m, "Damage", null, "AnyEnemy", null, "test", 0.9)],
                upgradeOperations: [MakeUpgradeOperation("upgradeDynamicVar", "Damage", 3m)]),
            calibration,
            layer: 1);
        AssertEqual(6m, strike.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(9m, strike.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(3m, strike.SmithValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate defend = estimator.Estimate(
            MakeFactEntry(
                "DefendIronclad",
                1,
                "Skill",
                "Self",
                [MakeAction("block", 5m, "Block", null, "Self", null, "test", 0.9)],
                upgradeOperations: [MakeUpgradeOperation("upgradeDynamicVar", "Block", 3m)]),
            calibration,
            layer: 1);
        AssertEqual(6m, defend.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(9.6m, defend.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate adrenaline = estimator.Estimate(
            MakeFactEntry(
                "Adrenaline",
                0,
                "Skill",
                "Self",
                [
                    MakeAction("draw", 2m, null, null, "Self", null, "test", 0.9),
                    MakeAction("energyGain", 1m, "Energy", null, "Self", null, "test", 0.9)
                ],
                keywords: ["Exhaust"],
                upgradeOperations: [MakeUpgradeOperation("upgradeDynamicVar", "Energy", 1m)]),
            calibration,
            layer: 1);
        AssertEqual(22m, adrenaline.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(30m, adrenaline.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate neutralize = estimator.Estimate(
            MakeFactEntry(
                "Neutralize",
                0,
                "Attack",
                "AnyEnemy",
                [MakeAction("debuffWeak", 1m, "Weak", null, "AnyEnemy", "power:Weak", "test", 0.9)],
                upgradeOperations: [MakeUpgradeOperation("upgradeDynamicVar", "Weak", 1m)]),
            calibration,
            layer: 1);
        AssertEqual(2.4m, neutralize.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(3.6m, neutralize.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(1.2m, neutralize.SmithValue, nameof(CardValueEstimatorUsesCalibration));

        CardValueEstimate bash = estimator.Estimate(
            MakeFactEntry(
                "Bash",
                2,
                "Attack",
                "AnyEnemy",
                [MakeAction("debuffVulnerable", 2m, "Vulnerable", null, "AnyEnemy", "power:Vulnerable", "test", 0.9)],
                upgradeOperations: [MakeUpgradeOperation("upgradeDynamicVar", "Vulnerable", 1m)]),
            calibration,
            layer: 40);
        AssertEqual(21m, bash.EstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(26.6m, bash.UpgradedEstimatedValue, nameof(CardValueEstimatorUsesCalibration));
        AssertEqual(5.6m, bash.SmithValue, nameof(CardValueEstimatorUsesCalibration));
    }

    private static void CardValueEstimatorSuppressesSimulatorManagedWarnings()
    {
        ValueCalibration calibration = MakeCalibration();
        CardValueEstimator estimator = new();

        CardValueEstimate venerate = estimator.Estimate(
            MakeFactEntry(
                "Venerate",
                1,
                "Skill",
                "Self",
                [MakeAction("starGain", 2m, "Stars", null, "Self", null, "test", 0.82)],
                upgradeOperations: [MakeUpgradeOperation("upgradeDynamicVar", "Stars", 1m)]),
            calibration,
            layer: 1);
        AssertTrue(venerate.Warnings.Count == 0, nameof(CardValueEstimatorSuppressesSimulatorManagedWarnings));

        CardValueEstimate ascendersBane = estimator.Estimate(
            MakeFactEntry(
                "AscendersBane",
                -1,
                "Curse",
                "None",
                [],
                keywords: ["Eternal", "Ethereal", "Unplayable"]),
            calibration,
            layer: 1);
        AssertTrue(
            !ascendersBane.Warnings.Any(warning =>
                warning.Contains("generic calibration fallback", StringComparison.Ordinal)
                || warning.Contains("No cost baseline", StringComparison.Ordinal)),
            nameof(CardValueEstimatorSuppressesSimulatorManagedWarnings));
    }

    private static void SimulationCardLibraryBuilderUsesParsedResources()
    {
        CardFactCatalogEntry entry = MakeFactEntry(
            "ResourceCard",
            1,
            "Skill",
            "Self",
            [
                MakeAction("damage", 6m, null, null, "AnyEnemy", null, "test", 0.9),
                MakeAction("draw", 1m, null, null, "Self", null, "test", 0.9),
                MakeAction("drawNextTurn", 1m, null, null, "Self", null, "test", 0.9),
                MakeAction("energyGain", 2m, null, null, "Self", null, "test", 0.9),
                MakeAction("energyNextTurn", 1m, null, null, "Self", null, "test", 0.9),
                MakeAction("starCost", 2m, null, null, "Self", null, "test", 0.9),
                MakeAction("starGain", 1m, null, null, "Self", null, "test", 0.9),
                MakeAction("starNextTurn", 3m, null, null, "Self", null, "test", 0.9),
                MakeAction("forge", 5m, null, null, "Self", null, "test", 0.9)
            ]);

        SimulationCard card = new SimulationCardLibraryBuilder()
            .Build([entry], MakeCalibration(), layer: 1)
            .Single();

        AssertEqual(6m, card.IntrinsicValue, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(6m, card.DamageValue, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.Draw, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.DrawNextTurn, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(2, card.EnergyGain, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.EnergyNextTurn, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(2, card.StarCost, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(1, card.StarGain, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(3, card.StarNextTurn, nameof(SimulationCardLibraryBuilderUsesParsedResources));
        AssertEqual(5, card.Forge, nameof(SimulationCardLibraryBuilderUsesParsedResources));
    }

    private static void SimulationCardLibraryBuilderSeparatesDynamicVulnerableFromEstimatedWeak()
    {
        CardFactCatalogEntry entry = MakeFactEntry(
            "DebuffAttack",
            0,
            "Attack",
            "AnyEnemy",
            [
                MakeAction("damage", 9m, null, null, "AnyEnemy", null, "test", 0.9),
                MakeAction("debuffVulnerable", 1m, null, null, "AnyEnemy", "power:Vulnerable", "test", 0.9),
                MakeAction("debuffWeak", 1m, null, null, "AnyEnemy", "power:Weak", "test", 0.9)
            ]);

        SimulationCard card = new SimulationCardLibraryBuilder()
            .Build([entry], MakeCalibration(), layer: 1)
            .Single();

        AssertEqual(15m, card.IntrinsicValue, nameof(SimulationCardLibraryBuilderSeparatesDynamicVulnerableFromEstimatedWeak));
        AssertEqual(9m, card.DamageValue, nameof(SimulationCardLibraryBuilderSeparatesDynamicVulnerableFromEstimatedWeak));
        AssertEqual(1, card.Vulnerable, nameof(SimulationCardLibraryBuilderSeparatesDynamicVulnerableFromEstimatedWeak));
    }

    private static void SimulationCardLibraryBuilderTreatsRetainAsRuntimeBehavior()
    {
        CardFactCatalogEntry entry = MakeFactEntry(
            "SovereignBlade",
            2,
            "Attack",
            "AnyEnemy",
            [MakeAction("damage", 10m, null, null, "AnyEnemy", null, "test", 0.9)],
            keywords: ["Retain"]);

        SimulationCard card = new SimulationCardLibraryBuilder()
            .Build([entry], MakeCalibration(), layer: 1)
            .Single();

        AssertEqual(10m, card.IntrinsicValue, nameof(SimulationCardLibraryBuilderTreatsRetainAsRuntimeBehavior));
        AssertEqual(10m, card.DamageValue, nameof(SimulationCardLibraryBuilderTreatsRetainAsRuntimeBehavior));
        AssertTrue(card.Retain, nameof(SimulationCardLibraryBuilderTreatsRetainAsRuntimeBehavior));
    }

    private static void SimulationCardLibraryBuilderUsesPersistentPowerFacts()
    {
        CardFactCatalogEntry childEntry = MakeFactEntry(
            "ChildOfTheStars",
            1,
            "Power",
            "Self",
            [
                MakeAction("power", 2m, "BlockForStars", null, "Self", "power:ChildOfTheStars;var:BlockForStars", "test", 0.78),
                MakeAction("persistentPowerTrigger", 2m, "BlockForStars", null, "Self", "AfterStarsSpent:gainBlockPerStarSpent", "ChildOfTheStarsPower.AfterStarsSpent", 0.75)
            ]);

        SimulationCard child = new SimulationCardLibraryBuilder()
            .Build([childEntry], MakeCalibration(), layer: 1)
            .Single();

        AssertEqual(0m, child.IntrinsicValue, nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
        AssertEqual(1.2m, child.BlockValuePerBlock, nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
        AssertEqual(2.4m, child.SetupPriorityValue, nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
        AssertTrue(child.HasSimulatedResourceEffect, nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
        AssertTrue(
            !child.Warnings.Any(warning => warning.Contains("persistentPowerTrigger", StringComparison.Ordinal)),
            nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));

        CardFactCatalogEntry blackHoleEntry = MakeFactEntry(
            "BlackHole",
            1,
            "Power",
            "Self",
            [
                MakeAction("power", 3m, "BlackHolePower", null, "Self", "power:BlackHole;var:BlackHolePower", "test", 0.78),
                MakeAction("persistentPowerTrigger", 3m, "BlackHolePower", null, "AllEnemies", "AfterCardPlayed:damageAllEnemiesOnStarSpent", "BlackHolePower.AfterCardPlayed", 0.75),
                MakeAction("persistentPowerTrigger", 3m, "BlackHolePower", null, "AllEnemies", "AfterStarsGained:damageAllEnemiesOnStarGained", "BlackHolePower.AfterStarsGained", 0.75)
            ]);

        SimulationCard blackHole = new SimulationCardLibraryBuilder()
            .Build([blackHoleEntry], MakeCalibration(), layer: 1)
            .Single();

        AssertEqual(1.3m, blackHole.AoeDamageMultiplier, nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
        AssertEqual(7.8m, blackHole.SetupPriorityValue, nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
        AssertTrue(
            !blackHole.Warnings.Any(warning => warning.Contains("persistentPowerTrigger", StringComparison.Ordinal)),
            nameof(SimulationCardLibraryBuilderUsesPersistentPowerFacts));
    }

    private static void SimulationCardLibraryBuilderTreatsCardObjectActionsAsRuntimeBehavior()
    {
        CardFactCatalogEntry entry = MakeFactEntry(
            "CardObjectRuntime",
            0,
            "Skill",
            "Self",
            [
                MakeAction("selectCards", 1m, null, null, "Self", "from:Hand", "CardSelectCmd.FromHand", 0.75),
                MakeAction("moveCardBetweenPiles", 1m, null, null, "Self", "from:Hand;to:Draw;position:Top", "CardPileCmd.Add", 0.75),
                MakeAction("transformCard", 1m, null, null, "Self", "from:Hand;card:SIM.TRANSFORMED_CARD", "CardCmd.Transform", 0.55)
            ]);

        SimulationCard card = new SimulationCardLibraryBuilder()
            .Build([entry], MakeCalibration(), layer: 1)
            .Single();

        AssertTrue(card.HasSimulatedResourceEffect, nameof(SimulationCardLibraryBuilderTreatsCardObjectActionsAsRuntimeBehavior));
        AssertTrue(
            !card.Warnings.Any(warning =>
                warning.Contains("selectCards", StringComparison.Ordinal)
                || warning.Contains("moveCardBetweenPiles", StringComparison.Ordinal)
                || warning.Contains("transformCard", StringComparison.Ordinal)),
            nameof(SimulationCardLibraryBuilderTreatsCardObjectActionsAsRuntimeBehavior));
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

    private static void DeckMonteCarloSimulatorIgnoresStartingSovereignBladeTokens()
    {
        SimulationCard initialBlade = MakeSimulationCard("SovereignBlade", value: 99m) with
        {
            EnergyCost = 0,
            DamageValue = 99m,
            Retain = true
        };
        SimulationCard strike = MakeSimulationCard("Strike", value: 6m);
        DeckSimulationReport report = new DeckMonteCarloSimulator().Simulate(
            [initialBlade, strike],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 2, BaseEnergy = 3, Seed = 1 });

        AssertEqual(1, report.DeckSize, nameof(DeckMonteCarloSimulatorIgnoresStartingSovereignBladeTokens));
        AssertEqual(6m, report.TotalExpectedValue, nameof(DeckMonteCarloSimulatorIgnoresStartingSovereignBladeTokens));
        AssertTrue(!report.PlayedCards.Any(card => card.TypeName == "SovereignBlade"), nameof(DeckMonteCarloSimulatorIgnoresStartingSovereignBladeTokens));
        AssertTrue(report.Warnings.Any(warning => warning.Contains("Starting Sovereign Blade token cards were ignored", StringComparison.Ordinal)), nameof(DeckMonteCarloSimulatorIgnoresStartingSovereignBladeTokens));
    }

    private static void DeckMonteCarloSimulatorCreditsForgeToSource()
    {
        SimulationCard refineBlade = MakeSimulationCard("RefineBlade", value: 0m) with
        {
            Forge = 9
        };
        DeckSimulationReport onceReport = new DeckMonteCarloSimulator().Simulate(
            [refineBlade],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 1, BaseEnergy = 3, Seed = 1 });

        AssertEqual(19m, onceReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(
            19m,
            onceReport.PlayedCards.Single(card => card.TypeName == "SovereignBlade").AverageValuePerPlay,
            nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        CardValueCreditSummary onceBladeCredit = onceReport.CardValueCredits.Single(card => card.TypeName == "SovereignBlade");
        CardValueCreditSummary onceRefineCredit = onceReport.CardValueCredits.Single(card => card.TypeName == "RefineBlade");
        AssertEqual(10m, onceBladeCredit.DirectValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(0m, onceBladeCredit.ForgeRealizedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(0m, onceRefineCredit.DirectValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(9m, onceRefineCredit.ForgeRealizedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(9m, onceRefineCredit.TotalCreditedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));

        DeckSimulationReport twiceReport = new DeckMonteCarloSimulator().Simulate(
            [refineBlade, refineBlade],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 2, BaseEnergy = 3, Seed = 1 });
        CardValueCreditSummary twiceBladeCredit = twiceReport.CardValueCredits.Single(card => card.TypeName == "SovereignBlade");
        CardValueCreditSummary twiceRefineCredit = twiceReport.CardValueCredits.Single(card => card.TypeName == "RefineBlade");
        AssertEqual(28m, twiceReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(10m, twiceBladeCredit.DirectValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(18m, twiceRefineCredit.ForgeRealizedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(18m, twiceRefineCredit.TotalCreditedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));

        SimulationCard honeBlade = MakeSimulationCard("HoneBlade", value: 0m) with
        {
            Forge = 4
        };
        DeckSimulationReport mixedReport = new DeckMonteCarloSimulator().Simulate(
            [refineBlade, honeBlade],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 2, BaseEnergy = 3, Seed = 1 });
        AssertEqual(23m, mixedReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(
            9m,
            mixedReport.CardValueCredits.Single(card => card.TypeName == "RefineBlade").ForgeRealizedValue,
            nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(
            4m,
            mixedReport.CardValueCredits.Single(card => card.TypeName == "HoneBlade").ForgeRealizedValue,
            nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
        AssertEqual(
            10m,
            mixedReport.CardValueCredits.Single(card => card.TypeName == "SovereignBlade").DirectValue,
            nameof(DeckMonteCarloSimulatorCreditsForgeToSource));
    }

    private static void DeckMonteCarloSimulatorShufflesDiscardForInTurnDraw()
    {
        SimulationCard payoff = MakeSimulationCard("Payoff", value: 10m);
        SimulationCard draw = MakeSimulationCard("DeepBreath", value: 0m) with
        {
            Draw = 1
        };

        DeckSimulationReport report = new DeckMonteCarloSimulator().Simulate(
            [payoff, draw],
            new DeckSimulationOptions { Runs = 64, Turns = 2, HandSize = 1, BaseEnergy = 3, Seed = 1 });

        AssertEqual(10m, report.Turns[1].ExpectedValue, nameof(DeckMonteCarloSimulatorShufflesDiscardForInTurnDraw));
    }

    private static void DeckMonteCarloSimulatorAppliesVulnerableDynamically()
    {
        SimulationCard vulnerable = MakeSimulationCard("Expose", value: 0m) with
        {
            Vulnerable = 1
        };
        SimulationCard attack = MakeSimulationCard("Meteor", value: 9m) with
        {
            DamageValue = 9m
        };

        DeckSimulationReport sameTurnReport = new DeckMonteCarloSimulator().Simulate(
            [vulnerable, attack],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 2, BaseEnergy = 3, Seed = 1 });
        AssertEqual(13m, sameTurnReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorAppliesVulnerableDynamically));

        DeckSimulationReport nextTurnReport = new DeckMonteCarloSimulator().Simulate(
            [vulnerable, attack],
            new DeckSimulationOptions { Runs = 64, Turns = 2, HandSize = 1, BaseEnergy = 3, Seed = 1 });
        AssertEqual(9m, nextTurnReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorAppliesVulnerableDynamically));
    }

    private static void DeckMonteCarloSimulatorCreditsPersistentPowers()
    {
        SimulationCard childOfTheStars = MakeSimulationCard("ChildOfTheStars", value: 0m) with
        {
            CardType = "Power",
            BlockValuePerBlock = 1.2m,
            SetupPriorityValue = 2.4m,
            Actions =
            [
                MakeAction(
                    "persistentPowerTrigger",
                    2m,
                    "BlockForStars",
                    null,
                    "Self",
                    "AfterStarsSpent:gainBlockPerStarSpent",
                    "ChildOfTheStarsPower.AfterStarsSpent",
                    0.75)
            ]
        };
        SimulationCard starGain = MakeSimulationCard("StarGainTwo", value: 0m) with
        {
            StarGain = 2
        };
        SimulationCard starSpend = MakeSimulationCard("StarSpendTwo", value: 0m) with
        {
            StarCost = 2
        };
        DeckSimulationReport childReport = new DeckMonteCarloSimulator().Simulate(
            [childOfTheStars, starGain, starSpend],
            new DeckSimulationOptions { Runs = 1, Turns = 2, HandSize = 3, BaseEnergy = 3, BaseStars = 0, Seed = 1 });
        CardValueCreditSummary childCredit = childReport.CardValueCredits.Single(card => card.TypeName == "ChildOfTheStars");
        AssertEqual(9.6m, childReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));
        AssertEqual(1, childCredit.DirectPlayCount, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));
        AssertEqual(9.6m, childCredit.PowerRealizedValue, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));
        AssertEqual(1, childReport.PlayedCards.Single(card => card.TypeName == "ChildOfTheStars").PlayCount, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));

        SimulationCard blackHole = MakeSimulationCard("BlackHole", value: 0m) with
        {
            CardType = "Power",
            DamageUnitValue = 1m,
            AoeDamageMultiplier = 1.3m,
            SetupPriorityValue = 7.8m,
            Actions =
            [
                MakeAction(
                    "persistentPowerTrigger",
                    3m,
                    "BlackHolePower",
                    null,
                    "AllEnemies",
                    "AfterCardPlayed:damageAllEnemiesOnStarSpent",
                    "BlackHolePower.AfterCardPlayed",
                    0.75),
                MakeAction(
                    "persistentPowerTrigger",
                    3m,
                    "BlackHolePower",
                    null,
                    "AllEnemies",
                    "AfterStarsGained:damageAllEnemiesOnStarGained",
                    "BlackHolePower.AfterStarsGained",
                    0.75)
            ]
        };
        DeckSimulationReport blackHoleReport = new DeckMonteCarloSimulator().Simulate(
            [blackHole, starGain, starSpend],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 3, BaseEnergy = 3, BaseStars = 0, Seed = 1 });
        CardValueCreditSummary blackHoleCredit = blackHoleReport.CardValueCredits.Single(card => card.TypeName == "BlackHole");
        AssertEqual(7.8m, blackHoleReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));
        AssertEqual(7.8m, blackHoleCredit.PowerRealizedValue, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));

        SimulationCard starsNextTurn = MakeSimulationCard("StarsNextTurnThree", value: 0m) with
        {
            StarNextTurn = 3
        };
        DeckSimulationReport nextTurnReport = new DeckMonteCarloSimulator().Simulate(
            [blackHole, starsNextTurn],
            new DeckSimulationOptions { Runs = 1, Turns = 2, HandSize = 2, BaseEnergy = 3, BaseStars = 0, Seed = 1 });
        CardValueCreditSummary nextTurnCredit = nextTurnReport.CardValueCredits.Single(card => card.TypeName == "BlackHole");
        AssertEqual(3.9m, nextTurnReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));
        AssertEqual(3.9m, nextTurnCredit.PowerRealizedValue, nameof(DeckMonteCarloSimulatorCreditsPersistentPowers));
    }

    private static void DeckMonteCarloSimulatorMovesCardObjectsByValue()
    {
        SimulationCard glimmer = MakeSimulationCard("Glimmer", value: 0m) with
        {
            Draw = 2,
            Actions =
            [
                MakeAction("moveCardBetweenPiles", 1m, "PutBack", null, "Self", "from:Hand;to:Draw;position:Top", "CardPileCmd.Add", 0.8)
            ]
        };
        SimulationCard high = MakeSimulationCard("HighValue", value: 20m);
        SimulationCard low = MakeSimulationCard("LowValue", value: 1m);
        DeckSimulationReport? glimmerReport = null;
        for (int seed = 1; seed <= 200; seed++)
        {
            DeckSimulationReport candidate = new DeckMonteCarloSimulator().Simulate(
                [glimmer, high, low],
                new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 1, BaseEnergy = 3, Seed = seed });
            if (candidate.PlayedCards.Any(card => card.TypeName == "Glimmer"))
            {
                glimmerReport = candidate;
                break;
            }
        }

        AssertTrue(glimmerReport is not null, nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));
        AssertEqual(1m, glimmerReport!.TotalExpectedValue, nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));
        AssertTrue(glimmerReport.PlayedCards.Any(card => card.TypeName == "LowValue"), nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));
        AssertTrue(!glimmerReport.PlayedCards.Any(card => card.TypeName == "HighValue"), nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));

        SimulationCard cull = MakeSimulationCard("Cull", value: 0m) with
        {
            SetupPriorityValue = 100m,
            Actions =
            [
                MakeAction("moveCardBetweenPiles", 1m, null, null, "Self", "from:Hand;to:Exhaust", "CardCmd.Exhaust", 0.8)
            ]
        };
        SimulationCard lowUnplayable = low with { EnergyCost = 99 };
        DeckSimulationReport cullReport = new DeckMonteCarloSimulator().Simulate(
            [cull, high, lowUnplayable],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 3, BaseEnergy = 3, Seed = 1 });

        AssertEqual(20m, cullReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));
        AssertTrue(cullReport.PlayedCards.Any(card => card.TypeName == "HighValue"), nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));
        AssertTrue(!cullReport.PlayedCards.Any(card => card.TypeName == "LowValue"), nameof(DeckMonteCarloSimulatorMovesCardObjectsByValue));
    }

    private static void DeckMonteCarloSimulatorTransformsLowestValueCardObjects()
    {
        SimulationCard charge = MakeSimulationCard("Charge", value: 0m) with
        {
            SetupPriorityValue = 100m,
            Actions =
            [
                MakeAction("transformCard", 1m, null, null, "Self", "from:Hand;card:MinionDiveBomb", "CardCmd.TransformTo", 0.8)
            ]
        };
        SimulationCard trash = MakeSimulationCard("Trash", value: 1m);
        SimulationCard minionDiveBomb = MakeSimulationCard("MinionDiveBomb", value: 11m) with
        {
            ModelId = "CARD.MINION_DIVE_BOMB",
            EnergyCost = 0,
            DamageValue = 11m
        };
        DeckSimulationReport explicitTransformReport = new DeckMonteCarloSimulator().Simulate(
            [charge, trash],
            new DeckSimulationOptions
            {
                Runs = 1,
                Turns = 1,
                HandSize = 2,
                BaseEnergy = 3,
                Seed = 1,
                CardLibrary = [charge, trash, minionDiveBomb]
            });

        AssertEqual(11m, explicitTransformReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorTransformsLowestValueCardObjects));
        AssertTrue(explicitTransformReport.PlayedCards.Any(card => card.TypeName == "MinionDiveBomb"), nameof(DeckMonteCarloSimulatorTransformsLowestValueCardObjects));
        AssertTrue(!explicitTransformReport.PlayedCards.Any(card => card.TypeName == "Trash"), nameof(DeckMonteCarloSimulatorTransformsLowestValueCardObjects));

        SimulationCard randomTransform = MakeSimulationCard("RandomTransform", value: 0m) with
        {
            SetupPriorityValue = 100m,
            Actions =
            [
                MakeAction("transformCard", 1m, null, null, "Self", "from:Hand;card:SIM.TRANSFORMED_CARD", "CardCmd.Transform", 0.6)
            ]
        };
        DeckSimulationReport randomTransformReport = new DeckMonteCarloSimulator().Simulate(
            [randomTransform, trash],
            new DeckSimulationOptions { Runs = 1, Turns = 1, HandSize = 2, BaseEnergy = 3, Seed = 1 });

        AssertEqual(11m, randomTransformReport.TotalExpectedValue, nameof(DeckMonteCarloSimulatorTransformsLowestValueCardObjects));
        AssertTrue(randomTransformReport.PlayedCards.Any(card => card.TypeName == "SimTransformedCard"), nameof(DeckMonteCarloSimulatorTransformsLowestValueCardObjects));
        AssertTrue(!randomTransformReport.PlayedCards.Any(card => card.TypeName == "Trash"), nameof(DeckMonteCarloSimulatorTransformsLowestValueCardObjects));
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
                },
                new SimulationScenarioVariant
                {
                    Id = "replace_card",
                    Label = "Replace Card",
                    RemoveCards =
                    [
                        new SimulationDeckCardRemoval
                        {
                            MatchTypeName = "DiyReflect20_20",
                            Count = 1
                        }
                    ],
                    AddCards =
                    [
                        new SimulationDeckCardSpec
                        {
                            DisplayName = "Added Test Card",
                            Count = 1,
                            Patch = new SimulationCardPatch
                            {
                                ModelId = "DIY.ADDED_TEST_CARD",
                                TypeName = "AddedTestCard",
                                IntrinsicValue = 10m,
                                StaticEstimatedValue = 10m,
                                Cost = 0,
                                EnergyCost = 0
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
        AssertEqual(44m, report.Results[0].CardValueCredits.Single().DirectValue, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
        AssertEqual(3, report.Results.Count, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
        AssertEqual(1, report.Results[2].DeckSize, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
        AssertEqual(10m, report.Results[2].TotalExpectedValue, nameof(SimulationScenarioRunnerBuildsDiyCardsAndVariants));
    }

    private static void RunHistoryDeckExtractorReconstructsRegentA10FloorDeck()
    {
        string root = Path.Combine(Path.GetTempPath(), "cvo-run-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            string historyRoot = Path.Combine(root, "Steam", "userdata", "profile", "saves", "history");
            Directory.CreateDirectory(historyRoot);
            string runPath = Path.Combine(historyRoot, "1001.run");
            File.WriteAllText(runPath, """
            {
              "win": true,
              "ascension": 10,
              "start_time": 1001,
              "build_id": "test-build",
              "seed": "TESTSEED",
              "players": [
                { "character": "CHARACTER.REGENT" }
              ],
              "map_point_history": [
                [
                  {
                    "player_stats": [
                      {
                        "cards_removed": [ { "id": "CARD.STRIKE_REGENT" } ],
                        "cards_gained": [ { "id": "CARD.CHARGE" } ]
                      }
                    ]
                  },
                  {
                    "player_stats": [
                      {
                        "cards_transformed": [
                          {
                            "original_card": { "id": "CARD.DEFEND_REGENT" },
                            "final_card": { "id": "CARD.BULWARK", "current_upgrade_level": 1 }
                          }
                        ],
                        "upgraded_cards": [ { "id": "CARD.CHARGE", "current_upgrade_level": 1 } ]
                      }
                    ]
                  },
                  {
                    "player_stats": [
                      {
                        "cards_gained": [ { "id": "CARD.REFINE_BLADE" } ]
                      }
                    ]
                  }
                ]
              ]
            }
            """);

            string catalogPath = Path.Combine(root, "card_catalog.generated.json");
            File.WriteAllText(catalogPath, JsonSerializer.Serialize(new[]
            {
                MakeCatalogEntry("StrikeRegent", "CARD.STRIKE_REGENT"),
                MakeCatalogEntry("DefendRegent", "CARD.DEFEND_REGENT"),
                MakeCatalogEntry("FallingStar", "CARD.FALLING_STAR"),
                MakeCatalogEntry("Venerate", "CARD.VENERATE"),
                MakeCatalogEntry("AscendersBane", "CARD.ASCENDERS_BANE"),
                MakeCatalogEntry("Charge", "CARD.CHARGE"),
                MakeCatalogEntry("Bulwark", "CARD.BULWARK"),
                MakeCatalogEntry("RefineBlade", "CARD.REFINE_BLADE")
            }));

            RunHistoryDeckExtractionReport report = new RunHistoryDeckExtractor().Extract(new RunHistoryDeckExtractionOptions
            {
                HistoryRoot = root,
                CatalogPath = catalogPath,
                RunId = "1001",
                Floor = 3
            });
            RunHistoryDeckResult run = report.Runs.Single();
            AssertEqual(12, run.DeckCount, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertEqual(3, FindRunCard(run, "CARD.STRIKE_REGENT", 0).Count, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertEqual(3, FindRunCard(run, "CARD.DEFEND_REGENT", 0).Count, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertEqual(1, FindRunCard(run, "CARD.BULWARK", 1).Count, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertEqual("Bulwark", FindRunCard(run, "CARD.BULWARK", 1).TypeName, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertEqual(1, FindRunCard(run, "CARD.CHARGE", 1).Count, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertEqual(1, FindRunCard(run, "CARD.REFINE_BLADE", 0).Count, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertTrue(run.Events.SequenceEqual([
                "F1 remove CARD.STRIKE_REGENT",
                "F1 gain CARD.CHARGE",
                "F2 transform CARD.DEFEND_REGENT -> CARD.BULWARK",
                "F2 upgrade CARD.CHARGE",
                "F3 gain CARD.REFINE_BLADE"
            ]), nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));

            RunHistoryDeckExtractionReport beforeFloorReward = new RunHistoryDeckExtractor().Extract(new RunHistoryDeckExtractionOptions
            {
                HistoryRoot = root,
                CatalogPath = catalogPath,
                RunId = "1001",
                Floor = 3,
                IncludeFloorRewards = false
            });
            AssertEqual(11, beforeFloorReward.Runs.Single().DeckCount, nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
            AssertTrue(!beforeFloorReward.Runs.Single().Cards.Any(card => card.Id == "CARD.REFINE_BLADE"), nameof(RunHistoryDeckExtractorReconstructsRegentA10FloorDeck));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void SimulationDeckDefinitionBuilderUsesRunHistoryOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "cvo-deck-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string inputPath = Path.Combine(root, "run-history.json");
            RunHistoryDeckExtractionReport report = new()
            {
                GeneratedAt = DateTimeOffset.UnixEpoch,
                HistoryRoot = root,
                CatalogPath = "catalog.json",
                Character = "CHARACTER.REGENT",
                Ascension = 10,
                Floor = 5,
                IncludesFloorRewards = true,
                Runs =
                [
                    new RunHistoryDeckResult
                    {
                        RunId = "1001",
                        StartTime = 1001,
                        Build = "test-build",
                        Seed = "TESTSEED",
                        Path = Path.Combine(root, "1001.run"),
                        Character = "CHARACTER.REGENT",
                        Ascension = 10,
                        Floor = 5,
                        IncludesFloorRewards = true,
                        DeckCount = 1,
                        Cards =
                        [
                            new RunHistoryDeckCard
                            {
                                Count = 1,
                                Id = "CARD.REFINE_BLADE",
                                TypeName = "RefineBlade",
                                Upgrade = 1
                            }
                        ]
                    }
                ]
            };
            JsonSerializerOptions jsonOptions = new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            File.WriteAllText(inputPath, JsonSerializer.Serialize(report, jsonOptions));

            SimulationDeckDefinitionBuilder builder = new();
            SimulationDeckDefinition deck = builder.BuildFromFile(new SimulationDeckBuildOptions
            {
                Name = "regent_test_floor5",
                InputPath = inputPath,
                RunId = "1001",
                Description = "Test deck.",
                Assumptions = ["Manual test assumption."]
            });

            AssertEqual("regent_test_floor5", deck.Name, nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertEqual("Test deck.", deck.Description, nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            SimulationDeckCardSpec card = deck.Cards.Single();
            AssertEqual("CARD.REFINE_BLADE", card.ModelId, nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertEqual("RefineBlade", card.TypeName, nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertEqual(1, card.Upgrade, nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertTrue(deck.Assumptions.Any(assumption => assumption.Contains("Run history id: 1001.", StringComparison.Ordinal)), nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertTrue(deck.Assumptions.Any(assumption => assumption.Contains("after applying floor 5 rewards/events", StringComparison.Ordinal)), nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertTrue(deck.Assumptions.Contains("Manual test assumption."), nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));

            string outputPath = Path.Combine(root, "deck.json");
            builder.WriteToFile(deck, outputPath);
            string output = File.ReadAllText(outputPath);
            AssertTrue(!output.Contains("cloneTypeName", StringComparison.Ordinal), nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
            AssertTrue(output.Contains("\"upgrade\": 1", StringComparison.Ordinal), nameof(SimulationDeckDefinitionBuilderUsesRunHistoryOutput));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

    private static void AssertUpgradeOperation(
        CardFactCatalogEntry entry,
        string kind,
        string name,
        decimal? amount,
        string testName)
    {
        AssertTrue(
            entry.UpgradeOperations.Any(operation =>
                operation.Kind == kind
                && operation.Name == name
                && operation.Amount == amount
                && operation.Evidence.Line.HasValue),
            testName);
    }

    private static RunHistoryDeckCard FindRunCard(RunHistoryDeckResult run, string id, int upgrade)
    {
        return run.Cards.Single(card => card.Id == id && card.Upgrade == upgrade);
    }

    private static ModelCatalogEntry MakeCatalogEntry(string typeName, string modelId)
    {
        return new ModelCatalogEntry(
            "card",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Cards.{typeName}",
            modelId,
            "sts2.dll",
            "test",
            1.0);
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

    private static CardFactCatalogEntry MakeFactEntry(
        string typeName,
        int cost,
        string cardType,
        string targetType,
        IReadOnlyList<CardActionFact> actions,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<DynamicVarFact>? dynamicVars = null,
        IReadOnlyList<UpgradeOperationFact>? upgradeOperations = null,
        IReadOnlyList<CardRawOperation>? rawOperations = null,
        IReadOnlyList<string>? unresolved = null)
    {
        return new CardFactCatalogEntry(
            $"CARD.{typeName.ToUpperInvariant()}",
            typeName,
            $"MegaCrit.Sts2.Core.Models.Cards.{typeName}",
            cost,
            cardType,
            "Common",
            targetType,
            keywords ?? [],
            tags ?? [],
            dynamicVars ?? [],
            upgradeOperations ?? [],
            actions,
            rawOperations ?? [],
            unresolved ?? [],
            "test",
            0.9);
    }

    private static CardActionFact MakeAction(
        string kind,
        decimal? amount,
        string? dynamicVarName,
        int? hitCount,
        string? targetType,
        string? parameter,
        string source,
        double confidence)
    {
        return new CardActionFact(
            kind,
            amount,
            dynamicVarName,
            hitCount,
            targetType,
            parameter,
            source,
            new SourceEvidence("test", null, 1, kind, confidence),
            confidence);
    }

    private static UpgradeOperationFact MakeUpgradeOperation(
        string kind,
        string name,
        decimal? amount,
        string? parameter = null,
        string? condition = null)
    {
        return new UpgradeOperationFact(
            kind,
            name,
            amount,
            parameter,
            condition,
            new SourceEvidence("test", null, 1, kind, 0.9));
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
                ["enemyCountAssumption"] = 2.25m,
                ["aoeDamageMultiplier"] = 1.3m
            }
        };
    }
}
