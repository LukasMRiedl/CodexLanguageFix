using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class BrowserBridgeTests
{
    [Fact]
    public async Task ManualPreemptsPreparationAndOnlyLatestPendingRequestRuns()
    {
        var inner = new ControlledProvider();
        using var provider = new PrioritizedCorrectionProvider(inner);
        var preparation = provider.PrepareAsync("preparation", default);
        var first = await inner.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var superseded = provider.CorrectAsync("superseded", default);
        var latest = provider.CorrectAsync("latest", default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => superseded);
        Assert.True(first.Cancellation.IsCancellationRequested);
        Assert.Equal(1, inner.Calls);
        first.Complete();
        var second = await inner.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("latest", second.Text);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.PrepareAsync("background", default));
        second.Complete();
        Assert.Equal("latest", (await latest).CorrectedText);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task StateRevisionChangeCancelsAndReaderHandlesCancelWhileProviderIsBusy()
    {
        var inner = new ControlledProvider();
        using var provider = new PrioritizedCorrectionProvider(inner);
        var pipeName = "CodexLanguageFix.Test." + Guid.NewGuid().ToString("N");
        using var bridge = new BrowserBridgeServer(provider, () => Task.FromResult(new BrowserState(true, true, 3, 190)),
            _ => { }, () => true, key => key, pipeName);
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000);
        var initial = new BrowserMessage(1, "prepare", "one", "doc", 0, "field", 1, "original");
        await Send(pipe, initial);
        var work = await inner.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Send(pipe, initial with { Type = "state", RequestId = "two", Revision = 2, Text = null });
        using var state = await Read(pipe);
        Assert.Equal("state", state.RootElement.GetProperty("type").GetString());
        Assert.True(work.Cancellation.IsCancellationRequested);
        await Send(pipe, initial with { Type = "cancel", RequestId = "three", Revision = 2, Text = null });
        using var cancelled = await Read(pipe);
        Assert.Equal("cancelled", cancelled.RootElement.GetProperty("type").GetString());
        work.Complete();
        pipe.Dispose();
        bridge.Dispose();
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PauseCancelsActiveCorrectionAndPushesState()
    {
        var inner = new ControlledProvider();
        using var provider = new PrioritizedCorrectionProvider(inner);
        var enabled = true;
        var pipeName = "CodexLanguageFix.Test." + Guid.NewGuid().ToString("N");
        using var bridge = new BrowserBridgeServer(provider, () => Task.FromResult(new BrowserState(enabled, true, 3, 190)),
            _ => { }, () => true, key => key, pipeName);
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000);
        await Send(pipe, new BrowserMessage(1, "correct", "one", "doc", 0, "field", 1, "original"));
        var work = await inner.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        enabled = false;
        var notification = bridge.SettingsChangedAsync();
        using var state = await Read(pipe);
        await notification.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(state.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(work.Cancellation.IsCancellationRequested);
        work.Complete();
        pipe.Dispose(); bridge.Dispose();
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Task Send(Stream stream, BrowserMessage message) => BrowserProtocol.WriteAsync(stream,
        JsonSerializer.SerializeToUtf8Bytes(message, BrowserProtocol.Json), default);
    private static async Task<JsonDocument> Read(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return JsonDocument.Parse((await BrowserProtocol.ReadAsync(stream, timeout.Token))!);
    }
    private sealed class ControlledProvider : ICorrectionProvider
    {
        public CorrectionProviderKind Kind => CorrectionProviderKind.Luna;
        internal readonly Channel<Work> Started = Channel.CreateUnbounded<Work>();
        internal int Calls;
        public async Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var work = new Work(text, cancellationToken);
            Started.Writer.TryWrite(work);
            // Deliberately ignore cancellation to prove the arbiter does not overlap provider invocations.
            await work.Done.Task;
            return new CorrectionProviderResult(text, 0, Kind, null, TimeSpan.Zero);
        }
        public Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed record Work(string Text, CancellationToken Cancellation)
    {
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete() => Done.TrySetResult();
    }
}
