using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Infrastructure;

internal sealed class BrowserBridgeServer : IDisposable
{
    private readonly IPreparationCorrectionProvider _provider;
    private readonly Func<Task<BrowserState>> _state;
    private readonly Action<string> _status;
    private readonly Func<bool> _foreground;
    private readonly Func<string, string> _message;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Session? _session;
    private int _disposeRequested;
    private readonly Task _listener;
    internal Task Completion => _listener;

    internal BrowserBridgeServer(IPreparationCorrectionProvider provider, Func<Task<BrowserState>> state,
        Action<string> status, Func<bool> foreground, Func<string, string> message, string? pipeName = null)
    {
        _provider = provider; _state = state; _status = status; _foreground = foreground; _message = message;
        _pipeName = pipeName ?? BrowserProtocol.PipeName;
        _listener = ListenAsync();
    }
    internal async Task SettingsChangedAsync()
    {
        try
        {
            var state = await _state();
            Session? session;
            lock (_gate) session = _session;
            if (session is null) return;
            BrowserMessage? identity;
            lock (session.Gate)
            {
                if (!state.Enabled || (!state.Prepare && !session.Manual)) session.Cancel();
                identity = session.Identity;
            }
            if (identity is not null) await session.SendAsync(Response(identity, "state", state), _lifetime.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    // Called by the existing foreground watcher; no second polling loop is introduced.
    internal void ForegroundChanged()
    {
        if (_foreground()) return;
        Session? session;
        lock (_gate) session = _session;
        session?.Dispose();
    }
    private async Task ListenAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(_lifetime.Token);
                    using var session = new Session(pipe);
                    lock (_gate) _session = session;
                    try
                    {
                        while (await BrowserProtocol.ReadAsync(pipe, _lifetime.Token) is { } bytes)
                        {
                            var m = JsonSerializer.Deserialize<BrowserMessage>(bytes, BrowserProtocol.Json);
                            if (m is null || !BrowserProtocol.IsValid(m) || !_foreground()) break;
                            var state = await _state();
                            lock (session.Gate)
                            {
                                if (session.Identity is { } previous && !SameTarget(previous, m)) session.Cancel();
                                session.Identity = m;
                            }
                            if (m.Type == "state") { await session.SendAsync(Response(m, "state", state), _lifetime.Token); continue; }
                            if (m.Type == "cancel")
                            {
                                lock (session.Gate) session.Cancel();
                                await session.SendAsync(Response(m, "cancelled"), _lifetime.Token); continue;
                            }
                            if (!state.Enabled || (m.Type == "prepare" && !state.Prepare))
                            { await session.SendAsync(Response(m, "error", message: _message("unavailable")), _lifetime.Token); continue; }
                            lock (session.Gate)
                            {
                                if (m.Type == "prepare" && session.Manual && !session.Work.IsCompleted) continue;
                                session.Cancel();
                                session.Operation?.Dispose();
                                session.Operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                                session.Manual = m.Type == "correct";
                                var generation = session.Generation;
                                session.Work = CorrectAsync(session, m, generation, session.Operation.Token);
                            }
                        }
                    }
                    finally { lock (_gate) _session = null; }
                }
                catch (Exception e) when (e is IOException or JsonException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
        finally { }
    }
    internal static bool SameTarget(BrowserMessage a, BrowserMessage b) => a.DocumentId == b.DocumentId
        && a.FrameId == b.FrameId && a.FieldId == b.FieldId && a.Revision == b.Revision;
    private async Task CorrectAsync(Session session, BrowserMessage request, long generation, CancellationToken cancellation)
    {
        try
        {
            var result = await (request.Type == "prepare"
                ? _provider.PrepareAsync(request.Text!, cancellation)
                : _provider.CorrectAsync(request.Text!, cancellation));
            lock (session.Gate)
            {
                if (generation != session.Generation || session.Disposed || !_foreground()) return;
                cancellation.ThrowIfCancellationRequested();
            }
            await session.SendAsync(Response(request, "result", text: result.CorrectedText, changeCount: result.ChangeCount), cancellation);
            if (request.Type == "correct") _status(_message("ready"));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (cancellation.IsCancellationRequested) return;
            var message = _message("error");
            _status(message);
            try { await session.SendAsync(Response(request, "error", message: message), cancellation); }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private static object Response(BrowserMessage m, string type, BrowserState? state = null, string? text = null, int? changeCount = null, string? message = null) => new
    {
        version = 1, type, m.RequestId, m.DocumentId, m.FrameId, m.FieldId, m.Revision,
        enabled = state?.Enabled, prepare = state?.Prepare,
        shortcutModifiers = state?.ShortcutModifiers, shortcutVirtualKey = state?.ShortcutVirtualKey,
        text, changeCount, message
    };
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
        _lifetime.Cancel();
        Session? session;
        lock (_gate) session = _session;
        session?.Dispose();
        _ = _listener.ContinueWith(_ => _lifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private sealed class Session(NamedPipeServerStream pipe) : IDisposable
    {
        internal readonly object Gate = new();
        private readonly SemaphoreSlim _writes = new(1, 1);
        internal BrowserMessage? Identity;
        internal CancellationTokenSource? Operation;
        internal long Generation;
        internal bool Manual;
        internal bool Disposed;
        internal Task Work = Task.CompletedTask;
        internal void Cancel() { Generation++; Operation?.Cancel(); }
        internal async Task SendAsync(object value, CancellationToken cancellation)
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(value, BrowserProtocol.Json);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var acquired = false;
            try
            {
                await _writes.WaitAsync(timeout.Token);
                acquired = true;
                await BrowserProtocol.WriteAsync(pipe, data, timeout.Token);
            }
            catch { Dispose(); throw; }
            finally { if (acquired) _writes.Release(); }
        }
        public void Dispose()
        {
            CancellationTokenSource? operation;
            lock (Gate)
            {
                if (Disposed) return;
                Disposed = true; Generation++; operation = Operation; Operation = null;
            }
            // Pipe cancellation may run reader continuations: never hold a session/server lock here.
            operation?.Cancel();
            operation?.Dispose();
            pipe.Dispose();
            // An in-flight send may still release the write semaphore.
        }
    }
}
