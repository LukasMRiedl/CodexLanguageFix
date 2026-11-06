using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class ComposerWorkerTests
{
    [Fact]
    public async Task UsesOneMtaThreadAndPropagatesFailuresWithoutLosingWorker()
    {
        using var worker = new ComposerWorker();
        var first = await worker.RunAsync(() => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        Assert.Equal(ApartmentState.MTA, first.Item2);
        await Assert.ThrowsAsync<ArgumentException>(() => worker.RunAsync<int>(() => throw new ArgumentException()));
        var next = await worker.RunAsync(() => Environment.CurrentManagedThreadId);
        Assert.Equal(first.Item1, next);
    }

    [Fact]
    public async Task QueueIsBoundedAndDisposalRejectsFurtherWork()
    {
        using var worker = new ComposerWorker();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var busy = worker.RunAsync(() => { entered.Set(); release.Wait(); return 1; });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        Task<int>[] queued = [];
        try
        {
            queued = Enumerable.Range(0, 4).Select(i => worker.RunAsync(() => i)).ToArray();
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(() => 5));
            worker.Dispose();
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(() => 6));
        }
        finally { release.Set(); }
        await busy;
        Assert.Equal(new[] { 0, 1, 2, 3 }, await Task.WhenAll(queued));
    }
}
