using System.Diagnostics;
using System.Globalization;

namespace CodexLanguageFix.Core;

public static class CoreCostProbe
{
    private const int WarmupIterations = 64;
    private const int MeasuredIterations = 30;
    private const ulong ChecksumOffset = 14695981039346656037UL;
    private const ulong ChecksumPrime = 1099511628211UL;
    private const string ProfileFingerprint = "model\0low\0priority\0test";

    public static object Run(string original, string corrected)
    {
        var operations = new[]
        {
            Measure("protected_spans",
                () => ProtectedSpanDetector.Detect(original),
                spans =>
                {
                    var checksum = Mix(ChecksumOffset, (ulong)spans.Count);
                    foreach (var span in spans)
                    {
                        checksum = Mix(checksum, (ulong)span.Start);
                        checksum = Mix(checksum, (ulong)span.Length);
                    }
                    return new Consumption(spans.Count, checksum);
                }),

            Measure("protected_text_self_restore",
                () =>
                {
                    var protectedText = LunaProtectedText.Create(original);
                    var restored = protectedText.TryRestore(protectedText.Text, out var value);
                    return (ProtectedLength: protectedText.Text.Length, Restored: restored, Value: value);
                },
                result =>
                {
                    var selfRestore = result.Restored && string.Equals(result.Value, original, StringComparison.Ordinal);
                    var checksum = Mix(ChecksumOffset, (ulong)result.ProtectedLength);
                    checksum = Mix(checksum, (ulong)result.Value.Length);
                    checksum = Mix(checksum, selfRestore ? 1UL : 0UL);
                    return new Consumption(selfRestore ? 1 : 0, checksum);
                }),

            Measure("structure_and_layout",
                () =>
                {
                    var preserved = TextStructure.IsPreserved(original, corrected);
                    var restored = TextStructure.TryRestoreLayout(original, corrected, out var layout);
                    return (Preserved: preserved, Restored: restored, Layout: layout);
                },
                result =>
                {
                    var checksum = Mix(ChecksumOffset, result.Preserved ? 1UL : 0UL);
                    checksum = Mix(checksum, result.Restored ? 1UL : 0UL);
                    checksum = Mix(checksum, (ulong)result.Layout.Length);
                    return new Consumption(result.Restored ? 1 : 0, checksum);
                }),

            Measure("edit_and_reverse_plan",
                () =>
                {
                    var plan = ComposerEditPlan.Create(original, corrected);
                    return (Plan: plan, Reverse: plan?.Reverse());
                },
                result =>
                {
                    var checksum = Mix(ChecksumOffset, result.Plan is null ? 0UL : 1UL);
                    var count = 0;
                    if (result.Plan is not null)
                    {
                        var consumed = ConsumeEdits(result.Plan.Edits, checksum);
                        checksum = consumed.Checksum;
                        count += consumed.ResultCount;
                    }
                    checksum = Mix(checksum, result.Reverse is null ? 0UL : 1UL);
                    if (result.Reverse is not null)
                    {
                        var consumed = ConsumeEdits(result.Reverse.Edits, checksum);
                        checksum = consumed.Checksum;
                        count += consumed.ResultCount;
                    }
                    return new Consumption(count, checksum);
                }),

            Measure("repeated_sentence_check",
                () => !RepeatedSentenceGuard.HasReducedUnchangedRepetitions(original, corrected),
                preserved => new Consumption(1, Mix(ChecksumOffset, preserved ? 1UL : 0UL))),

            Measure("grapheme_boundaries",
                () => StringInfo.ParseCombiningCharacters(original),
                boundaries =>
                {
                    var checksum = Mix(ChecksumOffset, (ulong)boundaries.Length);
                    foreach (var boundary in boundaries)
                    {
                        checksum = Mix(checksum, (ulong)boundary);
                    }
                    return new Consumption(boundaries.Length, checksum);
                }),

            Measure("cache_key",
                () => LunaCorrectionCache.CreateKey(original, ProfileFingerprint),
                key =>
                {
                    var checksum = Mix(ChecksumOffset, (ulong)key.Length);
                    if (key.Length > 0)
                    {
                        checksum = Mix(checksum, key[0]);
                        checksum = Mix(checksum, key[^1]);
                    }
                    return new Consumption(key.Length, checksum);
                })
        };

        return new
        {
            schema = 1,
            scope = "pure_core_operations_only",
            releasePass = false,
            originalUtf16Units = original.Length,
            correctedUtf16Units = corrected.Length,
            warmupIterations = WarmupIterations,
            measuredIterations = MeasuredIterations,
            operations
        };
    }

    private static OperationMeasurement Measure<T>(
        string id,
        Func<T> operation,
        Func<T, Consumption> consume)
    {
        var samples = new double[MeasuredIterations];
        var checksum = ChecksumOffset;
        var resultCount = 0;

        for (var iteration = 0; iteration < WarmupIterations + MeasuredIterations; iteration++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = operation();
            var stopped = Stopwatch.GetTimestamp();

            var consumed = consume(result);
            checksum = Mix(checksum, consumed.Checksum);
            resultCount = consumed.ResultCount;
            if (iteration >= WarmupIterations)
            {
                samples[iteration - WarmupIterations] = Stopwatch.GetElapsedTime(started, stopped).TotalMicroseconds;
            }
        }

        Array.Sort(samples);
        var middle = samples.Length / 2;
        var medianUs = (samples[middle - 1] + samples[middle]) / 2;
        var p95Index = (samples.Length * 95 + 99) / 100 - 1;

        return new OperationMeasurement(id, medianUs, samples[p95Index], samples[^1], resultCount, checksum);
    }

    private static Consumption ConsumeEdits(IReadOnlyList<ComposerTextEdit> edits, ulong checksum)
    {
        checksum = Mix(checksum, (ulong)edits.Count);
        foreach (var edit in edits)
        {
            checksum = Mix(checksum, (ulong)edit.Start);
            checksum = Mix(checksum, (ulong)edit.Original.Length);
            checksum = Mix(checksum, (ulong)edit.Replacement.Length);
        }
        return new Consumption(edits.Count, checksum);
    }

    private static ulong Mix(ulong checksum, ulong value) => unchecked((checksum ^ value) * ChecksumPrime);

    private sealed record OperationMeasurement(
        string Id,
        double MedianUs,
        double P95Us,
        double MaxUs,
        int ResultCount,
        ulong Checksum);

    private readonly record struct Consumption(int ResultCount, ulong Checksum);
}
