using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class LunaSpanEditDocumentTests
{
    [Fact]
    public void TryApply_AppliesOrderedEditsAndPreservesUnicodeAndCrLf()
    {
        var original = "🙂 Das ist korekt.\r\nEine zweite Zeile.";
        var firstStart = original.IndexOf("korekt", StringComparison.Ordinal);
        var secondStart = original.IndexOf("Zeile", StringComparison.Ordinal);

        Assert.True(LunaSpanEditDocument.TryApply(
            original,
            [
                new LunaSpanEdit(firstStart, "korekt".Length, "korrekt"),
                new LunaSpanEdit(secondStart, "Zeile".Length, "Satz")
            ],
            out var corrected,
            out var changeCount));

        Assert.Equal(2, changeCount);
        Assert.Equal("🙂 Das ist korrekt.\r\nEine zweite Satz.", corrected);
    }

    [Fact]
    public void TryApply_AllowsAdjacentEditsButRejectsOverlapAndDuplicateStarts()
    {
        const string original = "abcdef";

        Assert.True(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(0, 1, "A"), new LunaSpanEdit(1, 1, "B")],
            out var corrected,
            out var count));
        Assert.Equal("ABcdef", corrected);
        Assert.Equal(2, count);

        Assert.False(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(0, 3, "A"), new LunaSpanEdit(2, 1, "B")],
            out _,
            out _));
        Assert.False(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(2, 0, "X"), new LunaSpanEdit(2, 0, "Y")],
            out _,
            out _));
    }

    [Fact]
    public void TryApply_RejectsSurrogateAndCrLfSplits()
    {
        const string unicode = "A🙂B";
        Assert.False(LunaSpanEditDocument.TryApply(
            unicode,
            [new LunaSpanEdit(2, 1, "X")],
            out _,
            out _));

        const string crlf = "A\r\nB";
        Assert.False(LunaSpanEditDocument.TryApply(
            crlf,
            [new LunaSpanEdit(2, 0, "X")],
            out _,
            out _));
    }

    [Fact]
    public void TryApply_RejectsProtectedMarkersAndUnsafeText()
    {
        const string marker = "⟦CLF_PROTECTED_AABBCCDDEEFF0011_0001⟧";
        var original = "Vor " + marker + " nach.";
        var markerStart = original.IndexOf(marker, StringComparison.Ordinal);

        Assert.False(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(markerStart, marker.Length, "ersetzt")],
            out _,
            out _));
        Assert.False(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(0, 0, marker)],
            out _,
            out _));
        Assert.False(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(0, 1, "A\0")],
            out _,
            out _));
        Assert.False(LunaSpanEditDocument.TryApply(
            original,
            [new LunaSpanEdit(0, 1, "\uD800")],
            out _,
            out _));
    }

    [Fact]
    public void TryApply_EmptyEditsRoundTripExactly()
    {
        const string original = "🙂\r\nKorrekt.";

        Assert.True(LunaSpanEditDocument.TryApply(original, [], out var corrected, out var count));

        Assert.Equal(original, corrected);
        Assert.Equal(0, count);
    }
}
