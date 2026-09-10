using System.Globalization;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Core;

public sealed record ComposerTextEdit(int Start, string Original, string Replacement);

public sealed record ComposerEditPlan(string Original, string Corrected, IReadOnlyList<ComposerTextEdit> Edits)
{
    public ComposerEditPlan Reverse()
    {
        var offset = 0;
        var reversed = new List<ComposerTextEdit>(Edits.Count);
        foreach (var edit in Edits.OrderBy(edit => edit.Start))
        {
            reversed.Add(new ComposerTextEdit(edit.Start + offset, edit.Replacement, edit.Original));
            offset += edit.Replacement.Length - edit.Original.Length;
        }
        return new ComposerEditPlan(Corrected, Original, reversed.OrderByDescending(edit => edit.Start).ToArray());
    }

    public static ComposerEditPlan? Create(string original, string corrected)
    {
        if (!TextStructure.IsPreserved(original, corrected))
        {
            return null;
        }
        if (original == corrected)
        {
            return new ComposerEditPlan(original, corrected, []);
        }

        var anchors = MapFrozenRanges(original, corrected);
        if (anchors is null)
        {
            return null;
        }
        var before = anchors.Select(anchor => anchor.Before).ToArray();
        var after = anchors.Select(anchor => anchor.After).ToArray();

        var edits = new List<ComposerTextEdit>();
        var beforeStart = 0;
        var afterStart = 0;
        for (var index = 0; index <= before.Length; index++)
        {
            var beforeEnd = index == before.Length ? original.Length : before[index].Start;
            var afterEnd = index == after.Length ? corrected.Length : after[index].Start;
            if (!AddEdit(original[beforeStart..beforeEnd], corrected[afterStart..afterEnd], beforeStart, edits))
            {
                return null;
            }
            if (index == before.Length)
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

    private static bool AddEdit(string original, string corrected, int start, List<ComposerTextEdit> edits)
    {
        if (original == corrected)
        {
            return true;
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

        if (original.Length - prefix - suffix == 0 || corrected.Length - prefix - suffix == 0)
        {
            // Freie Grapheme dienen nach Möglichkeit als Auswahlanker. Geschützte Nachbarn
            // werden dafür nie einbezogen; ohne freien Anker bleibt die Änderung einseitig.
            if (prefix > 0)
            {
                prefix = originalBoundaries.Where(boundary => boundary < prefix
                    && correctedBoundaries.Contains(boundary)).Max();
            }
            else if (suffix > 0)
            {
                var originalEnd = original.Length - suffix;
                var correctedEnd = corrected.Length - suffix;
                var anchorLength = originalBoundaries.Where(boundary => boundary > originalEnd)
                    .Select(boundary => boundary - originalEnd)
                    .Where(length => correctedBoundaries.Contains(correctedEnd + length)).Min();
                suffix -= anchorLength;
            }
        }

        edits.Add(new ComposerTextEdit(start + prefix,
            original.Substring(prefix, original.Length - prefix - suffix),
            corrected.Substring(prefix, corrected.Length - prefix - suffix)));
        return true;
    }

    private sealed record FrozenAnchor(TextSpan Before, TextSpan After);

    private static IReadOnlyList<FrozenAnchor>? MapFrozenRanges(string original, string corrected)
    {
        var beforeStructure = GetStructureRanges(original);
        var afterStructure = GetStructureRanges(corrected);
        if (beforeStructure.Count != afterStructure.Count)
        {
            return null;
        }

        var anchors = new List<FrozenAnchor>();
        for (var index = 0; index < beforeStructure.Count; index++)
        {
            var before = beforeStructure[index];
            var after = afterStructure[index];
            if (!original.AsSpan(before.Start, before.Length).SequenceEqual(corrected.AsSpan(after.Start, after.Length)))
            {
                return null;
            }
            anchors.Add(new FrozenAnchor(before, after));
        }

        // Only the original decides what is technical. New punctuation or identifiers in
        // corrected prose must not enlarge an immutable source span.
        var technical = ProtectedSpanDetector.Detect(original);
        var earliest = new int[technical.Count];
        var cursor = 0;
        for (var index = 0; index < technical.Count; index++)
        {
            var span = technical[index];
            var value = original.Substring(span.Start, span.Length);
            var found = corrected.IndexOf(value, cursor, StringComparison.Ordinal);
            if (found < 0) return null;
            earliest[index] = found;
            cursor = found + span.Length;
        }

        cursor = corrected.Length;
        for (var index = technical.Count - 1; index >= 0; index--)
        {
            var span = technical[index];
            var value = original.Substring(span.Start, span.Length);
            var found = corrected.AsSpan(0, cursor).LastIndexOf(value.AsSpan(), StringComparison.Ordinal);
            // Earliest and latest ordered mappings agree exactly, otherwise identity is ambiguous.
            if (found != earliest[index]) return null;
            anchors.Add(new FrozenAnchor(span, new TextSpan(found, span.Length)));
            cursor = found;
        }

        var merged = new List<FrozenAnchor>();
        foreach (var anchor in anchors.OrderBy(item => item.Before.Start).ThenBy(item => item.Before.End))
        {
            if (merged.Count == 0)
            {
                merged.Add(anchor);
                continue;
            }
            var previous = merged[^1];
            if (anchor.Before.Start >= previous.Before.End)
            {
                if (anchor.After.Start < previous.After.End) return null;
                merged.Add(anchor);
            }
            else
            {
                // Overlapping structural and technical anchors must describe the same bytes.
                if (anchor.After.Start - previous.After.Start != anchor.Before.Start - previous.Before.Start) return null;
                var length = Math.Max(previous.Before.End, anchor.Before.End) - previous.Before.Start;
                merged[^1] = new FrozenAnchor(new TextSpan(previous.Before.Start, length), new TextSpan(previous.After.Start, length));
            }
        }
        var beforeBoundaries = StringInfo.ParseCombiningCharacters(original).Append(original.Length).ToHashSet();
        var afterBoundaries = StringInfo.ParseCombiningCharacters(corrected).Append(corrected.Length).ToHashSet();
        return merged.All(anchor => beforeBoundaries.Contains(anchor.Before.Start) && beforeBoundaries.Contains(anchor.Before.End)
            && afterBoundaries.Contains(anchor.After.Start) && afterBoundaries.Contains(anchor.After.End)) ? merged : null;
    }

    private static IReadOnlyList<TextSpan> GetStructureRanges(string text)
    {
        var ranges = new List<TextSpan>();
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
            if (merged.Count == 0 || span.Start >= merged[^1].End)
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
                    pending = null;
                }

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
