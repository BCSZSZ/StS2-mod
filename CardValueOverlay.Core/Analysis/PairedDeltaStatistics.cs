namespace CardValueOverlay.Core.Analysis;

public sealed record PairedDeltaSummary(
    int Count,
    double Mean,
    double SampleStandardDeviation,
    double StandardError,
    double LowerConfidence,
    double UpperConfidence,
    double LowerStopping,
    double UpperStopping)
{
    public bool HasStableSign => LowerStopping > 0d || UpperStopping < 0d;
}

/// <summary>
/// Computes paired Student-t intervals at realtime checkpoints. The displayed
/// interval uses the configured marginal confidence level. The stopping interval
/// spends the same family-wise alpha across all planned stopping looks using a
/// Bonferroni correction, preventing repeated peeking from inflating false stops.
/// </summary>
public static class PairedDeltaStatistics
{
    public static PairedDeltaSummary Calculate(
        IReadOnlyList<double> pairedDifferences,
        int confidenceLevelPercent,
        int plannedStoppingLooks)
    {
        ArgumentNullException.ThrowIfNull(pairedDifferences);
        int count = pairedDifferences.Count;
        if (count < 15 || count > 60 || count % 15 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pairedDifferences),
                count,
                "Paired realtime samples must contain a 15-run checkpoint from 15 through 60 runs.");
        }

        if (confidenceLevelPercent is < 80 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceLevelPercent));
        }

        if (plannedStoppingLooks is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedStoppingLooks));
        }

        double mean = 0d;
        double m2 = 0d;
        int seen = 0;
        foreach (double value in pairedDifferences)
        {
            seen++;
            double delta = value - mean;
            mean += delta / seen;
            m2 += delta * (value - mean);
        }

        double sampleVariance = m2 / (count - 1);
        double standardDeviation = Math.Sqrt(Math.Max(0d, sampleVariance));
        double standardError = standardDeviation / Math.Sqrt(count);
        double familyAlpha = 1d - (confidenceLevelPercent / 100d);
        double displayProbability = 1d - (familyAlpha / 2d);
        double stoppingProbability = 1d - (familyAlpha / (2d * plannedStoppingLooks));
        double displayHalfWidth = StudentTCriticalValue(displayProbability, count - 1) * standardError;
        double stoppingHalfWidth = StudentTCriticalValue(stoppingProbability, count - 1) * standardError;

        return new PairedDeltaSummary(
            count,
            mean,
            standardDeviation,
            standardError,
            mean - displayHalfWidth,
            mean + displayHalfWidth,
            mean - stoppingHalfWidth,
            mean + stoppingHalfWidth);
    }

    private static double StudentTCriticalValue(double probability, int degreesOfFreedom)
    {
        if (probability <= 0.5d || probability >= 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        if (degreesOfFreedom < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degreesOfFreedom));
        }

        // Cornish-Fisher expansion of the Student-t quantile around the normal quantile.
        // Realtime checkpoints have df >= 14. The fourth-order expansion keeps the runtime Core
        // assembly independent of a numerical library while remaining accurate enough for the
        // one-decimal interval displayed by the overlay.
        double z = InverseStandardNormal(probability);
        double z2 = z * z;
        double z3 = z2 * z;
        double z5 = z3 * z2;
        double z7 = z5 * z2;
        double z9 = z7 * z2;
        double v = degreesOfFreedom;
        double v2 = v * v;
        double v3 = v2 * v;
        double v4 = v3 * v;
        return z
            + ((z3 + z) / (4d * v))
            + ((5d * z5 + 16d * z3 + 3d * z) / (96d * v2))
            + ((3d * z7 + 19d * z5 + 17d * z3 - 15d * z) / (384d * v3))
            + ((79d * z9 + 776d * z7 + 1482d * z5 - 1920d * z3 - 945d * z) / (92160d * v4));
    }

    private static double InverseStandardNormal(double probability)
    {
        // Peter J. Acklam's rational approximation. Absolute error is below 1.2e-9 over
        // the probability range exposed by the settings (including Bonferroni tails).
        const double low = 0.02425d;
        const double high = 1d - low;
        double[] a =
        [
            -3.969683028665376e+01d, 2.209460984245205e+02d,
            -2.759285104469687e+02d, 1.383577518672690e+02d,
            -3.066479806614716e+01d, 2.506628277459239e+00d
        ];
        double[] b =
        [
            -5.447609879822406e+01d, 1.615858368580409e+02d,
            -1.556989798598866e+02d, 6.680131188771972e+01d,
            -1.328068155288572e+01d
        ];
        double[] c =
        [
            -7.784894002430293e-03d, -3.223964580411365e-01d,
            -2.400758277161838e+00d, -2.549732539343734e+00d,
            4.374664141464968e+00d, 2.938163982698783e+00d
        ];
        double[] d =
        [
            7.784695709041462e-03d, 3.224671290700398e-01d,
            2.445134137142996e+00d, 3.754408661907416e+00d
        ];

        if (probability < low)
        {
            double q = Math.Sqrt(-2d * Math.Log(probability));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1d);
        }

        if (probability > high)
        {
            double q = Math.Sqrt(-2d * Math.Log(1d - probability));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1d);
        }

        double centered = probability - 0.5d;
        double r = centered * centered;
        return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * centered
            / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1d);
    }
}
