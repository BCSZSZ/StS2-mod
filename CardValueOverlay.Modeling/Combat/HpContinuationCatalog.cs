using System.Text.Json;
using CardValueOverlay.Modeling.Combat.Portfolio;

namespace CardValueOverlay.Modeling.Combat;

public sealed record HpContinuationCalibration(
    int SchemaVersion,
    string CalibrationId,
    string Status,
    string ScaleAnchor,
    IReadOnlyList<HpContinuationContext> Contexts,
    IReadOnlyList<double> LambdaSafeSensitivity,
    IReadOnlyList<double> KappaSensitivity,
    IReadOnlyList<double> FutureReserveSensitivity,
    IReadOnlyList<string> Notes);

public sealed record HpContinuationContext(
    string Id,
    int Act,
    string Tier,
    int LossBudget,
    double LambdaSafe,
    double Kappa,
    double FutureReserveValue);

public sealed class HpContinuationCatalog
{
    private readonly Dictionary<string, HpContinuationContext> _contexts;

    public HpContinuationCatalog(HpContinuationCalibration calibration, string contentHash)
    {
        Calibration = calibration;
        ContentHash = contentHash;
        Validate(calibration);
        _contexts = calibration.Contexts.ToDictionary(context => context.Id, StringComparer.Ordinal);
    }

    public HpContinuationCalibration Calibration { get; }
    public string ContentHash { get; }

    public HpContinuationContext Get(string id) =>
        _contexts.TryGetValue(id, out HpContinuationContext? context)
            ? context
            : throw new KeyNotFoundException($"HP continuation context '{id}' was not found.");

    public static HpContinuationCatalog Load(string path)
    {
        HpContinuationCalibration calibration = JsonSerializer.Deserialize<HpContinuationCalibration>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"HP continuation file '{path}' is empty.");
        return new HpContinuationCatalog(calibration, CombatJson.Sha256File(path));
    }

    public static void Validate(HpContinuationCalibration calibration)
    {
        if (calibration.SchemaVersion != 1)
        {
            throw new InvalidOperationException("HP continuation schemaVersion must be 1.");
        }

        if (!string.Equals(calibration.Status, "prior", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Phase 1 HP continuation parameters must remain status='prior'.");
        }

        IReadOnlyList<(int Act, string Tier, int Budget)> expected = CombatPortfolioRules.ExpectedCells;
        if (calibration.Contexts.Count != expected.Count)
        {
            throw new InvalidOperationException("HP continuation calibration must define exactly twelve act/tier contexts.");
        }

        foreach ((int act, string tier, int budget) in expected)
        {
            HpContinuationContext? context = calibration.Contexts.SingleOrDefault(candidate =>
                candidate.Act == act && string.Equals(candidate.Tier, tier, StringComparison.OrdinalIgnoreCase));
            if (context is null || context.LossBudget != budget)
            {
                throw new InvalidOperationException($"HP context act {act}/{tier} must use confirmed loss budget {budget}.");
            }

            if (context.LambdaSafe <= 0 || context.Kappa < 0 || context.FutureReserveValue < 0)
            {
                throw new InvalidOperationException($"HP context '{context.Id}' has invalid prior parameters.");
            }
        }

        if (calibration.Contexts.Select(context => context.Id).Distinct(StringComparer.Ordinal).Count() != 12)
        {
            throw new InvalidOperationException("HP continuation context ids must be unique.");
        }
    }
}
