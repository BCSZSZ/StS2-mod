using System.Text.Json;
using CardValueOverlay.Core.Analysis;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.Values;

namespace CardValueOverlay.Core.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            DynamicValueWins();
            LayeredManualValueIsFallback();
            UpgradedLayeredValueIsSeparate();
            LayerThresholdChoosesNearestFloor();
            SmithValueUsesUpgradeState();
            CommonParameterUsesLayeredValues();
            EmptyValueStaysEmpty();
            ConfigParsesAndValidates();
            OldScalarValueIsRejected();
            AverageIgnoresMissingValues();
            AverageParsesUpgradedSuffix();
            SmithAverageUsesSmithValues();
            Console.WriteLine("All core tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void DynamicValueWins()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue(
            "strike",
            CardUpgradeState.Upgraded,
            25,
            new Dictionary<string, LayeredValueTable>
            {
                ["card:strike:upgraded"] = new() { [1] = 9, [20] = 11 }
            });

        AssertEqual(11, value.Value, nameof(DynamicValueWins));
        AssertEqual(ValueSource.Dynamic, value.Source, nameof(DynamicValueWins));
    }

    private static void LayeredManualValueIsFallback()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue("strike", CardUpgradeState.Unupgraded, 1);

        AssertEqual(1.5, value.Value, nameof(LayeredManualValueIsFallback));
        AssertEqual(ValueSource.Manual, value.Source, nameof(LayeredManualValueIsFallback));
    }

    private static void UpgradedLayeredValueIsSeparate()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue("strike", CardUpgradeState.Upgraded, 1);

        AssertEqual(2.0, value.Value, nameof(UpgradedLayeredValueIsSeparate));
        AssertEqual(ValueSource.Manual, value.Source, nameof(UpgradedLayeredValueIsSeparate));
    }

    private static void LayerThresholdChoosesNearestFloor()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue("strike", CardUpgradeState.Unupgraded, 25);

        AssertEqual(1.8, value.Value, nameof(LayerThresholdChoosesNearestFloor));
        AssertEqual(ValueSource.Manual, value.Source, nameof(LayerThresholdChoosesNearestFloor));
    }

    private static void SmithValueUsesUpgradeState()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> unupgraded = resolver.ResolveSmithValue("strike", CardUpgradeState.Unupgraded, 25);
        EffectiveValue<double> upgraded = resolver.ResolveSmithValue("strike", CardUpgradeState.Upgraded, 25);

        AssertEqual(0.7, unupgraded.Value, nameof(SmithValueUsesUpgradeState));
        AssertEqual(0.0, upgraded.Value, nameof(SmithValueUsesUpgradeState));
    }

    private static void CommonParameterUsesLayeredValues()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCommonParameter(CommonParameterIds.CardsDrawnPerTurn, 30);

        AssertEqual(7, value.Value, nameof(CommonParameterUsesLayeredValues));
        AssertEqual(ValueSource.Manual, value.Source, nameof(CommonParameterUsesLayeredValues));
    }

    private static void EmptyValueStaysEmpty()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue("unknown", CardUpgradeState.Unupgraded, 1);

        AssertEqual(null, value.Value, nameof(EmptyValueStaysEmpty));
        AssertEqual(ValueSource.None, value.Source, nameof(EmptyValueStaysEmpty));
    }

    private static void ConfigParsesAndValidates()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "overlay": { "displayMode": "fixedText", "fixedText": "T", "maxLines": 3 },
          "cards": {
            "strike": {
              "manualValues": {
                "unupgraded": { "1": 1.5, "20": 1.8 },
                "upgraded": { "1": 2.0, "20": 2.6 }
              },
              "smithValues": {
                "unupgraded": { "1": 0.5, "20": 0.7 },
                "upgraded": { "1": 0.0 }
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

        AssertTrue(result.IsValid, nameof(ConfigParsesAndValidates));
        AssertEqual(1.5, config.Cards["strike"].ManualValues.Unupgraded.Resolve(1), nameof(ConfigParsesAndValidates));
        AssertEqual(1.8, config.Cards["strike"].ManualValues.Unupgraded.Resolve(25), nameof(ConfigParsesAndValidates));
        AssertEqual(2.0, config.Cards["strike"].ManualValues.Upgraded.Resolve(1), nameof(ConfigParsesAndValidates));
        AssertEqual(0.5, config.Cards["strike"].SmithValues.Unupgraded.Resolve(1), nameof(ConfigParsesAndValidates));
    }

    private static void OldScalarValueIsRejected()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "overlay": { "displayMode": "fixedText", "fixedText": "T", "maxLines": 3 },
          "cards": {
            "strike": {
              "manualValues": {
                "unupgraded": 1.5,
                "upgraded": { "1": 2.0 }
              }
            }
          },
          "commonParameters": {
            "deck_count": { "fixedValues": {} },
            "cards_drawn_per_turn": { "fixedValues": { "1": 5 } },
            "turns_per_shuffle_cycle": { "fixedValues": {} }
          }
        }
        """;

        try
        {
            _ = CardValueConfigLoader.LoadFromJson(json);
        }
        catch (JsonException)
        {
            return;
        }

        throw new InvalidOperationException($"{nameof(OldScalarValueIsRejected)} failed. Old scalar values must be rejected.");
    }

    private static void AverageIgnoresMissingValues()
    {
        ValueResolver resolver = CreateResolver();
        AverageExpectationResult result = ExpectationCalculator.CalculateAverage(
            new[] { "strike", "defend", "unknown" },
            resolver);

        AssertEqual(3, result.RequestedCount, nameof(AverageIgnoresMissingValues));
        AssertEqual(2, result.ValuedCount, nameof(AverageIgnoresMissingValues));
        AssertEqual(1, result.MissingCount, nameof(AverageIgnoresMissingValues));
        AssertEqual(2.0, result.Average, nameof(AverageIgnoresMissingValues));
    }

    private static void AverageParsesUpgradedSuffix()
    {
        ValueResolver resolver = CreateResolver();
        AverageExpectationResult result = ExpectationCalculator.CalculateAverage(
            new[] { "strike+", "defend" },
            resolver);

        AssertEqual(2, result.RequestedCount, nameof(AverageParsesUpgradedSuffix));
        AssertEqual(2, result.ValuedCount, nameof(AverageParsesUpgradedSuffix));
        AssertEqual(2.25, result.Average, nameof(AverageParsesUpgradedSuffix));
    }

    private static void SmithAverageUsesSmithValues()
    {
        ValueResolver resolver = CreateResolver();
        AverageExpectationResult result = ExpectationCalculator.CalculateSmithAverage(
            new[] { "strike", "defend+" },
            resolver);

        AssertEqual(2, result.RequestedCount, nameof(SmithAverageUsesSmithValues));
        AssertEqual(2, result.ValuedCount, nameof(SmithAverageUsesSmithValues));
        AssertEqual(0.25, result.Average, nameof(SmithAverageUsesSmithValues));
    }

    private static ValueResolver CreateResolver()
    {
        CardValueConfig config = new()
        {
            Cards = new Dictionary<string, CardValueEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["strike"] = new()
                {
                    ManualValues = new()
                    {
                        Unupgraded = new() { [1] = 1.5, [20] = 1.8 },
                        Upgraded = new() { [1] = 2.0, [20] = 2.6 }
                    },
                    SmithValues = new()
                    {
                        Unupgraded = new() { [1] = 0.5, [20] = 0.7 },
                        Upgraded = new() { [1] = 0.0 }
                    }
                },
                ["defend"] = new()
                {
                    ManualValues = new()
                    {
                        Unupgraded = new() { [1] = 2.5, [20] = 3.2 },
                        Upgraded = new() { [1] = 3.0, [20] = 3.8 }
                    },
                    SmithValues = new()
                    {
                        Unupgraded = new() { [1] = 0.5, [20] = 0.6 },
                        Upgraded = new() { [1] = 0.0 }
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
}
