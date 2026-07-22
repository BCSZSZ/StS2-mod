using System.Text;
using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record HpSensitivityPoint(
    string ContextId,
    int Act,
    string Tier,
    int LossBudget,
    double LambdaSafe,
    double Kappa,
    double FutureReserveValue,
    double UtilityAtFullHp,
    double UtilityAtBudgetHp,
    double UtilityTenPastBudget,
    double MarginalAtHighHp,
    double MarginalAtLowHp);

public sealed record HpSensitivityReport(
    int SchemaVersion,
    string GeneratedAt,
    string CalibrationId,
    string CalibrationHash,
    string Status,
    bool Empirical,
    bool Approved,
    int AssumedMaxHp,
    IReadOnlyList<double> LambdaSafeSensitivity,
    IReadOnlyList<double> KappaSensitivity,
    IReadOnlyList<double> FutureReserveSensitivity,
    IReadOnlyList<HpSensitivityPoint> Contexts,
    IReadOnlyList<string> Notes);

public sealed class HpSensitivityReportWriter
{
    public (string JsonPath, string MarkdownPath) Write(
        HpContinuationCatalog catalog,
        string outputDirectory,
        int assumedMaxHp = 80)
    {
        HpContinuationEvaluator evaluator = new();
        HpSensitivityPoint[] points = catalog.Calibration.Contexts
            .OrderBy(context => context.Act)
            .ThenBy(context => context.Tier, StringComparer.Ordinal)
            .Select(context =>
            {
                int budgetHp = Math.Max(1, assumedMaxHp - context.LossBudget);
                int pastBudgetHp = Math.Max(1, budgetHp - 10);
                return new HpSensitivityPoint(
                    context.Id,
                    context.Act,
                    context.Tier,
                    context.LossBudget,
                    context.LambdaSafe,
                    context.Kappa,
                    context.FutureReserveValue,
                    evaluator.EvaluateAlive(assumedMaxHp, assumedMaxHp, context),
                    evaluator.EvaluateAlive(budgetHp, assumedMaxHp, context),
                    evaluator.EvaluateAlive(pastBudgetHp, assumedMaxHp, context),
                    evaluator.MarginalHpValue(Math.Max(1, assumedMaxHp - 2), assumedMaxHp, context),
                    evaluator.MarginalHpValue(1, assumedMaxHp, context));
            })
            .ToArray();
        HpSensitivityReport report = new(
            1,
            DateTimeOffset.UtcNow.ToString("O"),
            catalog.Calibration.CalibrationId,
            catalog.ContentHash,
            "prior-sensitivity-only",
            false,
            false,
            assumedMaxHp,
            catalog.Calibration.LambdaSafeSensitivity,
            catalog.Calibration.KappaSensitivity,
            catalog.Calibration.FutureReserveSensitivity,
            points,
            [
                "Loss budgets are confirmed soft knees, never free HP allowances or damage targets.",
                "lambdaSafe, kappa, and futureReserveValue are uncalibrated priors.",
                "Failure-inclusive history plus HEAL/SMITH and HP-trade revealed preference are required before approval."
            ]);

        Directory.CreateDirectory(outputDirectory);
        string jsonPath = Path.Combine(outputDirectory, "hp_continuation_sensitivity.generated.json");
        string markdownPath = Path.Combine(outputDirectory, "hp_continuation_sensitivity.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, CombatJson.Options));
        File.WriteAllText(markdownPath, ToMarkdown(report), new UTF8Encoding(false));
        return (jsonPath, markdownPath);
    }

    private static string ToMarkdown(HpSensitivityReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# HP continuation sensitivity (Phase 1 prior)");
        builder.AppendLine();
        builder.AppendLine("Status: **prior-sensitivity-only**; empirical: **false**; approved: **false**.");
        builder.AppendLine();
        builder.AppendLine("| Context | B | lambda | kappa | reserve | Phi(full) | Phi(at B) | Phi(B+10 loss) | high-HP marginal | low-HP marginal |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (HpSensitivityPoint point in report.Contexts)
        {
            builder.AppendLine($"| {point.ContextId} | {point.LossBudget} | {point.LambdaSafe:0.###} | {point.Kappa:0.###} | {point.FutureReserveValue:0.##} | " +
                $"{point.UtilityAtFullHp:0.###} | {point.UtilityAtBudgetHp:0.###} | {point.UtilityTenPastBudget:0.###} | " +
                $"{point.MarginalAtHighHp:0.###} | {point.MarginalAtLowHp:0.###} |");
        }
        builder.AppendLine();
        foreach (string note in report.Notes) builder.AppendLine($"- {note}");
        return builder.ToString();
    }
}
