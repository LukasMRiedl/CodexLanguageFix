using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class BenchmarkCorpusTests
{
    [Fact]
    public void Corpus_HasExactly128CasesWithPlannedLanguageDistributionAndExplicitOracles()
    {
        var cases = QualityCorpus.Load();

        Assert.Equal(128, cases.Count);
        Assert.Equal(128, cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(60, cases.Count(item => item.Language == "de"));
        Assert.Equal(60, cases.Count(item => item.Language == "en"));
        Assert.Equal(4, cases.Count(item => item.Language == "es"));
        Assert.Equal(4, cases.Count(item => item.Language == "fr"));
        Assert.Equal(96, cases.Count(item => (item.Language is "de" or "en") && !item.IsControl));
        Assert.Equal(24, cases.Count(item => (item.Language is "de" or "en") && item.IsControl));
        Assert.Equal(8, cases.Count(item => item.Language is "es" or "fr"));
        Assert.All(cases.Where(item => item.Language is "es" or "fr"), item => Assert.True(item.IsControl));

        foreach (var benchmarkCase in cases)
        {
            Assert.Contains(benchmarkCase.Target, benchmarkCase.AcceptableOutputs);
            Assert.NotEmpty(benchmarkCase.Tags);
            Assert.NotEmpty(benchmarkCase.Category);
            Assert.NotEmpty(benchmarkCase.Input);
            Assert.True(benchmarkCase.Weight > 0);
            Assert.NotEmpty(benchmarkCase.AcceptableOutputs);
            if (benchmarkCase.IsControl)
            {
                Assert.Equal(benchmarkCase.Input, benchmarkCase.Target);
                Assert.Contains(benchmarkCase.Input, benchmarkCase.AcceptableOutputs);
            }
            else
            {
                Assert.NotEqual(benchmarkCase.Input, benchmarkCase.Target);
                Assert.All(benchmarkCase.AcceptableOutputs, output => Assert.NotEqual(benchmarkCase.Input, output));
            }
        }
    }

    [Fact]
    public void Corpus_CanonicalTargetsPassEveryOfflineHardGate()
    {
        foreach (var benchmarkCase in QualityCorpus.Load())
        {
            var evaluation = CorpusQualityEvaluator.Evaluate(benchmarkCase, benchmarkCase.Target);

            Assert.True(
                evaluation.HardGatePassed,
                $"Kanonisches Ziel verletzt ein Korpus-Gate: {benchmarkCase.Id}");
        }
    }

}
