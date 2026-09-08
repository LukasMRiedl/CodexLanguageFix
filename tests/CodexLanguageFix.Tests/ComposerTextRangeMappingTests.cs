using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class ComposerTextRangeMappingTests
{
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
