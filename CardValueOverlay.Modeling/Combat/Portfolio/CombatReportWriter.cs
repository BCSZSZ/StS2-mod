using System.Text;
using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed class CombatReportWriter
{
    public (string JsonPath, string MarkdownPath) Write(
        CombatDeckDeltaReport report,
        string outputDirectory = "data/generated/combat_aware")
    {
        Directory.CreateDirectory(outputDirectory);
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string jsonPath = Path.Combine(outputDirectory, $"combat_deck_delta_{stamp}.generated.json");
        string markdownPath = Path.Combine(outputDirectory, $"combat_deck_delta_{stamp}.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, CombatJson.Options));
        File.WriteAllText(markdownPath, ToMarkdown(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(Path.Combine(outputDirectory, "latest.generated.json"), JsonSerializer.Serialize(report, CombatJson.Options));
        File.WriteAllText(Path.Combine(outputDirectory, "latest.md"), ToMarkdown(report), new UTF8Encoding(false));
        return (jsonPath, markdownPath);
    }

    private static string ToMarkdown(CombatDeckDeltaReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Combat-aware Phase 1 deck dEV review");
        builder.AppendLine();
        builder.AppendLine($"- Candidate: `{report.Candidate}`");
        builder.AppendLine($"- Portfolio: `{report.PortfolioId}` (`{report.Status}`)");
        builder.AppendLine($"- HP calibration: `{report.HpCalibrationId}` (`prior`)");
        builder.AppendLine($"- Ascension: {report.Ascension}");
        builder.AppendLine($"- Runtime candidate: **{report.RuntimeCandidate.ToString().ToLowerInvariant()}**");
        builder.AppendLine();
        builder.AppendLine("> This is a research review artifact. Primary dEV is intentionally withheld until HP parameters and target weights are empirically approved.");
        foreach (CombatHorizonDeltaReport horizon in report.Horizons)
        {
            builder.AppendLine();
            builder.AppendLine($"## Horizon {horizon.Horizon}");
            builder.AppendLine();
            builder.AppendLine($"Research balanced dEV: `{horizon.ResearchBalancedDeltaEv:0.###}` " +
                $"(95% paired CI `{horizon.ConfidenceLow:0.###}` to `{horizon.ConfidenceHigh:0.###}`); " +
                $"supported target mass `{horizon.SupportedTargetWeightMass:P1}`; ESS `{horizon.EffectiveSampleSize:0.0}`.");
            builder.AppendLine();
            builder.AppendLine("| Cell | dEV | CI | n | mean HP loss base/candidate | P(loss>B) base/candidate | death base/candidate | exact |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (CombatCellDeltaReport cell in horizon.Cells)
            {
                builder.AppendLine($"| {cell.CellId} | {cell.DeltaEv:0.###} | [{cell.ConfidenceLow:0.###}, {cell.ConfidenceHigh:0.###}] | {cell.SampleCount} | " +
                    $"{cell.BaselineRisk.MeanLoss:0.##}/{cell.CandidateRisk.MeanLoss:0.##} | " +
                    $"{cell.BaselineRisk.ProbabilityLossExceedsBudget:P1}/{cell.CandidateRisk.ProbabilityLossExceedsBudget:P1} | " +
                    $"{cell.BaselineRisk.DeathProbability:P1}/{cell.CandidateRisk.DeathProbability:P1} | {cell.ExactFraction:P1} |");
            }
        }
        if (report.UnsupportedSamples.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Unsupported samples");
            builder.AppendLine();
            foreach (string item in report.UnsupportedSamples.Take(100)) builder.AppendLine($"- {item}");
            if (report.UnsupportedSamples.Count > 100) builder.AppendLine($"- ... {report.UnsupportedSamples.Count - 100} more in JSON.");
        }
        return builder.ToString();
    }
}
