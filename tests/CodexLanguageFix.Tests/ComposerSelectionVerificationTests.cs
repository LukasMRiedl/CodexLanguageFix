using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ComposerSelectionVerificationTests
{
    [Fact]
    public void TransientCacheMismatchesRequireAnExactConfirmation()
    {
        var checks = new Queue<ComposerSelectionCheck>([
            ComposerSelectionCheck.CountMismatch,
            ComposerSelectionCheck.TextMismatch,
            ComposerSelectionCheck.Exact]);

        Assert.Equal(ComposerSelectionCheck.Exact,
            ComposerSelectionVerification.WaitForExact(() => checks.Dequeue()));
        Assert.Empty(checks);
    }

    [Fact]
    public void PersistentMismatchNeverAuthorizesWriting()
    {
        var attempts = 0;
        var result = ComposerSelectionVerification.WaitForExact(() =>
        {
            attempts++;
            Assert.True(attempts <= 50, "Die Auswahlbestätigung muss zeitlich begrenzt bleiben.");
            return ComposerSelectionCheck.TextMismatch;
        });

        Assert.Equal(ComposerSelectionCheck.TextMismatch, result);
        Assert.True(attempts > 1);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void EditorOrSourceChangeStopsImmediately(
        bool editorChanged, bool afterTransientMismatch)
    {
        var terminalChange = editorChanged ? ComposerSelectionCheck.EditorChanged : ComposerSelectionCheck.SourceChanged;
        var attempts = 0;
        var result = ComposerSelectionVerification.WaitForExact(() =>
        {
            attempts++;
            if (afterTransientMismatch && attempts == 1) return ComposerSelectionCheck.TextMismatch;
            Assert.Equal(afterTransientMismatch ? 2 : 1, attempts);
            return terminalChange;
        });

        Assert.Equal(terminalChange, result);
        Assert.Equal(afterTransientMismatch ? 2 : 1, attempts);
    }
}
