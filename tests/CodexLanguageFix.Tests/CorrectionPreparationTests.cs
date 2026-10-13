using System.Windows;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class CorrectionPreparationTests
{
    [Fact]
    public async Task QuietPeriod_StartsOnceAndManualCallAdoptsTheSameTask()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        preparation.Observe(snapshot, true);
        time.Advance(1199);
        preparation.Observe(snapshot, true);
        Assert.Empty(provider.Calls);
        time.Advance(1);
        preparation.Observe(snapshot, true);
        preparation.Observe(snapshot, true);
        Assert.Single(provider.Calls);
        var pending = preparation.BeginManual(snapshot);
        Assert.NotNull(pending);
        Assert.Same(pending, preparation.BeginManual(snapshot));
        provider.Complete(0);
        Assert.True(await pending!);
        Assert.False(preparation.IsReady(snapshot));
        time.Advance(5000);
        preparation.Observe(snapshot, true);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task CompletedPreparationIsReadyOnlyForItsExactEditorAndText()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        Start(preparation, time, snapshot);
        provider.Complete(0);
        await provider.Calls[0].Completion.Task;
        Assert.True(preparation.IsReady(snapshot));
        Assert.False(preparation.IsReady(snapshot with { Text = "anderer Text" }));
        Assert.False(preparation.IsReady(snapshot with { RuntimeId = [2] }));
    }

    [Fact]
    public async Task ContinuedTypingCancelsAndCoalescesUntilOldOperationFinishes()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var original = Snapshot();
        Start(preparation, time, original);
        var updated = original with { Text = original.Text + " weiter" };
        preparation.Observe(updated, true);
        Assert.True(provider.Calls[0].Token.IsCancellationRequested);
        time.Advance(1200);
        preparation.Observe(updated, true);
        Assert.Single(provider.Calls);
        provider.Complete(0);
        await Task.Yield();
        Assert.False(preparation.IsReady(original));
        preparation.Observe(updated, true);
        Assert.Equal(2, provider.Calls.Count);
        Assert.Equal(updated.Text, provider.Calls[1].Text);
        provider.Complete(1);
    }

    [Fact]
    public void RapidChangesRestartQuietPeriod()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        preparation.Observe(snapshot, true);
        for (var index = 0; index < 10; index++)
        {
            time.Advance(1000);
            snapshot = snapshot with { Text = snapshot.Text + "a" };
            preparation.Observe(snapshot, true);
        }
        Assert.Empty(provider.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FocusLossOrMissingEditorInvalidatesLateResults(bool missingEditor)
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        Start(preparation, time, snapshot);
        preparation.Observe(missingEditor ? null : snapshot, !missingEditor && false);
        Assert.True(provider.Calls[0].Token.IsCancellationRequested);
        provider.Complete(0);
        await Task.Yield();
        Assert.False(preparation.IsReady(snapshot));
        Assert.Null(preparation.BeginManual(snapshot));
    }

    [Fact]
    public async Task FailureDoesNotRetryAutomaticallyButAllowsManualRequest()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        Start(preparation, time, snapshot);
        provider.Calls[0].Completion.SetException(new InvalidOperationException());
        await Task.Yield();
        preparation.Observe(snapshot, true);
        Assert.Single(provider.Calls);
        Assert.False(await preparation.BeginManual(snapshot)!);
        Assert.False(preparation.IsReady(snapshot));
    }

    [Fact]
    public void OwnWriteAndUndoRemainSuppressedUntilEdited()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        preparation.Suppress(snapshot);
        time.Advance(10000);
        preparation.Observe(snapshot, true);
        Assert.Empty(provider.Calls);
        var edited = snapshot with { Text = snapshot.Text + "!" };
        preparation.Observe(edited, true);
        time.Advance(1200);
        preparation.Observe(edited, true);
        Assert.Single(provider.Calls);
        provider.Complete(0);
    }

    [Fact]
    public void BusyManualCorrectionDoesNotStartBackgroundRequests()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        preparation.Observe(snapshot, true, false);
        time.Advance(5000);
        preparation.Observe(snapshot, true, false);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void StalePreparationDoesNotDelayNewManualRequest()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        var snapshot = Snapshot();
        Start(preparation, time, snapshot);
        Assert.Null(preparation.BeginManual(snapshot with { Text = "neuer Entwurf" }));
        Assert.True(provider.Calls[0].Token.IsCancellationRequested);
        Assert.False(provider.Calls[0].Completion.Task.IsCompleted);
        provider.Complete(0);
    }

    [Fact]
    public void InvalidTextAndDisabledPreparationNeverCallProvider()
    {
        var time = new ManualTime();
        var provider = new Provider();
        using var preparation = new CorrectionPreparation(provider, time);
        foreach (var text in new[] { "", " ", new string('x', 20001) })
            Start(preparation, time, Snapshot() with { Text = text });
        preparation.Observe(Snapshot(), false);
        time.Advance(5000);
        preparation.Observe(Snapshot(), false);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void LanguageToolCannotBeUsedForAutomaticPreparation() =>
        Assert.Throws<ArgumentException>(() => new CorrectionPreparation(new Provider(CorrectionProviderKind.LanguageTool)));

    [Fact]
    public void DisposeCancelsRunningOperationAndPreventsNewRequests()
    {
        var time = new ManualTime();
        var provider = new Provider();
        var preparation = new CorrectionPreparation(provider, time);
        Start(preparation, time, Snapshot());
        preparation.Dispose();
        Assert.True(provider.Calls[0].Token.IsCancellationRequested);
        Start(preparation, time, Snapshot() with { Text = "anders" });
        Assert.Single(provider.Calls);
        provider.Complete(0);
    }

    private static ComposerSnapshot Snapshot() => new(new object(), [1], "Das ist ein fehler.", new Rect(0, 0, 100, 40), 1);

    private static void Start(CorrectionPreparation preparation, ManualTime time, ComposerSnapshot snapshot)
    {
        preparation.Observe(snapshot, true);
        time.Advance(1200);
        preparation.Observe(snapshot, true);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        public void Advance(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class Provider(CorrectionProviderKind kind = CorrectionProviderKind.Luna) : ICorrectionProvider
    {
        public CorrectionProviderKind Kind => kind;
        public List<Call> Calls { get; } = [];
        public Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
        {
            var call = new Call(text, cancellationToken);
            Calls.Add(call);
            return call.Completion.Task;
        }
        public Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Complete(int index) => Calls[index].Completion.SetResult(new CorrectionProviderResult(
            Calls[index].Text, 0, Kind, null, TimeSpan.Zero));
    }

    private sealed record Call(string Text, CancellationToken Token)
    {
        public TaskCompletionSource<CorrectionProviderResult> Completion { get; } = new();
    }
}
