using System.Diagnostics;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ComposerWriteVerificationTests
{
    [Fact]
    public void ExactTextSucceedsOnFirstRead()
    {
        var reads = 0;
        var result = ComposerWriteVerification.WaitForExact("Korrigiert 🙂\r\n", () =>
        {
            reads++;
            return "Korrigiert 🙂\r\n";
        }, () => true);

        Assert.Equal(ComposerWriteCheck.Exact, result);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void DelayedReadbackAfterOldDeadlineSucceedsWithoutAnotherWrite()
    {
        var elapsed = Stopwatch.StartNew();
        var staleReads = 0;
        var result = ComposerWriteVerification.WaitForExact("Korrigiert", () =>
        {
            if (elapsed.ElapsedMilliseconds >= 250) return "Korrigiert";
            staleReads++;
            return "Original";
        }, () => true);

        Assert.Equal(ComposerWriteCheck.Exact, result);
        Assert.True(staleReads > 0);
        Assert.True(elapsed.ElapsedMilliseconds >= 250);
    }

    [Fact]
    public void PersistentMismatchTimesOutWithoutAcceptingNormalizedText()
    {
        var elapsed = Stopwatch.StartNew();
        var reads = 0;
        var result = ComposerWriteVerification.WaitForExact("Äpfel\r\n", () =>
        {
            reads++;
            return "Äpfel\n";
        }, () => true);

        Assert.Equal(ComposerWriteCheck.TextMismatch, result);
        Assert.True(reads > 1);
        Assert.True(elapsed.ElapsedMilliseconds >= 1900);
    }

    [Fact]
    public void LostIdentityStopsBeforeReading()
    {
        var result = ComposerWriteVerification.WaitForExact("Korrigiert",
            () => throw new InvalidOperationException("Ein fremder Editor darf nicht gelesen werden."),
            () => false);

        Assert.Equal(ComposerWriteCheck.EditorChanged, result);
    }

    [Fact]
    public void LostIdentityDuringReadingDoesNotAcceptMatchingText()
    {
        var editorCurrent = true;
        var reads = 0;
        var result = ComposerWriteVerification.WaitForExact("Korrigiert", () =>
        {
            reads++;
            editorCurrent = false;
            return "Korrigiert";
        }, () => editorCurrent);

        Assert.Equal(ComposerWriteCheck.EditorChanged, result);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void LostFocusAfterStaleReadStopsWithoutFurtherReads()
    {
        var identityChecks = 0;
        var reads = 0;
        var result = ComposerWriteVerification.WaitForExact("Korrigiert", () =>
        {
            reads++;
            return "Original";
        }, () => ++identityChecks <= 2);

        Assert.Equal(ComposerWriteCheck.EditorChanged, result);
        Assert.Equal(1, reads);
        Assert.Equal(3, identityChecks);
    }
}
