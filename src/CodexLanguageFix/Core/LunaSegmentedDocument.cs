using System.Text;

namespace CodexLanguageFix.Core;

internal sealed class LunaSegmentedDocument
{
    private readonly IReadOnlyList<string> _separators;

    private LunaSegmentedDocument(IReadOnlyList<LunaTextSegment> segments, IReadOnlyList<string> separators)
    {
        Segments = segments;
        _separators = separators;
    }

    public IReadOnlyList<LunaTextSegment> Segments { get; }

    public static LunaSegmentedDocument Create(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return new LunaSegmentedDocument([], []);
        }

        var segments = new List<LunaTextSegment>();
        var separators = new List<string>();
        var pendingSeparator = new StringBuilder();
        var segmentStart = 0;
        var index = 0;
        while (index < text.Length)
        {
            var separatorStart = FindSeparator(text, index);
            if (separatorStart < 0)
            {
                break;
            }

            var separatorEnd = separatorStart;
            while (separatorEnd < text.Length && char.IsWhiteSpace(text[separatorEnd]))
            {
                separatorEnd++;
            }

            if (separatorStart > segmentStart)
            {
                var segmentText = text[segmentStart..separatorStart];
                if (segments.Count == 0)
                {
                    // Führende Trenner gehören zum ersten Segment, damit die
                    // bestehende Segment-/Trenner-Repräsentation verlustfrei bleibt.
                    segmentText = pendingSeparator.ToString() + segmentText;
                }
                else
                {
                    separators.Add(pendingSeparator.ToString());
                }

                pendingSeparator.Clear();
                segments.Add(new LunaTextSegment(segments.Count, segmentText));
            }

            // Ein weiterer Trenner direkt am Segmentanfang (beispielsweise bei
            // Leerzeilen) wird mit dem bereits wartenden Trenner zusammengeführt.
            pendingSeparator.Append(text, separatorStart, separatorEnd - separatorStart);
            segmentStart = separatorEnd;
            index = separatorEnd;
        }

        if (segmentStart < text.Length)
        {
            var segmentText = text[segmentStart..];
            if (segments.Count == 0)
            {
                segmentText = pendingSeparator.ToString() + segmentText;
            }
            else
            {
                separators.Add(pendingSeparator.ToString());
            }

            pendingSeparator.Clear();
            segments.Add(new LunaTextSegment(segments.Count, segmentText));
        }
        else if (segments.Count > 0 && pendingSeparator.Length > 0)
        {
            // Suffix-Trenner gehören zum letzten Segment.
            var last = segments[^1];
            segments[^1] = last with { Text = last.Text + pendingSeparator.ToString() };
        }

        if (segments.Count == 0)
        {
            segments.Add(new LunaTextSegment(0, text));
        }

        return new LunaSegmentedDocument(segments, separators);
    }

    public bool TryApply(IReadOnlyList<LunaSegmentChange> changes, out string corrected, out int changeCount)
    {
        ArgumentNullException.ThrowIfNull(changes);
        corrected = string.Empty;
        changeCount = 0;
        var replacements = new Dictionary<int, string>();
        var previousId = -1;

        foreach (var change in changes)
        {
            if (change.Id <= previousId
                || change.Id < 0
                || change.Id >= Segments.Count
                || replacements.ContainsKey(change.Id)
                || change.Text is null
                || string.Equals(change.Text, Segments[change.Id].Text, StringComparison.Ordinal)
                || change.Text.Length > Math.Max(1_000, Segments[change.Id].Text.Length * 2 + 500)
                || change.Text.Any(character => char.IsControl(character)
                    && character is not ('\r' or '\n' or '\t'))
                || !HasStableLineBreaks(Segments[change.Id].Text, change.Text)
                || ContainsUnpairedSurrogate(change.Text))
            {
                return false;
            }

            replacements.Add(change.Id, change.Text);
            previousId = change.Id;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < Segments.Count; index++)
        {
            builder.Append(replacements.TryGetValue(index, out var replacement)
                ? replacement
                : Segments[index].Text);
            if (index < _separators.Count)
            {
                builder.Append(_separators[index]);
            }
        }

        corrected = builder.ToString();
        changeCount = replacements.Count;
        return true;
    }

    private static int FindSeparator(string text, int start)
    {
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                return index;
            }

            if (text[index] is '.' or '!' or '?'
                && index + 1 < text.Length
                && char.IsWhiteSpace(text[index + 1]))
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static bool HasStableLineBreaks(string original, string corrected)
    {
        var sourceBreaks = GetLineBreaks(original);
        var correctedBreaks = GetLineBreaks(corrected);
        return sourceBreaks.SequenceEqual(correctedBreaks, StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetLineBreaks(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    yield return "\r\n";
                    index++;
                }
                else
                {
                    yield return "\r";
                }
            }
            else if (text[index] == '\n')
            {
                yield return "\n";
            }
        }
    }

    private static bool ContainsUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record LunaTextSegment(int Id, string Text);

internal sealed record LunaSegmentChange(int Id, string? Text);
