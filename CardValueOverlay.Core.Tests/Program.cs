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
            ManualValueIsFallback();
            EmptyValueStaysEmpty();
            ConfigParsesAndValidates();
            AverageIgnoresMissingValues();
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
            new Dictionary<string, double?> { ["strike"] = 9 });

        AssertEqual(9, value.Value, nameof(DynamicValueWins));
        AssertEqual(ValueSource.Dynamic, value.Source, nameof(DynamicValueWins));
    }

    private static void ManualValueIsFallback()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue("strike");

        AssertEqual(1.5, value.Value, nameof(ManualValueIsFallback));
        AssertEqual(ValueSource.Manual, value.Source, nameof(ManualValueIsFallback));
    }

    private static void EmptyValueStaysEmpty()
    {
        ValueResolver resolver = CreateResolver();
        EffectiveValue<double> value = resolver.ResolveCardValue("unknown");

        AssertEqual(null, value.Value, nameof(EmptyValueStaysEmpty));
        AssertEqual(ValueSource.None, value.Source, nameof(EmptyValueStaysEmpty));
    }

    private static void ConfigParsesAndValidates()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "overlay": { "displayMode": "fixedText", "fixedText": "T", "maxLines": 3 },
          "cards": { "strike": { "manualValue": 1.5 } },
          "commonParameters": {
            "deck_count": { "fixedValue": null },
            "cards_drawn_per_turn": { "fixedValue": 5 },
            "turns_per_shuffle_cycle": { "fixedValue": null }
          }
        }
        """;

        CardValueConfig config = CardValueConfigLoader.LoadFromJson(json);
        ConfigValidationResult result = CardValueConfigLoader.Validate(config);

        AssertTrue(result.IsValid, nameof(ConfigParsesAndValidates));
        AssertEqual(1.5, config.Cards["strike"].ManualValue, nameof(ConfigParsesAndValidates));
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

    private static ValueResolver CreateResolver()
    {
        CardValueConfig config = new()
        {
            Cards = new Dictionary<string, CardValueEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["strike"] = new() { ManualValue = 1.5 },
                ["defend"] = new() { ManualValue = 2.5 }
            },
            CommonParameters = CardValueConfig.CreateDefault().CommonParameters
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
