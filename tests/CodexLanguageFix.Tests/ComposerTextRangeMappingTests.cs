using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class ComposerTextRangeMappingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RepeatedBulletsUseTheirOwnExactTextNodeAsSearchAnchor(int bulletIndex)
    {
        const string leaf = "Das ist ein fehler.";
        var full = string.Join("\n", Enumerable.Repeat("• " + leaf, 3));
        var leafStart = bulletIndex * (leaf.Length + 3) + 2;
        var start = leafStart + leaf.IndexOf('f');
        Assert.True(ComposerTextRangeMapping.IsSearchAnchor(full, start, full[..leafStart], leaf));
        if (bulletIndex > 0)
            Assert.False(ComposerTextRangeMapping.IsSearchAnchor(full, start, full[..2], leaf));
    }

    [Theory]
    [InlineData("wrong-prefix")]
    [InlineData("missing-marker")]
    [InlineData("wrong-marker")]
    [InlineData("normalized-newline")]
    [InlineData("wrong-text")]
    public void SearchAnchorRejectsChangedDocumentParts(string change)
    {
        const string full = "• fehler\r\n• fehler";
        const string prefix = "• fehler\r\n• ";
        Assert.False(ComposerTextRangeMapping.IsSearchAnchor(full, prefix.Length,
            change switch
            {
                "wrong-prefix" => "• Fehler\r\n• ",
                "missing-marker" => "• fehler\r\n",
                "wrong-marker" => "• fehler\r\n- ",
                "normalized-newline" => "• fehler\n• ",
                _ => prefix
            }, change == "wrong-text" ? "Fehler" : "fehler"));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SearchAnchorCannotCrossAnEarlierLineBreak(string newline)
    {
        var full = "a" + newline + "b";
        Assert.True(ComposerTextRangeMapping.IsSearchAnchor(full, 0, "", full));
        Assert.False(ComposerTextRangeMapping.IsSearchAnchor(full, 1 + newline.Length, "", full));
        Assert.True(ComposerTextRangeMapping.IsSearchAnchor(full, 1 + newline.Length, "a" + newline, "b"));
    }

    [Theory]
    [InlineData("🙂")]
    [InlineData("a\u0308")]
    [InlineData("👩\u200d💻")]
    public void UnicodeSearchAnchorsKeepExactUtf16OffsetsAndContent(string grapheme)
    {
        var leaf = grapheme + " fehler";
        var prefix = "• " + leaf + "\r\n• ";
        var full = prefix + leaf;
        var start = prefix.Length + grapheme.Length + 1;
        Assert.True(ComposerTextRangeMapping.IsSearchAnchor(full, start, prefix, leaf));
        Assert.False(ComposerTextRangeMapping.IsSearchAnchor(full, start, prefix[..^1], leaf));
        Assert.False(ComposerTextRangeMapping.IsSearchAnchor(full, start, prefix, leaf[1..]));
    }

    [Theory]
    [InlineData(-1, "", "abc")]
    [InlineData(3, "", "abc")]
    [InlineData(int.MaxValue, "", "abc")]
    [InlineData(1, "ab", "c")]
    [InlineData(0, "", "")]
    [InlineData(3, "abc", "")]
    [InlineData(0, "", "abcd")]
    [InlineData(2, "ab", "cd")]
    [InlineData(2, "abcd", "")]
    public void InvalidSearchAnchorBoundsAreRejectedWithoutSlicingExceptions(int start, string prefix, string rangeText) =>
        Assert.False(ComposerTextRangeMapping.IsSearchAnchor("abc", start, prefix, rangeText));

    [Theory]
    [InlineData("Das ist ein fehler.", 12, "f")]
    [InlineData("Das ist ein fehler.\r\n", 12, "f")]
    [InlineData("Das ist ein Test", 15, "t")]
    [InlineData("Das ist ein Test", 16, "")]
    [InlineData("Das ist ein Test\n", 16, "")]
    [InlineData("This is a test.", 9, "")]
    [InlineData("Grüße 🙂", 0, "")]
    [InlineData("Grüße 🙂", 8, "")]
    [InlineData("A\r\nB", 3, "")]
    [InlineData("• This is an test.\n• Grüße 🙂", 11, "n")]
    public void SelectionPositionCanBeProvenFromExactDocumentParts(string full, int start, string expected)
    {
        Assert.True(ComposerTextRangeMapping.MatchesSelection(full, start, expected,
            full, full[..start], full[..(start + expected.Length)], expected, full[(start + expected.Length)..]));
    }

    [Theory]
    [InlineData("document")]
    [InlineData("prefix")]
    [InlineData("through")]
    [InlineData("selection")]
    [InlineData("suffix")]
    public void EveryObservedDocumentPartMustMatchWithoutNormalization(string changedPart)
    {
        const string full = "• fehler\r\n• Grüße a\u0308 🙂";
        const int start = 2;
        const string expected = "f";
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, start, expected,
            changedPart == "document" ? full.Replace("\r\n", "\n") : full,
            changedPart == "prefix" ? "" : full[..start],
            changedPart == "through" ? full[..(start + 2)] : full[..(start + 1)],
            changedPart == "selection" ? "f\r\n" : expected,
            changedPart == "suffix" ? full[(start + 1)..].Normalize() : full[(start + 1)..]));
    }

    [Fact]
    public void SameSelectedTextAtAnotherOccurrenceIsRejected()
    {
        const string full = "ana\nana";
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, 4, "ana",
            full, "", "ana", "ana", "\nana"));
    }

    [Theory]
    [InlineData("🙂")]
    [InlineData("a\u0308")]
    [InlineData("👩\u200d💻")]
    public void RepeatedUnicodeRequiresTheExactUtf16Position(string grapheme)
    {
        var full = $"• Grüße {grapheme}\r\n• Grüße {grapheme}\r\n";
        var first = full.IndexOf(grapheme, StringComparison.Ordinal);
        var requested = full.LastIndexOf(grapheme, StringComparison.Ordinal);
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, requested, grapheme,
            full, full[..first], full[..(first + grapheme.Length)], grapheme, full[(first + grapheme.Length)..]));
        Assert.True(ComposerTextRangeMapping.MatchesSelection(full, requested, grapheme,
            full, full[..requested], full[..(requested + grapheme.Length)], grapheme, full[(requested + grapheme.Length)..]));
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, requested, grapheme,
            full, full[..requested], full[..(requested + grapheme.Length)], grapheme[..^1], full[(requested + grapheme.Length)..]));
    }

    [Theory]
    [InlineData("prefix")]
    [InlineData("through")]
    [InlineData("suffix")]
    public void ContextCannotCollapseCrLfEvenWhenDocumentAndSelectedTextStillMatch(string changedPart)
    {
        const string full = "one\r\nt\r\nlast";
        const int start = 5;
        var prefix = full[..start];
        var through = full[..(start + 1)];
        var suffix = full[(start + 1)..];
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, start, "t", full,
            changedPart == "prefix" ? prefix.Replace("\r\n", "\n") : prefix,
            changedPart == "through" ? through.Replace("\r\n", "\n") : through,
            "t", changedPart == "suffix" ? suffix.Replace("\r\n", "\n") : suffix));
    }

    [Theory]
    [InlineData("\ufffc")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void SurroundingProtectedObjectsAndSeparatorsCannotDisappear(string separator)
    {
        var full = "A" + separator + "t" + separator + "B";
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, 2, "t",
            full, "A", full[..3], "t", full[3..]));
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, 2, "t",
            full, full[..2], full[..3], "t", "B"));
    }

    [Fact]
    public void SameLengthForeignOrChangedDocumentDoesNotProveTheSelection()
    {
        const string full = "a t b";
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, 2, "t",
            "x t y", "a ", "a t", "t", " b"));
    }

    [Fact]
    public void EmptySelectionCannotHideAnotherCaretPositionOrConsumedLineBreak()
    {
        const string full = "A\r\nB";
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, 3, "",
            full, "A", "A", "", "\r\nB"));
        Assert.False(ComposerTextRangeMapping.MatchesSelection(full, 1, "",
            full, "A", "A\r\n", "", "B"));
    }

    [Fact]
    public void InvalidSelectionOffsetsAreRejectedWithoutSlicingExceptions()
    {
        foreach (var start in new[] { -1, 4, int.MaxValue })
            Assert.False(ComposerTextRangeMapping.MatchesSelection("abc", start, "a", "abc", "", "", "a", ""));
    }

    [Fact]
    public void RichListRangeRequiresItsExactSerializedPrefix()
    {
        const string text = "Das ist ein fehler.\n• This is an test.\n• Grüße 🙂 an test@example.com";
        var start = text.IndexOf("an test.", StringComparison.Ordinal) + 1;
        Assert.True(ComposerTextRangeMapping.Matches(text, start, "n", text[..start], "n"));
        Assert.False(ComposerTextRangeMapping.Matches(text, start, "n", text[..start].Replace("• ", ""), "n"));
        Assert.False(ComposerTextRangeMapping.Matches(text, start, "n", text[..start].Replace("\n", "\r\n"), "n"));
        Assert.False(ComposerTextRangeMapping.Matches(text, start, "n", text[..start], "an"));
    }

    [Fact]
    public void EqualAndOverlappingTextStillRequiresTheRequestedOccurrence()
    {
        Assert.False(ComposerTextRangeMapping.Matches("banana", 3, "ana", "b", "ana"));
        Assert.True(ComposerTextRangeMapping.Matches("banana", 3, "ana", "ban", "ana"));
        Assert.False(ComposerTextRangeMapping.Matches("n n n", 4, "n", "n ", "n"));
        Assert.True(ComposerTextRangeMapping.Matches("n n n", 4, "n", "n n ", "n"));
    }

    [Theory]
    [InlineData(-1, "a")]
    [InlineData(4, "")]
    [InlineData(3, "a")]
    [InlineData(1, "x")]
    public void InvalidSourceOffsetsAndContentAreRejected(int start, string expected) =>
        Assert.False(ComposerTextRangeMapping.Matches("abc", start, expected, "", expected));

    [Theory]
    [InlineData("", 0, 0, "")]
    [InlineData("Text", 0, 0, "T")]
    [InlineData("Text", 4, 3, "t")]
    [InlineData("This is a test.", 9, 8, "a")]
    [InlineData("• Grüße 🙂 an test@example.com", 10, 8, "🙂")]
    [InlineData("a\u0308b", 2, 0, "a\u0308")]
    [InlineData("A\r\nB", 3, 1, "\r\n")]
    public void InsertionUsesOnlyACompleteKnownNeighbourGrapheme(
        string full, int start, int anchorStart, string anchorText)
    {
        var anchor = ComposerTextRangeMapping.InsertionAnchor(full, start);
        Assert.NotNull(anchor);
        Assert.Equal(anchorStart, anchor.Value.Start);
        Assert.Equal(anchorText, full.Substring(anchor.Value.Start, anchor.Value.Length));
        Assert.True(ComposerTextRangeMapping.Matches(full, start, "", full[..start], ""));
    }

    [Theory]
    [InlineData("a", -1)]
    [InlineData("a", 2)]
    [InlineData("🙂", 1)]
    [InlineData("a\u0308", 1)]
    [InlineData("A\r\nB", 2)]
    public void InsertionRejectsUnknownOrSplitGraphemeOffsets(string full, int start) =>
        Assert.Null(ComposerTextRangeMapping.InsertionAnchor(full, start));
}
