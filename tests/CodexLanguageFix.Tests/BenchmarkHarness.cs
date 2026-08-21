using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

internal sealed record LivePromptVariant(string Id, string Instructions);

internal sealed record LiveBenchmarkCorpusSource(string Sha256, string License);

internal sealed record LiveBenchmarkCell(string Variant, string Effort, string Protocol = LiveBenchmarkProtocols.Full)
{
    public LiveBenchmarkCell NormalizeProtocol() => this with { Protocol = LiveBenchmarkProtocols.Normalize(Protocol) };
}

internal static class LiveBenchmarkProtocols
{
    public const string Full = "full-v1";
    public const string SegmentedAll = "segment-all-v1";
    public const string Segment500 = "segment-500-v1";
    public const string Segment1000 = "segment-1000-v1";
    public const string Segment2000 = "segment-2000-v1";
    public const string Span = "span-v1";

    public static IReadOnlyList<string> All { get; } =
    [Full, SegmentedAll, Segment500, Segment1000, Segment2000, Span];

    public static string Normalize(string protocol) => protocol.Trim().ToLowerInvariant() switch
    {
        "full" or "full-v1" => Full,
        "segment-all" or "segment-all-v1" or "segments" => SegmentedAll,
        "segment-500" or "segment-500-v1" => Segment500,
        "segment-1000" or "segment-1000-v1" => Segment1000,
        "segment-2000" or "segment-2000-v1" => Segment2000,
        "span" or "span-v1" => Span,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unbekanntes Luna-Ausgabeprotokoll.")
    };

    public static int PatchThreshold(string protocol) => Normalize(protocol) switch
    {
        Full => int.MaxValue,
        SegmentedAll => 1,
        Segment500 => 500,
        Segment1000 => 1_000,
        Segment2000 => 2_000,
        Span => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol)
    };

    public static bool IsSupportedByCurrentProvider(string protocol)
    {
        _ = Normalize(protocol);
        return true;
    }
}

internal sealed record LiveBenchmarkConfiguration(
    IReadOnlyList<string> PromptVariants,
    IReadOnlyList<string> Efforts,
    int Tier,
    int Repetitions,
    int Parallelism,
    int Seed,
    bool Warmup,
    bool CompareLanguageTool,
    int TimeoutSeconds,
    int MinimumCorrectedExact,
    string? ReportPath,
    IReadOnlySet<string>? SelectedCaseIds,
    IReadOnlySet<string>? LanguageToolCaseIds)
{
    // Die Standardausführung des opt-in-Livetests sucht automatisch in drei
    // Stufen. Unit-Tests und gezielte Einzelmessungen können diese Orchestrierung
    // mit `AutomaticSearch = false` deaktivieren.
    public bool AutomaticSearch { get; init; }

    public bool RequirePerformanceGate { get; init; }

    public IReadOnlyList<LiveBenchmarkCell>? SelectedCells { get; init; }

    public int LanguageToolRepetitions { get; init; } = 3;

    public int LanguageToolMaxStartsPerMinute { get; init; } = 20;

    /// <summary>
    /// Ausgabeprotokolle, die zusätzlich zum Volltextpfad gemessen werden.
    /// Die Voreinstellung bleibt absichtlich beim produktiven Volltextpfad;
    /// die Protokollmatrix wird opt-in über die Umgebungsvariable aktiviert.
    /// </summary>
    public IReadOnlyList<string> Protocols { get; init; } = [LiveBenchmarkProtocols.Full];

    /// <summary>Warme Provider-Vergleiche verwenden standardmäßig drei Runden mit 20 Fällen.</summary>
    public int WarmLanguageToolRounds { get; init; } = 3;

    public int WarmLanguageToolCases { get; init; } = 20;

    /// <summary>Kaltstarts werden mit einem neu erzeugten App-Server-Client gemessen.</summary>
    public int ColdStartCount { get; init; } = 10;

    /// <summary>
    /// Ein schemaidentischer, inhaltsloser Warm-up darf separat bewertet werden.
    /// Er überträgt keinen Composer-Inhalt.
    /// </summary>
    public bool ContentlessSchemaWarmup { get; init; } = true;

    /// <summary>Reine Zielgröße; der vollständige Lauf wird niemals aufgrund dieser Angabe abgebrochen.</summary>
    public int TargetLiveDurationMinutes { get; init; } = 20;

    public static LiveBenchmarkConfiguration FromEnvironment()
    {
        var variants = ReadList("CODEX_LANGUAGE_FIX_PROMPT_VARIANTS");
        if (variants.Count == 0)
        {
            variants = ["baseline", "audit", "safe", "balanced", "ultra"];
        }

        var efforts = ReadList("CODEX_LANGUAGE_FIX_BENCHMARK_EFFORTS");
        if (efforts.Count == 0)
        {
            efforts = ["none", "low", "medium"];
        }

        var tier = ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_TIER", 32, 32, 128);
        if (tier is not (32 or 64 or 128))
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Die Benchmark-Stufe muss 32, 64 oder 128 sein.");
        }

        return new LiveBenchmarkConfiguration(
            variants,
            efforts,
            tier,
            ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_REPETITIONS", 3, 1, 20),
            ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_PARALLELISM", 2, 1, 4),
            ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_SEED", 42, int.MinValue, int.MaxValue),
            ReadBool("CODEX_LANGUAGE_FIX_BENCHMARK_WARMUP", true),
            ReadBool(
                "CODEX_LANGUAGE_FIX_BENCHMARK_COMPARE_LANGUAGETOOL",
                ReadBool("CODEX_LANGUAGE_FIX_BENCHMARK_LANGUAGETOOL", false)),
            ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_TIMEOUT_SECONDS", 120, 1, 600),
            ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_MIN_CORRECTED_EXACT", 0, 0, int.MaxValue),
            Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_BENCHMARK_REPORT"),
            ReadSet("CODEX_LANGUAGE_FIX_BENCHMARK_CASES"),
            ReadSet("CODEX_LANGUAGE_FIX_BENCHMARK_LANGUAGETOOL_CASES"))
        {
            AutomaticSearch = ReadBool("CODEX_LANGUAGE_FIX_BENCHMARK_AUTO_SEARCH", true),
            RequirePerformanceGate = ReadBool("CODEX_LANGUAGE_FIX_BENCHMARK_REQUIRE_PERFORMANCE_GATE", true),
            Protocols = ReadProtocols(),
            WarmLanguageToolRounds = ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_WARM_ROUNDS", 3, 1, 3),
            WarmLanguageToolCases = ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_WARM_CASES", 20, 1, 20),
            ColdStartCount = ReadInt("CODEX_LANGUAGE_FIX_BENCHMARK_COLD_STARTS", 10, 0, 10),
            ContentlessSchemaWarmup = ReadBool("CODEX_LANGUAGE_FIX_BENCHMARK_SCHEMA_WARMUP", true)
        };
    }

    private static IReadOnlyList<string> ReadProtocols()
    {
        var configured = ReadList("CODEX_LANGUAGE_FIX_BENCHMARK_PROTOCOLS");
        if (configured.Count == 0)
        {
            return LiveBenchmarkProtocols.All;
        }

        return configured
            .Select(LiveBenchmarkProtocols.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadList(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(value => value.Length > 0)
        .ToArray();

    private static IReadOnlySet<string>? ReadSet(string name)
    {
        var values = ReadList(name);
        if (values.Count == 0)
        {
            return null;
        }

        var set = values.ToHashSet(StringComparer.Ordinal);
        if (set.Count != values.Count)
        {
            throw new InvalidOperationException($"Die Umgebungsvariable '{name}' enthält doppelte Werte.");
        }

        return set;
    }

    private static int ReadInt(string name, int defaultValue, int minimum, int maximum)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value))
        {
            throw new FormatException($"Die Umgebungsvariable '{name}' muss eine Ganzzahl sein.");
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static bool ReadBool(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Trim() switch
        {
            "1" or "true" or "TRUE" or "True" => true,
            "0" or "false" or "FALSE" or "False" => false,
            _ => throw new FormatException($"Die Umgebungsvariable '{name}' muss 0/1 oder true/false sein.")
        };
    }
}

internal static class LivePromptCatalog
{
    private static readonly string[] Ids = ["baseline", "audit", "safe", "balanced", "ultra"];

    public static IReadOnlyList<LivePromptVariant> All =>
        Ids.Select(id => new LivePromptVariant(id, LunaPromptCatalog.Get(id))).ToArray();

    public static LivePromptVariant Get(string id) =>
        new(id, LunaPromptCatalog.Get(id));
}

internal static class LiveBenchmarkCorpusSelection
{
    public static IReadOnlyList<CorpusCase> LoadForBenchmark(string? path = null)
    {
        var cases = QualityCorpus.Load(path).ToList();
        var ids = cases.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var sourcePaths = ReadList("CODEX_LANGUAGE_FIX_BENCHMARK_LICENSED_CORPORA");
        if (sourcePaths.Count == 0)
        {
            return cases;
        }

        var licenses = ReadList("CODEX_LANGUAGE_FIX_BENCHMARK_LICENSES");
        if (licenses.Count != sourcePaths.Count)
        {
            throw new InvalidOperationException(
                "Jede zusätzliche Korpusdatei benötigt eine gleichnamig positionierte Lizenzangabe in 'CODEX_LANGUAGE_FIX_BENCHMARK_LICENSES'.");
        }

        foreach (var sourcePath in sourcePaths)
        {
            var sourceCases = QualityCorpus.Load(Path.GetFullPath(sourcePath));
            foreach (var item in sourceCases)
            {
                if (!ids.Add(item.Id))
                {
                    throw new InvalidDataException($"Doppelte Korpus-ID über Quellen hinweg: '{item.Id}'.");
                }

                cases.Add(item);
            }
        }

        return cases;
    }

    public static IReadOnlyList<LiveBenchmarkCorpusSource> LicensedSources()
    {
        var sourcePaths = ReadList("CODEX_LANGUAGE_FIX_BENCHMARK_LICENSED_CORPORA");
        var licenses = ReadList("CODEX_LANGUAGE_FIX_BENCHMARK_LICENSES");
        if (sourcePaths.Count == 0)
        {
            return [];
        }

        if (sourcePaths.Count != licenses.Count)
        {
            throw new InvalidOperationException(
                "Die Anzahl der Lizenzangaben entspricht nicht der Anzahl zusätzlicher Korpusdateien.");
        }

        return sourcePaths
            .Select((path, index) => new LiveBenchmarkCorpusSource(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(path)))).ToLowerInvariant(),
                licenses[index]))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadList(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .ToArray();

    public static IReadOnlyList<CorpusCase> Select(
        IReadOnlyList<CorpusCase> corpus,
        LiveBenchmarkConfiguration configuration)
    {
        if (configuration.SelectedCaseIds is { Count: > 0 } selectedIds)
        {
            var selected = corpus
                .Where(item => selectedIds.Contains(item.Id))
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            if (selected.Length != selectedIds.Count)
            {
                var missing = selectedIds.Except(selected.Select(item => item.Id), StringComparer.Ordinal);
                throw new InvalidOperationException($"Unbekannte Benchmark-IDs: {string.Join(", ", missing)}");
            }

            return selected;
        }

        if (corpus.Count <= configuration.Tier)
        {
            return corpus.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        }

        var groups = corpus
            .GroupBy(item => item.Language, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray())
            .ToArray();
        var denominator = groups.Sum(group => group.Length);
        var quotas = groups
            .Select(group => (int)Math.Floor((double)configuration.Tier * group.Length / denominator))
            .ToArray();
        var remaining = configuration.Tier - quotas.Sum();
        foreach (var allocation in groups
                     .Select((group, index) => new
                     {
                         Index = index,
                         Fraction = (double)configuration.Tier * group.Length / denominator - quotas[index]
                     })
                     .OrderByDescending(item => item.Fraction)
                     .ThenBy(item => item.Index))
        {
            if (remaining-- <= 0)
            {
                break;
            }

            quotas[allocation.Index]++;
        }

        return groups
            .SelectMany((group, index) => EvenlyTake(group, quotas[index]))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string Hash(IReadOnlyList<CorpusCase> cases)
    {
        var canonical = string.Join(
            '\n',
            cases.Select(item => string.Join(
                '\u001f',
                item.Id,
                item.Language,
                item.Kind,
                item.Category,
                item.Input,
                item.Target,
                item.IsControl ? "1" : "0")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static IReadOnlyList<CorpusCase> EvenlyTake(IReadOnlyList<CorpusCase> source, int count)
    {
        if (count >= source.Count)
        {
            return source;
        }

        if (count == 1)
        {
            return [source[source.Count / 2]];
        }

        return Enumerable.Range(0, count)
            .Select(index => source[(int)Math.Round((double)index * (source.Count - 1) / (count - 1))])
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }
}

internal static class LiveBenchmarkScheduler
{
    public static IReadOnlyList<LiveBenchmarkCell> CreateCells(LiveBenchmarkConfiguration configuration)
    {
        if (configuration.SelectedCells is { Count: > 0 } selectedCells)
        {
            var cells = selectedCells
                .Select(cell => new LiveBenchmarkCell(
                    cell.Variant,
                    NormalizeEffort(cell.Effort),
                    LiveBenchmarkProtocols.Normalize(cell.Protocol)))
                .Distinct()
                .OrderBy(cell => cell.Variant, StringComparer.Ordinal)
                .ThenBy(cell => cell.Effort, StringComparer.Ordinal)
                .ThenBy(cell => cell.Protocol, StringComparer.Ordinal)
                .ToArray();
            if (cells.Length == 0)
            {
                throw new InvalidOperationException("Die ausgewählte Benchmark-Matrix ist leer.");
            }

            // Die Validierung ruft den zentralen Katalog auf und verhindert,
            // dass eine Stufe versehentlich eine unbekannte Promptvariante misst.
            foreach (var cell in cells)
            {
                _ = LivePromptCatalog.Get(cell.Variant);
            }

            return cells;
        }

        var variants = configuration.PromptVariants.Select(LivePromptCatalog.Get).Select(item => item.Id).ToArray();
        var efforts = configuration.Efforts.Select(NormalizeEffort).Distinct(StringComparer.Ordinal).ToArray();
        var protocols = configuration.Protocols
            .Select(LiveBenchmarkProtocols.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return variants
            .SelectMany(variant => efforts.SelectMany(effort => protocols.Select(protocol =>
                new LiveBenchmarkCell(variant, effort, protocol))))
            .ToArray();
    }

    public static IReadOnlyList<T> Mix<T>(IEnumerable<T> values, int seed)
    {
        var result = values.ToArray();
        var random = new Random(seed);
        for (var index = result.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (result[index], result[swap]) = (result[swap], result[index]);
        }

        return result;
    }

    public static string NormalizeEffort(string effort) => effort switch
    {
        "none" or "low" or "medium" => effort,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "Unbekannte Luna-Denkstufe.")
    };
}

internal static class LiveBenchmarkStatistics
{
    public static double Mean(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? double.NaN : array.Average();
    }
}

internal sealed record LiveBenchmarkObservation(
    string Provider,
    string Variant,
    string? Effort,
    int Repetition,
    string CaseId,
    string Language,
    string Category,
    bool Control,
    bool Succeeded,
    bool Exact,
    double DurationMs,
    double? ProviderDurationMs,
    string? ErrorType)
{
    public string Protocol { get; init; } = LiveBenchmarkProtocols.Full;

    /// <summary>Teilzeiten des transport-unabhängigen Luna-Ausführungsergebnisses.</summary>
    public double? ServerAccountCheckMs { get; init; }

    public double? ThreadProvisionMs { get; init; }

    public double? TurnStartMs { get; init; }

    public double? TimeToFirstTextDeltaMs { get; init; }

    public double? ModelCompletionMs { get; init; }

    public double? SchemaValidationMs { get; init; }

    public double? PlaceholderValidationMs { get; init; }

    public double? RestoreMs { get; init; }

    public double? ComposerWriteMs { get; init; }

    public double? CleanupMs { get; init; }

    public int OutputCharacters { get; init; }

    public string WarmupKind { get; init; } = "none";

    public bool ColdStart { get; init; }

    public double Weight { get; init; } = 1;

    public bool TruePositive { get; init; }

    public bool FalsePositive { get; init; }

    public bool FalseNegative { get; init; }

    public bool SafetyCritical { get; init; }
}

internal sealed record LiveBenchmarkFailure(
    string CaseId,
    int Repetition,
    string Reason,
    double DurationMs);

internal sealed record LiveBenchmarkCellReport(
    string Provider,
    string Variant,
    string? Effort,
    int Repetitions,
    int Total,
    int Successful,
    int Errors,
    int Exact,
    int CorrectedCasesExact,
    int CorrectedCasesTotal,
    int ControlsExact,
    int ControlsTotal,
    double MeanDurationMs,
    double P50DurationMs,
    double P95DurationMs,
    double? MeanProviderDurationMs,
    int WarmupCount,
    double? WarmupMeanDurationMs,
    IReadOnlyList<LiveBenchmarkFailure> Failures)
{
    public string Protocol { get; init; } = LiveBenchmarkProtocols.Full;

    public double? MeanTimeToFirstTextDeltaMs { get; init; }

    public double? P50TimeToFirstTextDeltaMs { get; init; }

    public double? P95TimeToFirstTextDeltaMs { get; init; }

    public double? MeanModelCompletionMs { get; init; }

    public double? P50ModelCompletionMs { get; init; }

    public double? P95ModelCompletionMs { get; init; }

    public double? MeanSchemaValidationMs { get; init; }

    public double? MeanPlaceholderValidationMs { get; init; }

    public double? MeanComposerWriteMs { get; init; }

    public double? MeanCleanupMs { get; init; }

    public double? MeanOutputCharacters { get; init; }

    public string WarmupKind { get; init; } = "none";

    public int ColdStartCount { get; init; }

    public double? WeightedTruePositives { get; init; }

    public double? WeightedFalsePositives { get; init; }

    public double? WeightedFalseNegatives { get; init; }

    public double F05 => WeightedTruePositives.HasValue
        ? QualityStatistics.F05(
            WeightedTruePositives.Value,
            WeightedFalsePositives ?? 0,
            WeightedFalseNegatives ?? 0)
        : QualityStatistics.F05(
            CorrectedCasesExact,
            ControlsTotal - ControlsExact,
            CorrectedCasesTotal - CorrectedCasesExact);

    public double F05ConfidenceIntervalWidth =>
        LiveBenchmarkQuality.ConfidenceIntervalWidth(
            CorrectedCasesExact + ControlsExact,
            CorrectedCasesTotal + ControlsTotal);
}

internal sealed record LiveBenchmarkPromptMetadata(
    string Id,
    string Sha256,
    int CharacterCount);

internal sealed record LiveBenchmarkCapability(
    string Effort,
    bool Supported,
    string? Reason);

internal sealed record LiveBenchmarkSkippedCell(
    string Provider,
    string Variant,
    string Effort,
    string Reason)
{
    public string Protocol { get; init; } = LiveBenchmarkProtocols.Full;
}

internal sealed record LiveBenchmarkStageSummary(
    int Stage,
    int Tier,
    int Repetitions,
    int CorpusCount,
    double WallDurationMs,
    IReadOnlyList<LiveBenchmarkCellReport> Cells,
    IReadOnlyList<LiveBenchmarkSkippedCell> SkippedCells)
{
    public IReadOnlyList<LiveBenchmarkObservation> Observations { get; init; } = [];

    public int PlannedMeasurements { get; init; }

    public int ExecutedMeasurements { get; init; }

    public bool CompleteCoverage { get; init; }
}

internal sealed record LiveBenchmarkQualityGate(
    string Provider,
    string Variant,
    string? Effort,
    double F05,
    double F05ConfidenceIntervalWidth,
    double? P50RatioToBaseline,
    double? P95RatioToBaseline,
    bool QualityNonInferior,
    bool P50Within90Percent,
    bool P95Within90Percent,
    bool ConfidenceIntervalUnderOne,
    bool Passed)
{
    public string Protocol { get; init; } = LiveBenchmarkProtocols.Full;

    public double? QualityDifferenceLower95 { get; init; }

    public double? P50RatioUpper95 { get; init; }

    public double? P95RatioUpper95 { get; init; }
}

internal sealed record LiveBenchmarkProviderComparison(
    string Provider,
    string BaselineProvider,
    int PairedCases,
    int PairedRepetitions,
    double LunaF05,
    double LanguageToolF05,
    double? P50Ratio,
    double? P95Ratio,
    bool QualityNonInferior,
    bool Faster,
    double ConfidenceIntervalWidth,
    bool Passed)
{
    public string Protocol { get; init; } = LiveBenchmarkProtocols.Full;

    public double? QualityDifferenceLower95 { get; init; }

    public double? P50RatioUpper95 { get; init; }

    public double? P95RatioUpper95 { get; init; }
}

internal sealed record LiveBenchmarkReport(
    int SchemaVersion,
    string RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double WallDurationMs,
    string ReportPath,
    int Seed,
    int Tier,
    int CorpusCount,
    string CorpusSha256,
    string Model,
    string ServiceTier,
    int Repetitions,
    int MaxParallelism,
    bool Warmup,
    IReadOnlyList<LiveBenchmarkPromptMetadata> Prompts,
    IReadOnlyList<string> Efforts,
    IReadOnlyList<LiveBenchmarkCapability> Capabilities,
    IReadOnlyList<LiveBenchmarkCellReport> Cells,
    IReadOnlyList<LiveBenchmarkObservation> Observations,
    IReadOnlyList<LiveBenchmarkFailure> WarmupFailures)
{
    public string Commit { get; init; } = "unknown";

    public string Protocol { get; init; } = "full-v1";

    public IReadOnlyList<string> Protocols { get; init; } = [LiveBenchmarkProtocols.Full];

    public string WarmupKind { get; init; } = "contentful-prime";

    public int WarmLanguageToolRounds { get; init; }

    public int WarmLanguageToolCases { get; init; }

    public int ColdStartCount { get; init; }

    public IReadOnlyList<LiveBenchmarkObservation> ColdStartObservations { get; init; } = [];

    public IReadOnlyList<LiveBenchmarkObservation> WarmupObservations { get; init; } = [];

    public IReadOnlyList<LiveBenchmarkCorpusSource> LicensedCorpusSources { get; init; } = [];

    public bool LanguageToolComparisonRequested { get; init; }

    public string Temperature { get; init; } = "warm";

    public IReadOnlyList<LiveBenchmarkSkippedCell> SkippedCells { get; init; } = [];

    public IReadOnlyList<LiveBenchmarkStageSummary> Stages { get; init; } = [];

    public IReadOnlyList<LiveBenchmarkQualityGate> QualityGates { get; init; } = [];

    public LiveBenchmarkProviderComparison? LanguageToolComparison { get; init; }

    public int TargetLiveDurationMinutes { get; init; } = 20;

    public int PlannedMeasurements { get; init; }

    public int ExecutedMeasurements { get; init; }

    public bool CompleteStageCoverage { get; init; }
}

internal sealed record LiveBenchmarkRunResult(LiveBenchmarkReport Report);

internal sealed record LiveBenchmarkStageDefinition(
    int Stage,
    int Tier,
    int Repetitions,
    int TopVariantsPerEffort,
    IReadOnlyList<string> CaseIds,
    bool UsesLongCases = false);

internal static class LiveBenchmarkStagePlan
{
    public static IReadOnlyList<LiveBenchmarkStageDefinition> Definitions { get; } =
    [
        new(1, 4, 1, 0, ["DE017", "EN052", "DE027", "FR003"]),
        new(2, 8, 1, 1, ["DE017", "DE043", "DE056", "EN017", "EN052", "DE059", "EN057", "ES001"]),
        new(3, 2, 1, 1, ["LONG-DEEN-0900", "LONG-DEEN-2400"], UsesLongCases: true),
        new(4, 8, 1, 1, ["DE017", "DE043", "EN017", "EN052", "DE059", "EN057", "LONG-DEEN-0900", "LONG-DEEN-2400"], UsesLongCases: true)
    ];

    public static IReadOnlyList<string> LanguageToolCaseIds { get; } =
        ["DE017", "DE043", "EN017", "EN052", "DE059", "EN057", "DE026", "EN026"];

    public static IReadOnlyList<LiveBenchmarkCell> SelectTopCells(
        LiveBenchmarkReport report,
        int variantsPerEffort)
    {
        if (variantsPerEffort < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(variantsPerEffort));
        }

        var cells = report.Cells
            .Where(cell => cell.Provider == "luna"
                           && cell.Effort is not null
                           && IsSafe(report, cell))
            .GroupBy(cell => cell.Effort!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(cell => cell.F05)
                .ThenBy(cell => cell.P95DurationMs)
                .ThenBy(cell => cell.P50DurationMs)
                .ThenBy(cell => cell.Variant, StringComparer.Ordinal)
                .Take(variantsPerEffort)
                .Select(cell => new LiveBenchmarkCell(cell.Variant, group.Key, cell.Protocol)))
            .Distinct()
            .OrderBy(cell => cell.Variant, StringComparer.Ordinal)
            .ThenBy(cell => cell.Effort, StringComparer.Ordinal)
            .ToArray();

        if (cells.Length == 0)
        {
            throw new InvalidOperationException("Die Stufenauswahl besitzt keine fehlerfreien Luna-Zellen.");
        }

        return cells;
    }

    public static LiveBenchmarkCell SelectBestCell(LiveBenchmarkReport report)
    {
        var cell = report.Cells
            .Where(item => item.Provider == "luna" && item.Effort is not null && IsSafe(report, item))
            .OrderByDescending(item => item.F05)
            .ThenBy(item => item.P95DurationMs)
            .ThenBy(item => item.P50DurationMs)
            .ThenBy(item => LivePromptCatalog.Get(item.Variant).Instructions.Length)
            .ThenBy(item => item.Variant, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Die Finalauswahl besitzt keine fehlerfreie Luna-Zelle.");
        return new LiveBenchmarkCell(cell.Variant, cell.Effort!, cell.Protocol);
    }

    public static LiveBenchmarkCell SelectProductionWinner(LiveBenchmarkReport report)
    {
        var baseline = report.Cells.SingleOrDefault(item =>
            item.Provider == "luna"
            && item.Variant == "baseline"
            && item.Effort == "none"
            && item.Protocol == LiveBenchmarkProtocols.Full);
        if (baseline is null || !IsSafe(report, baseline))
        {
            throw new InvalidOperationException("Die produktive Baseline fehlt oder besteht die Sicherheitsgatter nicht.");
        }

        var winner = report.Cells
            .Where(item => item.Provider == "luna"
                           && item.Effort is not null
                           && IsSafe(report, item)
                           && item.F05 + 1e-12 >= baseline.F05)
            .OrderByDescending(item => item.F05)
            .ThenBy(item => item.P95DurationMs)
            .ThenBy(item => item.P50DurationMs)
            .ThenBy(item => LivePromptCatalog.Get(item.Variant).Instructions.Length)
            .ThenBy(item => item.Variant, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? baseline;
        return new LiveBenchmarkCell(winner.Variant, winner.Effort!, winner.Protocol);
    }

    private static bool IsSafe(LiveBenchmarkReport report, LiveBenchmarkCellReport cell)
    {
        if (cell.Errors != 0 || cell.ControlsExact != cell.ControlsTotal)
        {
            return false;
        }

        var observations = report.Observations.Where(item =>
                item.Provider == cell.Provider
                && item.Variant == cell.Variant
                && item.Effort == cell.Effort
                && item.Protocol == cell.Protocol)
            .ToArray();
        return observations.Length == cell.Total
               && observations.All(item => item.Succeeded && (!item.SafetyCritical || item.Exact));
    }
}

internal static class LiveBenchmarkCompactCorpus
{
    private static readonly string[] ComponentIds = ["DE022", "DE048", "DE055", "EN049", "EN053", "EN054"];

    public static IReadOnlyList<CorpusCase> SelectStageCases(
        IReadOnlyList<CorpusCase> corpus,
        LiveBenchmarkStageDefinition definition)
    {
        var available = definition.UsesLongCases
            ? corpus.Concat(BuildLongCases(corpus)).ToArray()
            : corpus;
        var byId = available.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var missing = definition.CaseIds.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Unbekannte kompakte Benchmark-IDs: {string.Join(", ", missing)}");
        }

        return definition.CaseIds.Select(id => byId[id]).ToArray();
    }

    public static IReadOnlyList<CorpusCase> BuildLongCases(IReadOnlyList<CorpusCase> corpus)
    {
        var byId = corpus.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var components = ComponentIds.Select(id => byId.TryGetValue(id, out var item)
                ? item
                : throw new InvalidOperationException($"Der Langtextbaustein {id} fehlt."))
            .ToArray();
        return [Build("LONG-DEEN-0900", 900, components), Build("LONG-DEEN-2400", 2_400, components)];
    }

    private static CorpusCase Build(string id, int minimumLength, IReadOnlyList<CorpusCase> components)
    {
        const string separator = "\r\n\r\n";
        var inputs = new List<string>();
        var targets = new List<string>();
        for (var index = 0; inputs.Count == 0 || string.Join(separator, inputs).Length < minimumLength; index++)
        {
            var component = components[index % components.Count];
            inputs.Add(component.Input);
            targets.Add(component.Target);
        }

        var input = string.Join(separator, inputs);
        var target = string.Join(separator, targets);
        return new CorpusCase(
            id,
            "de-en",
            "correction",
            "long-protocol-stress",
            ["long", "markdown", "unicode", "line-breaks", "protected", "technical"],
            input,
            target,
            [target],
            components.SelectMany(item => item.MustPreserve).Distinct(StringComparer.Ordinal).ToArray(),
            [],
            [],
            null,
            2);
    }
}

internal sealed class LiveBenchmarkRuntime : IDisposable
{
    private LiveBenchmarkRuntime(
        CodexAppServerClient client,
        AppLocalizer localizer,
        IReadOnlyList<LiveBenchmarkCapability> capabilities)
    {
        Client = client;
        Localizer = localizer;
        Capabilities = capabilities;
    }

    public CodexAppServerClient Client { get; private set; }

    public AppLocalizer Localizer { get; }

    public IReadOnlyList<LiveBenchmarkCapability> Capabilities { get; }

    public async Task RenewClientAsync(CancellationToken cancellationToken)
    {
        Client.Dispose();
        var runtimeDirectory = CreateRuntimeDirectory();
        var replacement = new CodexAppServerClient(runtimeDirectory, Localizer);
        try
        {
            var account = await replacement.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            if (!account.IsChatGpt)
            {
                throw new InvalidOperationException("Für den Live-Benchmark muss Codex mit ChatGPT-OAuth angemeldet sein.");
            }

            Client = replacement;
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    public static async Task<LiveBenchmarkRuntime> CreateAsync(
        IReadOnlyList<string> efforts,
        CancellationToken cancellationToken)
    {
        var localizer = new AppLocalizer("de");
        var runtimeDirectory = CreateRuntimeDirectory();
        var client = new CodexAppServerClient(runtimeDirectory, localizer);
        try
        {
            var account = await client.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            if (!account.IsChatGpt)
            {
                throw new InvalidOperationException("Für den Live-Benchmark muss Codex mit ChatGPT-OAuth angemeldet sein.");
            }

            var capabilities = new List<LiveBenchmarkCapability>();
            foreach (var effort in efforts.Select(LiveBenchmarkScheduler.NormalizeEffort).Distinct(StringComparer.Ordinal))
            {
                var supported = await client.SupportsLunaEffortAsync(effort, cancellationToken).ConfigureAwait(false);
                string? reason = supported ? null : "Der aktive Codex-Modellkatalog unterstützt diese Denkstufe nicht.";
                if (supported)
                {
                    try
                    {
                        await client.RunStructuredCorrectionWithTimingAsync(
                            JsonSerializer.Serialize(new { source_text = string.Empty }),
                            LunaCorrectionProvider.OutputSchema,
                            LunaPromptCatalog.Baseline,
                            effort,
                            cancellationToken,
                            replenishPreparedThread: false).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        supported = false;
                        reason = $"Echte Luna-Anfrage abgelehnt: {exception.GetType().Name}.";
                    }
                }

                capabilities.Add(new LiveBenchmarkCapability(effort, supported, reason));
            }

            return new LiveBenchmarkRuntime(client, localizer, capabilities);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Dispose() => Client.Dispose();

    private static string CreateRuntimeDirectory() => Path.Combine(
        Path.GetTempPath(),
        "CodexLanguageFix-Luna-Benchmark-v3",
        $"stage-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
}

internal static class LiveBenchmarkRunner
{
    public static async Task<LiveBenchmarkRunResult> RunAsync(
        LiveBenchmarkConfiguration configuration,
        IReadOnlyList<CorpusCase> corpus,
        CancellationToken cancellationToken)
    {
        var totalWall = Stopwatch.StartNew();
        using var runtime = await LiveBenchmarkRuntime.CreateAsync(configuration.Efforts, cancellationToken).ConfigureAwait(false);
        var result = configuration.AutomaticSearch
            ? await RunStagedAsync(configuration, corpus, runtime, cancellationToken).ConfigureAwait(false)
            : await RunSingleAsync(configuration, corpus, runtime, cancellationToken).ConfigureAwait(false);
        totalWall.Stop();
        return new LiveBenchmarkRunResult(result.Report with
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            WallDurationMs = totalWall.Elapsed.TotalMilliseconds,
            TargetLiveDurationMinutes = configuration.TargetLiveDurationMinutes
        });
    }

    private static async Task<LiveBenchmarkRunResult> RunSingleAsync(
        LiveBenchmarkConfiguration configuration,
        IReadOnlyList<CorpusCase> corpus,
        LiveBenchmarkRuntime runtime,
        CancellationToken cancellationToken,
        IReadOnlyList<CorpusCase>? providerComparisonCorpus = null)
    {
        if (corpus.Count == 0)
        {
            throw new InvalidOperationException("Das ausgewählte Benchmark-Korpus ist leer.");
        }

        var cells = LiveBenchmarkScheduler.CreateCells(configuration);
        if (cells.Count == 0)
        {
            throw new InvalidOperationException("Die Benchmark-Matrix ist leer.");
        }

        var prompts = cells
            .Select(cell => LivePromptCatalog.Get(cell.Variant))
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var reportPath = LiveBenchmarkReportWriter.ResolvePath(configuration.ReportPath, runId);
        var startedAt = DateTimeOffset.UtcNow;
        var wall = Stopwatch.StartNew();
        var localizer = runtime.Localizer;
        var observations = new ConcurrentBag<LiveBenchmarkObservation>();
        var warmups = new ConcurrentBag<LiveBenchmarkObservation>();
        var coldStarts = new ConcurrentBag<LiveBenchmarkObservation>();
        var client = runtime.Client;
        var capabilities = runtime.Capabilities;

        var supportedEfforts = capabilities
            .Where(item => item.Supported)
            .Select(item => item.Effort)
            .ToHashSet(StringComparer.Ordinal);
        var skippedCells = cells
            .Where(cell => !supportedEfforts.Contains(cell.Effort)
                           || !LiveBenchmarkProtocols.IsSupportedByCurrentProvider(cell.Protocol))
            .Select(cell => new LiveBenchmarkSkippedCell(
                "luna",
                cell.Variant,
                cell.Effort,
                !supportedEfforts.Contains(cell.Effort)
                    ? capabilities.Single(item => item.Effort == cell.Effort).Reason
                        ?? "Denkstufe wird vom aktiven Luna-Katalog nicht unterstützt."
                    : "Das Span-Operationsprotokoll ist in diesem Providerlauf noch nicht ausführbar.")
            {
                Protocol = cell.Protocol
            })
            .ToArray();
        var runnableCells = cells
            .Where(cell => supportedEfforts.Contains(cell.Effort)
                           && LiveBenchmarkProtocols.IsSupportedByCurrentProvider(cell.Protocol))
            .ToArray();
        if (runnableCells.Length == 0)
        {
            throw new InvalidOperationException("Keine angeforderte Luna-Denkstufe wird vom aktiven Modellkatalog unterstützt.");
        }

        using var inFlight = new SemaphoreSlim(configuration.Parallelism, configuration.Parallelism);
        var firstCase = corpus[0];

        if (configuration.ContentlessSchemaWarmup)
        {
            foreach (var cell in runnableCells
                         .GroupBy(item => (item.Effort, item.Protocol))
                         .Select(group => group.First())
                         .OrderBy(item => item.Effort, StringComparer.Ordinal)
                         .ThenBy(item => item.Protocol, StringComparer.Ordinal))
            {
                warmups.Add(await RunSchemaIdenticalWarmupAsync(
                    client,
                    localizer,
                    cell,
                    configuration,
                    inFlight,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        if (configuration.Warmup)
        {
            var tasks = runnableCells
                .Select(cell => RunLunaRequestAsync(
                    client,
                    localizer,
                    cell,
                    0,
                    firstCase,
                    configuration,
                    inFlight,
                    cancellationToken));
            foreach (var result in await Task.WhenAll(tasks).ConfigureAwait(false))
            {
                warmups.Add(result);
            }
        }

        for (var repetition = 1; repetition <= configuration.Repetitions; repetition++)
        {
            for (var caseIndex = 0; caseIndex < corpus.Count; caseIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mixedCells = LiveBenchmarkScheduler.Mix(
                    runnableCells,
                    configuration.Seed + repetition * 100_003 + caseIndex * 997);
                var tasks = mixedCells.Select(cell => RunLunaRequestAsync(
                    client,
                    localizer,
                    cell,
                    repetition,
                    corpus[caseIndex],
                    configuration,
                    inFlight,
                    cancellationToken));
                foreach (var result in await Task.WhenAll(tasks).ConfigureAwait(false))
                {
                    observations.Add(result);
                }
            }
        }

        if (configuration.ColdStartCount > 0)
        {
            var coldCell = runnableCells
                .OrderBy(cell => cell.Protocol, StringComparer.Ordinal)
                .ThenBy(cell => cell.Effort, StringComparer.Ordinal)
                .ThenBy(cell => cell.Variant, StringComparer.Ordinal)
                .First();
            var coldCases = SelectRepresentativeProviderCases(
                    corpus,
                    Math.Min(configuration.ColdStartCount, 20),
                    configuration.Seed + 91_771)
                .ToArray();
            for (var index = 0; index < configuration.ColdStartCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var caseItem = coldCases[index % coldCases.Length];
                var coldDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "CodexLanguageFix-Luna-ColdStart-v3",
                    runId,
                    index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
                using var coldClient = new CodexAppServerClient(coldDirectory, localizer);
                var coldAccount = await coldClient.GetAccountAsync(cancellationToken).ConfigureAwait(false);
                if (!coldAccount.IsChatGpt)
                {
                    throw new InvalidOperationException("Für Kaltstarts muss Codex mit ChatGPT-OAuth angemeldet sein.");
                }

                var coldResult = await RunLunaRequestAsync(
                    coldClient,
                    localizer,
                    coldCell,
                    10_000 + index,
                    caseItem,
                    configuration,
                    inFlight,
                    cancellationToken).ConfigureAwait(false);
                coldStarts.Add(coldResult with { ColdStart = true, WarmupKind = "cold-start" });
            }
        }

        if (configuration.CompareLanguageTool)
        {
            var comparisonCell = BuildCellReports(observations, warmups, configuration)
                .Where(cell => cell.Provider == "luna"
                               && cell.Effort is not null
                               && cell.Errors == 0
                               && cell.ControlsExact == cell.ControlsTotal
                               && observations.Where(item => item.Provider == "luna"
                                                               && item.Variant == cell.Variant
                                                               && item.Effort == cell.Effort
                                                               && item.Protocol == cell.Protocol)
                                   .All(item => item.Succeeded && (!item.SafetyCritical || item.Exact)))
                .OrderByDescending(cell => cell.F05)
                .ThenBy(cell => cell.P95DurationMs)
                .ThenBy(cell => cell.P50DurationMs)
                .ThenBy(cell => LivePromptCatalog.Get(cell.Variant).Instructions.Length)
                .Select(cell => new LiveBenchmarkCell(cell.Variant, cell.Effort!, cell.Protocol))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Keine sichere Luna-Zelle ist für den LanguageTool-Vergleich verfügbar.");
            await RunLanguageToolComparisonAsync(
                providerComparisonCorpus ?? corpus,
                configuration,
                client,
                localizer,
                comparisonCell,
                observations,
                inFlight,
                cancellationToken).ConfigureAwait(false);
        }

        wall.Stop();
        var report = new LiveBenchmarkReport(
            3,
            runId,
            startedAt,
            DateTimeOffset.UtcNow,
            wall.Elapsed.TotalMilliseconds,
            reportPath,
            configuration.Seed,
            configuration.Tier,
            corpus.Count,
            LiveBenchmarkCorpusSelection.Hash(corpus),
            "gpt-5.6-luna",
            "priority",
            configuration.Repetitions,
            configuration.Parallelism,
            configuration.Warmup,
            prompts.Select(prompt => new LiveBenchmarkPromptMetadata(
                    prompt.Id,
                    HashText(prompt.Instructions),
                    prompt.Instructions.Length))
                .ToArray(),
            configuration.Efforts.Select(LiveBenchmarkScheduler.NormalizeEffort).Distinct(StringComparer.Ordinal).ToArray(),
            capabilities,
            BuildCellReports(observations, warmups, configuration),
            observations.OrderBy(item => item.Provider, StringComparer.Ordinal)
                .ThenBy(item => item.Variant, StringComparer.Ordinal)
                .ThenBy(item => item.Effort, StringComparer.Ordinal)
                .ThenBy(item => item.Repetition)
                .ThenBy(item => item.CaseId, StringComparer.Ordinal)
                .ToArray(),
            warmups.Where(item => !item.Succeeded).Select(ToFailure).ToArray())
        {
            SkippedCells = skippedCells,
            Commit = ResolveCommit(),
            Protocols = configuration.Protocols.Select(LiveBenchmarkProtocols.Normalize).Distinct(StringComparer.Ordinal).ToArray(),
            WarmupKind = configuration.ContentlessSchemaWarmup ? "contentless-schema+contentful-prime" : "contentful-prime",
            WarmLanguageToolRounds = configuration.WarmLanguageToolRounds,
            WarmLanguageToolCases = configuration.WarmLanguageToolCases,
            ColdStartCount = configuration.ColdStartCount,
            WarmupObservations = warmups.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray(),
            ColdStartObservations = coldStarts
                .OrderBy(item => item.Repetition)
                .ThenBy(item => item.CaseId, StringComparer.Ordinal)
                .ToArray(),
            LicensedCorpusSources = LiveBenchmarkCorpusSelection.LicensedSources(),
            LanguageToolComparisonRequested = configuration.CompareLanguageTool
        };
        report = report with { LanguageToolComparison = LiveBenchmarkQuality.CompareLanguageTool(report) };
        var plannedMeasurements = runnableCells.Length * corpus.Count * configuration.Repetitions
                                  + configuration.ColdStartCount;
        if (configuration.CompareLanguageTool)
        {
            plannedMeasurements += 2 * configuration.WarmLanguageToolCases
                * Math.Min(configuration.WarmLanguageToolRounds, configuration.LanguageToolRepetitions);
        }
        report = report with
        {
            TargetLiveDurationMinutes = configuration.TargetLiveDurationMinutes,
            PlannedMeasurements = plannedMeasurements,
            ExecutedMeasurements = report.Observations.Count + report.ColdStartObservations.Count,
            CompleteStageCoverage = skippedCells.Length == 0
                                    && report.Observations.Count + report.ColdStartObservations.Count == plannedMeasurements
        };
        return new LiveBenchmarkRunResult(report);
    }

    private static async Task<LiveBenchmarkRunResult> RunStagedAsync(
        LiveBenchmarkConfiguration configuration,
        IReadOnlyList<CorpusCase> corpus,
        LiveBenchmarkRuntime runtime,
        CancellationToken cancellationToken)
    {
        var stageReports = new List<LiveBenchmarkStageSummary>();
        LiveBenchmarkReport? previous = null;
        IReadOnlyList<LiveBenchmarkCell>? selectedCells = null;

        foreach (var definition in LiveBenchmarkStagePlan.Definitions)
        {
            if (definition.Stage > 1)
            {
                await runtime.RenewClientAsync(cancellationToken).ConfigureAwait(false);
            }

            var stageConfiguration = configuration with
            {
                AutomaticSearch = false,
                RequirePerformanceGate = false,
                Tier = definition.Tier,
                Repetitions = definition.Repetitions,
                Parallelism = Math.Min(configuration.Parallelism, 4),
                Warmup = false,
                ContentlessSchemaWarmup = false,
                ColdStartCount = 0,
                CompareLanguageTool = definition.Stage == 4 && configuration.CompareLanguageTool,
                WarmLanguageToolRounds = 1,
                WarmLanguageToolCases = LiveBenchmarkStagePlan.LanguageToolCaseIds.Count,
                LanguageToolRepetitions = 1,
                LanguageToolCaseIds = LiveBenchmarkStagePlan.LanguageToolCaseIds.ToHashSet(StringComparer.Ordinal),
                SelectedCells = selectedCells,
                Protocols = definition.Stage <= 2
                    ? [LiveBenchmarkProtocols.Full]
                    : configuration.Protocols
            };
            if (definition.Stage == 2 && previous is not null)
            {
                selectedCells = LiveBenchmarkStagePlan.SelectTopCells(
                    previous,
                    definition.TopVariantsPerEffort);
                stageConfiguration = stageConfiguration with { SelectedCells = selectedCells };
            }
            else if (definition.Stage == 3 && previous is not null)
            {
                var finalists = LiveBenchmarkStagePlan.SelectTopCells(
                    previous,
                    definition.TopVariantsPerEffort);
                selectedCells = finalists
                    .SelectMany(finalist => configuration.Protocols.Select(protocol =>
                        finalist with { Protocol = LiveBenchmarkProtocols.Normalize(protocol) }))
                    .ToArray();
                stageConfiguration = stageConfiguration with { SelectedCells = selectedCells };
            }
            else if (definition.Stage == 4 && previous is not null)
            {
                selectedCells =
                [
                    LiveBenchmarkStagePlan.SelectBestCell(previous),
                    new LiveBenchmarkCell("baseline", "none", LiveBenchmarkProtocols.Full)
                ];
                selectedCells = selectedCells.Distinct().ToArray();
                stageConfiguration = stageConfiguration with { SelectedCells = selectedCells };
            }

            var selectedCorpus = LiveBenchmarkCompactCorpus.SelectStageCases(corpus, definition);
            var stageRun = await RunSingleAsync(
                stageConfiguration,
                selectedCorpus,
                runtime,
                cancellationToken,
                corpus).ConfigureAwait(false);
            previous = stageRun.Report;
            stageReports.Add(new LiveBenchmarkStageSummary(
                definition.Stage,
                definition.Tier,
                definition.Repetitions,
                selectedCorpus.Count,
                stageRun.Report.WallDurationMs,
                stageRun.Report.Cells,
                stageRun.Report.SkippedCells)
            {
                Observations = stageRun.Report.Observations,
                PlannedMeasurements = stageRun.Report.PlannedMeasurements,
                ExecutedMeasurements = stageRun.Report.ExecutedMeasurements,
                CompleteCoverage = stageRun.Report.CompleteStageCoverage
            });
            var durableStageReport = stageRun.Report with
            {
                ReportPath = LiveBenchmarkReportWriter.ResolveStagePath(
                    stageRun.Report.ReportPath,
                    definition.Stage),
                Stages = stageReports.ToArray()
            };
            LiveBenchmarkReportWriter.Write(durableStageReport);
        }

        if (previous is null)
        {
            throw new InvalidOperationException("Der gestufte Benchmark hat keine Stufe ausgeführt.");
        }

        var finalReport = previous with
        {
            Stages = stageReports,
            QualityGates = LiveBenchmarkQuality.BuildGates(stageReports),
            LanguageToolComparison = LiveBenchmarkQuality.CompareLanguageTool(previous),
            PlannedMeasurements = stageReports.Sum(item => item.PlannedMeasurements),
            ExecutedMeasurements = stageReports.Sum(item => item.ExecutedMeasurements),
            CompleteStageCoverage = stageReports.Count == LiveBenchmarkStagePlan.Definitions.Count
                                    && stageReports.All(item => item.CompleteCoverage
                                                                    && item.PlannedMeasurements == item.ExecutedMeasurements)
        };
        return new LiveBenchmarkRunResult(finalReport);
    }

    private static async Task<LiveBenchmarkObservation> RunLunaRequestAsync(
        CodexAppServerClient client,
        AppLocalizer localizer,
        LiveBenchmarkCell cell,
        int repetition,
        CorpusCase caseItem,
        LiveBenchmarkConfiguration configuration,
        SemaphoreSlim inFlight,
        CancellationToken cancellationToken)
    {
        await inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var prompt = LivePromptCatalog.Get(cell.Variant);
            var wrapper = new PromptOverrideClient(client, prompt.Instructions, cell.Effort);
            var normalizedProtocol = LiveBenchmarkProtocols.Normalize(cell.Protocol);
            var outputProtocol = normalizedProtocol switch
            {
                LiveBenchmarkProtocols.Full => LunaOutputProtocol.FullText,
                LiveBenchmarkProtocols.Span => LunaOutputProtocol.SpanEdits,
                _ => LunaOutputProtocol.Segments
            };
            var provider = new LunaCorrectionProvider(
                wrapper,
                localizer,
                cacheEnabled: false,
                patchThreshold: LiveBenchmarkProtocols.PatchThreshold(cell.Protocol),
                outputProtocol: outputProtocol,
                fallbackEnabled: false,
                usePreparedThreads: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            try
            {
                // Der äußere Timer misst bewusst die komplette CorrectAsync-Latenz.
                var result = await provider.CorrectAsync(caseItem.Input, timeout.Token).ConfigureAwait(false);
                stopwatch.Stop();
                var evaluation = CorpusQualityEvaluator.Evaluate(caseItem, result.CorrectedText);
                var observation = new LiveBenchmarkObservation(
                    "luna",
                    cell.Variant,
                    cell.Effort,
                    repetition,
                    caseItem.Id,
                    caseItem.Language,
                    caseItem.Category,
                    caseItem.IsControl,
                    true,
                    evaluation.HardGatePassed,
                    stopwatch.Elapsed.TotalMilliseconds,
                    result.Elapsed.TotalMilliseconds,
                    null)
                {
                    Weight = caseItem.Weight,
                    TruePositive = evaluation.IsTruePositive,
                    FalsePositive = evaluation.IsFalsePositive,
                    FalseNegative = evaluation.IsFalseNegative,
                    SafetyCritical = IsSafetyCritical(caseItem),
                    Protocol = cell.Protocol,
                    OutputCharacters = result.CorrectedText.Length,
                    WarmupKind = repetition == 0 ? "contentful-prime" : "none"
                };
                return EnrichTiming(observation, result);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                return FailedObservation("Timeout");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return FailedObservation(exception.GetType().Name);
            }

            LiveBenchmarkObservation FailedObservation(string errorType) => new(
                "luna",
                cell.Variant,
                cell.Effort,
                repetition,
                caseItem.Id,
                caseItem.Language,
                caseItem.Category,
                caseItem.IsControl,
                false,
                false,
                stopwatch.Elapsed.TotalMilliseconds,
                null,
                errorType)
            {
                Weight = caseItem.Weight,
                FalseNegative = !caseItem.IsControl,
                SafetyCritical = IsSafetyCritical(caseItem),
                Protocol = cell.Protocol,
                WarmupKind = repetition == 0 ? "contentful-prime" : "none"
            };
        }
        finally
        {
            inFlight.Release();
        }
    }

    private static async Task<LiveBenchmarkObservation> RunSchemaIdenticalWarmupAsync(
        CodexAppServerClient client,
        AppLocalizer localizer,
        LiveBenchmarkCell cell,
        LiveBenchmarkConfiguration configuration,
        SemaphoreSlim inFlight,
        CancellationToken cancellationToken)
    {
        await inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (client is not IStructuredCodexCorrectionClient structured)
            {
                return SchemaWarmupFailure(cell, stopwatch, "StructuredClientUnavailable");
            }

            var prompt = LivePromptCatalog.Get(cell.Variant);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            var normalizedProtocol = LiveBenchmarkProtocols.Normalize(cell.Protocol);
            var schema = normalizedProtocol switch
            {
                LiveBenchmarkProtocols.Full => LunaCorrectionProvider.OutputSchema,
                LiveBenchmarkProtocols.Span => LunaCorrectionProvider.SpanEditOutputSchema,
                _ => LunaCorrectionProvider.PatchOutputSchema
            };
            var input = normalizedProtocol switch
            {
                LiveBenchmarkProtocols.Full or LiveBenchmarkProtocols.Span => "{\"source_text\":\"\"}",
                _ => "{\"segments\":[]}"
            };
            var raw = await structured.RunStructuredCorrectionAsync(
                input,
                schema,
                prompt.Instructions,
                cell.Effort,
                timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            if (!IsSchemaShapedWarmup(raw, LiveBenchmarkProtocols.Normalize(cell.Protocol)))
            {
                return SchemaWarmupFailure(cell, stopwatch, "InvalidSchemaWarmup");
            }

            return new LiveBenchmarkObservation(
                "luna",
                cell.Variant,
                cell.Effort,
                0,
                "__schema-warmup__",
                "",
                "warmup",
                true,
                true,
                true,
                stopwatch.Elapsed.TotalMilliseconds,
                stopwatch.Elapsed.TotalMilliseconds,
                null)
            {
                Protocol = cell.Protocol,
                WarmupKind = "contentless-schema",
                OutputCharacters = raw.Length
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return SchemaWarmupFailure(cell, stopwatch, "Timeout");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return SchemaWarmupFailure(cell, stopwatch, exception.GetType().Name);
        }
        finally
        {
            inFlight.Release();
        }
    }

    private static LiveBenchmarkObservation SchemaWarmupFailure(
        LiveBenchmarkCell cell,
        Stopwatch stopwatch,
        string errorType)
    {
        stopwatch.Stop();
        return new LiveBenchmarkObservation(
            "luna",
            cell.Variant,
            cell.Effort,
            0,
            "__schema-warmup__",
            "",
            "warmup",
            true,
            false,
            false,
            stopwatch.Elapsed.TotalMilliseconds,
            null,
            errorType)
        {
            Protocol = cell.Protocol,
            WarmupKind = "contentless-schema"
        };
    }

    private static bool IsSchemaShapedWarmup(string raw, string protocol)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            return protocol == LiveBenchmarkProtocols.Full
                ? root.ValueKind == JsonValueKind.Object
                  && root.TryGetProperty("corrected_text", out var corrected)
                  && corrected.ValueKind == JsonValueKind.String
                : root.ValueKind == JsonValueKind.Object
                  && root.TryGetProperty("changes", out var changes)
                  && changes.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task RunLanguageToolComparisonAsync(
        IReadOnlyList<CorpusCase> corpus,
        LiveBenchmarkConfiguration configuration,
        CodexAppServerClient lunaClient,
        AppLocalizer localizer,
        LiveBenchmarkCell lunaCell,
        ConcurrentBag<LiveBenchmarkObservation> observations,
        SemaphoreSlim inFlight,
        CancellationToken cancellationToken)
    {
        var selected = configuration.LanguageToolCaseIds is { Count: > 0 } ids
            ? corpus.Where(item => ids.Contains(item.Id)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
            : SelectRepresentativeProviderCases(corpus, configuration.WarmLanguageToolCases, configuration.Seed);
        if (configuration.LanguageToolCaseIds is { Count: > 0 } requested && selected.Length != requested.Count)
        {
            var missing = requested.Except(selected.Select(item => item.Id), StringComparer.Ordinal);
            throw new InvalidOperationException($"Unbekannte LanguageTool-Benchmark-IDs: {string.Join(", ", missing)}");
        }

        if (selected.Length > 20)
        {
            throw new InvalidOperationException("Der opt-in LanguageTool-Vergleich darf wegen des öffentlichen Limits höchstens 20 Fälle enthalten.");
        }

        using var client = new LanguageToolClient();
        var provider = new LanguageToolCorrectionProvider(client, new CorrectionEngine());
        var nextAllowedStart = DateTimeOffset.UtcNow;
        var spacing = TimeSpan.FromSeconds(
            60d / Math.Min(configuration.LanguageToolMaxStartsPerMinute, 20));
        var rounds = Math.Min(configuration.WarmLanguageToolRounds, configuration.LanguageToolRepetitions);
        for (var repetition = 1; repetition <= rounds; repetition++)
        {
            foreach (var caseItem in LiveBenchmarkScheduler.Mix(selected, configuration.Seed + 7_919 + repetition * 101))
            {
                var delay = nextAllowedStart - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                nextAllowedStart = DateTimeOffset.UtcNow + spacing;
                var lunaTask = RunLunaRequestAsync(
                    lunaClient,
                    localizer,
                    lunaCell,
                    repetition,
                    caseItem,
                    configuration,
                    inFlight,
                    cancellationToken);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
                    try
                    {
                        var result = await provider.CorrectAsync(caseItem.Input, timeout.Token).ConfigureAwait(false);
                        stopwatch.Stop();
                        var evaluation = CorpusQualityEvaluator.Evaluate(caseItem, result.CorrectedText);
                        var languageToolObservation = new LiveBenchmarkObservation(
                            "language-tool",
                            "language-tool",
                            null,
                            repetition,
                            caseItem.Id,
                            caseItem.Language,
                            caseItem.Category,
                            caseItem.IsControl,
                            true,
                            evaluation.HardGatePassed,
                            stopwatch.Elapsed.TotalMilliseconds,
                            result.Elapsed.TotalMilliseconds,
                            null)
                        {
                            Weight = caseItem.Weight,
                            TruePositive = evaluation.IsTruePositive,
                            FalsePositive = evaluation.IsFalsePositive,
                            FalseNegative = evaluation.IsFalseNegative,
                            SafetyCritical = IsSafetyCritical(caseItem),
                            Protocol = LiveBenchmarkProtocols.Full,
                            OutputCharacters = result.CorrectedText.Length
                        };
                        observations.Add(languageToolObservation);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        observations.Add(FailedLanguageTool(caseItem, repetition, stopwatch.Elapsed.TotalMilliseconds, "Timeout"));
                    }
                    catch (Exception exception)
                    {
                        stopwatch.Stop();
                        observations.Add(FailedLanguageTool(caseItem, repetition, stopwatch.Elapsed.TotalMilliseconds, exception.GetType().Name));
                    }
                }
                finally
                {
                    var lunaObservation = await lunaTask.ConfigureAwait(false);
                    observations.Add(lunaObservation with { Provider = "luna-comparison" });
                }
            }
        }
    }

    internal static CorpusCase[] SelectRepresentativeProviderCases(
        IReadOnlyList<CorpusCase> corpus,
        int maximum,
        int seed)
    {
        if (maximum < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        var quotas = new (string Language, bool Control, int Count)[]
        {
            ("de", false, 6),
            ("en", false, 6),
            ("de", true, 3),
            ("en", true, 3),
            ("es", true, 1),
            ("fr", true, 1)
        };
        var buckets = quotas
            .Select((quota, index) => LiveBenchmarkScheduler.Mix(
                    corpus.Where(item => item.Language == quota.Language && item.IsControl == quota.Control),
                    seed + index * 1_009)
                .Take(quota.Count)
                .ToArray())
            .ToArray();
        var selected = new List<CorpusCase>(Math.Min(maximum, corpus.Count));
        for (var round = 0; selected.Count < maximum && buckets.Any(bucket => round < bucket.Length); round++)
        {
            foreach (var bucket in buckets)
            {
                if (round < bucket.Length && selected.Count < maximum)
                {
                    selected.Add(bucket[round]);
                }
            }
        }
        if (selected.Count < Math.Min(maximum, corpus.Count))
        {
            selected.AddRange(LiveBenchmarkScheduler.Mix(
                    corpus.Where(item => selected.All(existing => existing.Id != item.Id)),
                    seed + 9_973)
                .Take(Math.Min(maximum, corpus.Count) - selected.Count));
        }

        return selected.ToArray();
    }

    private static LiveBenchmarkObservation FailedLanguageTool(CorpusCase caseItem, int repetition, double durationMs, string errorType) =>
        new(
            "language-tool",
            "language-tool",
            null,
            repetition,
            caseItem.Id,
            caseItem.Language,
            caseItem.Category,
            caseItem.IsControl,
            false,
            false,
            durationMs,
            null,
            errorType)
        {
            Weight = caseItem.Weight,
            FalseNegative = !caseItem.IsControl,
            SafetyCritical = IsSafetyCritical(caseItem)
        };

    private static bool IsSafetyCritical(CorpusCase caseItem) =>
        caseItem.IsControl
        || caseItem.MustPreserve.Count > 0
        || caseItem.Tags.Contains("injection", StringComparer.Ordinal)
        || caseItem.Tags.Contains("protected", StringComparer.Ordinal);

    private static IReadOnlyList<LiveBenchmarkCellReport> BuildCellReports(
        IEnumerable<LiveBenchmarkObservation> observations,
        IEnumerable<LiveBenchmarkObservation> warmups,
        LiveBenchmarkConfiguration configuration)
    {
        var warmupGroups = warmups
            .GroupBy(item => (item.Provider, item.Variant, item.Effort, item.Protocol))
            .ToDictionary(group => group.Key, group => group.ToArray());
        return observations
            .GroupBy(item => (item.Provider, item.Variant, item.Effort, item.Protocol))
            .OrderBy(group => group.Key.Provider, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Variant, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Effort, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Protocol, StringComparer.Ordinal)
            .Select(group =>
            {
                var items = group.ToArray();
                var corrected = items.Where(item => !item.Control).ToArray();
                var controls = items.Where(item => item.Control).ToArray();
                warmupGroups.TryGetValue(group.Key, out var groupWarmups);
                groupWarmups ??= [];
                return new LiveBenchmarkCellReport(
                    group.Key.Provider,
                    group.Key.Variant,
                    group.Key.Effort,
                    group.Key.Provider == "language-tool" ? configuration.LanguageToolRepetitions : configuration.Repetitions,
                    items.Length,
                    items.Count(item => item.Succeeded),
                    items.Count(item => !item.Succeeded),
                    items.Count(item => item.Exact),
                    corrected.Count(item => item.Exact),
                    corrected.Length,
                    controls.Count(item => item.Exact),
                    controls.Length,
                    LiveBenchmarkStatistics.Mean(items.Select(item => item.DurationMs)),
                    QualityStatistics.Percentile(items.Select(item => item.DurationMs), 0.50),
                    QualityStatistics.Percentile(items.Select(item => item.DurationMs), 0.95),
                    MeanNullable(items.Select(item => item.ProviderDurationMs)),
                    groupWarmups.Length,
                    groupWarmups.Length == 0 ? null : LiveBenchmarkStatistics.Mean(groupWarmups.Select(item => item.DurationMs)),
                    items
                        .Where(item => !item.Exact)
                        .Select(ToFailure)
                        .OrderBy(item => item.CaseId, StringComparer.Ordinal)
                        .ThenBy(item => item.Repetition)
                        .ToArray())
                {
                    Protocol = group.Key.Protocol,
                    MeanTimeToFirstTextDeltaMs = MeanNullable(items.Select(item => item.TimeToFirstTextDeltaMs)),
                    P50TimeToFirstTextDeltaMs = PercentileNullable(items.Select(item => item.TimeToFirstTextDeltaMs), 0.50),
                    P95TimeToFirstTextDeltaMs = PercentileNullable(items.Select(item => item.TimeToFirstTextDeltaMs), 0.95),
                    MeanModelCompletionMs = MeanNullable(items.Select(item => item.ModelCompletionMs)),
                    P50ModelCompletionMs = PercentileNullable(items.Select(item => item.ModelCompletionMs), 0.50),
                    P95ModelCompletionMs = PercentileNullable(items.Select(item => item.ModelCompletionMs), 0.95),
                    MeanSchemaValidationMs = MeanNullable(items.Select(item => item.SchemaValidationMs)),
                    MeanPlaceholderValidationMs = MeanNullable(items.Select(item => item.PlaceholderValidationMs)),
                    MeanComposerWriteMs = MeanNullable(items.Select(item => item.ComposerWriteMs)),
                    MeanCleanupMs = MeanNullable(items.Select(item => item.CleanupMs)),
                    MeanOutputCharacters = items.Length == 0 ? null : items.Average(item => (double)item.OutputCharacters),
                    WarmupKind = groupWarmups.Length == 0
                        ? "none"
                        : string.Join(",", groupWarmups.Select(item => item.WarmupKind).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal)),
                    ColdStartCount = items.Count(item => item.ColdStart),
                    WeightedTruePositives = items.Where(item => item.TruePositive).Sum(item => item.Weight),
                    WeightedFalsePositives = items.Where(item => item.FalsePositive).Sum(item => item.Weight),
                    WeightedFalseNegatives = items.Where(item => item.FalseNegative).Sum(item => item.Weight)
                };
            })
            .ToArray();
    }

    private static double? MeanNullable(IEnumerable<double?> values)
    {
        var existing = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return existing.Length == 0 ? null : existing.Average();
    }

    private static double? PercentileNullable(IEnumerable<double?> values, double percentile)
    {
        var existing = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return existing.Length == 0 ? null : QualityStatistics.Percentile(existing, percentile);
    }

    private static LiveBenchmarkFailure ToFailure(LiveBenchmarkObservation observation) =>
        new(
            observation.CaseId,
            observation.Repetition,
            observation.ErrorType
                ?? (observation.Control ? "ControlChanged" : "HardGateFailed"),
            observation.DurationMs);

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ResolveCommit()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_BENCHMARK_COMMIT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is not null && process.WaitForExit(2_000) && process.ExitCode == 0)
            {
                var value = process.StandardOutput.ReadToEnd().Trim();
                return value.Length == 40 ? value : "unknown";
            }
        }
        catch
        {
            // Ein fehlendes Git-Programm darf den eigentlichen Live-Lauf nicht verdecken.
        }

        return "unknown";
    }

    /// <summary>
    /// Liest optionale Laufzeit-Telemetrie ohne eine Kopplung des Test-Harness
    /// an den konkreten Transportdatensatz. So bleibt der Bericht mit älteren
    /// Test-Doubles ausführbar, während ein neuer LunaCorrectionExecution-
    /// Datensatz automatisch in Schema v3 auftaucht.
    /// </summary>
    private static LiveBenchmarkObservation EnrichTiming(
        LiveBenchmarkObservation observation,
        object result)
    {
        if (result is CorrectionProviderResult { LunaExecution: { } execution })
        {
            var timing = execution.Timings;
            return observation with
            {
                ServerAccountCheckMs = timing.ServerAndAccount.TotalMilliseconds,
                ThreadProvisionMs = timing.ThreadProvision.TotalMilliseconds,
                TurnStartMs = timing.TurnStart.TotalMilliseconds,
                TimeToFirstTextDeltaMs = timing.TimeToFirstTextDelta?.TotalMilliseconds,
                ModelCompletionMs = timing.ModelCompletion.TotalMilliseconds,
                SchemaValidationMs = timing.Validation.TotalMilliseconds,
                PlaceholderValidationMs = timing.Restoration.TotalMilliseconds,
                ComposerWriteMs = timing.ComposerWrite.TotalMilliseconds,
                CleanupMs = timing.Cleanup.TotalMilliseconds,
                OutputCharacters = execution.OutputCharacters,
                Protocol = observation.Protocol
            };
        }

        return observation with
        {
            ServerAccountCheckMs = ReadTiming(result, "ServerAccountCheck", "ServerAccountCheckMs", "AccountCheck", "AccountCheckMs"),
            ThreadProvisionMs = ReadTiming(result, "ThreadProvision", "ThreadProvisionMs", "ThreadPreparation", "ThreadPreparationMs"),
            TurnStartMs = ReadTiming(result, "TurnStart", "TurnStartMs"),
            TimeToFirstTextDeltaMs = ReadTiming(result, "TimeToFirstTextDelta", "TimeToFirstTextDeltaMs", "TimeToFirstToken", "TimeToFirstTokenMs", "Ttft", "TtftMs"),
            ModelCompletionMs = ReadTiming(result, "ModelCompletion", "ModelCompletionMs", "Completion", "CompletionMs"),
            SchemaValidationMs = ReadTiming(result, "SchemaValidation", "SchemaValidationMs", "Validation", "ValidationMs"),
            PlaceholderValidationMs = ReadTiming(result, "PlaceholderValidation", "PlaceholderValidationMs"),
            RestoreMs = ReadTiming(result, "Restore", "RestoreMs", "Restoration", "RestorationMs"),
            ComposerWriteMs = ReadTiming(result, "ComposerWrite", "ComposerWriteMs"),
            CleanupMs = ReadTiming(result, "Cleanup", "CleanupMs")
        };
    }

    private static double? ReadTiming(object root, params string[] names)
    {
        foreach (var candidate in EnumerateTimingObjects(root))
        {
            var type = candidate.GetType();
            foreach (var name in names)
            {
                var property = type.GetProperty(
                    name,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.IgnoreCase);
                if (property?.GetValue(candidate) is { } value && TryConvertMilliseconds(value, out var milliseconds))
                {
                    return milliseconds;
                }
            }
        }

        return null;
    }

    private static IEnumerable<object> EnumerateTimingObjects(object root)
    {
        yield return root;
        var rootType = root.GetType();
        foreach (var propertyName in new[] { "Execution", "Timing", "ProtocolTiming", "LunaExecution" })
        {
            var value = rootType.GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(root);
            if (value is null)
            {
                continue;
            }

            yield return value;
            var valueType = value.GetType();
            foreach (var nestedName in new[] { "Timing", "ProtocolTiming", "Durations" })
            {
                if (valueType.GetProperty(
                        nestedName,
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(value) is { } nested)
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool TryConvertMilliseconds(object value, out double milliseconds)
    {
        switch (value)
        {
            case TimeSpan duration:
                milliseconds = duration.TotalMilliseconds;
                return true;
            case double number when double.IsFinite(number):
                milliseconds = number;
                return true;
            case float number when float.IsFinite(number):
                milliseconds = number;
                return true;
            case long number:
                milliseconds = number;
                return true;
            case int number:
                milliseconds = number;
                return true;
            default:
                milliseconds = 0;
                return false;
        }
    }

    private sealed class PromptOverrideClient(
        CodexAppServerClient inner,
        string instructions,
        string effort) : ICodexAppServerClient, IStructuredCodexCorrectionClient, ILunaTimedTransportClient
    {
        public Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken) =>
            inner.GetAccountAsync(cancellationToken);

        public Task ConnectChatGptAsync(CancellationToken cancellationToken) =>
            inner.ConnectChatGptAsync(cancellationToken);

        public Task<bool> SupportsLunaAsync(CancellationToken cancellationToken) =>
            inner.SupportsLunaAsync(cancellationToken);

        public Task<string> RunCorrectionAsync(string protectedText, CancellationToken cancellationToken) =>
            inner.RunCorrectionAsync(protectedText, instructions, effort, cancellationToken);

        public Task<string> RunStructuredCorrectionAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            string requestedEffort,
            CancellationToken cancellationToken) =>
            ((IStructuredCodexCorrectionClient)inner).RunStructuredCorrectionAsync(
                inputJson,
                outputSchema,
                instructions,
                effort,
                cancellationToken);

        public Task<LunaTransportExecution> RunStructuredCorrectionWithTimingAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            string requestedEffort,
            CancellationToken cancellationToken,
            bool replenishPreparedThread = false) =>
            ((ILunaTimedTransportClient)inner).RunStructuredCorrectionWithTimingAsync(
                inputJson,
                outputSchema,
                instructions,
                effort,
                cancellationToken,
                replenishPreparedThread);

        public void Dispose()
        {
            // Der gemeinsame App-Server gehört dem Benchmark.
        }
    }
}

internal static class LiveBenchmarkReportWriter
{
    public static string ResolvePath(string? configuredPath, string runId)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(Path.GetTempPath(), $"CodexLanguageFix-Luna-Benchmark-v3-{runId}.json");
        }

        var fullPath = Path.GetFullPath(configuredPath);
        if (Directory.Exists(fullPath) || !Path.HasExtension(fullPath))
        {
            return Path.Combine(fullPath, $"CodexLanguageFix-Luna-Benchmark-v3-{runId}.json");
        }

        return Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            $"{Path.GetFileNameWithoutExtension(fullPath)}-{runId}{Path.GetExtension(fullPath)}");
    }

    public static string ResolveStagePath(string reportPath, int stage)
    {
        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        var fullPath = Path.GetFullPath(reportPath);
        return Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            $"{Path.GetFileNameWithoutExtension(fullPath)}-stage-{stage}{Path.GetExtension(fullPath)}");
    }

    public static void Write(LiveBenchmarkReport report)
    {
        var directory = Path.GetDirectoryName(report.ReportPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Der Benchmark-Bericht besitzt keinen gültigen Zielordner.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = report.ReportPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(report, options), new UTF8Encoding(false));
        File.Move(temporaryPath, report.ReportPath, false);
    }
}

internal static class LiveBenchmarkQuality
{
    private const double NonInferiorityMargin = 0.005;
    private const double RequiredLatencyRatio = 0.90;

    public static double ConfidenceIntervalWidth(int successes, int total)
    {
        if (total <= 0 || successes < 0 || successes > total)
        {
            return 2;
        }

        const double z = 1.96;
        var n = (double)total;
        var proportion = successes / n;
        var denominator = 1 + z * z / n;
        var radius = z * Math.Sqrt(proportion * (1 - proportion) / n + z * z / (4 * n * n));
        return 2 * radius / denominator;
    }

    public static IReadOnlyList<LiveBenchmarkQualityGate> BuildGates(
        IReadOnlyList<LiveBenchmarkStageSummary> stages)
    {
        var final = stages.OrderByDescending(stage => stage.Stage).FirstOrDefault();
        if (final is null)
        {
            return [];
        }

        var baseline = final.Cells.FirstOrDefault(cell =>
            cell.Provider == "luna"
            && cell.Variant == "baseline"
            && cell.Effort == "none"
            && cell.Protocol == LiveBenchmarkProtocols.Full);
        var baselineObservations = final.Observations
            .Where(item => item.Provider == "luna"
                           && item.Variant == "baseline"
                           && item.Effort == "none"
                           && item.Protocol == LiveBenchmarkProtocols.Full)
            .ToArray();
        return final.Cells
            .Where(cell => cell.Provider == "luna" && cell.Effort is not null)
            .OrderBy(cell => cell.Effort, StringComparer.Ordinal)
            .ThenBy(cell => cell.Variant, StringComparer.Ordinal)
            .Select(cell =>
            {
                var candidateObservations = final.Observations
                    .Where(item => item.Provider == "luna"
                                   && item.Variant == cell.Variant
                                   && item.Effort == cell.Effort
                                   && item.Protocol == cell.Protocol)
                    .ToArray();
                var pairs = Pair(candidateObservations, baselineObservations);
                double? p50Ratio = baseline is null || baseline.P50DurationMs <= 0
                    ? null
                    : cell.P50DurationMs / baseline.P50DurationMs;
                double? p95Ratio = baseline is null || baseline.P95DurationMs <= 0
                    ? null
                    : cell.P95DurationMs / baseline.P95DurationMs;
                (double Lower, double Upper)? qualityInterval = pairs.Count == 0
                    ? null
                    : BootstrapQualityDifference(pairs, 2_000, 71);
                (double Lower, double Upper)? p50Interval = pairs.Count == 0
                    ? null
                    : BootstrapLatencyRatio(pairs, 0.50, 2_000, 73);
                (double Lower, double Upper)? p95Interval = pairs.Count == 0
                    ? null
                    : BootstrapLatencyRatio(pairs, 0.95, 2_000, 79);
                var qualityNonInferior = qualityInterval is not null
                    && qualityInterval.Value.Lower >= -NonInferiorityMargin;
                var p50Within = p50Ratio.HasValue && p50Ratio.Value <= RequiredLatencyRatio;
                var p95Within = p95Ratio.HasValue && p95Ratio.Value <= RequiredLatencyRatio;
                var p50BootstrapPassed = p50Interval is not null && p50Interval.Value.Upper < 1;
                var p95BootstrapPassed = p95Interval is not null && p95Interval.Value.Upper < 1;
                var ciPassed = qualityInterval is not null;
                return new LiveBenchmarkQualityGate(
                    cell.Provider,
                    cell.Variant,
                    cell.Effort,
                    cell.F05,
                    cell.F05ConfidenceIntervalWidth,
                    p50Ratio,
                    p95Ratio,
                    qualityNonInferior,
                    p50Within,
                    p95Within,
                    p50BootstrapPassed && p95BootstrapPassed,
                    qualityNonInferior
                        && p50Within
                        && p95Within
                        && p50BootstrapPassed
                        && p95BootstrapPassed
                        && ciPassed)
                {
                    Protocol = cell.Protocol,
                    QualityDifferenceLower95 = qualityInterval?.Lower,
                    P50RatioUpper95 = p50Interval?.Upper,
                    P95RatioUpper95 = p95Interval?.Upper
                };
            })
            .ToArray();
    }

    public static LiveBenchmarkProviderComparison? CompareLanguageTool(LiveBenchmarkReport report)
    {
        var comparisonProvider = report.Observations.Any(item => item.Provider == "luna-comparison")
            ? "luna-comparison"
            : "luna";
        var lunaCells = report.Cells
            .Where(cell => cell.Provider == comparisonProvider)
            .OrderByDescending(cell => cell.F05)
            .ThenBy(cell => cell.P95DurationMs)
            .ToArray();
        if (lunaCells.Length == 0)
        {
            return null;
        }

        var bestLuna = lunaCells[0];
        var lunaObservations = report.Observations
            .Where(item => item.Provider == comparisonProvider
                           && item.Variant == bestLuna.Variant
                           && item.Effort == bestLuna.Effort
                           && item.Protocol == bestLuna.Protocol)
            .ToArray();
        var languageToolObservations = report.Observations
            .Where(item => item.Provider == "language-tool")
            .ToArray();
        var pairedKeys = lunaObservations
            .Select(item => (item.CaseId, item.Repetition))
            .Intersect(languageToolObservations.Select(item => (item.CaseId, item.Repetition)))
            .ToHashSet();
        if (pairedKeys.Count == 0)
        {
            return null;
        }

        var lunaPaired = lunaObservations
            .Where(item => pairedKeys.Contains((item.CaseId, item.Repetition)))
            .ToArray();
        var languageToolPaired = languageToolObservations
            .Where(item => pairedKeys.Contains((item.CaseId, item.Repetition)))
            .ToArray();
        var pairs = Pair(lunaPaired, languageToolPaired);
        if (pairs.Count == 0)
        {
            return null;
        }

        var lunaF05 = F05(pairs.Select(item => item.Candidate));
        var languageToolF05 = F05(pairs.Select(item => item.Baseline));
        var lunaP50 = QualityStatistics.Percentile(lunaPaired.Select(item => item.DurationMs), 0.50);
        var languageToolP50 = QualityStatistics.Percentile(languageToolPaired.Select(item => item.DurationMs), 0.50);
        var lunaP95 = QualityStatistics.Percentile(lunaPaired.Select(item => item.DurationMs), 0.95);
        var languageToolP95 = QualityStatistics.Percentile(languageToolPaired.Select(item => item.DurationMs), 0.95);
        double? p50Ratio = languageToolP50 <= 0 ? null : lunaP50 / languageToolP50;
        double? p95Ratio = languageToolP95 <= 0 ? null : lunaP95 / languageToolP95;
        var qualityInterval = BootstrapQualityDifference(pairs, 5_000, 101);
        var p50Interval = BootstrapLatencyRatio(pairs, 0.50, 5_000, 103);
        var p95Interval = BootstrapLatencyRatio(pairs, 0.95, 5_000, 107);
        var qualityNonInferior = qualityInterval.Lower >= -NonInferiorityMargin;
        var faster = p50Ratio <= RequiredLatencyRatio
                     && p95Ratio <= RequiredLatencyRatio
                     && p50Interval.Upper < 1
                     && p95Interval.Upper < 1;
        var ciWidth = ConfidenceIntervalWidth(
            lunaPaired.Count(item => item.Exact),
            lunaPaired.Length);
        return new LiveBenchmarkProviderComparison(
            comparisonProvider,
            "language-tool",
            pairedKeys.Select(key => key.CaseId).Distinct(StringComparer.Ordinal).Count(),
            pairedKeys.Select(key => key.Repetition).Distinct().Count(),
            lunaF05,
            languageToolF05,
            p50Ratio,
            p95Ratio,
            qualityNonInferior,
            faster,
            ciWidth,
            qualityNonInferior && faster && ciWidth < 1)
        {
            Protocol = bestLuna.Protocol,
            QualityDifferenceLower95 = qualityInterval.Lower,
            P50RatioUpper95 = p50Interval.Upper,
            P95RatioUpper95 = p95Interval.Upper
        };
    }

    private static double F05(IEnumerable<LiveBenchmarkObservation> observations)
    {
        var values = observations.ToArray();
        return QualityStatistics.F05(
            values.Where(item => item.TruePositive).Sum(item => item.Weight),
            values.Where(item => item.FalsePositive).Sum(item => item.Weight),
            values.Where(item => item.FalseNegative).Sum(item => item.Weight));
    }

    private static IReadOnlyList<ObservationPair> Pair(
        IEnumerable<LiveBenchmarkObservation> candidates,
        IEnumerable<LiveBenchmarkObservation> baselines)
    {
        var baselineByKey = baselines.ToDictionary(item => (item.CaseId, item.Repetition));
        return candidates
            .Where(item => baselineByKey.ContainsKey((item.CaseId, item.Repetition)))
            .OrderBy(item => item.CaseId, StringComparer.Ordinal)
            .ThenBy(item => item.Repetition)
            .Select(item => new ObservationPair(item, baselineByKey[(item.CaseId, item.Repetition)]))
            .ToArray();
    }

    private static (double Lower, double Upper) BootstrapQualityDifference(
        IReadOnlyList<ObservationPair> pairs,
        int resamples,
        int seed)
    {
        var random = new Random(seed);
        var samples = new double[resamples];
        for (var sample = 0; sample < resamples; sample++)
        {
            var candidate = new LiveBenchmarkObservation[pairs.Count];
            var baseline = new LiveBenchmarkObservation[pairs.Count];
            for (var draw = 0; draw < pairs.Count; draw++)
            {
                var pair = pairs[random.Next(pairs.Count)];
                candidate[draw] = pair.Candidate;
                baseline[draw] = pair.Baseline;
            }

            samples[sample] = F05(candidate) - F05(baseline);
        }

        return (QualityStatistics.Percentile(samples, 0.025), QualityStatistics.Percentile(samples, 0.975));
    }

    private static (double Lower, double Upper) BootstrapLatencyRatio(
        IReadOnlyList<ObservationPair> pairs,
        double percentile,
        int resamples,
        int seed)
    {
        var random = new Random(seed);
        var samples = new double[resamples];
        for (var sample = 0; sample < resamples; sample++)
        {
            var candidate = new double[pairs.Count];
            var baseline = new double[pairs.Count];
            for (var draw = 0; draw < pairs.Count; draw++)
            {
                var pair = pairs[random.Next(pairs.Count)];
                candidate[draw] = pair.Candidate.DurationMs;
                baseline[draw] = pair.Baseline.DurationMs;
            }

            var denominator = QualityStatistics.Percentile(baseline, percentile);
            samples[sample] = denominator <= 0
                ? double.PositiveInfinity
                : QualityStatistics.Percentile(candidate, percentile) / denominator;
        }

        var finite = samples.Where(double.IsFinite).ToArray();
        if (finite.Length != samples.Length)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        return (QualityStatistics.Percentile(finite, 0.025), QualityStatistics.Percentile(finite, 0.975));
    }

    private sealed record ObservationPair(
        LiveBenchmarkObservation Candidate,
        LiveBenchmarkObservation Baseline);
}

internal static class LiveBenchmarkHardAssertions
{
    public static void Validate(LiveBenchmarkReport report, LiveBenchmarkConfiguration configuration, int corpusCount)
    {
        if (report.SchemaVersion != 3)
        {
            throw new InvalidOperationException("Der Benchmark-Bericht besitzt nicht Schema-Version 3.");
        }

        if (configuration.AutomaticSearch)
        {
            if (report.Stages.Count != LiveBenchmarkStagePlan.Definitions.Count
                || !report.CompleteStageCoverage
                || report.PlannedMeasurements != report.ExecutedMeasurements
                || report.Stages.Any(stage => stage.PlannedMeasurements != stage.ExecutedMeasurements))
            {
                throw new InvalidOperationException(
                    $"Die kompakte Qualifikation ist unvollständig: {report.ExecutedMeasurements} von {report.PlannedMeasurements} Messungen.");
            }
        }

        var expected = report.Cells.Sum(cell => cell.Total);
        if (report.Observations.Count != expected)
        {
            throw new InvalidOperationException($"Benchmark unvollständig: {report.Observations.Count} statt {expected} Beobachtungen.");
        }

        var duplicates = report.Observations
            .GroupBy(item => (item.Provider, item.Variant, item.Effort, item.Protocol, item.Repetition, item.CaseId))
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key.Provider}/{group.Key.Variant}/{group.Key.Effort}/{group.Key.Protocol}/{group.Key.Repetition}/{group.Key.CaseId}")
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Doppelte oder fehlende Benchmark-Beobachtungen: {string.Join(", ", duplicates)}");
        }

        var errors = report.Cells.Sum(cell => cell.Errors) + report.WarmupFailures.Count;
        if (errors > 0)
        {
            throw new InvalidOperationException($"Der Benchmark enthält {errors} harte Fehler. Siehe Bericht: {report.ReportPath}");
        }

        var changedControls = report.Cells
            .Where(cell => cell.Provider.StartsWith("luna", StringComparison.Ordinal))
            .Sum(cell => cell.ControlsTotal - cell.ControlsExact);
        if (changedControls > 0)
        {
                throw new InvalidOperationException($"Der Benchmark hat {changedControls} Kontrollfälle verändert. Siehe Bericht: {report.ReportPath}");
        }

        var safetyFailures = report.Observations
            .Where(item => item.Provider.StartsWith("luna", StringComparison.Ordinal)
                           && item.SafetyCritical
                           && !item.Exact)
            .Select(item => item.CaseId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (safetyFailures.Length > 0)
        {
            throw new InvalidOperationException(
                $"Luna verletzt harte Injektions-, Erhaltungs- oder Platzhaltergatter: {string.Join(", ", safetyFailures)}.");
        }

        if (report.Cells
            .Where(cell => cell.Provider == "luna")
            .Any(cell => cell.CorrectedCasesExact < configuration.MinimumCorrectedExact))
        {
            throw new InvalidOperationException($"Mindestens eine Luna-Zelle unterschreitet die Mindestzahl exakter Korrekturen ({configuration.MinimumCorrectedExact}).");
        }

        if (configuration.CompareLanguageTool && report.LanguageToolComparison is null)
        {
            throw new InvalidOperationException(
                "Der angeforderte gepaarte LanguageTool-Vergleich fehlt im Benchmark-Bericht.");
        }

        if (configuration.RequirePerformanceGate)
        {
            if (report.QualityGates.Count == 0 || !report.QualityGates.Any(gate => gate.Passed))
            {
                throw new InvalidOperationException(
                    "Keine finale Luna-Zelle besteht die gepaarte Qualitäts-Nichtunterlegenheit.");
            }

            if (!configuration.CompareLanguageTool)
            {
                throw new InvalidOperationException(
                    "Das vollständige Performancegatter benötigt den gepaarten LanguageTool-Vergleich.");
            }

            if (report.LanguageToolComparison is not { Passed: true })
            {
                throw new InvalidOperationException(
                    "Der gepaarte LanguageTool-Vergleich fehlt oder Luna erfüllt nicht gleichzeitig Qualitäts- und Latenzgatter.");
            }
        }
    }
}
