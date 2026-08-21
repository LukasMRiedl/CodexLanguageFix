using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LiveLunaPromptBenchmarkTests
{
    [Fact]
    public async Task PromptEffortMatrix_IsMeasuredAgainstSelectedCorpus()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LUNA_BENCHMARK"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var configuration = LiveBenchmarkConfiguration.FromEnvironment();
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "benchmark_cases.json");
        var corpus = LiveBenchmarkCorpusSelection.LoadForBenchmark(sourcePath);
        var selected = LiveBenchmarkCorpusSelection.Select(corpus, configuration);
        var run = await LiveBenchmarkRunner.RunAsync(
            configuration,
            configuration.AutomaticSearch ? corpus : selected,
            CancellationToken.None);

        LiveBenchmarkReportWriter.Write(run.Report);
        LiveBenchmarkHardAssertions.Validate(run.Report, configuration, run.Report.CorpusCount);
    }
}
