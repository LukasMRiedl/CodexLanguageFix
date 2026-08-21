namespace CodexLanguageFix.Tests;

public sealed class QualityStatisticsTests
{
    [Fact]
    public void F05_WeightsPrecisionMoreThanRecall()
    {
        var score = QualityStatistics.F05(truePositives: 8, falsePositives: 2, falseNegatives: 4);

        Assert.Equal(0.7692307692307692, score, precision: 12);
    }

    [Fact]
    public void F05_ReturnsOneForAnEmptyEvaluationAndZeroForMissedCorrections()
    {
        Assert.Equal(1, QualityStatistics.F05(0, 0, 0));
        Assert.Equal(0, QualityStatistics.F05(0, 0, 3));
    }

    [Fact]
    public void Percentile_UsesLinearInterpolationAndRejectsInvalidInputs()
    {
        var values = new[] { 40d, 10d, 30d, 20d };

        Assert.Equal(10, QualityStatistics.Percentile(values, 0));
        Assert.Equal(25, QualityStatistics.Percentile(values, 0.5));
        Assert.Equal(38.5, QualityStatistics.Percentile(values, 0.95));
        Assert.Equal(40, QualityStatistics.Percentile(values, 1));
        Assert.Throws<ArgumentException>(() => QualityStatistics.Percentile([], 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityStatistics.Percentile(values, 1.1));
    }

    [Fact]
    public void BootstrapMeanDifference_IsSeededAndReturnsAStableInterval()
    {
        var candidate = new[] { 110d, 120d, 130d, 140d };
        var baseline = new[] { 100d, 100d, 100d, 100d };

        var first = QualityStatistics.BootstrapMeanDifference(candidate, baseline, 500, seed: 42);
        var second = QualityStatistics.BootstrapMeanDifference(candidate, baseline, 500, seed: 42);

        Assert.Equal(first, second);
        Assert.Equal(25, first.Estimate);
        Assert.True(first.Lower <= first.Estimate);
        Assert.True(first.Estimate <= first.Upper);
        Assert.Equal(500, first.Resamples);
    }

    [Fact]
    public void BootstrapMeanDifference_RequiresPairedNonEmptySeries()
    {
        Assert.Throws<ArgumentException>(() =>
            QualityStatistics.BootstrapMeanDifference([1d], [], 100));
        Assert.Throws<ArgumentException>(() =>
            QualityStatistics.BootstrapMeanDifference([1d, 2d], [1d], 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QualityStatistics.BootstrapMeanDifference([1d], [1d], 0));
    }
}
