using System.Globalization;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Windows;

internal static class ComposerTextRangeMapping
{
    internal static bool IsSearchAnchor(string full, int start, string prefix, string rangeText) =>
        prefix.Length <= full.Length && rangeText.Length <= full.Length - prefix.Length
        && start >= prefix.Length && start - prefix.Length < rangeText.Length
        && prefix.AsSpan().SequenceEqual(full.AsSpan(0, prefix.Length))
        && rangeText.AsSpan().SequenceEqual(full.AsSpan(prefix.Length, rangeText.Length))
        && rangeText.AsSpan(0, start - prefix.Length).IndexOfAny('\r', '\n') < 0;

    internal static bool Matches(string full, int start, string expected, string prefix, string rangeText) =>
        start >= 0 && start <= full.Length && expected.Length <= full.Length - start
        && full.AsSpan(start, expected.Length).SequenceEqual(expected)
        && prefix.AsSpan().SequenceEqual(full.AsSpan(0, start))
        && rangeText == expected;

    internal static bool MatchesSelection(string full, int start, string expected,
        string documentText, string prefix, string through, string selectionText, string suffix) =>
        documentText == full && Matches(full, start, expected, prefix, selectionText)
        && through.AsSpan().SequenceEqual(full.AsSpan(0, start + expected.Length))
        && suffix.AsSpan().SequenceEqual(full.AsSpan(start + expected.Length));

    internal static TextSpan? InsertionAnchor(string full, int start)
    {
        if (start < 0 || start > full.Length) return null;
        if (full.Length == 0) return new TextSpan(0, 0);

        var boundaries = StringInfo.ParseCombiningCharacters(full);
        var index = start == full.Length ? boundaries.Length : Array.BinarySearch(boundaries, start);
        if (index < 0) return null;
        if (index == 0)
            return new TextSpan(0, boundaries.Length > 1 ? boundaries[1] : full.Length);

        return new TextSpan(boundaries[index - 1], start - boundaries[index - 1]);
    }
}
