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

    [Fact]
    public void GermanAndEnglishCorpus_StillExercisesCorrectionEngineOffsets()
    {
        var engine = new CorrectionEngine();
        var cases = QualityCorpus.Load()
            .Where(item => item.Language is "de" or "en")
            .Where(item => !item.Tags.Contains("protected", StringComparer.Ordinal))
            .ToArray();

        foreach (var benchmarkCase in cases)
        {
            var prompt = engine.Annotate(benchmarkCase.Input);
            IReadOnlyList<LanguageToolMatch> matches;
            if (benchmarkCase.IsControl)
            {
                matches = [];
            }
            else
            {
                var (offset, inputLength, replacement) = SingleReplacement(
                    benchmarkCase.Input,
                    benchmarkCase.Target);
                matches =
                [
                    new LanguageToolMatch(
                        offset,
                        inputLength,
                        [replacement],
                        "CORPUS",
                        "TEST",
                        "test",
                        1,
                        0)
                ];
            }

            var outcome = engine.Apply(benchmarkCase.Input, prompt, matches);
            Assert.Equal(benchmarkCase.Target, outcome.CorrectedText);
        }
    }

    private static (int Offset, int InputLength, string Replacement) SingleReplacement(string input, string target)
    {
        var prefix = 0;
        while (prefix < input.Length
               && prefix < target.Length
               && input[prefix] == target[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < input.Length - prefix
               && suffix < target.Length - prefix
               && input[input.Length - suffix - 1] == target[target.Length - suffix - 1])
        {
            suffix++;
        }

        return (
            prefix,
            input.Length - prefix - suffix,
            target.Substring(prefix, target.Length - prefix - suffix));
    }
}
