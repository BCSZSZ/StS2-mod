using System.Text.Json;

namespace CardValueOverlay.Modeling.Simulation;

public sealed class NeuralSearchCardScorer : ISearchCardScorer
{
    public const int SupportedSchemaVersion = 1;
    public const int SupportedFeatureVersion = 1;
    public const string UnknownCardToken = "<UNK>";

    private readonly SearchPolicyRankerModel model;
    private readonly Dictionary<string, int> cardIndexes;

    private NeuralSearchCardScorer(SearchPolicyRankerModel model)
    {
        this.model = model;
        cardIndexes = model.CardIdVocab
            .Select((cardId, index) => (cardId, index))
            .ToDictionary(pair => pair.cardId, pair => pair.index, StringComparer.OrdinalIgnoreCase);
    }

    public static NeuralSearchCardScorer Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Search policy model file was not found: {path}", path);
        }

        SearchPolicyRankerModel model =
            JsonSerializer.Deserialize<SearchPolicyRankerModel>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Failed to read search policy model from {path}.");
        if (model.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException($"Search policy model schemaVersion {model.SchemaVersion} is not supported; expected {SupportedSchemaVersion}.");
        }

        if (model.FeatureVersion != SupportedFeatureVersion)
        {
            throw new InvalidOperationException($"Search policy model featureVersion {model.FeatureVersion} is not supported; expected {SupportedFeatureVersion}.");
        }

        if (model.NumericFeatureNames.Count != model.Normalization.Mean.Count
            || model.NumericFeatureNames.Count != model.Normalization.Std.Count)
        {
            throw new InvalidOperationException("Search policy model normalization shape does not match numericFeatureNames.");
        }

        if (model.CardIdVocab.Count == 0)
        {
            throw new InvalidOperationException("Search policy model cardIdVocab must contain at least the unknown bucket.");
        }

        if (!model.CardIdVocab.Contains(UnknownCardToken, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Search policy model cardIdVocab must contain {UnknownCardToken}.");
        }

        return new NeuralSearchCardScorer(model);
    }

    public decimal Score(SearchCardScoringContext context)
    {
        double[] input = BuildInput(context);
        double[] values = input;
        foreach (SearchPolicyRankerLayer layer in model.Layers)
        {
            values = ApplyLayer(values, layer);
        }

        if (values.Length != 1)
        {
            throw new InvalidOperationException($"Search policy model output length {values.Length} is invalid; expected 1.");
        }

        return (decimal)values[0];
    }

    private double[] BuildInput(SearchCardScoringContext context)
    {
        double[] input = new double[model.NumericFeatureNames.Count + model.CardIdVocab.Count];
        for (int i = 0; i < model.NumericFeatureNames.Count; i++)
        {
            context.Features.TryGetValue(model.NumericFeatureNames[i], out double raw);
            double std = model.Normalization.Std[i];
            input[i] = std == 0d ? raw - model.Normalization.Mean[i] : (raw - model.Normalization.Mean[i]) / std;
        }

        int cardOffset = model.NumericFeatureNames.Count;
        int cardIndex = cardIndexes.TryGetValue(context.CardModelId, out int knownIndex)
            ? knownIndex
            : cardIndexes.TryGetValue(UnknownCardToken, out int unknownIndex)
                ? unknownIndex
                : 0;
        input[cardOffset + cardIndex] = 1d;
        return input;
    }

    private static double[] ApplyLayer(double[] input, SearchPolicyRankerLayer layer)
    {
        if (layer.Weights.Count != layer.Bias.Count)
        {
            throw new InvalidOperationException("Search policy model layer weights/bias shape mismatch.");
        }

        double[] output = new double[layer.Bias.Count];
        for (int row = 0; row < layer.Weights.Count; row++)
        {
            IReadOnlyList<double> weights = layer.Weights[row];
            if (weights.Count != input.Length)
            {
                throw new InvalidOperationException("Search policy model layer input shape mismatch.");
            }

            double value = layer.Bias[row];
            for (int col = 0; col < input.Length; col++)
            {
                value += weights[col] * input[col];
            }

            output[row] = layer.Activation.ToLowerInvariant() switch
            {
                "relu" => Math.Max(0d, value),
                "linear" => value,
                _ => throw new InvalidOperationException($"Search policy model activation '{layer.Activation}' is not supported.")
            };
        }

        return output;
    }

    private sealed record SearchPolicyRankerModel(
        int SchemaVersion,
        int FeatureVersion,
        IReadOnlyList<string> NumericFeatureNames,
        IReadOnlyList<string> CardIdVocab,
        SearchPolicyRankerNormalization Normalization,
        IReadOnlyList<SearchPolicyRankerLayer> Layers,
        SearchPolicyRankerMetadata? Metadata);

    private sealed record SearchPolicyRankerNormalization(
        IReadOnlyList<double> Mean,
        IReadOnlyList<double> Std);

    private sealed record SearchPolicyRankerLayer(
        IReadOnlyList<IReadOnlyList<double>> Weights,
        IReadOnlyList<double> Bias,
        string Activation);

    private sealed record SearchPolicyRankerMetadata(
        string? TrainingDatasetHash,
        string? CreatedAt,
        int? TeacherMaxBranchingCards,
        int? TeacherMaxCardsPlayedPerTurn);
}
