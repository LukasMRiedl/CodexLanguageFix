using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class LunaSegmentedDocumentTests
{
    [Theory]
    [InlineData("Erster Satz. Zweiter Satz.")]
    [InlineData("Erste Zeile\r\nZweite Zeile\r\n")]
    [InlineData("\r\nErste Zeile\r\n\r\nZweite Zeile\n")]
    [InlineData("\n\n")]
    [InlineData("Emoji 🙂 bleibt. Markdown **auch**.")]
    [InlineData("Ein einzelner Satz ohne Trennung")]
    public void Create_AndEmptyPatch_RoundTripsByteExactly(string input)
    {
        var document = LunaSegmentedDocument.Create(input);

        Assert.True(document.TryApply([], out var reconstructed, out var changes));
        Assert.Equal(input, reconstructed);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void TryApply_AppliesOrderedChangesAndPreservesSeparators()
    {
        var document = LunaSegmentedDocument.Create("Das ist korekt. Zweite Zeile\r\nDritte Zeile.");

        Assert.True(document.TryApply(
            [new LunaSegmentChange(0, "Das ist korrekt."), new LunaSegmentChange(2, "Dritte Zeile!")],
            out var corrected,
            out var count));

        Assert.Equal("Das ist korrekt. Zweite Zeile\r\nDritte Zeile!", corrected);
        Assert.Equal(2, count);
    }

    [Fact]
    public void TryApply_RejectsUnknownDuplicateReorderedUnchangedAndControlCharacterChanges()
    {
        var document = LunaSegmentedDocument.Create("Eins. Zwei.");

        Assert.False(document.TryApply([new LunaSegmentChange(2, "Drei.")], out _, out _));
        Assert.False(document.TryApply([new LunaSegmentChange(0, "A."), new LunaSegmentChange(0, "B.")], out _, out _));
        Assert.False(document.TryApply([new LunaSegmentChange(1, "B."), new LunaSegmentChange(0, "A.")], out _, out _));
        Assert.False(document.TryApply([new LunaSegmentChange(0, "Eins.")], out _, out _));
        Assert.False(document.TryApply([new LunaSegmentChange(0, "A\0.")], out _, out _));
    }
}
