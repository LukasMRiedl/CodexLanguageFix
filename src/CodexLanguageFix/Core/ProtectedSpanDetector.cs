using System.Text.RegularExpressions;

namespace CodexLanguageFix.Core;

public static partial class ProtectedSpanDetector
{
    public static IReadOnlyList<TextSpan> Detect(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var spans = new List<TextSpan>();
        AddFencedCodeBlocks(text, spans);
        AddInlineCode(text, spans);

        AddMatches(text, MarkdownLinkTargetRegex(), spans);
        AddMatches(text, UrlRegex(), spans);
        AddMatches(text, EmailRegex(), spans);
        AddMatches(text, WindowsPathRegex(), spans);
        AddMatches(text, UnixPathRegex(), spans);
        AddMatches(text, CommandOptionRegex(), spans);
        AddMatches(text, IdentifierRegex(), spans);
        AddMatches(text, GuidRegex(), spans);

        return Merge(spans, text.Length);
    }

    private static void AddFencedCodeBlocks(string text, ICollection<TextSpan> spans)
    {
        var position = 0;
        var fenceStart = -1;
        var fenceCharacter = '\0';
        var fenceLength = 0;

        while (position < text.Length)
        {
            var lineStart = position;
            var newline = text.IndexOf('\n', position);
            var lineEnd = newline >= 0 ? newline + 1 : text.Length;
            var contentEnd = newline >= 0 && newline > lineStart && text[newline - 1] == '\r' ? newline - 1 : newline >= 0 ? newline : text.Length;
            var line = text.AsSpan(lineStart, contentEnd - lineStart);
            var leading = 0;
            while (leading < line.Length && leading < 4 && (line[leading] == ' ' || line[leading] == '\t'))
            {
                leading++;
            }

            if (leading <= 3 && leading < line.Length && (line[leading] == '`' || line[leading] == '~'))
            {
                var currentCharacter = line[leading];
                var count = 0;
                while (leading + count < line.Length && line[leading + count] == currentCharacter)
                {
                    count++;
                }

                if (count >= 3)
                {
                    if (fenceStart < 0)
                    {
                        fenceStart = lineStart;
                        fenceCharacter = currentCharacter;
                        fenceLength = count;
                    }
                    else if (currentCharacter == fenceCharacter && count >= fenceLength)
                    {
                        spans.Add(new TextSpan(fenceStart, lineEnd - fenceStart));
                        fenceStart = -1;
                    }
                }
            }

            position = lineEnd;
        }

        if (fenceStart >= 0)
        {
            spans.Add(new TextSpan(fenceStart, text.Length - fenceStart));
        }
    }

    private static void AddInlineCode(string text, ICollection<TextSpan> spans)
    {
        for (var index = 0; index < text.Length;)
        {
            if (text[index] != '`' || IsCovered(index, spans))
            {
                index++;
                continue;
            }

            var tickCount = 1;
            while (index + tickCount < text.Length && text[index + tickCount] == '`')
            {
                tickCount++;
            }

            var closing = text.IndexOf(new string('`', tickCount), index + tickCount, StringComparison.Ordinal);
            if (closing >= 0 && !text.AsSpan(index, closing + tickCount - index).Contains('\n'))
            {
                spans.Add(new TextSpan(index, closing + tickCount - index));
                index = closing + tickCount;
            }
            else
            {
                index += tickCount;
            }
        }
    }

    private static void AddMatches(string text, Regex regex, ICollection<TextSpan> spans)
    {
        foreach (Match match in regex.Matches(text))
        {
            if (match.Success && match.Length > 0)
            {
                spans.Add(new TextSpan(match.Index, match.Length));
            }
        }
    }

    private static bool IsCovered(int index, IEnumerable<TextSpan> spans) =>
        spans.Any(span => index >= span.Start && index < span.End);

    private static IReadOnlyList<TextSpan> Merge(IEnumerable<TextSpan> spans, int textLength)
    {
        var ordered = spans
            .Select(span => new TextSpan(Math.Clamp(span.Start, 0, textLength), Math.Clamp(span.Length, 0, textLength - Math.Clamp(span.Start, 0, textLength))))
            .Where(span => span.Length > 0)
            .OrderBy(span => span.Start)
            .ThenByDescending(span => span.Length)
            .ToArray();

        if (ordered.Length == 0)
        {
            return [];
        }

        var merged = new List<TextSpan>();
        var current = ordered[0];
        foreach (var next in ordered.Skip(1))
        {
            if (next.Start <= current.End)
            {
                current = new TextSpan(current.Start, Math.Max(current.End, next.End) - current.Start);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    [GeneratedRegex(@"(?<=\]\()[^)\r\n]+(?=\))", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkTargetRegex();

    [GeneratedRegex(@"\b(?:https?://|www\.)[^\s<>()]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\w)(?:[A-Za-z]:\\|\\\\)[^\s<>\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<!\w)/(?:[^\s/<>\""']+/)+[^\s<>\""']*", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex(@"(?<![\w-])--?[A-Za-z0-9][\w-]*(?:=[^\s]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex CommandOptionRegex();

    [GeneratedRegex(@"\b(?:[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+|[a-z][a-z0-9]+(?:[A-Z][A-Za-z0-9]*)+|[A-Z][a-z0-9]+(?:[A-Z][A-Za-z0-9]*)+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();
}
