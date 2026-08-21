namespace CodexLanguageFix.Tests;

public sealed class CorpusQualityEvaluatorTests
{
    [Fact]
    public void Evaluate_AcceptsADeclaredAlternativeAndPreservesHardGates()
    {
        var benchmarkCase = QualityCorpus.Load().Single(item => item.Id == "EN017");
        var alternative = benchmarkCase.AcceptableOutputs[1];

        var evaluation = CorpusQualityEvaluator.Evaluate(benchmarkCase, alternative);

        Assert.True(benchmarkCase.HasAlternatives);
        Assert.True(evaluation.Accepted);
        Assert.True(evaluation.MinimalEditPassed);
        Assert.True(evaluation.HardGatePassed);
    }

    [Fact]
    public void Evaluate_RejectsMutationOfMustPreserveContent()
    {
        var benchmarkCase = QualityCorpus.Load().Single(item => item.Id == "DE022");
        var mutated = benchmarkCase.Target.Replace("`var eror = 1;`", "`var error = 1;`", StringComparison.Ordinal);

        var evaluation = CorpusQualityEvaluator.Evaluate(benchmarkCase, mutated);

        Assert.True(evaluation.Accepted == false);
        Assert.False(evaluation.MustPreservePassed);
        Assert.False(evaluation.HardGatePassed);
    }

    [Fact]
    public void Evaluate_RejectsReorderedProtectedContent()
    {
        var benchmarkCase = QualityCorpus.Load().Single(item => item.Id == "DE028");
        var reordered = benchmarkCase.Target
            .Replace(
                "`var eror = 1;` bleibt unverändert: https://example.test/falsch",
                "https://example.test/falsch bleibt unverändert: `var eror = 1;`",
                StringComparison.Ordinal);

        var evaluation = CorpusQualityEvaluator.Evaluate(benchmarkCase, reordered);

        Assert.False(evaluation.MustPreservePassed);
        Assert.False(evaluation.HardGatePassed);
    }

    [Fact]
    public void Evaluate_RejectsControlChangesAndCountsOvercorrectionAsFalsePositive()
    {
        var benchmarkCase = QualityCorpus.Load().Single(item => item.Id == "DE027");
        var changed = benchmarkCase.Input.Replace("Geheimnisse", "Werkzeuge", StringComparison.Ordinal);

        var evaluation = CorpusQualityEvaluator.Evaluate(benchmarkCase, changed);
        var score = CorpusQualityEvaluator.Score([evaluation]);

        Assert.True(evaluation.ActualChanged);
        Assert.False(evaluation.ControlUnchanged);
        Assert.True(evaluation.IsFalsePositive);
        Assert.Equal(1, score.FalsePositives);
        Assert.Equal(0, score.F05);
    }

    [Fact]
    public void Evaluate_RejectsForbiddenTextAndMissingCorrection()
    {
        var spellingCase = QualityCorpus.Load().Single(item => item.Id == "DE002");

        var unchanged = CorpusQualityEvaluator.Evaluate(spellingCase, spellingCase.Input);
        var typo = CorpusQualityEvaluator.Evaluate(spellingCase, "Der Bericht ist vollstädig.");

        Assert.False(unchanged.HardGatePassed);
        Assert.True(unchanged.IsFalseNegative);
        Assert.False(typo.MustNotContainPassed);
        Assert.False(typo.HardGatePassed);
    }

    [Fact]
    public void Score_PenalizesAChangedWrongCorrectionAsFalsePositiveAndFalseNegative()
    {
        var benchmarkCase = QualityCorpus.Load().Single(item => item.Id == "DE002");
        var evaluation = CorpusQualityEvaluator.Evaluate(
            benchmarkCase,
            "Der Bericht ist vollständig!");
        var score = CorpusQualityEvaluator.Score([evaluation]);

        Assert.True(evaluation.ActualChanged);
        Assert.True(evaluation.IsFalsePositive);
        Assert.True(evaluation.IsFalseNegative);
        Assert.Equal(1, score.FalsePositives);
        Assert.Equal(1, score.FalseNegatives);
        Assert.Equal(0, score.F05);
    }

    [Fact]
    public void Score_ComputesCorrectionF05FromTrueFalsePositivesAndNegatives()
    {
        var cases = QualityCorpus.Load();
        var truePositive = CorpusQualityEvaluator.Evaluate(
            cases.Single(item => item.Id == "DE001"),
            "Ich weiß, dass du kommst.");
        var falseNegative = CorpusQualityEvaluator.Evaluate(
            cases.Single(item => item.Id == "DE002"),
            "Der Bericht ist vollstädig.");
        var falsePositive = CorpusQualityEvaluator.Evaluate(
            cases.Single(item => item.Id == "DE025"),
            "Der Prozess hat sich aufgehängt, obwohl nicht genügend Speicher verfügbar war.");

        var score = CorpusQualityEvaluator.Score([truePositive, falseNegative, falsePositive]);

        Assert.Equal(1, score.TruePositives);
        Assert.Equal(1, score.FalseNegatives);
        Assert.Equal(1, score.FalsePositives);
        Assert.Equal(1, score.Controls);
        Assert.Equal(0, score.ControlsPassed);
        Assert.Equal(0.5, score.F05, precision: 12);
        Assert.False(score.HardGatesPassed);
    }
}
