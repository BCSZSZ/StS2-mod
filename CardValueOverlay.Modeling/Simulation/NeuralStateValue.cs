using System.Text.Json;

namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Learned state-value evaluator V(s): estimates the forward realized value from a
/// simulation state, given the state's context-feature dictionary. Used as the search's
/// line evaluator (brain 2), replacing the hand-curated setup-priority proxy. The
/// simulator builds the feature dictionary with the same builder used during data
/// collection, so inference features match training features by construction.
/// </summary>
public interface IStateValueEstimator
{
    double Evaluate(IReadOnlyDictionary<string, double> contextFeatures);
}

public sealed class NeuralStateValue : IStateValueEstimator
{
    public const int SupportedSchemaVersion = 1;

    private readonly StateValueModel model;

    private NeuralStateValue(StateValueModel model)
    {
        this.model = model;
    }

    public static NeuralStateValue Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"State-value model file was not found: {path}", path);
        }

        StateValueModel model =
            JsonSerializer.Deserialize<StateValueModel>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Failed to read state-value model from {path}.");
        if (model.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException($"State-value model schemaVersion {model.SchemaVersion} is not supported; expected {SupportedSchemaVersion}.");
        }

        if (model.FeatureNames.Count != model.Normalization.Mean.Count
            || model.FeatureNames.Count != model.Normalization.Std.Count)
        {
            throw new InvalidOperationException("State-value model normalization shape does not match featureNames.");
        }

        if (model.Layers.Count == 0)
        {
            throw new InvalidOperationException("State-value model must contain at least one layer.");
        }

        return new NeuralStateValue(model);
    }

    public double Evaluate(IReadOnlyDictionary<string, double> contextFeatures)
    {
        IReadOnlyDictionary<string, double> features = contextFeatures;

        double[] values = new double[model.FeatureNames.Count];
        for (int i = 0; i < model.FeatureNames.Count; i++)
        {
            features.TryGetValue(model.FeatureNames[i], out double raw);
            double std = model.Normalization.Std[i];
            values[i] = std == 0d ? raw - model.Normalization.Mean[i] : (raw - model.Normalization.Mean[i]) / std;
        }

        // ReLU after every layer except the last (linear head).
        for (int li = 0; li < model.Layers.Count; li++)
        {
            values = ApplyLayer(values, model.Layers[li], applyRelu: li < model.Layers.Count - 1);
        }

        if (values.Length != 1)
        {
            throw new InvalidOperationException($"State-value model output length {values.Length} is invalid; expected 1.");
        }

        // De-normalize back to raw value units.
        return values[0] * model.LabelNormalization.Std + model.LabelNormalization.Mean;
    }

    private static double[] ApplyLayer(double[] input, StateValueLayer layer, bool applyRelu)
    {
        if (layer.Weight.Count != layer.Bias.Count)
        {
            throw new InvalidOperationException("State-value model layer weight/bias shape mismatch.");
        }

        double[] output = new double[layer.Bias.Count];
        for (int row = 0; row < layer.Weight.Count; row++)
        {
            IReadOnlyList<double> weights = layer.Weight[row];
            if (weights.Count != input.Length)
            {
                throw new InvalidOperationException("State-value model layer input shape mismatch.");
            }

            double value = layer.Bias[row];
            for (int col = 0; col < input.Length; col++)
            {
                value += weights[col] * input[col];
            }

            output[row] = applyRelu ? Math.Max(0d, value) : value;
        }

        return output;
    }

    private sealed record StateValueModel(
        int SchemaVersion,
        IReadOnlyList<string> FeatureNames,
        StateValueNormalization Normalization,
        StateValueLabelNormalization LabelNormalization,
        IReadOnlyList<StateValueLayer> Layers);

    private sealed record StateValueNormalization(
        IReadOnlyList<double> Mean,
        IReadOnlyList<double> Std);

    private sealed record StateValueLabelNormalization(
        double Mean,
        double Std);

    private sealed record StateValueLayer(
        IReadOnlyList<IReadOnlyList<double>> Weight,
        IReadOnlyList<double> Bias);
}
