using System.Text.Json;

namespace CodexLanguageFix.Tests;

public sealed class LiveBenchmarkHarnessTests
{
    [Fact]
    public void PromptCatalog_ContainsFiveDistinctVariants()
    {
        Assert.Equal(5, LivePromptCatalog.All.Count);
        Assert.Equal(5, LivePromptCatalog.All.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, LivePromptCatalog.All.Select(item => item.Instructions).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SchedulerMix_IsDeterministicAndPreservesEveryEntry()
    {
        var source = Enumerable.Range(0, 32).ToArray();

        var first = LiveBenchmarkScheduler.Mix(source, 42);
        var second = LiveBenchmarkScheduler.Mix(source, 42);

        Assert.Equal(first, second);
        Assert.Equal(source.Order(), first.Order());
    }

    [Fact]
    public void Scheduler_CreatesFiveByThreeMatrix()
    {
        var configuration = new LiveBenchmarkConfiguration(
            ["baseline", "audit", "safe", "balanced", "ultra"],
            ["none", "low", "medium"],
            32,
            1,
            3,
            42,
            true,
            false,
            120,
            0,
            null,
            null,
            null);

        var cells = LiveBenchmarkScheduler.CreateCells(configuration);

        Assert.Equal(15, cells.Count);
        Assert.Equal(5, cells.Select(item => item.Variant).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, cells.Select(item => item.Effort).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Scheduler_CreatesConfigurableProtocolMatrix()
    {
        var configuration = new LiveBenchmarkConfiguration(
            ["baseline"],
            ["none"],
            32,
            1,
            2,
            42,
            true,
            false,
            120,
            0,
            null,
            null,
            null)
        {
            Protocols = ["full", "segment-all", "segment-500", "segment-1000", "segment-2000", "span"]
        };

        var cells = LiveBenchmarkScheduler.CreateCells(configuration);

        Assert.Equal(6, cells.Count);
        Assert.Equal(
            [
                LiveBenchmarkProtocols.Full,
                LiveBenchmarkProtocols.SegmentedAll,
                LiveBenchmarkProtocols.Segment500,
                LiveBenchmarkProtocols.Segment1000,
                LiveBenchmarkProtocols.Segment2000,
                LiveBenchmarkProtocols.Span
            ],
            cells.Select(item => item.Protocol));
    }

    [Fact]
    public void StagePlan_IsolatesPromptSearchThenRunsProtocolAndFinalQualification()
    {
        Assert.Equal(
            [(1, 4, 1, 0), (2, 8, 1, 1), (3, 2, 1, 1), (4, 8, 1, 1)],
            LiveBenchmarkStagePlan.Definitions
                .Select(item => (item.Stage, item.Tier, item.Repetitions, item.TopVariantsPerEffort)));
        Assert.Equal([4, 8, 2, 8], LiveBenchmarkStagePlan.Definitions.Select(item => item.CaseIds.Count));
        Assert.True(LiveBenchmarkStagePlan.Definitions[2].UsesLongCases);
        Assert.True(LiveBenchmarkStagePlan.Definitions[3].UsesLongCases);
        Assert.Equal(8, LiveBenchmarkStagePlan.LanguageToolCaseIds.Count);
    }

    [Fact]
    public void StagePlan_IsCalibratedForSevenSecondCorrections()
    {
        var matrixMeasurements = new[] { 15 * 4, 3 * 8, 18 * 2, 2 * 8 };

        Assert.Equal([60, 24, 36, 16], matrixMeasurements);
        Assert.Equal(136, matrixMeasurements.Sum());
        Assert.Equal(147, matrixMeasurements.Sum() + 8 + 3); // Providervergleich + drei Capability-Prüfungen
    }

    [Fact]
    public void CompactCorpus_BuildsDeterministicCrLfMarkdownUnicodeProtectedLongCases()
    {
        var corpus = QualityCorpus.Load();

        var first = LiveBenchmarkCompactCorpus.BuildLongCases(corpus);
        var second = LiveBenchmarkCompactCorpus.BuildLongCases(corpus);

        Assert.Equal(first.Select(item => item.Id), second.Select(item => item.Id));
        Assert.Equal(first.Select(item => item.Input), second.Select(item => item.Input));
        Assert.Equal(first.Select(item => item.Target), second.Select(item => item.Target));
        Assert.Equal(
            first.Select(item => string.Join('\u001f', item.MustPreserve)),
            second.Select(item => string.Join('\u001f', item.MustPreserve)));
        Assert.Equal(["LONG-DEEN-0900", "LONG-DEEN-2400"], first.Select(item => item.Id));
        Assert.InRange(first[0].Input.Length, 900, 1_050);
        Assert.InRange(first[1].Input.Length, 2_400, 2_550);
        Assert.All(first, item =>
        {
            Assert.Contains("\r\n\r\n", item.Input, StringComparison.Ordinal);
            Assert.Contains("**", item.Input, StringComparison.Ordinal);
            Assert.Contains("„", item.Input, StringComparison.Ordinal);
            Assert.NotEmpty(item.MustPreserve);
            Assert.True(CorpusQualityEvaluator.Evaluate(item, item.Target).HardGatePassed);
        });
    }

    [Fact]
    public void ReportWriter_GivesEveryStageANonCollidingDurablePath()
    {
        var reportPath = Path.Combine(Path.GetTempPath(), "qualification.json");

        var first = LiveBenchmarkReportWriter.ResolveStagePath(reportPath, 1);
        var fourth = LiveBenchmarkReportWriter.ResolveStagePath(reportPath, 4);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "qualification-stage-1.json"), first);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "qualification-stage-4.json"), fourth);
        Assert.NotEqual(reportPath, first);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LiveBenchmarkReportWriter.ResolveStagePath(reportPath, 0));
    }

    [Fact]
    public void StagePlan_SelectsTheFastestHighQualityVariantsPerEffort()
    {
        var cells = new List<LiveBenchmarkCellReport>();
        foreach (var effort in new[] { "none", "low", "medium" })
        {
            cells.Add(Cell("baseline", effort, correctedExact: 6, p95: 300));
            cells.Add(Cell("audit", effort, correctedExact: 9, p95: 200));
            cells.Add(Cell("safe", effort, correctedExact: 9, p95: 150));
        }
        var observations = cells.SelectMany(cell => Enumerable.Range(0, cell.Total).Select(index =>
            new LiveBenchmarkObservation(
                "luna",
                cell.Variant,
                cell.Effort,
                1,
                $"{cell.Variant}-{cell.Effort}-{index:D2}",
                index < 8 ? "de" : "en",
                index < 8 ? "correction" : "control",
                index >= 8,
                true,
                true,
                100,
                100,
                null)
            {
                Protocol = cell.Protocol
            })).ToArray();

        var report = new LiveBenchmarkReport(
            3,
            "run",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            Path.Combine(Path.GetTempPath(), "report.json"),
            42,
            32,
            32,
            "hash",
            "gpt-5.6-luna",
            "priority",
            1,
            2,
            true,
            [],
            ["none", "low", "medium"],
            [],
            cells,
            observations,
            []);

        var selected = LiveBenchmarkStagePlan.SelectTopCells(report, variantsPerEffort: 2);

        Assert.Equal(6, selected.Count);
        Assert.Equal(3, selected.Select(item => item.Effort).Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            selected.GroupBy(item => item.Effort),
            group => Assert.Equal(["audit", "safe"], group.Select(item => item.Variant)));
    }

    [Fact]
    public void QualityConfidenceInterval_IsFiniteAndFailsWithoutObservations()
    {
        Assert.InRange(LiveBenchmarkQuality.ConfidenceIntervalWidth(8, 10), 0d, 1d);
        Assert.True(LiveBenchmarkQuality.ConfidenceIntervalWidth(0, 0) >= 1d);
    }

    [Fact]
    public void CorpusLoader_ReadsEveryRootArrayAndKeepsUnsupportedLanguageAsControl()
    {
        var path = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "de": [{"id":"DE1","language":"de","kind":"correction","category":"test","tags":["test"],"input":"falsch","target":"richtig","acceptedOutputs":["richtig"],"weight":1}],
              "xx": [{"id":"XX1","language":"xx","kind":"control","category":"unsupported-language","tags":["control"],"input":"保持原文","target":"保持原文","acceptedOutputs":["保持原文"],"weight":1}]
            }
            """);

        try
        {
            var cases = QualityCorpus.Load(path);

            Assert.Equal(["DE1", "XX1"], cases.Select(item => item.Id));
            Assert.True(cases.Single(item => item.Language == "xx").IsControl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProviderComparisonSelection_IsBilingualAndContainsControlsAndRetentionCases()
    {
        var selected = LiveBenchmarkRunner.SelectRepresentativeProviderCases(
            QualityCorpus.Load(),
            maximum: 20,
            seed: 42);

        Assert.Equal(20, selected.Length);
        Assert.Contains(selected, item => item.Language == "de" && !item.IsControl);
        Assert.Contains(selected, item => item.Language == "en" && !item.IsControl);
        Assert.Contains(selected, item => item.Language == "de" && item.IsControl);
        Assert.Contains(selected, item => item.Language == "en" && item.IsControl);
        Assert.Contains(selected, item => item.Language == "es" && item.IsControl);
        Assert.Contains(selected, item => item.Language == "fr" && item.IsControl);
    }

    [Fact]
    public void ColdStartSelection_RemainsBilingualAndContainsControls()
    {
        var selected = LiveBenchmarkRunner.SelectRepresentativeProviderCases(
            QualityCorpus.Load(),
            maximum: 10,
            seed: 42);

        Assert.Equal(10, selected.Length);
        Assert.Contains(selected, item => item.Language == "de" && !item.IsControl);
        Assert.Contains(selected, item => item.Language == "en" && !item.IsControl);
        Assert.Contains(selected, item => item.IsControl);
    }

    [Fact]
    public void ReportSerialization_ContainsNoCorpusText()
    {
        var report = new LiveBenchmarkReport(
            3,
            "run",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            Path.Combine(Path.GetTempPath(), "report.json"),
            42,
            32,
            1,
            "hash",
            "gpt-5.6-luna",
            "priority",
            1,
            2,
            true,
            [new LiveBenchmarkPromptMetadata("baseline", "prompt-hash", 10)],
            ["none"],
            [new LiveBenchmarkCapability("none", true, null)],
            [],
            [new LiveBenchmarkObservation("luna", "baseline", "none", 1, "DE001", "de", "spelling", false, true, false, 10, 9, null)],
            []);

        var json = JsonSerializer.Serialize(report);

        Assert.DoesNotContain("source_text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Target text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"input\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"target\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"correctedText\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HardAssertions_RejectRequestedMissingLanguageToolComparison()
    {
        var report = new LiveBenchmarkReport(
            3,
            "run",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            Path.Combine(Path.GetTempPath(), "report.json"),
            42,
            32,
            1,
            "hash",
            "gpt-5.6-luna",
            "priority",
            1,
            2,
            true,
            [],
            ["none"],
            [],
            [],
            [],
            [])
        {
            LanguageToolComparisonRequested = true
        };
        var configuration = new LiveBenchmarkConfiguration(
            ["baseline"],
            ["none"],
            32,
            1,
            2,
            42,
            true,
            true,
            120,
            0,
            null,
            null,
            null)
        {
            RequirePerformanceGate = false
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LiveBenchmarkHardAssertions.Validate(report, configuration, 1));

        Assert.Contains("LanguageTool", exception.Message, StringComparison.Ordinal);
    }

    private static LiveBenchmarkCellReport Cell(
        string variant,
        string effort,
        int correctedExact,
        double p95) =>
        new(
            "luna",
            variant,
            effort,
            1,
            10,
            10,
            0,
            correctedExact + 2,
            correctedExact,
            10,
            2,
            2,
            100,
            100,
            p95,
            100,
            0,
            null,
            []);
}
