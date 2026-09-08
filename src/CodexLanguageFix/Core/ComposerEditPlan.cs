using System.Globalization;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Core;

public sealed record ComposerTextEdit(int Start, string Original, string Replacement);

public sealed record ComposerEditPlan(string Original, string Corrected, IReadOnlyList<ComposerTextEdit> Edits)
{
    public static ComposerEditPlan? Create(string original, string corrected)
    {
        if (!TextStructure.IsPreserved(original, corrected))
        {
            return null;
        }

        var before = GetFrozenRanges(original);
        var after = GetFrozenRanges(corrected);
        if (before.Count != after.Count)
        {
            return null;
        }

        var edits = new List<ComposerTextEdit>();
        var beforeStart = 0;
        var afterStart = 0;
        for (var index = 0; index <= before.Count; index++)
        {
            var beforeEnd = index == before.Count ? original.Length : before[index].Start;
            var afterEnd = index == after.Count ? corrected.Length : after[index].Start;
            AddEdit(original[beforeStart..beforeEnd], corrected[afterStart..afterEnd], beforeStart, edits);
            if (index == before.Count)
            {
                break;
            }

            if (!original.AsSpan(before[index].Start, before[index].Length)
                .SequenceEqual(corrected.AsSpan(after[index].Start, after[index].Length)))
            {
                return null;
            }

            beforeStart = before[index].End;
            afterStart = after[index].End;
        }

        var ordered = edits.OrderByDescending(edit => edit.Start).ToArray();
        var reconstructed = original;
        foreach (var edit in ordered)
        {
            reconstructed = reconstructed.Remove(edit.Start, edit.Original.Length).Insert(edit.Start, edit.Replacement);
        }

        return reconstructed == corrected ? new ComposerEditPlan(original, corrected, ordered) : null;
    }

    private static void AddEdit(string original, string corrected, int start, List<ComposerTextEdit> edits)
    {
        if (original == corrected)
        {
            return;
        }

        var prefix = 0;
        while (prefix < Math.Min(original.Length, corrected.Length) && original[prefix] == corrected[prefix])
        {
            prefix++;
        }

        var originalBoundaries = StringInfo.ParseCombiningCharacters(original).Append(original.Length).ToHashSet();
        var correctedBoundaries = StringInfo.ParseCombiningCharacters(corrected).Append(corrected.Length).ToHashSet();
        while (!originalBoundaries.Contains(prefix) || !correctedBoundaries.Contains(prefix))
        {
            prefix--;
        }

        var suffix = 0;
        while (suffix < Math.Min(original.Length, corrected.Length) - prefix
            && original[^(suffix + 1)] == corrected[^(suffix + 1)])
        {
            suffix++;
        }

        while (!originalBoundaries.Contains(original.Length - suffix)
            || !correctedBoundaries.Contains(corrected.Length - suffix))
        {
            suffix--;
        }

        edits.Add(new ComposerTextEdit(start + prefix,
            original.Substring(prefix, original.Length - prefix - suffix),
            corrected.Substring(prefix, corrected.Length - prefix - suffix)));
    }

    private static IReadOnlyList<TextSpan> GetFrozenRanges(string text)
    {
        var ranges = ProtectedSpanDetector.Detect(text).ToList();
        foreach (var line in TextStructure.GetLines(text))
        {
            if (line.PrefixLength > 0)
            {
                ranges.Add(new TextSpan(line.Start, line.PrefixLength));
            }

            var suffixLength = line.TrailingWhitespaceLength + line.LineEnding.Length;
            if (suffixLength > 0)
            {
                ranges.Add(new TextSpan(line.Start + line.Content.Length - line.TrailingWhitespaceLength, suffixLength));
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsControl(text[index]) || text[index] is '\ufffc' or '\u2028' or '\u2029')
            {
                ranges.Add(new TextSpan(index, 1));
            }
        }

        var merged = new List<TextSpan>();
        foreach (var span in ranges.OrderBy(range => range.Start))
        {
            if (merged.Count == 0 || span.Start > merged[^1].End)
            {
                merged.Add(span);
            }
            else
            {
                var previous = merged[^1];
                merged[^1] = new TextSpan(previous.Start, Math.Max(previous.End, span.End) - previous.Start);
            }
        }

        return merged;
    }
}

internal interface IComposerEditTarget
{
    bool IsCurrentEditor { get; }
    string? Read();
    bool CanReplace(int start, string expected);
    bool Replace(int start, string expected, string replacement);
}

internal static class ComposerWriteTransaction
{
    public static ComposerWriteResult Apply(ComposerEditPlan plan, IComposerEditTarget target)
    {
        var expected = plan.Original;
        var applied = new List<ComposerTextEdit>();
        ComposerTextEdit? pending = null;
        string? pendingAfter = null;
        var attemptedWrite = false;
        try
        {
            if (!target.IsCurrentEditor || target.Read() != expected
                || plan.Edits.Any(edit => !target.CanReplace(edit.Start, edit.Original)))
            {
                return Recover("selection_preflight_failed");
            }

            foreach (var edit in plan.Edits)
            {
                if (!target.IsCurrentEditor || target.Read() != expected)
                {
                    return Recover("editor_changed");
                }

                var next = expected.Remove(edit.Start, edit.Original.Length).Insert(edit.Start, edit.Replacement);
                pending = edit;
                pendingAfter = next;
                attemptedWrite = true;
                var succeeded = target.Replace(edit.Start, edit.Original, edit.Replacement);
                var observed = target.Read();
                if (observed == next)
                {
                    applied.Add(edit);
                    expected = next;
                }

                pending = null;

                if (!succeeded || observed != next)
                {
                    return Recover("write_not_verified");
                }
            }

            if (!target.IsCurrentEditor || target.Read() != plan.Corrected)
            {
                return Recover("final_text_mismatch");
            }

            return new ComposerWriteResult(ComposerWriteState.Applied, plan.Original, plan.Corrected, applied.Count, "ok_verified");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Recover("write_exception");
        }

        ComposerWriteResult Recover(string status)
        {
            try
            {
                if (pending is not null && target.IsCurrentEditor && target.Read() == pendingAfter)
                {
                    applied.Add(pending);
                    expected = pendingAfter!;
                }

                if (target.IsCurrentEditor && target.Read() == expected)
                {
                    for (var index = applied.Count - 1; index >= 0; index--)
                    {
                        var edit = applied[index];
                        if (!target.IsCurrentEditor || target.Read() != expected)
                        {
                            break;
                        }

                        var previous = expected.Remove(edit.Start, edit.Replacement.Length).Insert(edit.Start, edit.Original);
                        _ = target.Replace(edit.Start, edit.Replacement, edit.Original);
                        if (target.Read() != previous)
                        {
                            break;
                        }

                        expected = previous;
                    }
                }

                var observed = target.Read();
                return new ComposerWriteResult(!attemptedWrite || observed == plan.Original ? ComposerWriteState.Unchanged : ComposerWriteState.PartiallyApplied,
                    plan.Original, observed, applied.Count, status);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new ComposerWriteResult(attemptedWrite ? ComposerWriteState.PartiallyApplied : ComposerWriteState.Unchanged,
                    plan.Original, null, applied.Count, status);
            }
        }
    }
}
