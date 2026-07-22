using CardValueOverlay.Modeling.Combat;
using CardValueOverlay.Modeling.Combat.Portfolio;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Tests;

internal static class CombatPhase1TestSuite
{
    public static void RunAll()
    {
        TwelveCellPriorContractsValidate();
        DamageUsesActualEnemyHpAndJournalRestoresState();
        BlockAndMultiHitUsePhysicalResolution();
        StatusModifiersAndEnemyHealingUsePhysicalResolution();
        CardCompilerUsesRawOrderedActionsOnly();
        StarResourcesUsePhysicalLegalityAndRoundTrip();
        StarNextTurnResolvesAfterEnergyReset();
        DelayedResourcesResolveAtNextTurnBoundary();
        HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible();
        InnateInitialDrawIsForcedAndNormalized();
        CardInstanceIdentityAndGroupedChanceRoundTrip();
        ForgeCreatesAndMutatesSovereignBladeInstances();
        VisibleSelectMoveContinuationsAreExplicitAndReversible();
        TypedPersistentPowersUsePhysicalEventsAndRoundTrip();
        OrbitPowerInstancesKeepIndependentProgress();
        MonsterParserPreservesSourceEffectOrder();
        MonsterCompilerUsesA10AndRejectsUnknownBranchProbability();
        EncounterOverridesRequireSourcedRealizations();
        HpContinuationIsMonotoneWithSteeperLowHpMarginal();
        HpContinuationTelescopesAndDeathDominates();
        DrawProbabilitiesAreExactAndNormalized();
        InformationStateSolverDoesNotObserveUnknownOrder();
        InformationStateSolverCanUseKnownTopInformation();
        InformationStateSolverComputesMaxOfExpectedOutcomes();
        MemoizationAndBudgetContractsHold();
        CombatSimulationRunnerReturnsPhysicalMetrics();
        IndependentHorizonBatchMatchesSingleRuns();
        SemanticStreamsAreIndependentAndDeterministic();
        BaselineCacheKeyCoversSemanticInputs();
        ToyDeckDeltaHasZeroPositiveAndNegativeCases();
        TenThousandApplyUndoTransitionsPreserveIntegrity();
        TwoHundredRunIntegrityRegression();
    }

    private static void TwelveCellPriorContractsValidate()
    {
        HpContinuationCatalog hp = HpContinuationCatalog.Load("data/manual-tags/hp_continuation_calibration.json");
        LoadedCombatPortfolio portfolio = CombatPortfolioLoader.Load(
            "data/manual-tags/combat_value_portfolios.json",
            hp,
            "phase1-research-balanced-v1");
        Equal(12, hp.Calibration.Contexts.Count, nameof(TwelveCellPriorContractsValidate));
        Equal(12, portfolio.Definition.Cells.Count, nameof(TwelveCellPriorContractsValidate));
        Equal(0, hp.Get("act1-weak").LossBudget, nameof(TwelveCellPriorContractsValidate));
        Equal(40, hp.Get("act3-boss").LossBudget, nameof(TwelveCellPriorContractsValidate));
        True(portfolio.Warnings.All(warning => warning.Contains("prior", StringComparison.OrdinalIgnoreCase)), nameof(TwelveCellPriorContractsValidate));
    }

    private static void DamageUsesActualEnemyHpAndJournalRestoresState()
    {
        CombatInformationState state = State(enemyHp: 20, playerHp: 20);
        CombatMutationJournal journal = new();
        journal.SetScalar(state, CombatScalarField.MonsterBlock, 6, 0);
        int mark = journal.Mark();
        string before = state.Encode();
        CombatDamageResult result = new CombatDamageResolver().ResolvePlayerAttack(state, 0, 10, 1, journal);
        Equal(4, result.HpLost, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
        Equal(4d, result.OffenseReward, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
        Equal(16, state.Monsters[0].Hp, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
        journal.UndoTo(state, mark);
        Equal(before, state.Encode(), nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
        Equal(0d, state.Ledger.ActualEnemyHpDamage, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));

        state = State(enemyHp: 4, playerHp: 20);
        journal = new CombatMutationJournal();
        result = new CombatDamageResolver().ResolvePlayerAttack(state, 0, 10, 1, journal);
        Equal(4, result.HpLost, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
        Equal(6, result.Overkill, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
        result = new CombatDamageResolver().ResolvePlayerAttack(state, 0, 10, 1, journal);
        Equal(0, result.HpLost, nameof(DamageUsesActualEnemyHpAndJournalRestoresState));
    }

    private static void BlockAndMultiHitUsePhysicalResolution()
    {
        CombatInformationState state = State(enemyHp: 20, playerHp: 20);
        CombatMutationJournal journal = new();
        journal.SetScalar(state, CombatScalarField.PlayerBlock, 3);
        CombatDamageResult result = new CombatDamageResolver().ResolveMonsterAttack(state, 0, 10, 1, journal);
        Equal(13, state.Player.Hp, nameof(BlockAndMultiHitUsePhysicalResolution));
        Equal(7, result.HpLost, nameof(BlockAndMultiHitUsePhysicalResolution));

        state = State(enemyHp: 20, playerHp: 20);
        journal = new CombatMutationJournal();
        journal.SetScalar(state, CombatScalarField.PlayerBlock, 12);
        result = new CombatDamageResolver().ResolveMonsterAttack(state, 0, 5, 2, journal);
        Equal(20, state.Player.Hp, nameof(BlockAndMultiHitUsePhysicalResolution));
        Equal(2, state.Player.Block, nameof(BlockAndMultiHitUsePhysicalResolution));
        Equal(0, result.HpLost, nameof(BlockAndMultiHitUsePhysicalResolution));
    }

    private static void StatusModifiersAndEnemyHealingUsePhysicalResolution()
    {
        CombatInformationState state = State(enemyHp: 20, playerHp: 20);
        CombatMutationJournal journal = new();
        journal.SetScalar(state, CombatScalarField.PlayerWeak, 1);
        journal.SetScalar(state, CombatScalarField.MonsterVulnerable, 1, 0);
        CombatDamageResult result = new CombatDamageResolver().ResolvePlayerAttack(state, 0, 8, 1, journal);
        Equal(9, result.HpLost, nameof(StatusModifiersAndEnemyHealingUsePhysicalResolution));

        state = State(enemyHp: 20, playerHp: 20);
        journal = new CombatMutationJournal();
        journal.SetScalar(state, CombatScalarField.PlayerDexterity, 2);
        journal.SetScalar(state, CombatScalarField.PlayerFrail, 1);
        Equal(7, new CombatDamageResolver().GainPlayerBlock(state, 8, journal), nameof(StatusModifiersAndEnemyHealingUsePhysicalResolution));

        state = State(enemyHp: 20, playerHp: 20);
        journal = new CombatMutationJournal();
        CombatDamageResolver resolver = new();
        resolver.ResolvePlayerAttack(state, 0, 10, 1, journal);
        Equal(4, resolver.HealMonster(state, 0, 4, journal), nameof(StatusModifiersAndEnemyHealingUsePhysicalResolution));
        Equal(6d, state.Ledger.NetOffenseValue, nameof(StatusModifiersAndEnemyHealingUsePhysicalResolution));
        Equal(14, state.Monsters[0].Hp, nameof(StatusModifiersAndEnemyHealingUsePhysicalResolution));
    }

    private static void CardCompilerUsesRawOrderedActionsOnly()
    {
        SourceEvidence evidence = new("test.cs", "OnPlay", 1, "fixture", 1d);
        CardForm form = new(
            "CARD.TEST",
            "TestCard",
            "Tests.TestCard",
            0,
            1,
            "Attack",
            "Common",
            "AnyEnemy",
            [],
            [],
            [
                new CardActionFact("block", 5, null, null, "Self", null, "first", evidence, 1d),
                new CardActionFact("damage", 7, null, 2, "AnyEnemy", null, "second", evidence, 1d)
            ],
            [],
            "fixture",
            1d);
        CombatCardDefinition card = new CombatCardCompiler().CompileForm(form, 11);
        True(card.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.Block, card.Effects[0].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.Damage, card.Effects[1].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(2, card.Effects[1].HitCount, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition aoe = new CombatCardCompiler().CompileForm(
            form with
            {
                TargetType = "AllEnemies",
                Actions = [new CardActionFact("damage", 7, null, 1, "AllEnemies", null, "aoe", evidence, 1d)]
            },
            13);
        Equal(CombatCardTarget.AllEnemies, aoe.Effects.Single().Target, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition unsupported = new CombatCardCompiler().CompileForm(
            form with
            {
                Actions = [new CardActionFact("createCard", 1, null, null, "Self", null, "unknown", evidence, 1d)]
            },
            12);
        True(!unsupported.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition starCard = new CombatCardCompiler().CompileForm(
            form with
            {
                Actions =
                [
                    new CardActionFact("starCost", 2, null, null, "Self", null, "cost", evidence, 1d),
                    new CardActionFact("starGain", 3, null, null, "Self", null, "gain", evidence, 1d),
                    new CardActionFact("starNextTurn", 4, null, null, "Self", null, "next", evidence, 1d)
                ]
            },
            14);
        True(starCard.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(2, starCard.StarCost, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.GainStars, starCard.Effects[0].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.GainStarsNextTurn, starCard.Effects[1].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition delayedCard = new CombatCardCompiler().CompileForm(
            form with
            {
                Actions =
                [
                    new CardActionFact("drawNextTurn", 2, null, null, "Self", null, "draw-next", evidence, 1d),
                    new CardActionFact("energyNextTurn", 3, null, null, "Self", null, "energy-next", evidence, 1d),
                    new CardActionFact("blockNextTurn", 4, null, null, "Self", "power:BlockNextTurn", "block-next", evidence, 1d)
                ]
            },
            15);
        True(delayedCard.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.DrawNextTurn, delayedCard.Effects[0].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.GainEnergyNextTurn, delayedCard.Effects[1].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.GainBlockNextTurn, delayedCard.Effects[2].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition lifecycleCard = new CombatCardCompiler().CompileForm(
            form with
            {
                Keywords = ["Retain", "Ethereal", "Innate"],
                Actions = [new CardActionFact("power", 1, null, null, "Self", "power:RetainHand", "retain-hand", evidence, 1d)]
            },
            16);
        True(lifecycleCard.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        True(lifecycleCard.Retains, nameof(CardCompilerUsesRawOrderedActionsOnly));
        True(lifecycleCard.Ethereal, nameof(CardCompilerUsesRawOrderedActionsOnly));
        True(lifecycleCard.Innate, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.GainRetainHand, lifecycleCard.Effects.Single().Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition unknownKeyword = new CombatCardCompiler().CompileForm(
            form with { Keywords = ["FutureSemanticKeyword"] },
            17);
        True(!unknownKeyword.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        True(
            unknownKeyword.UnsupportedReasons.Any(reason => reason.Contains("FutureSemanticKeyword", StringComparison.Ordinal)),
            nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition childOfTheStars = new CombatCardCompiler().CompileForm(
            form with
            {
                CardType = "Power",
                Actions =
                [
                    new CardActionFact(
                        "persistentPowerTrigger", 2, "BlockForStars", null, "Self",
                        "AfterStarsSpent:gainBlockPerStarSpent", "trigger", evidence, 1d),
                    new CardActionFact(
                        "power", 2, "BlockForStars", null, "Self",
                        "power:ChildOfTheStars;var:BlockForStars", "install", evidence, 1d)
                ]
            },
            18);
        True(childOfTheStars.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.InstallChildOfTheStars, childOfTheStars.Effects.Single().Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));

        (string PowerName, CombatCardEffectKind Effect)[] typedPowers =
        [
            ("Orbit", CombatCardEffectKind.InstallOrbit),
            ("PaleBlueDot", CombatCardEffectKind.InstallPaleBlueDot),
            ("Fasten", CombatCardEffectKind.InstallFasten)
        ];
        foreach ((string powerName, CombatCardEffectKind expectedEffect) in typedPowers)
        {
            CombatCardDefinition power = new CombatCardCompiler().CompileForm(
                form with
                {
                    CardType = "Power",
                    Tags = powerName == "Fasten" ? ["Defend"] : [],
                    Actions =
                    [
                        new CardActionFact(
                            "power", 1, null, null, "Self",
                            $"power:{powerName};var:Amount", "install", evidence, 1d)
                    ]
                },
                19);
            True(power.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
            Equal(expectedEffect, power.Effects.Single().Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
            Equal(powerName == "Fasten", power.DefendTagged, nameof(CardCompilerUsesRawOrderedActionsOnly));
        }

        CombatCardDefinition forge = new CombatCardCompiler().CompileForm(
            form with
            {
                Actions =
                [new CardActionFact("forge", 5, "Forge", null, "Self", null, "forge", evidence, 1d)]
            },
            20);
        True(forge.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.Forge, forge.Effects.Single().Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal("Forge", forge.Effects.Single().DynamicVarName!, nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition dynamicForge = new CombatCardCompiler().CompileForm(
            form with
            {
                Actions =
                [new CardActionFact(
                    "forge", 0, null, null, "AnyEnemy", "calculatedForge", "dynamic-forge", evidence, 0.45)]
            },
            21);
        True(!dynamicForge.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        True(
            dynamicForge.UnsupportedReasons.Any(reason => reason.Contains("dynamic semantics", StringComparison.Ordinal)),
            nameof(CardCompilerUsesRawOrderedActionsOnly));

        CombatCardDefinition selectMove = new CombatCardCompiler().CompileForm(
            form with
            {
                CardType = "Skill",
                TargetType = "Self",
                Actions =
                [
                    new CardActionFact("draw", 3, "Cards", null, "Self", null, "draw", evidence, 1d),
                    new CardActionFact("selectCards", 1, "PutBack", null, "Self", "from:Hand", "select", evidence, 1d),
                    new CardActionFact(
                        "moveCardBetweenPiles", 1, "PutBack", null, "Self",
                        "from:Hand;to:Draw;position:Top", "move", evidence, 1d)
                ]
            },
            22);
        True(selectMove.IsSupported, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(2, selectMove.Effects.Count, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatCardEffectKind.SelectAndMoveCard, selectMove.Effects[1].Kind, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatPile.Hand, selectMove.Effects[1].CardSelection!.SourcePile, nameof(CardCompilerUsesRawOrderedActionsOnly));
        Equal(CombatPile.KnownTop, selectMove.Effects[1].CardSelection!.DestinationPile, nameof(CardCompilerUsesRawOrderedActionsOnly));
    }

    private static void StarResourcesUsePhysicalLegalityAndRoundTrip()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(
                0, "CARD.VENERATE", "Venerate", 0, "Skill", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.GainStars, 2, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, []),
            new CombatCardDefinition(
                1, "CARD.FALLING_STAR", "FallingStar", 0, "Attack", 0, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 8, 1, CombatCardTarget.Enemy, "fixture", 0)],
                false, true, true, [], StarCost: 2)
        ]);
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = IdleMonsters(50);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 2 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0, 1],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0, 1]);
        CombatTransitionKernel kernel = new(cards, monsters, options);
        CombatMutationJournal journal = new();
        string root = state.Encode();
        CombatStateKey rootKey = state.GetCanonicalKey();

        True(
            !kernel.GetLegalActions(state).Any(action => action.CardDefinitionId == 1),
            nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
        int rootMark = journal.Mark();
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        Equal(2, state.Stars, nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
        True(rootKey != state.GetCanonicalKey(), nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
        True(
            kernel.GetLegalActions(state).Any(action => action.CardDefinitionId == 1 && action.TargetMonsterIndex == 0),
            nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));

        int attackMark = journal.Mark();
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 1, 0), journal);
        Equal(0, state.Stars, nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
        Equal(42, state.Monsters[0].Hp, nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
        journal.UndoTo(state, attackMark);
        Equal(2, state.Stars, nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
        journal.UndoTo(state, rootMark);
        Equal(root, state.Encode(), nameof(StarResourcesUsePhysicalLegalityAndRoundTrip));
    }

    private static void StarNextTurnResolvesAfterEnergyReset()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(
                0, "CARD.HIDDEN_CACHE", "HiddenCache", 0, "Skill", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.GainStarsNextTurn, 3, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [])
        ]);
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = IdleMonsters(50);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 1, InitialStars = 1 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0]);
        CombatTransitionKernel kernel = new(cards, monsters, options);
        CombatMutationJournal journal = new();
        string root = state.Encode();
        int mark = journal.Mark();

        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        Equal(1, state.Stars, nameof(StarNextTurnResolvesAfterEnergyReset));
        Equal(3, state.StarsNextTurn, nameof(StarNextTurnResolvesAfterEnergyReset));
        kernel.Prepare(state, CombatAction.EndTurn, journal);
        Equal(4, state.Stars, nameof(StarNextTurnResolvesAfterEnergyReset));
        Equal(0, state.StarsNextTurn, nameof(StarNextTurnResolvesAfterEnergyReset));
        Equal(2, state.Turn, nameof(StarNextTurnResolvesAfterEnergyReset));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(StarNextTurnResolvesAfterEnergyReset));
    }

    private static void DelayedResourcesResolveAtNextTurnBoundary()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(
                0, "CARD.DELAYED", "Delayed", 0, "Skill", 1, CombatCardTarget.Self,
                [
                    new CombatCardEffect(CombatCardEffectKind.DrawNextTurn, 2, 1, CombatCardTarget.Self, "fixture", 0),
                    new CombatCardEffect(CombatCardEffectKind.GainEnergyNextTurn, 2, 1, CombatCardTarget.Self, "fixture", 1),
                    new CombatCardEffect(CombatCardEffectKind.GainBlockNextTurn, 8, 1, CombatCardTarget.Self, "fixture", 2)
                ],
                false, true, true, [])
        ]);
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = IdleMonsters(50);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 1 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0]);
        CombatMutationJournal journal = new();
        journal.SetScalar(state, CombatScalarField.PlayerDexterity, 2);
        journal.SetScalar(state, CombatScalarField.PlayerFrail, 1);
        CombatTransitionKernel kernel = new(cards, monsters, options);
        string root = state.Encode();
        int mark = journal.Mark();

        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        Equal(2, state.DrawNextTurn, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(2, state.EnergyNextTurn, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(7, state.BlockNextTurn, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        CombatPreparedTransition nextTurn = kernel.Prepare(state, CombatAction.EndTurn, journal);
        Equal(3, nextTurn.DrawCount, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(5, state.Energy, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(7, state.Player.Block, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(0, state.DrawNextTurn, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(0, state.EnergyNextTurn, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        Equal(0, state.BlockNextTurn, nameof(DelayedResourcesResolveAtNextTurnBoundary));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(DelayedResourcesResolveAtNextTurnBoundary));
    }

    private static void HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(
                0, "CARD.RETAIN", "Retain", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [], Retains: true),
            new CombatCardDefinition(
                1, "CARD.ETHEREAL_RETAIN", "EtherealRetain", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [], Retains: true, Ethereal: true),
            new CombatCardDefinition(
                2, "CARD.ORDINARY", "Ordinary", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, []),
            new CombatCardDefinition(
                3, "CARD.CONVERGENCE", "Convergence", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.GainRetainHand, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [])
        ]);
        CombatSimulationOptions options = new() { HorizonTurns = 3, HandSize = 2 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0, 1, 2, 3],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0, 1, 2, 3]);
        CombatTransitionKernel kernel = new(cards, IdleMonsters(50), options);
        CombatMutationJournal journal = new();
        string root = state.Encode();
        int mark = journal.Mark();

        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 3), journal);
        Equal(1, state.RetainHandTurns, nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        CombatPreparedTransition firstNextTurn = kernel.Prepare(state, CombatAction.EndTurn, journal);
        Equal(2, firstNextTurn.DrawCount, nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        Equal(0, state.RetainHandTurns, nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        True(state.Hand.Any(instanceId => state.GetDefinitionId(instanceId) == 0), nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        True(state.Hand.Any(instanceId => state.GetDefinitionId(instanceId) == 2), nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        True(state.Exhaust.Any(instanceId => state.GetDefinitionId(instanceId) == 1), nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));

        kernel.Prepare(state, CombatAction.EndTurn, journal);
        Equal(1, state.Hand.Count, nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        Equal(0, state.GetDefinitionId(state.Hand.Single()), nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        True(state.Discard.Any(instanceId => state.GetDefinitionId(instanceId) == 2), nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(HandLifecycleKeywordsAndRetainHandArePhysicalAndReversible));
    }

    private static void InnateInitialDrawIsForcedAndNormalized()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(0, "CARD.INNATE", "Innate", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [], Innate: true),
            new CombatCardDefinition(1, "CARD.ONE", "One", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, []),
            new CombatCardDefinition(2, "CARD.TWO", "Two", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [])
        ]);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 2 };
        CombatChanceResolver chance = new();
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0, 1, 2],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options);
        string root = state.Encode();
        IReadOnlyList<CombatDrawOutcome> outcomes = chance.EnumerateInitialDrawOutcomes(state, cards, 2, 10);
        Equal(2, outcomes.Count, nameof(InnateInitialDrawIsForcedAndNormalized));
        True(outcomes.All(outcome => outcome.DrawnCards.Any(instanceId => state.GetDefinitionId(instanceId) == 0)), nameof(InnateInitialDrawIsForcedAndNormalized));
        True(outcomes.All(outcome => outcome.DrawnCards.Count == 2), nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(1d, outcomes.Sum(outcome => outcome.Probability), nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(root, state.Encode(), nameof(InnateInitialDrawIsForcedAndNormalized));

        state = CombatStateFactory.Create(
            80,
            80,
            Enumerable.Repeat(0, 11).Append(1),
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options);
        outcomes = chance.EnumerateInitialDrawOutcomes(state, cards, 2, 10);
        Equal(1, outcomes.Count, nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(10, outcomes.Single().DrawnCards.Count, nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(10, outcomes.Single().DrawnCards.Count(instanceId => state.GetDefinitionId(instanceId) == 0), nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(1, outcomes.Single().RemainingUnknownCards.Count(instanceId => state.GetDefinitionId(instanceId) == 0), nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(1, outcomes.Single().RemainingUnknownCards.Count(instanceId => state.GetDefinitionId(instanceId) == 1), nameof(InnateInitialDrawIsForcedAndNormalized));
        Equal(1d, outcomes.Single().Probability, nameof(InnateInitialDrawIsForcedAndNormalized));
    }

    private static void CardInstanceIdentityAndGroupedChanceRoundTrip()
    {
        CombatSimulationOptions options = new() { HorizonTurns = 1, HandSize = 1 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0, 0, 1],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options);
        state.ValidateIntegrity();
        Equal(3, state.CardInstances.Count, nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));
        Equal(3, state.UnknownDraw.Distinct().Count(), nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));

        CombatInformationState alphaEquivalent = CombatStateFactory.Create(
            80,
            80,
            [0, 1, 0],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options);
        Equal(state.GetCanonicalKey(), alphaEquivalent.GetCanonicalKey(), nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));

        CombatChanceResolver chance = new();
        IReadOnlyList<CombatDrawOutcome> grouped = chance.EnumerateDrawOutcomes(state, 1, 10);
        Equal(2, grouped.Count, nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));
        CombatDrawOutcome duplicateGroup = grouped.Single(outcome =>
            state.GetDefinitionId(outcome.DrawnCards.Single()) == 0);
        Equal(2d / 3d, duplicateGroup.Probability, nameof(CardInstanceIdentityAndGroupedChanceRoundTrip), 1e-12);

        CombatMutationJournal journal = new();
        string root = state.Encode();
        int mark = journal.Mark();
        int forgedCopy = state.UnknownDraw.First(instanceId => state.GetDefinitionId(instanceId) == 0);
        journal.SetCardInstance(state, forgedCopy, 0, 4);
        Equal(3, chance.EnumerateDrawOutcomes(state, 1, 10).Count, nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));

        CombatChanceOutcome applied = new(
            duplicateGroup.Probability,
            duplicateGroup,
            [],
            duplicateGroup.StableKey);
        mark = journal.Mark();
        chance.Apply(state, applied, journal);
        state.ValidateIntegrity();
        Equal(1, state.Hand.Count, nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));
        Equal(2, state.UnknownDraw.Count, nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(CardInstanceIdentityAndGroupedChanceRoundTrip));
    }

    private static void ForgeCreatesAndMutatesSovereignBladeInstances()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(
                0, "CARD.SOVEREIGN_BLADE", "SovereignBlade", 0, "Attack", 0, CombatCardTarget.Enemy,
                [new CombatCardEffect(
                    CombatCardEffectKind.Damage,
                    10,
                    1,
                    CombatCardTarget.Enemy,
                    "fixture",
                    0,
                    "Damage")],
                false, true, true, [], Retains: true),
            new CombatCardDefinition(
                1, "CARD.FORGE_FIVE", "ForgeFive", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Forge, 5, 1, CombatCardTarget.Self, "fixture", 0, "Forge")],
                false, true, true, []),
            new CombatCardDefinition(
                2, "CARD.FORGE_THREE", "ForgeThree", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Forge, 3, 1, CombatCardTarget.Self, "fixture", 0, "Forge")],
                false, true, true, [])
        ]);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 2 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [1, 2],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [1, 2]);
        CombatTransitionKernel kernel = new(cards, IdleMonsters(50), options);
        CombatMutationJournal journal = new();
        string root = state.Encode();
        int rootMark = journal.Mark();

        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 1), journal);
        int firstBladeId = state.Hand.Single(instanceId => state.GetDefinitionId(instanceId) == 0);
        Equal(5, state.GetCardInstance(firstBladeId).ForgeDamageBonus, nameof(ForgeCreatesAndMutatesSovereignBladeInstances));

        int firstBladePosition = state.Hand.ToList().IndexOf(firstBladeId);
        journal.RemovePileAt(state, CombatPile.Hand, firstBladePosition);
        journal.AddPile(state, CombatPile.Exhaust, firstBladeId);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 2), journal);

        int secondBladeId = state.Hand.Single(instanceId => state.GetDefinitionId(instanceId) == 0);
        True(secondBladeId != firstBladeId, nameof(ForgeCreatesAndMutatesSovereignBladeInstances));
        Equal(8, state.GetCardInstance(firstBladeId).ForgeDamageBonus, nameof(ForgeCreatesAndMutatesSovereignBladeInstances));
        Equal(3, state.GetCardInstance(secondBladeId).ForgeDamageBonus, nameof(ForgeCreatesAndMutatesSovereignBladeInstances));
        state.ValidateIntegrity();

        kernel.Prepare(
            state,
            new CombatAction(CombatActionKind.PlayCard, 0, 0, CardForgeDamageBonus: 3),
            journal);
        Equal(37, state.Monsters[0].Hp, nameof(ForgeCreatesAndMutatesSovereignBladeInstances));
        Equal(13d, state.Ledger.ActualEnemyHpDamage, nameof(ForgeCreatesAndMutatesSovereignBladeInstances));

        journal.UndoTo(state, rootMark);
        state.ValidateIntegrity();
        Equal(root, state.Encode(), nameof(ForgeCreatesAndMutatesSovereignBladeInstances));
    }

    private static void VisibleSelectMoveContinuationsAreExplicitAndReversible()
    {
        CombatCardSelectionSpec discardToTop = new(
            CombatPile.Discard,
            CombatPile.KnownTop,
            CombatCardInsertionPosition.Top,
            1);
        CombatCardSelectionSpec handToTop = new(
            CombatPile.Hand,
            CombatPile.KnownTop,
            CombatCardInsertionPosition.Top,
            1);
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(
                0, "CARD.COSMIC_INDIFFERENCE", "CosmicIndifference", 0, "Skill", 1, CombatCardTarget.Self,
                [
                    new CombatCardEffect(CombatCardEffectKind.Block, 6, 1, CombatCardTarget.Self, "fixture", 0),
                    new CombatCardEffect(
                        CombatCardEffectKind.SelectAndMoveCard, 1, 1, CombatCardTarget.Self,
                        "fixture", 1, CardSelection: discardToTop)
                ],
                false, true, true, []),
            new CombatCardDefinition(
                1, "CARD.GLIMMER", "Glimmer", 0, "Skill", 1, CombatCardTarget.Self,
                [
                    new CombatCardEffect(CombatCardEffectKind.Draw, 2, 1, CombatCardTarget.Self, "fixture", 0),
                    new CombatCardEffect(
                        CombatCardEffectKind.SelectAndMoveCard, 1, 1, CombatCardTarget.Self,
                        "fixture", 1, CardSelection: handToTop)
                ],
                false, true, true, []),
            new CombatCardDefinition(
                2, "CARD.ATTACK", "Attack", 0, "Attack", 1, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 7, 1, CombatCardTarget.Enemy, "fixture", 0)],
                false, true, true, []),
            new CombatCardDefinition(
                3, "CARD.DEFEND", "Defend", 0, "Skill", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 5, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [])
        ]);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 3 };
        CombatTransitionKernel kernel = new(cards, IdleMonsters(50), options);

        CombatInformationState cosmic = CombatStateFactory.Create(
            80,
            80,
            [0, 2],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0]);
        int discardedAttack = cosmic.UnknownDraw.Single();
        CombatMutationJournal setup = new();
        setup.RemovePileAt(cosmic, CombatPile.UnknownDraw, 0);
        setup.AddPile(cosmic, CombatPile.Discard, discardedAttack);
        cosmic.ValidateIntegrity();

        CombatMutationJournal journal = new();
        string cosmicRoot = cosmic.Encode();
        int cosmicMark = journal.Mark();
        kernel.Prepare(cosmic, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        True(cosmic.PendingCardSelection is not null, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        Equal(1, cosmic.Play.Count, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        Equal(6, cosmic.Player.Block, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        IReadOnlyList<CombatAction> cosmicChoices = kernel.GetLegalActions(cosmic);
        Equal(1, cosmicChoices.Count, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        Equal(CombatActionKind.ChooseCard, cosmicChoices.Single().Kind, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));

        kernel.Prepare(cosmic, cosmicChoices.Single(), journal);
        True(cosmic.PendingCardSelection is null, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        Equal(discardedAttack, cosmic.KnownTop.Single(), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        Equal(0, cosmic.Play.Count, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        True(cosmic.Discard.Any(instanceId => cosmic.GetDefinitionId(instanceId) == 0), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        cosmic.ValidateIntegrity();
        journal.UndoTo(cosmic, cosmicMark);
        Equal(cosmicRoot, cosmic.Encode(), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));

        CombatInformationState glimmer = CombatStateFactory.Create(
            80,
            80,
            [1, 2, 3],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [1]);
        journal = new CombatMutationJournal();
        string glimmerRoot = glimmer.Encode();
        int glimmerMark = journal.Mark();
        CombatPreparedTransition prepared = kernel.Prepare(
            glimmer,
            new CombatAction(CombatActionKind.PlayCard, 1),
            journal);
        Equal(2, prepared.DrawCount, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        True(glimmer.PendingCardSelection is not null, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));

        CombatChanceResolver chance = new();
        CombatDrawOutcome draw = chance.EnumerateDrawOutcomes(glimmer, prepared.DrawCount, options.MaxHandSize).Single();
        CombatChanceOutcome drawOutcome = new(draw.Probability, draw, [], draw.StableKey);
        chance.Apply(glimmer, drawOutcome, journal);
        IReadOnlyList<CombatAction> glimmerChoices = kernel.GetLegalActions(glimmer);
        Equal(2, glimmerChoices.Count, nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        True(glimmerChoices.All(action => action.Kind == CombatActionKind.ChooseCard), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));

        CombatAction putAttackBack = glimmerChoices.Single(action => action.CardDefinitionId == 2);
        kernel.Prepare(glimmer, putAttackBack, journal);
        Equal(2, glimmer.GetDefinitionId(glimmer.KnownTop.Single()), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        True(glimmer.Discard.Any(instanceId => glimmer.GetDefinitionId(instanceId) == 1), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
        glimmer.ValidateIntegrity();
        journal.UndoTo(glimmer, glimmerMark);
        Equal(glimmerRoot, glimmer.Encode(), nameof(VisibleSelectMoveContinuationsAreExplicitAndReversible));
    }

    private static void TypedPersistentPowersUsePhysicalEventsAndRoundTrip()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(0, "CARD.CHILD", "Child", 0, "Power", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.InstallChildOfTheStars, 2, 1, CombatCardTarget.Self, "fixture", 0)],
                true, true, true, []),
            new CombatCardDefinition(1, "CARD.ORBIT", "Orbit", 0, "Power", 2, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.InstallOrbit, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                true, true, true, []),
            new CombatCardDefinition(2, "CARD.FASTEN", "Fasten", 0, "Power", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.InstallFasten, 4, 1, CombatCardTarget.Self, "fixture", 0)],
                true, true, true, []),
            new CombatCardDefinition(3, "CARD.PALE", "Pale", 0, "Power", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.InstallPaleBlueDot, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                true, true, true, []),
            new CombatCardDefinition(4, "CARD.DEFEND", "Defend", 0, "Skill", 1, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 5, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [], DefendTagged: true),
            new CombatCardDefinition(5, "CARD.SPENDER", "Spender", 0, "Attack", 1, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 1, 1, CombatCardTarget.Enemy, "fixture", 0)],
                false, true, true, [], StarCost: 2)
        ]);
        CombatSimulationOptions options = new()
        {
            HorizonTurns = 2,
            HandSize = 2,
            EnergyPerTurn = 20,
            InitialStars = 4
        };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0, 1, 2, 3, 4, 5],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0, 1, 2, 3, 4, 5]);
        CombatTransitionKernel kernel = new(cards, IdleMonsters(50), options);
        CombatMutationJournal journal = new();
        string root = state.Encode();
        int mark = journal.Mark();

        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 1), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 2), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 3), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 4), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 5, 0), journal);

        Equal(2, state.ChildOfTheStarsBlockPerStar, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(4, state.FastenDefendBlock, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(1, state.PaleBlueDotDraw, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(1, state.OrbitPowers.Count, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(0, state.OrbitPowers.Single().EnergyProgress, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(14, state.Energy, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(2, state.Stars, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(13, state.Player.Block, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(49, state.Monsters[0].Hp, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));

        CombatPreparedTransition nextTurn = kernel.Prepare(state, CombatAction.EndTurn, journal);
        Equal(6, state.CardsPlayedPreviousTurn, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        Equal(3, nextTurn.DrawCount, nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(TypedPersistentPowersUsePhysicalEventsAndRoundTrip));
    }

    private static void OrbitPowerInstancesKeepIndependentProgress()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(0, "CARD.ORBIT", "Orbit", 0, "Power", 2, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.InstallOrbit, 1, 1, CombatCardTarget.Self, "fixture", 0)],
                true, true, true, []),
            new CombatCardDefinition(1, "CARD.SPEND_THREE", "SpendThree", 0, "Skill", 3, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Block, 0, 1, CombatCardTarget.Self, "fixture", 0)],
                false, true, true, [])
        ]);
        CombatSimulationOptions options = new() { HorizonTurns = 2, HandSize = 2, EnergyPerTurn = 20 };
        CombatInformationState state = CombatStateFactory.Create(
            80,
            80,
            [0, 1, 0, 1],
            [new CombatMonsterSeed("dummy-1", "Dummy", 50, "IDLE")],
            options,
            initialHand: [0, 1, 0, 1]);
        CombatTransitionKernel kernel = new(cards, IdleMonsters(50), options);
        CombatMutationJournal journal = new();
        string root = state.Encode();
        int mark = journal.Mark();

        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 1), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 0), journal);
        kernel.Prepare(state, new CombatAction(CombatActionKind.PlayCard, 1), journal);

        Equal(2, state.OrbitPowers.Count, nameof(OrbitPowerInstancesKeepIndependentProgress));
        True(
            state.OrbitPowers.Select(power => power.EnergyProgress).Order().SequenceEqual([0, 3]),
            nameof(OrbitPowerInstancesKeepIndependentProgress));
        Equal(12, state.Energy, nameof(OrbitPowerInstancesKeepIndependentProgress));
        journal.UndoTo(state, mark);
        Equal(root, state.Encode(), nameof(OrbitPowerInstancesKeepIndependentProgress));
    }

    private static void MonsterCompilerUsesA10AndRejectsUnknownBranchProbability()
    {
        MonsterOverrideCatalog overrides = MonsterOverrideCatalog.Load("data/manual-tags/monster_move_overrides.json");
        MonsterMoveNumeric hp = new("A10 hp", 20, 24, "ToughEnemies", 1d);
        MonsterMoveNumeric damage = new("A10 damage", 6, 9, "DeadlyEnemies", 1d);
        MonsterMoveStateEntry move = new(
            "ATTACK",
            "AttackMove",
            ["SingleAttackIntent"],
            [new MonsterMoveEffectTerm("attack", damage, new MonsterMoveNumeric("1", 1, null, null, 1d), "player", null, "DamageCmd.Attack", 1d)],
            ["ATTACK"],
            [],
            1d);
        MonsterMoveProfileEntry profile = new(
            "MONSTER.TEST",
            "TestMonster",
            "Tests.TestMonster",
            new MonsterHpRange(hp, hp),
            [move],
            "ATTACK",
            [],
            "fixture",
            1d);
        CombatMonsterDefinition compiled = new MonsterIntentCompiler().Compile(profile, overrides);
        True(compiled.IsSupported, nameof(MonsterCompilerUsesA10AndRejectsUnknownBranchProbability));
        Equal(24, compiled.MaxHpA10, nameof(MonsterCompilerUsesA10AndRejectsUnknownBranchProbability));
        Equal(9, compiled.Intents["ATTACK"].Effects[0].Amount, nameof(MonsterCompilerUsesA10AndRejectsUnknownBranchProbability));

        compiled = new MonsterIntentCompiler().Compile(
            profile with { Moves = [move with { FollowUpStateIds = ["ATTACK", "OTHER"] }] },
            overrides);
        True(!compiled.IsSupported, nameof(MonsterCompilerUsesA10AndRejectsUnknownBranchProbability));
    }

    private static void EncounterOverridesRequireSourcedRealizations()
    {
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = new Dictionary<string, CombatMonsterDefinition>(StringComparer.Ordinal)
        {
            ["MonsterA"] = IdleMonster("MonsterA", 20),
            ["MonsterB"] = IdleMonster("MonsterB", 30)
        };
        EncounterPatternEntry pattern = new(
            "ENCOUNTER.CONDITIONAL",
            "ConditionalEncounter",
            "Tests.ConditionalEncounter",
            [new EncounterActReference("Act1", 0, 1, true, 3, 15)],
            "Monster",
            true,
            "Weak",
            [],
            [new EncounterMonsterSlot(1, "front", null, ["MonsterA", "MonsterB"], "fixture", 1d)],
            ["MonsterA", "MonsterB"],
            1,
            true,
            [],
            "fixture",
            1d);
        EncounterOverrideCatalog empty = EncounterOverrideCatalog.Create(new EncounterOverrideFile(
            1,
            new Dictionary<string, EncounterOverrideEntry>(StringComparer.Ordinal),
            []));
        EncounterCombatDefinition unsupported = new EncounterCombatCompiler().Compile([pattern], monsters, empty).Single();
        True(!unsupported.IsSupported, nameof(EncounterOverridesRequireSourcedRealizations));

        EncounterOverrideCatalog sourced = EncounterOverrideCatalog.Create(new EncounterOverrideFile(
            1,
            new Dictionary<string, EncounterOverrideEntry>(StringComparer.Ordinal)
            {
                ["ENCOUNTER.CONDITIONAL:act1"] = new EncounterOverrideEntry(
                    [
                        new EncounterRealizationOverride("a", 0.4d, new Dictionary<int, string> { [1] = "MonsterA" }),
                        new EncounterRealizationOverride("b", 0.6d, new Dictionary<int, string> { [1] = "MonsterB" })
                    ],
                    "Tests.ConditionalEncounter.GenerateMonsters",
                    "Fixture probability table",
                    1d)
            },
            []));
        EncounterCombatDefinition[] compiled = new EncounterCombatCompiler().Compile([pattern], monsters, sourced).ToArray();
        Equal(2, compiled.Length, nameof(EncounterOverridesRequireSourcedRealizations));
        True(compiled.All(item => item.IsSupported), nameof(EncounterOverridesRequireSourcedRealizations));
        Equal(1d, compiled.Sum(item => item.RealizationProbability), nameof(EncounterOverridesRequireSourcedRealizations), 1e-12);
        True(compiled.Select(item => item.StableId).Distinct(StringComparer.Ordinal).Count() == 2, nameof(EncounterOverridesRequireSourcedRealizations));
    }

    private static void MonsterParserPreservesSourceEffectOrder()
    {
        const string source = """
        public sealed class OrderedMonster : MonsterModel
        {
            protected override MonsterMoveStateMachine GenerateMoveStateMachine()
            {
                MoveState move = new MoveState("MOVE", OrderedMove, new DefendIntent(), new SingleAttackIntent(7), new DebuffIntent());
                move.FollowUpState = move;
                return new MonsterMoveStateMachine(list, move);
            }

            private async Task OrderedMove(IReadOnlyList<Creature> targets)
            {
                await CreatureCmd.GainBlock(base.Creature, 4, ValueProp.Move, null);
                await DamageCmd.Attack(7).FromMonster(this).Execute(null);
                await PowerCmd.Apply<WeakPower>(choiceContext, targets[0], 2, base.Creature, null);
            }
        }
        """;
        ModelCatalogEntry model = new(
            "enemy", "OrderedMonster", "Tests.OrderedMonster", "MONSTER.ORDERED_MONSTER", "sts2.dll", "fixture", 1d);
        MonsterMoveStateEntry move = new MonsterMoveParser().Parse(model, source).Moves.Single();
        Equal("block", move.Effects[0].Kind, nameof(MonsterParserPreservesSourceEffectOrder));
        Equal("attack", move.Effects[1].Kind, nameof(MonsterParserPreservesSourceEffectOrder));
        Equal("debuffWeak", move.Effects[2].Kind, nameof(MonsterParserPreservesSourceEffectOrder));
    }

    private static void HpContinuationIsMonotoneWithSteeperLowHpMarginal()
    {
        HpContinuationContext context = new("test", 1, "normal", 8, 0.2, 0.05, 50);
        HpContinuationEvaluator evaluator = new();
        for (int hp = 1; hp < 80; hp++)
        {
            True(evaluator.EvaluateAlive(hp + 1, 80, context) >= evaluator.EvaluateAlive(hp, 80, context), nameof(HpContinuationIsMonotoneWithSteeperLowHpMarginal));
        }
        True(
            evaluator.MarginalHpValue(10, 80, context) >= evaluator.MarginalHpValue(70, 80, context),
            nameof(HpContinuationIsMonotoneWithSteeperLowHpMarginal));
        True(evaluator.HpLossCost(80, 80, 1, context) > 0, nameof(HpContinuationIsMonotoneWithSteeperLowHpMarginal));
        Equal(0d, evaluator.HealingValue(80, 80, 10, context), nameof(HpContinuationIsMonotoneWithSteeperLowHpMarginal));
    }

    private static void HpContinuationTelescopesAndDeathDominates()
    {
        HpContinuationContext context = new("test", 1, "normal", 8, 0.2, 0.05, 50);
        HpContinuationEvaluator evaluator = new();
        double oneDrop = evaluator.EvaluateAlive(70, 80, context) - evaluator.EvaluateAlive(80, 80, context);
        double tenDrops = 0d;
        for (int hp = 80; hp > 70; hp--)
        {
            tenDrops += evaluator.EvaluateAlive(hp - 1, 80, context) - evaluator.EvaluateAlive(hp, 80, context);
        }
        Equal(oneDrop, tenDrops, nameof(HpContinuationTelescopesAndDeathDominates), 1e-12);

        CombatInformationState state = State(enemyHp: 40, playerHp: 1);
        new CombatDamageResolver().LosePlayerHp(state, 1, new CombatMutationJournal());
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = IdleMonsters(40);
        CombatTerminalValue terminal = new CombatTerminalEvaluator(new ReferenceCombatPolicy(monsters)).Evaluate(state, context);
        True(terminal.DeathDominanceApplied, nameof(HpContinuationTelescopesAndDeathDominates));
        True(terminal.Value < -state.InitialEnemyHpTotal, nameof(HpContinuationTelescopesAndDeathDominates));
    }

    private static void DrawProbabilitiesAreExactAndNormalized()
    {
        CombatSimulationOptions options = new() { HorizonTurns = 1 };
        CombatInformationState state = CombatStateFactory.Create(
            20,
            20,
            [1, 1, 2, 3],
            [new CombatMonsterSeed("m1", "Dummy", 20, "IDLE")],
            options);
        IReadOnlyList<CombatDrawOutcome> outcomes = new CombatChanceResolver().EnumerateDrawOutcomes(state, 2, 10);
        Equal(1d, outcomes.Sum(outcome => outcome.Probability), nameof(DrawProbabilitiesAreExactAndNormalized), 1e-12);
        CombatDrawOutcome pair = outcomes.Single(outcome =>
            outcome.DrawnCards.Select(state.GetDefinitionId).SequenceEqual([1, 1]));
        Equal(1d / 6d, pair.Probability, nameof(DrawProbabilitiesAreExactAndNormalized), 1e-12);
        CombatDrawOutcome mixed = outcomes.Single(outcome =>
            outcome.DrawnCards.Select(state.GetDefinitionId).SequenceEqual([1, 2]));
        Equal(2d / 6d, mixed.Probability, nameof(DrawProbabilitiesAreExactAndNormalized), 1e-12);
    }

    private static void InformationStateSolverDoesNotObserveUnknownOrder()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatInformationState first = fixture.CreateState([0, 1, 2], [0]);
        CombatInformationState second = fixture.CreateState([0, 2, 1], [0]);
        CombatSolveResult a = fixture.Solver().Solve(first, fixture.HpContext);
        CombatSolveResult b = fixture.Solver().Solve(second, fixture.HpContext);
        Equal(a.Value, b.Value, nameof(InformationStateSolverDoesNotObserveUnknownOrder), 1e-9);
        Equal(a.BestAction, b.BestAction, nameof(InformationStateSolverDoesNotObserveUnknownOrder));
    }

    private static void InformationStateSolverCanUseKnownTopInformation()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatInformationState attackTop = CombatStateFactory.Create(
            80, 80, [0, 1, 2], [new CombatMonsterSeed("dummy-1", "Dummy", 100, "IDLE")],
            fixture.Options, initialHand: [0], knownTop: [1]);
        CombatInformationState dudTop = CombatStateFactory.Create(
            80, 80, [0, 1, 2], [new CombatMonsterSeed("dummy-1", "Dummy", 100, "IDLE")],
            fixture.Options, initialHand: [0], knownTop: [2]);
        CombatSolveResult attack = fixture.Solver().Solve(attackTop, fixture.HpContext);
        CombatSolveResult dud = fixture.Solver().Solve(dudTop, fixture.HpContext);
        True(attack.Value > dud.Value, nameof(InformationStateSolverCanUseKnownTopInformation));
    }

    private static void InformationStateSolverComputesMaxOfExpectedOutcomes()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatInformationState state = fixture.CreateState([0, 1, 2], [0]);
        CombatSolveResult result = fixture.Solver().Solve(state, fixture.HpContext);
        Equal(5d, result.Value, nameof(InformationStateSolverComputesMaxOfExpectedOutcomes), 1e-9);
        Equal(new CombatAction(CombatActionKind.PlayCard, 0), result.BestAction, nameof(InformationStateSolverComputesMaxOfExpectedOutcomes));
        Equal(CombatSolveStatus.Exact, result.Status, nameof(InformationStateSolverComputesMaxOfExpectedOutcomes));
    }

    private static void MemoizationAndBudgetContractsHold()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatInformationState memoState = fixture.CreateState([0, 1, 2], [0]);
        CombatSolveResult memo = fixture.Solver().Solve(memoState, fixture.HpContext);
        CombatSimulationOptions noMemoOptions = fixture.Options with { EnableMemoization = false };
        CombatInformationState noMemoState = fixture.CreateState([0, 1, 2], [0]);
        CombatSolveResult noMemo = fixture.Solver(noMemoOptions).Solve(noMemoState, fixture.HpContext);
        Equal(memo.Value, noMemo.Value, nameof(MemoizationAndBudgetContractsHold), 1e-12);
        Equal(memo.BestAction, noMemo.BestAction, nameof(MemoizationAndBudgetContractsHold));

        CombatSimulationOptions stateBudget = fixture.Options with { MaximumCanonicalStates = 1 };
        CombatSolveResult exceeded = fixture.Solver(stateBudget).Solve(fixture.CreateState([0, 1, 2], [0]), fixture.HpContext);
        Equal(CombatSolveStatus.ExactBudgetExceeded, exceeded.Status, nameof(MemoizationAndBudgetContractsHold));
        True(double.IsNaN(exceeded.Value), nameof(MemoizationAndBudgetContractsHold));

        CombatSimulationOptions chanceBudget = fixture.Options with { ExactOutcomeLimit = 1 };
        exceeded = fixture.Solver(chanceBudget).Solve(fixture.CreateState([0, 1, 2], [0]), fixture.HpContext);
        Equal(CombatSolveStatus.ExactBudgetExceeded, exceeded.Status, nameof(MemoizationAndBudgetContractsHold));
    }

    private static void CombatSimulationRunnerReturnsPhysicalMetrics()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatSample sample = new(
            "fixture-sample", "act1-normal", 1, "normal", "fixture-deck", "fixture", "fixture-encounter:act1",
            80, 80, "act1-normal", 1UL, 1d, 1d, 1d, true, null);
        CompiledCombatDeck deck = new("fixture-deck", "fixture", [1], true, []);
        CombatMonsterDefinition monster = fixture.Monsters["Dummy"];
        EncounterCombatDefinition encounter = new(
            "fixture-encounter", "FixtureEncounter", 1, "normal",
            [new EncounterMonsterDefinition(1, "Dummy", monster)], true, []);
        CombatContextResult result = new CombatSimulationRunner(fixture.Cards, fixture.Monsters).EvaluateContext(
            sample,
            deck,
            encounter,
            new HpContinuationContext("act1-normal", 1, "normal", 8, 0.2, 0.03, 50),
            1);
        Equal(10d, result.Metrics.ActualEnemyHpDamage, nameof(CombatSimulationRunnerReturnsPhysicalMetrics), 1e-9);
        Equal(10d, result.Metrics.Value, nameof(CombatSimulationRunnerReturnsPhysicalMetrics), 1e-9);
        Equal(CombatSolveStatus.Exact, result.SolverStatus, nameof(CombatSimulationRunnerReturnsPhysicalMetrics));
    }

    private static void IndependentHorizonBatchMatchesSingleRuns()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatSample sample = Sample("fixture-deck", "fixture-encounter:act1", "act1-normal");
        CompiledCombatDeck deck = new("fixture-deck", "fixture", [1], true, []);
        EncounterCombatDefinition encounter = new(
            "fixture-encounter", "FixtureEncounter", 1, "normal",
            [new EncounterMonsterDefinition(1, "Dummy", fixture.Monsters["Dummy"])], true, []);
        CombatSimulationRunner runner = new(fixture.Cards, fixture.Monsters);
        IReadOnlyDictionary<int, CombatContextResult> batch = runner.RunIndependentHorizons(
            sample, deck, encounter, fixture.HpContext, [4, 8, 12]);
        foreach (int horizon in new[] { 4, 8, 12 })
        {
            CombatContextResult single = runner.EvaluateContext(sample, deck, encounter, fixture.HpContext, horizon);
            Equal(single.Metrics.Value, batch[horizon].Metrics.Value, nameof(IndependentHorizonBatchMatchesSingleRuns), 1e-12);
            Equal(single.SolverStatus, batch[horizon].SolverStatus, nameof(IndependentHorizonBatchMatchesSingleRuns));
        }
    }

    private static void SemanticStreamsAreIndependentAndDeterministic()
    {
        ulong shuffle = SemanticRandomStreams.ForDeckShuffle(123, 1, "card-a");
        Equal(shuffle, SemanticRandomStreams.ForDeckShuffle(123, 1, "card-a"), nameof(SemanticStreamsAreIndependentAndDeterministic));
        True(shuffle != SemanticRandomStreams.ForMonsterTransition(123, "monster-a", 1), nameof(SemanticStreamsAreIndependentAndDeterministic));
        True(SemanticRandomStreams.ForPlanningChance(123, 1, "x") != SemanticRandomStreams.ForEvaluationChance(123, 1, "x"), nameof(SemanticStreamsAreIndependentAndDeterministic));
    }

    private static void BaselineCacheKeyCoversSemanticInputs()
    {
        CombatBaselineCache cache = new("data/generated/combat_aware/test-cache-unused");
        CompiledCombatDeck deck = new("deck", "fixture", [1, 2], true, []);
        CombatSample sample = Sample("deck", "encounter:act1", "act1-normal");
        CombatSimulationOptions options = new() { HorizonTurns = 4 };
        string key = cache.BuildKey(deck, sample, 4, options, "hp-a", "combat-a");
        True(key != cache.BuildKey(deck, sample, 4, options, "hp-b", "combat-a"), nameof(BaselineCacheKeyCoversSemanticInputs));
        True(key != cache.BuildKey(deck, sample, 4, options, "hp-a", "combat-b"), nameof(BaselineCacheKeyCoversSemanticInputs));
        True(key != cache.BuildKey(deck, sample, 8, options with { HorizonTurns = 8 }, "hp-a", "combat-a"), nameof(BaselineCacheKeyCoversSemanticInputs));
        True(key != cache.BuildKey(deck with { CardDefinitionIds = [1, 2, 3] }, sample, 4, options, "hp-a", "combat-a"), nameof(BaselineCacheKeyCoversSemanticInputs));
        True(key != cache.BuildKey(deck, sample, 4, options with { HandSize = 4 }, "hp-a", "combat-a"), nameof(BaselineCacheKeyCoversSemanticInputs));
        True(key != cache.BuildKey(deck, sample, 4, options with { InitialStars = 1 }, "hp-a", "combat-a"), nameof(BaselineCacheKeyCoversSemanticInputs));
    }

    private static void ToyDeckDeltaHasZeroPositiveAndNegativeCases()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(0, "ATTACK.BASE", "BaseAttack", 0, "Attack", 1, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 10, 1, CombatCardTarget.Enemy, "fixture", 0)], false, true, true, []),
            new CombatCardDefinition(1, "DUD", "Dud", 0, "Status", 0, CombatCardTarget.Self, [], false, false, true, []),
            new CombatCardDefinition(2, "ATTACK.CANDIDATE", "CandidateAttack", 0, "Attack", 1, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 10, 1, CombatCardTarget.Enemy, "fixture", 0)], false, true, true, []),
            new CombatCardDefinition(3, "BLANK", "Blank", 0, "Status", 0, CombatCardTarget.Self, [], false, false, true, []),
            new CombatCardDefinition(4, "CURSE", "Curse", 0, "Curse", 0, CombatCardTarget.Self, [], false, false, true, [])
        ]);
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters = IdleMonsters(100);
        EncounterCombatDefinition encounter = new(
            "encounter", "Encounter", 1, "normal",
            [new EncounterMonsterDefinition(1, "Dummy", monsters["Dummy"])], true, []);
        CombatSample sample = Sample("deck", encounter.StableId, "act1-normal");
        CombatSamplePlan plan = new("toy", "portfolio-hash", 1, [sample], 1d, []);
        HpContinuationCatalog hp = HpContinuationCatalog.Load("data/manual-tags/hp_continuation_calibration.json");
        CombatDeckDeltaEstimator estimator = new(cards, monsters, hp);
        CombatDeckDeltaOptions options = new(1, 1, 1, false);

        double zero = EstimateToyDelta(estimator, plan, encounter, new CompiledCombatDeck("deck", "fixture", [1, 1, 1, 1, 1, 1], true, []), cards.Get(3), options);
        double positive = EstimateToyDelta(estimator, plan, encounter, new CompiledCombatDeck("deck", "fixture", [0, 1, 1, 1, 1, 1], true, []), cards.Get(2), options);
        double negative = EstimateToyDelta(estimator, plan, encounter, new CompiledCombatDeck("deck", "fixture", [0, 1, 1, 1, 1, 1], true, []), cards.Get(4), options);
        Equal(0d, zero, nameof(ToyDeckDeltaHasZeroPositiveAndNegativeCases), 1e-12);
        True(positive > 0d, nameof(ToyDeckDeltaHasZeroPositiveAndNegativeCases));
        True(negative < 0d, nameof(ToyDeckDeltaHasZeroPositiveAndNegativeCases));
    }

    private static double EstimateToyDelta(
        CombatDeckDeltaEstimator estimator,
        CombatSamplePlan plan,
        EncounterCombatDefinition encounter,
        CompiledCombatDeck deck,
        CombatCardDefinition candidate,
        CombatDeckDeltaOptions options)
    {
        CombatDeckDeltaReport report = estimator.Estimate(
            plan,
            new Dictionary<string, CompiledCombatDeck>(StringComparer.Ordinal) { [deck.DeckId] = deck },
            new Dictionary<string, EncounterCombatDefinition>(StringComparer.Ordinal) { [encounter.StableId] = encounter },
            candidate,
            [1],
            options,
            "toy-combat-hash");
        True(!report.RuntimeCandidate, nameof(ToyDeckDeltaHasZeroPositiveAndNegativeCases));
        True(report.Horizons.Single().PrimaryDeltaEv is null, nameof(ToyDeckDeltaHasZeroPositiveAndNegativeCases));
        return report.Horizons.Single().Cells.Single().DeltaEv;
    }

    private static void TenThousandApplyUndoTransitionsPreserveIntegrity()
    {
        SolverFixture fixture = MakeSolverFixture();
        CombatInformationState state = fixture.CreateState([0, 1, 2], [0]);
        CombatTransitionKernel kernel = new(fixture.Cards, fixture.Monsters, fixture.Options);
        CombatChanceResolver chance = new();
        CombatMutationJournal journal = new();
        string root = state.Encode();
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            IReadOnlyList<CombatAction> actions = kernel.GetLegalActions(state);
            CombatAction action = actions[iteration % actions.Count];
            int actionMark = journal.Mark();
            CombatPreparedTransition prepared = kernel.Prepare(state, action, journal);
            IReadOnlyList<CombatChanceOutcome> outcomes = chance.Combine(
                chance.EnumerateDrawOutcomes(state, prepared.DrawCount, fixture.Options.MaxHandSize),
                prepared.MonsterTransitions,
                CombatSolveMode.Exact,
                int.MaxValue);
            CombatChanceOutcome outcome = outcomes[iteration % outcomes.Count];
            int outcomeMark = journal.Mark();
            chance.Apply(state, outcome, journal);
            state.ValidateIntegrity();
            Equal(1d, outcomes.Sum(item => item.Probability), nameof(TenThousandApplyUndoTransitionsPreserveIntegrity), 1e-12);
            journal.UndoTo(state, outcomeMark);
            journal.UndoTo(state, actionMark);
            Equal(root, state.Encode(), nameof(TenThousandApplyUndoTransitionsPreserveIntegrity));
        }
    }

    private static void TwoHundredRunIntegrityRegression()
    {
        SolverFixture fixture = MakeSolverFixture();
        for (int run = 0; run < 200; run++)
        {
            CombatInformationState state = fixture.CreateState([0, 1, 2], [0]);
            string before = state.Encode();
            CombatSolveResult result = fixture.Solver().Solve(state, fixture.HpContext);
            Equal(CombatSolveStatus.Exact, result.Status, nameof(TwoHundredRunIntegrityRegression));
            Equal(before, state.Encode(), nameof(TwoHundredRunIntegrityRegression));
            state.ValidateIntegrity();
        }
    }

    private static SolverFixture MakeSolverFixture()
    {
        CombatCardCatalog cards = new(
        [
            new CombatCardDefinition(0, "CARD.DRAW", "Draw", 0, "Skill", 0, CombatCardTarget.Self,
                [new CombatCardEffect(CombatCardEffectKind.Draw, 1, 1, CombatCardTarget.Self, "fixture", 0)], false, true, true, []),
            new CombatCardDefinition(1, "CARD.ATTACK", "Attack", 0, "Attack", 0, CombatCardTarget.Enemy,
                [new CombatCardEffect(CombatCardEffectKind.Damage, 10, 1, CombatCardTarget.Enemy, "fixture", 0)], false, true, true, []),
            new CombatCardDefinition(2, "CARD.DUD", "Dud", 0, "Curse", 0, CombatCardTarget.Self,
                [], false, false, true, [])
        ]);
        MonsterIntentDefinition idle = new(
            "IDLE",
            [],
            [new MonsterIntentTransition("IDLE", 1d, "fixture")]);
        CombatMonsterDefinition monster = new(
            "MONSTER.DUMMY",
            "Dummy",
            100,
            100,
            "IDLE",
            new Dictionary<string, MonsterIntentDefinition>(StringComparer.Ordinal) { ["IDLE"] = idle },
            true,
            [],
            "fixture");
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters =
            new Dictionary<string, CombatMonsterDefinition>(StringComparer.Ordinal) { [monster.TypeName] = monster };
        CombatSimulationOptions options = new()
        {
            HorizonTurns = 1,
            HandSize = 5,
            MaxHandSize = 10,
            EnergyPerTurn = 3,
            MaximumCanonicalStates = 10_000
        };
        HpContinuationContext context = new("fixture", 1, "normal", 8, 0.2, 0.03, 50);
        return new SolverFixture(cards, monsters, options, context);
    }

    private static IReadOnlyDictionary<string, CombatMonsterDefinition> IdleMonsters(int hp)
    {
        CombatMonsterDefinition monster = IdleMonster("Dummy", hp);
        return new Dictionary<string, CombatMonsterDefinition>(StringComparer.Ordinal) { ["Dummy"] = monster };
    }

    private static CombatMonsterDefinition IdleMonster(string typeName, int hp)
    {
        MonsterIntentDefinition idle = new("IDLE", [], [new MonsterIntentTransition("IDLE", 1d, "fixture")]);
        return new CombatMonsterDefinition(
            $"MONSTER.{typeName.ToUpperInvariant()}", typeName, hp, hp, "IDLE",
            new Dictionary<string, MonsterIntentDefinition>(StringComparer.Ordinal) { ["IDLE"] = idle },
            true, [], "fixture");
    }

    private static CombatSample Sample(string deckId, string encounterId, string hpContextId) => new(
        "fixture-sample", "act1-normal", 1, "normal", deckId, "fixture", encounterId,
        80, 80, hpContextId, 1UL, 1d, 1d, 1d, true, null);

    private static CombatInformationState State(int enemyHp, int playerHp) => CombatStateFactory.Create(
        playerHp,
        playerHp,
        [],
        [new CombatMonsterSeed("m1", "Dummy", enemyHp, "IDLE")],
        new CombatSimulationOptions { HorizonTurns = 1 });

    private sealed record SolverFixture(
        CombatCardCatalog Cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> Monsters,
        CombatSimulationOptions Options,
        HpContinuationContext HpContext)
    {
        public CombatInformationState CreateState(IEnumerable<int> deck, IEnumerable<int> hand) => CombatStateFactory.Create(
            80,
            80,
            deck,
            [new CombatMonsterSeed("dummy-1", "Dummy", 100, "IDLE")],
            Options,
            hand);

        public InformationStateSolver Solver(CombatSimulationOptions? options = null)
        {
            CombatSimulationOptions actualOptions = options ?? Options;
            CombatTransitionKernel kernel = new(Cards, Monsters, actualOptions);
            return new InformationStateSolver(
                kernel,
                new CombatChanceResolver(),
                new CombatTerminalEvaluator(new ReferenceCombatPolicy(Monsters, new ReferenceTailOptions(0, 0))),
                actualOptions);
        }
    }

    private static void True(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"{name}: assertion failed.");
    }

    private static void Equal<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}.");
        }
    }

    private static void Equal(double expected, double actual, string name, double tolerance = 0d)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{name}: expected {expected:R}, actual {actual:R}.");
        }
    }
}
