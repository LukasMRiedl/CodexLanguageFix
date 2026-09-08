using NativeInput = CodexLanguageFix.Windows.CodexComposerAccessor.UnicodeInput;

namespace CodexLanguageFix.Tests;

public sealed class NativeSelectionInputTests
{
    [Theory]
    [InlineData("x", 1)]
    [InlineData("🙂", 1)]
    [InlineData("a\u0308", 1)]
    [InlineData("abc", 3)]
    public void InsertionCaretMovesWithoutSelectingOrWriting(string text, int count)
    {
        var keys = NativeInput.CaretKeys(text);
        Assert.Equal(count * 2, keys.Length);
        Assert.All(keys, key => Assert.Equal((ushort)0x27, key.Key));
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(1u, keys[index * 2].Flags);
            Assert.Equal(3u, keys[index * 2 + 1].Flags);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("\ufffc")]
    public void InsertionCaretDoesNotCrossBreaksOrObjects(string text) => Assert.Empty(NativeInput.CaretKeys(text));

    [Theory]
    [InlineData("abc", 3)]
    [InlineData("🙂", 1)]
    [InlineData("a\u0308", 1)]
    [InlineData("👩\u200d💻", 1)]
    public void SelectionPlanMovesByWholeGraphemesWithBalancedKeys(string text, int count)
    {
        var keys = NativeInput.SelectionKeys(text);
        Assert.Equal(count * 2 + 2, keys.Length);
        Assert.Equal(((ushort)0x10, 0u), keys[0]);
        Assert.Equal(((ushort)0x10, 2u), keys[^1]);
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(((ushort)0x27, 1u), keys[index * 2 + 1]);
            Assert.Equal(((ushort)0x27, 3u), keys[index * 2 + 2]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\n")]
    [InlineData("a\r\n")]
    [InlineData("a\t")]
    [InlineData("a\ufffc")]
    [InlineData("a\u2028")]
    [InlineData("a\u2029")]
    public void SelectionPlanRejectsEmptyTextAndProtectedSeparators(string text) =>
        Assert.Empty(NativeInput.SelectionKeys(text));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnyHeldModifierRejectsInput(int held)
    {
        var states = new short[5];
        states[held] = short.MinValue;
        Assert.False(NativeInput.AreModifierStatesReleased(states[0], states[1], states[2], states[3], states[4]));
        Assert.True(NativeInput.AreModifierStatesReleased(1, 1, 1, 1, 1));
    }

    [Fact]
    public void EveryPartialSelectionBatchReleasesOnlyItsOutstandingKeys()
    {
        var keys = NativeInput.SelectionKeys("abc");
        for (uint sent = 1; sent < keys.Length; sent++)
        {
            var release = NativeInput.SelectionReleaseKeys(sent, keys.Length);
            Assert.Equal(((ushort)0x10, 2u), release[^1]);
            Assert.All(release, key => Assert.True((key.Flags & 2u) != 0));
            Assert.Equal(sent > 1 && sent % 2 == 0 ? 2 : 1, release.Length);
            if (release.Length == 2) Assert.Equal(((ushort)0x27, 3u), release[0]);
        }
        Assert.Empty(NativeInput.SelectionReleaseKeys(0, keys.Length));
        Assert.Empty(NativeInput.SelectionReleaseKeys((uint)keys.Length, keys.Length));
    }
}
