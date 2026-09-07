using System.Text.RegularExpressions;

namespace CodexLanguageFix.Core;

public static class TextStructure
{
    private static readonly Regex PrefixToken = new(
        @"\G(?:[^\S\r\n]+|>[^\S\r\n]?|(?:[-+*•◦▪‣⁃]|[0-9]+[.)])[^\S\r\n]+|#{1,6}[^\S\r\n]+|\[[ xX]\][^\S\r\n]+)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool IsPreserved(string original, string corrected)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(corrected);
        var source = GetLines(original);
        var target = GetLines(corrected);
        if (source.Count != target.Count)
        {
            return false;
        }

        for (var index = 0; index < source.Count; index++)
        {
            var before = source[index];
            var after = target[index];
            if (before.LineEnding != after.LineEnding
                || !before.Content.AsSpan(0, before.PrefixLength)
                    .SequenceEqual(after.Content.AsSpan(0, after.PrefixLength))
                || !before.Content.AsSpan(before.Content.Length - before.TrailingWhitespaceLength)
                    .SequenceEqual(after.Content.AsSpan(after.Content.Length - after.TrailingWhitespaceLength)))
            {
                return false;
            }

            var beforeBody = before.Content.AsSpan(before.PrefixLength,
                Math.Max(0, before.Content.Length - before.PrefixLength - before.TrailingWhitespaceLength));
            var afterBody = after.Content.AsSpan(after.PrefixLength,
                Math.Max(0, after.Content.Length - after.PrefixLength - after.TrailingWhitespaceLength));
            if (beforeBody.IsWhiteSpace() != afterBody.IsWhiteSpace())
            {
                return false;
            }
        }

        return true;
    }

    internal static IReadOnlyList<TextStructureLine> GetLines(string text)
    {
        var lines = new List<TextStructureLine>();
        var start = 0;
        while (true)
        {
            var end = start;
            while (end < text.Length && text[end] is not ('\r' or '\n'))
            {
                end++;
            }

            var endingLength = end == text.Length ? 0
                : text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
            var content = text[start..end];
            var prefixLength = 0;
            for (var match = PrefixToken.Match(content); match.Success; match = PrefixToken.Match(content, prefixLength))
            {
                prefixLength += match.Length;
            }

            var trailing = content.Length;
            while (trailing > prefixLength && char.IsWhiteSpace(content[trailing - 1]))
            {
                trailing--;
            }

            lines.Add(new TextStructureLine(start, content, text.Substring(end, endingLength),
                prefixLength, content.Length - trailing));
            if (endingLength == 0)
            {
                return lines;
            }

            start = end + endingLength;
        }
    }
}

internal sealed record TextStructureLine(
    int Start,
    string Content,
    string LineEnding,
    int PrefixLength,
    int TrailingWhitespaceLength);
