namespace CodexLanguageFix.Tests;

internal sealed record QualityBootstrapInterval(
    double Estimate,
    double Lower,
    double Upper,
    int Resamples);

internal static class QualityStatistics
{
    public static double F05(double truePositives, double falsePositives, double falseNegatives) =>
        FScore(0.5, truePositives, falsePositives, falseNegatives);

    public static double FScore(
        double beta,
        double truePositives,
        double falsePositives,
        double falseNegatives)
    {
        if (beta <= 0 || double.IsNaN(beta) || double.IsInfinity(beta))
        {
            throw new ArgumentOutOfRangeException(nameof(beta));
        }

        if (truePositives < 0
            || falsePositives < 0
            || falseNegatives < 0
            || double.IsNaN(truePositives)
            || double.IsNaN(falsePositives)
            || double.IsNaN(falseNegatives)
            || double.IsInfinity(truePositives)
            || double.IsInfinity(falsePositives)
            || double.IsInfinity(falseNegatives))
        {
            throw new ArgumentOutOfRangeException(nameof(truePositives));
        }

        if (truePositives == 0 && falsePositives == 0 && falseNegatives == 0)
        {
            return 1;
        }

        var precision = truePositives + falsePositives == 0
            ? 0
            : (double)truePositives / (truePositives + falsePositives);
        var recall = truePositives + falseNegatives == 0
            ? 0
            : (double)truePositives / (truePositives + falseNegatives);
        var betaSquared = beta * beta;
        var denominator = betaSquared * precision + recall;
        return denominator == 0
            ? 0
            : (1 + betaSquared) * precision * recall / denominator;
    }

    public static double Percentile(IEnumerable<double> values, double probability)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (probability is < 0 or > 1 || double.IsNaN(probability))
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        var ordered = values.ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("Mindestens ein Messwert ist erforderlich.", nameof(values));
        }

        if (ordered.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            throw new ArgumentException("Perzentile benötigen endliche Messwerte.", nameof(values));
        }

        Array.Sort(ordered);
        if (ordered.Length == 1)
        {
            return ordered[0];
        }

        var position = probability * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return ordered[lower];
        }

        var fraction = position - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * fraction;
    }

    public static QualityBootstrapInterval BootstrapMean(
        IReadOnlyList<double> values,
        int resamples = 2_000,
        int seed = 17)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Messwert ist erforderlich.", nameof(values));
        }

        var zeros = new double[values.Count];
        return BootstrapMeanDifference(values, zeros, resamples, seed);
    }

    public static QualityBootstrapInterval BootstrapMeanDifference(
        IReadOnlyList<double> candidate,
        IReadOnlyList<double> baseline,
        int resamples = 2_000,
        int seed = 17)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baseline);
        if (candidate.Count == 0 || candidate.Count != baseline.Count)
        {
            throw new ArgumentException("Gepaarte Messreihen müssen gleich lang und nicht leer sein.");
        }

        if (resamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(resamples));
        }

        var differences = new double[candidate.Count];
        for (var index = 0; index < differences.Length; index++)
        {
            differences[index] = candidate[index] - baseline[index];
        }

        if (differences.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            throw new ArgumentException("Bootstrap benötigt endliche Messwerte.");
        }

        var estimate = differences.Average();
        var random = new Random(seed);
        var samples = new double[resamples];
        for (var sample = 0; sample < resamples; sample++)
        {
            var sum = 0d;
            for (var draw = 0; draw < differences.Length; draw++)
            {
                sum += differences[random.Next(differences.Length)];
            }

            samples[sample] = sum / differences.Length;
        }

        return new QualityBootstrapInterval(
            estimate,
            Percentile(samples, 0.025),
            Percentile(samples, 0.975),
            resamples);
    }
}
