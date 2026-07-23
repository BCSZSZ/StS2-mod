using System.Text.Json;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Analysis;
using CardValueOverlay.Core.Ancient;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.Values;

namespace CardValueOverlay.Core.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            DynamicTrainingValueWins();
            TrainingValueUsesUpgradeStateAndHorizon();
            CommonParameterUsesLayeredValues();
            EmptyValueStaysEmpty();
            ConfigParsesAndValidatesTrainingValues();
            RealtimeSimulationSettingsClampAndIdentifyCacheEntries();
            RealtimeWorkerPolicyUsesConservativeProcessorFractions();
            RealtimeSearchBudgetPolicyUsesValidatedHorizonLimits();
            RealtimeSearchBranchPolicyUsesValidatedSelectiveGap();
            PairedDeltaIntervalsUsePlannedLookCriticalValues();
            GenerationMetadataWarnsButDoesNotInvalidateConfig();
            UnupgradedOnlyTrainingValuesDoNotWarn();
            OldSchemaIsRejected();
            AverageIgnoresMissingValues();
            AverageParsesUpgradedSuffix();
            CardAdoptionCatalogTests.RunAll();
            LocalRunHistoryStatsBuilderTests.RunAll();
            AncientChoiceBandsUseEmpiricalQuartiles();
            Console.WriteLine("All core tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void DynamicTrainingValueWins()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue(
            "CARD.STRIKE_REGENT",
            CardUpgradeState.Upgraded,
            TrainingValueHorizon.Longline,
            new Dictionary<string, TrainingHorizonValues>
            {
                ["card:CARD.STRIKE_REGENT:upgraded"] = new()
                {
                    Shortline = 9,
                    Midline = 10,
                    Longline = 11
                }
            });

        AssertEqual(11, value.Value, nameof(DynamicTrainingValueWins));
        AssertEqual(ValueSource.Dynamic, value.Source, nameof(DynamicTrainingValueWins));
    }

    private static void TrainingValueUsesUpgradeStateAndHorizon()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> unupgraded = resolver.ResolveCardValue(
            "CARD.STRIKE_REGENT",
            CardUpgradeState.Unupgraded,
            TrainingValueHorizon.Shortline);
        EffectiveValue<double> upgraded = resolver.ResolveCardValue(
            "CARD.STRIKE_REGENT",
            CardUpgradeState.Upgraded,
            TrainingValueHorizon.Longline);

        AssertEqual(1.5, unupgraded.Value, nameof(TrainingValueUsesUpgradeStateAndHorizon));
        AssertEqual(2.6, upgraded.Value, nameof(TrainingValueUsesUpgradeStateAndHorizon));
        AssertEqual(ValueSource.Training, upgraded.Source, nameof(TrainingValueUsesUpgradeStateAndHorizon));
    }

    private static void CommonParameterUsesLayeredValues()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCommonParameter(CommonParameterIds.CardsDrawnPerTurn, 30);

        AssertEqual(7, value.Value, nameof(CommonParameterUsesLayeredValues));
        AssertEqual(ValueSource.Training, value.Source, nameof(CommonParameterUsesLayeredValues));
    }

    private static void RealtimeSimulationSettingsClampAndIdentifyCacheEntries()
    {
        RealtimeSimulationSettings defaults = RealtimeSimulationSettings.Normalize(3, 6, 15, 60, 30, 95, true);
        RealtimeSimulationSettings low = RealtimeSimulationSettings.Normalize(0, 2, 1, 1, 1, 1, false);
        RealtimeSimulationSettings high = RealtimeSimulationSettings.Normalize(9, 20, 500, 500, 500, 500, true);
        RealtimeSimulationSettings snapped = RealtimeSimulationSettings.Normalize(3, 6, 29, 59, 44, 94, true);
        RealtimeSimulationSettings inverted = RealtimeSimulationSettings.Normalize(3, 6, 45, 15, 30, 95, true);

        AssertEqual(new RealtimeSimulationSettings(3, 6, 15, 60, 30, 95, true), defaults, nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(new RealtimeSimulationSettings(1, 4, 15, 15, 15, 80, false), low, nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(new RealtimeSimulationSettings(5, 6, 60, 60, 60, 99, true), high, nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(new RealtimeSimulationSettings(3, 6, 15, 45, 30, 94, true), snapped, nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(new RealtimeSimulationSettings(3, 6, 45, 45, 45, 95, true), inverted, nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(15, defaults.EffectiveMinimumRuns(complexCard: false), nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(30, defaults.EffectiveMinimumRuns(complexCard: true), nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(4, defaults.PlannedStoppingLooks(complexCard: false), nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(3, defaults.PlannedStoppingLooks(complexCard: true), nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
        AssertEqual(
            "branch3|depth6|minRuns15|maxRuns60|complexMinRuns30|confidence95|earlyStop1",
            defaults.CacheKey,
            nameof(RealtimeSimulationSettingsClampAndIdentifyCacheEntries));
    }

    private static void RealtimeWorkerPolicyUsesConservativeProcessorFractions()
    {
        const string testName = nameof(RealtimeWorkerPolicyUsesConservativeProcessorFractions);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunDegree(1, inCombat: false, turns: 4), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunDegree(4, inCombat: false, turns: 4), testName);
        AssertEqual(2, RealtimeWorkerPolicy.ResolveRunDegree(8, inCombat: false, turns: 4), testName);
        AssertEqual(3, RealtimeWorkerPolicy.ResolveRunDegree(12, inCombat: false, turns: 4), testName);
        AssertEqual(4, RealtimeWorkerPolicy.ResolveRunDegree(20, inCombat: false, turns: 4), testName);
        AssertEqual(4, RealtimeWorkerPolicy.ResolveRunDegree(64, inCombat: false, turns: 4), testName);

        AssertEqual(2, RealtimeWorkerPolicy.ResolveRunDegree(20, inCombat: false, turns: 8), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunDegree(20, inCombat: false, turns: 12), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunDegree(64, inCombat: true, turns: 4), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunDegree(64, inCombat: true, turns: 8), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunDegree(64, inCombat: true, turns: 12), testName);

        AssertEqual(4, RealtimeWorkerPolicy.ResolveRunsPerSlice(20, inCombat: false, turns: 4), testName);
        AssertEqual(2, RealtimeWorkerPolicy.ResolveRunsPerSlice(20, inCombat: false, turns: 8), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunsPerSlice(20, inCombat: false, turns: 12), testName);
        AssertEqual(1, RealtimeWorkerPolicy.ResolveRunsPerSlice(64, inCombat: true, turns: 4), testName);

        AssertThrows<ArgumentOutOfRangeException>(
            () => RealtimeWorkerPolicy.ResolveRunDegree(0, inCombat: false, turns: 4),
            testName);
        AssertThrows<ArgumentOutOfRangeException>(
            () => RealtimeWorkerPolicy.ResolveRunsPerSlice(0, inCombat: false, turns: 4),
            testName);
        AssertThrows<ArgumentOutOfRangeException>(
            () => RealtimeWorkerPolicy.ResolveRunDegree(20, inCombat: false, turns: 6),
            testName);
    }

    private static void RealtimeSearchBudgetPolicyUsesValidatedHorizonLimits()
    {
        const string testName = nameof(RealtimeSearchBudgetPolicyUsesValidatedHorizonLimits);
        AssertEqual(250_000, RealtimeSearchBudgetPolicy.ResolveMaxSearchNodesPerTurn(4), testName);
        AssertEqual(60_000, RealtimeSearchBudgetPolicy.ResolveMaxSearchNodesPerTurn(8), testName);
        AssertEqual(100_000, RealtimeSearchBudgetPolicy.ResolveMaxSearchNodesPerTurn(12), testName);
        AssertThrows<ArgumentOutOfRangeException>(
            () => RealtimeSearchBudgetPolicy.ResolveMaxSearchNodesPerTurn(6),
            testName);
    }

    private static void RealtimeSearchBranchPolicyUsesValidatedSelectiveGap()
    {
        AssertEqual(
            13,
            RealtimeSearchBranchPolicy.SelectiveThirdBranchMinScoreGap,
            nameof(RealtimeSearchBranchPolicyUsesValidatedSelectiveGap));
    }

    private static void PairedDeltaIntervalsUsePlannedLookCriticalValues()
    {
        (int Count, double DisplayCritical, double StoppingCritical)[] plannedLooks =
        [
            (15, 2.1447852436621244d, 2.863981579523614d),
            (30, 2.0452296071040124d, 2.6631954079488835d),
            (45, 2.0153675716103927d, 2.604549671929415d),
            (60, 2.0009953787148946d, 2.576587562454782d)
        ];

        foreach ((int count, double displayCritical, double stoppingCritical) in plannedLooks)
        {
            double[] values = Enumerable.Range(0, count)
                .Select(index => (index % 3) switch { 0 => 8d, 1 => 10d, _ => 12d })
                .ToArray();
            PairedDeltaSummary summary = PairedDeltaStatistics.Calculate(values, 95, 4);
            double expectedStandardError = Math.Sqrt(8d / (3d * (count - 1)));

            AssertNear(10d, summary.Mean, 0.0000001d, nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
            AssertNear(
                10d - displayCritical * expectedStandardError,
                summary.LowerConfidence,
                0.0001d,
                nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
            AssertNear(
                10d + stoppingCritical * expectedStandardError,
                summary.UpperStopping,
                0.0001d,
                nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
            AssertTrue(summary.LowerStopping < summary.LowerConfidence, nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
            AssertTrue(summary.UpperStopping > summary.UpperConfidence, nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
            AssertTrue(summary.HasStableSign, nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
        }

        double[] configurableValues = Enumerable.Range(0, 30)
            .Select(index => index % 2 == 0 ? -1d : 1d)
            .ToArray();
        PairedDeltaSummary confidence90 = PairedDeltaStatistics.Calculate(configurableValues, 90, 3);
        PairedDeltaSummary confidence99 = PairedDeltaStatistics.Calculate(configurableValues, 99, 3);
        PairedDeltaSummary oneLook = PairedDeltaStatistics.Calculate(configurableValues, 95, 1);
        PairedDeltaSummary fourLooks = PairedDeltaStatistics.Calculate(configurableValues, 95, 4);
        AssertTrue(
            confidence99.UpperConfidence > confidence90.UpperConfidence,
            nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
        AssertTrue(
            fourLooks.UpperStopping > oneLook.UpperStopping,
            nameof(PairedDeltaIntervalsUsePlannedLookCriticalValues));
    }

    private static void EmptyValueStaysEmpty()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue(
            "CARD.UNKNOWN",
            CardUpgradeState.Unupgraded,
            TrainingValueHorizon.Midline);

        AssertEqual(null, value.Value, nameof(EmptyValueStaysEmpty));
        AssertEqual(ValueSource.None, value.Source, nameof(EmptyValueStaysEmpty));
    }

    private static void ConfigParsesAndValidatesTrainingValues()
    {
        const string json = """
        {
          "schemaVersion": 3,
          "overlay": {
            "displayMode": "trainingValue",
            "valueHorizon": "midline",
            "fixedText": "T",
            "maxLines": 3
          },
          "training": {
            "source": "dashen_77_selected_100",
            "deckCount": 100,
            "runsPerDeck": 1000,
            "horizons": { "shortline": 4, "midline": 8, "longline": 12 }
          },
          "cards": {
            "CARD.STRIKE_REGENT": {
              "typeName": "StrikeRegent",
              "trainingValues": {
                "unupgraded": { "shortline": 1.5, "midline": 1.7, "longline": 1.8 },
                "upgraded": { "shortline": 2.0, "midline": 2.4, "longline": 2.6 }
              },
              "generation": {
                "method": "monteCarlo",
                "updatedAt": {
                  "shortline": "2026-06-29T00:00:00Z",
                  "midline": "2026-06-29T00:00:00Z",
                  "longline": "2026-06-29T00:00:00Z"
                }
              }
            }
          },
          "commonParameters": {
            "deck_count": { "fixedValues": {} },
            "cards_drawn_per_turn": { "fixedValues": { "1": 5, "20": 7 } },
            "turns_per_shuffle_cycle": { "fixedValues": {} }
          }
        }
        """;

        CardValueConfig config = CardValueConfigLoader.LoadFromJson(json);
        ConfigValidationResult result = CardValueConfigLoader.Validate(config);

        AssertTrue(result.IsValid, nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual(OverlayDisplayMode.TrainingValue, config.Overlay.DisplayMode, nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual(TrainingValueHorizon.Midline, config.Overlay.ValueHorizon, nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual(TrainingHorizonTurnCounts.Longline, config.Training.Horizons["longline"], nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual(1.7, config.Cards["CARD.STRIKE_REGENT"].TrainingValues.Unupgraded.Midline, nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual(2.6, config.Cards["CARD.STRIKE_REGENT"].TrainingValues.Upgraded.Longline, nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual(CardValueGenerationMethods.MonteCarlo, config.Cards["CARD.STRIKE_REGENT"].Generation?.Method, nameof(ConfigParsesAndValidatesTrainingValues));
        AssertEqual("2026-06-29T00:00:00Z", config.Cards["CARD.STRIKE_REGENT"].Generation?.UpdatedAt?.Midline, nameof(ConfigParsesAndValidatesTrainingValues));
        string serialized = CardValueConfigLoader.ToJson(config);
        AssertTrue(serialized.Contains("\"generation\"", StringComparison.Ordinal), nameof(ConfigParsesAndValidatesTrainingValues));
        AssertTrue(serialized.Contains("\"updatedAt\"", StringComparison.Ordinal), nameof(ConfigParsesAndValidatesTrainingValues));
    }

    private static void GenerationMetadataWarnsButDoesNotInvalidateConfig()
    {
        CardValueConfig config = new()
        {
            Cards = new Dictionary<string, CardValueEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["CARD.TEST"] = new()
                {
                    TypeName = "TestCard",
                    TrainingValues = new()
                    {
                        Unupgraded = new() { Shortline = 1, Midline = 2, Longline = 3 }
                    },
                    Generation = new()
                    {
                        Method = "handWave",
                        UpdatedAt = new()
                        {
                            Shortline = "not-a-date",
                            Midline = "2026-06-29T00:00:00Z",
                            Longline = "2026-06-29T00:00:00Z"
                        }
                    }
                },
                ["CARD.NULL_META"] = new()
                {
                    TypeName = "NullMetaCard",
                    TrainingValues = new()
                    {
                        Unupgraded = new() { Shortline = 1, Midline = 2, Longline = 3 }
                    },
                    Generation = new()
                    {
                        Method = CardValueGenerationMethods.MonteCarlo,
                        UpdatedAt = null
                    }
                }
            },
            CommonParameters = new Dictionary<string, CommonParameterEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [CommonParameterIds.DeckCount] = new(),
                [CommonParameterIds.CardsDrawnPerTurn] = new()
                {
                    FixedValues = new() { [1] = 5 }
                },
                [CommonParameterIds.TurnsPerShuffleCycle] = new()
            }
        };

        ConfigValidationResult result = CardValueConfigLoader.Validate(config);
        AssertTrue(result.IsValid, nameof(GenerationMetadataWarnsButDoesNotInvalidateConfig));
        AssertTrue(result.Warnings.Any(warning => warning.Contains("generation.method", StringComparison.Ordinal)), nameof(GenerationMetadataWarnsButDoesNotInvalidateConfig));
        AssertTrue(result.Warnings.Any(warning => warning.Contains("updatedAt.shortline", StringComparison.Ordinal)), nameof(GenerationMetadataWarnsButDoesNotInvalidateConfig));
        AssertTrue(result.Warnings.Any(warning => warning.Contains("updatedAt is empty", StringComparison.Ordinal)), nameof(GenerationMetadataWarnsButDoesNotInvalidateConfig));
    }

    private static void OldSchemaIsRejected()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "overlay": { "displayMode": "fixedText", "fixedText": "T", "maxLines": 3 },
          "cards": {
            "strike": {
              "manualValues": {
                "unupgraded": { "1": 1.5 },
                "upgraded": { "1": 2.0 }
              }
            }
          }
        }
        """;

        CardValueConfig config = CardValueConfigLoader.LoadFromJson(json);
        ConfigValidationResult result = CardValueConfigLoader.Validate(config);
        AssertTrue(!result.IsValid, nameof(OldSchemaIsRejected));
    }

    private static void UnupgradedOnlyTrainingValuesDoNotWarn()
    {
        CardValueConfig config = new()
        {
            Cards = new Dictionary<string, CardValueEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["CARD.ASCENDERS_BANE"] = new()
                {
                    TypeName = "AscendersBane",
                    TrainingValues = new()
                    {
                        Unupgraded = new() { Shortline = 0, Midline = 0, Longline = 0 }
                    }
                }
            },
            CommonParameters = new Dictionary<string, CommonParameterEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [CommonParameterIds.DeckCount] = new(),
                [CommonParameterIds.CardsDrawnPerTurn] = new()
                {
                    FixedValues = new() { [1] = 5 }
                },
                [CommonParameterIds.TurnsPerShuffleCycle] = new()
            }
        };

        ConfigValidationResult result = CardValueConfigLoader.Validate(config);
        AssertTrue(result.IsValid, nameof(UnupgradedOnlyTrainingValuesDoNotWarn));
        AssertTrue(!result.Warnings.Any(warning => warning.Contains("upgraded", StringComparison.OrdinalIgnoreCase)), nameof(UnupgradedOnlyTrainingValuesDoNotWarn));
    }

    private static void AverageIgnoresMissingValues()
    {
        ValueResolver resolver = CreateResolver();
        AverageExpectationResult result = ExpectationCalculator.CalculateAverage(
            new[] { "CARD.STRIKE_REGENT", "CARD.DEFEND_REGENT", "CARD.UNKNOWN" },
            resolver,
            TrainingValueHorizon.Midline);

        AssertEqual(3, result.RequestedCount, nameof(AverageIgnoresMissingValues));
        AssertEqual(2, result.ValuedCount, nameof(AverageIgnoresMissingValues));
        AssertEqual(1, result.MissingCount, nameof(AverageIgnoresMissingValues));
        AssertEqual(2.1, result.Average, nameof(AverageIgnoresMissingValues));
    }

    private static void AverageParsesUpgradedSuffix()
    {
        ValueResolver resolver = CreateResolver();
        AverageExpectationResult result = ExpectationCalculator.CalculateAverage(
            new[] { "CARD.STRIKE_REGENT+", "CARD.DEFEND_REGENT" },
            resolver,
            TrainingValueHorizon.Shortline);

        AssertEqual(2, result.RequestedCount, nameof(AverageParsesUpgradedSuffix));
        AssertEqual(2, result.ValuedCount, nameof(AverageParsesUpgradedSuffix));
        AssertEqual(2.5, result.Average, nameof(AverageParsesUpgradedSuffix));
    }

    private static void AncientChoiceBandsUseEmpiricalQuartiles()
    {
        const string json = """
        {
          "schemaVersion": 3,
          "characters": {
            "CHARACTER.REGENT": {
              "sampleRuns": 4,
              "totalChoiceScreens": 10,
              "outcomeSampleRuns": 8,
              "outcomeWins": 4,
              "outcomeChoiceScreens": 18,
              "choices": {
                "ZERO": {
                  "offerCount": 10,
                  "pickCount": 0,
                  "pickRate": 0
                },
                "LOW": {
                  "offerCount": 10,
                  "pickCount": 1,
                  "pickRate": 0.1,
                  "pickedRunCount": 5,
                  "pickedWinCount": 2,
                  "pickedWinRate": 0.4
                },
                "MIDDLE": {
                  "offerCount": 10,
                  "pickCount": 3,
                  "pickRate": 0.3
                },
                "HIGH": {
                  "offerCount": 10,
                  "pickCount": 8,
                  "pickRate": 0.8
                }
              }
            },
            "CHARACTER.SILENT": {
              "sampleRuns": 2,
              "totalChoiceScreens": 2,
              "outcomeSampleRuns": 3,
              "outcomeWins": 2,
              "outcomeChoiceScreens": 3,
              "choices": {
                "LOW": {
                  "offerCount": 2,
                  "pickCount": 2,
                  "pickRate": 1.0,
                  "pickedRunCount": 2,
                  "pickedWinCount": 2,
                  "pickedWinRate": 1.0
                }
              }
            }
          }
        }
        """;

        AncientChoiceCatalog catalog = AncientChoiceCatalog.LoadFromJson(json);
        AncientChoiceDisplayStats? low = catalog.Resolve(
            "SOME_ANCIENT.pages.INITIAL.options.LOW",
            "CHARACTER.REGENT");
        AncientChoiceDisplayStats? middle = catalog.Resolve("MIDDLE", "CHARACTER.REGENT");
        AncientChoiceDisplayStats? high = catalog.Resolve("HIGH", "CHARACTER.REGENT");
        AncientChoiceDisplayStats? zero = catalog.Resolve("ZERO", "CHARACTER.REGENT");
        AncientChoiceDisplayStats? silent = catalog.Resolve("LOW", "CHARACTER.SILENT");

        AssertEqual(0.1, low?.PickRate, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(5, low?.PickedRunCount, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(2, low?.PickedWinCount, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(0.4, low?.PickedWinRate, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Low, low?.PickRateBand, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Middle, middle?.PickRateBand, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.High, high?.PickRateBand, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(CardAdoptionStatBand.Unknown, zero?.PickRateBand, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(1d, silent?.PickRate, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(2, silent?.PickedRunCount, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(2, silent?.PickedWinCount, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(1d, silent?.PickedWinRate, nameof(AncientChoiceBandsUseEmpiricalQuartiles));
        AssertEqual(null, catalog.Resolve("LOW", null), nameof(AncientChoiceBandsUseEmpiricalQuartiles));
    }

    private static ValueResolver CreateResolver()
    {
        CardValueConfig config = new()
        {
            Cards = new Dictionary<string, CardValueEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["CARD.STRIKE_REGENT"] = new()
                {
                    TypeName = "StrikeRegent",
                    TrainingValues = new()
                    {
                        Unupgraded = new() { Shortline = 1.5, Midline = 1.7, Longline = 1.8 },
                        Upgraded = new() { Shortline = 2.0, Midline = 2.4, Longline = 2.6 }
                    }
                },
                ["CARD.DEFEND_REGENT"] = new()
                {
                    TypeName = "DefendRegent",
                    TrainingValues = new()
                    {
                        Unupgraded = new() { Shortline = 3.0, Midline = 2.5, Longline = 3.2 },
                        Upgraded = new() { Shortline = 3.5, Midline = 3.0, Longline = 3.8 }
                    }
                }
            },
            CommonParameters = new Dictionary<string, CommonParameterEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [CommonParameterIds.DeckCount] = new(),
                [CommonParameterIds.CardsDrawnPerTurn] = new()
                {
                    FixedValues = new() { [1] = 5, [20] = 7 }
                },
                [CommonParameterIds.TurnsPerShuffleCycle] = new()
            }
        };

        return new ValueResolver(config);
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

    private static void AssertNear(double expected, double actual, double tolerance, string testName)
    {
        if (Math.Abs(expected - actual) > tolerance)
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
