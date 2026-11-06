using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Windows;

// Native UI Automation failures must not terminate the tray, browser bridge or correction provider.
internal sealed class IsolatedComposerAccessor : IComposerAccessor, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<Process> _startWorker;
    private readonly TimeSpan _timeout;
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _worker;
    private Guid _session;
    private bool _disposed;
    private int _disposeRequested;
    private string? _nativePhase;
    private long _restartAfter;
    private long _diagnosticGeneration;

    internal IsolatedComposerAccessor() : this(StartWorker, TimeSpan.FromSeconds(10)) { }
    internal IsolatedComposerAccessor(Func<Process> startWorker, TimeSpan timeout)
    {
        _startWorker = startWorker;
        _timeout = timeout;
    }

    public string LastCaptureStatus { get; private set; } = "not_attempted";
    public string LastWriteStatus { get; private set; } = "not_attempted";
    public ComposerHost? LastCaptureHost { get; private set; }
    public string? LastFieldCategory { get; private set; }
    public bool LastCaptureWasKeyboardFocused { get; private set; }
    public ComposerWriteResult? LastWriteResult { get; private set; }
    internal long CaptureCount { get; private set; }
    internal long LayoutCaptureCount { get; private set; }

    public ComposerSnapshot? TryCaptureFocusedComposer() => Capture(ComposerOperation.Capture);
    public ComposerSnapshot? TryCaptureFocusedComposerWithoutLayout(bool requireKeyboardFocus = false) =>
        Capture(ComposerOperation.CaptureWithoutLayout, requireKeyboardFocus);
    public ComposerSnapshot? TryPollFocusedComposer() => Capture(ComposerOperation.Poll);

    public void InvalidateLayout()
    {
        lock (_gate)
        {
            // Do not start a worker just to invalidate a cache that does not exist.
            if (_worker is not null) _ = Exchange(new(1, ComposerOperation.Invalidate), allowStart: false);
        }
    }

    private ComposerSnapshot? Capture(ComposerOperation operation, bool requireKeyboardFocus = false)
    {
        lock (_gate)
        {
            var response = Exchange(new(1, operation, RequireKeyboardFocus: requireKeyboardFocus), allowStart: true);
            return response?.Snapshot?.ToSnapshot(new WorkerElement(_session, response.Snapshot.Token));
        }
    }

    public ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!CurrentToken(snapshot, out var token)) return null;
            var response = Exchange(new(1, ComposerOperation.Refresh, token), allowStart: false);
            return response?.Snapshot?.ToSnapshot(new WorkerElement(_session, response.Snapshot.Token));
        }
    }

    public bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement)
    {
        lock (_gate)
        {
            LastWriteResult = null;
            if (!CurrentToken(snapshot, out var token))
            {
                LastWriteStatus = "worker_snapshot_expired";
                return false;
            }
            var response = Exchange(new(1, ComposerOperation.Replace, token,
                ExpectedText: expectedText, Replacement: replacement), allowStart: false);
            if (response is null)
            {
                // The process may have died after a partial native write. Never replay the request.
                LastWriteResult = new(ComposerWriteState.PartiallyApplied, expectedText, null, 0, LastWriteStatus);
                return false;
            }
            LastWriteResult = response.WriteResult;
            return response.Replaced;
        }
    }

    private bool CurrentToken(ComposerSnapshot snapshot, out long token)
    {
        token = 0;
        if (_disposed || _worker is null || snapshot.NativeElement is not WorkerElement element
            || element.Session != _session) return false;
        token = element.Token;
        return true;
    }

    private ComposerProcessResponse? Exchange(ComposerProcessRequest request, bool allowStart)
    {
        if (_disposed) return null;
        try
        {
            if (_worker is null)
            {
                if (!allowStart) return null;
                if (Stopwatch.GetTimestamp() < _restartAfter) return null;
                _worker = _startWorker();
                _session = Guid.NewGuid();
                _nativePhase = null;
                _ = DrainDiagnostics(_worker.StandardError, Interlocked.Increment(ref _diagnosticGeneration));
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(_timeout);
            Volatile.Write(ref _nativePhase, null);
            BrowserProtocol.WriteAsync(_worker.StandardInput.BaseStream,
                JsonSerializer.SerializeToUtf8Bytes(request, BrowserProtocol.Json), timeout.Token)
                .WaitAsync(timeout.Token).GetAwaiter().GetResult();
            var body = BrowserProtocol.ReadAsync(_worker.StandardOutput.BaseStream, timeout.Token)
                .WaitAsync(timeout.Token).GetAwaiter().GetResult()
                ?? throw new EndOfStreamException();
            var response = JsonSerializer.Deserialize<ComposerProcessResponse>(body, BrowserProtocol.Json);
            if (response is null || response.Version != 1 || response.Failed)
                throw new InvalidDataException("Automation worker failed.");
            LastCaptureStatus = response.CaptureStatus;
            LastWriteStatus = response.WriteStatus;
            LastCaptureHost = response.Host;
            LastFieldCategory = response.FieldCategory;
            LastCaptureWasKeyboardFocused = response.KeyboardFocused;
            CaptureCount = response.CaptureCount;
            LayoutCaptureCount = response.LayoutCaptureCount;
            return response;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or Win32Exception
            or JsonException or OperationCanceledException or UnauthorizedAccessException)
        {
            var phase = Volatile.Read(ref _nativePhase);
            LastCaptureStatus = "worker_unavailable";
            LastWriteStatus = phase is null ? "worker_unavailable" : "worker_unavailable_" + phase;
            LastCaptureWasKeyboardFocused = false;
            _restartAfter = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
            StopWorker();
            return null;
        }
    }

    private async Task DrainDiagnostics(StreamReader reader, long generation)
    {
        // Only short technical phase names survive; native crash output is discarded.
        var buffer = new char[256];
        var line = new StringBuilder(160);
        var overlong = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
            {
                for (var index = 0; index < count; index++)
                {
                    var character = buffer[index];
                    if (character == '\n')
                    {
                        var value = line.ToString().TrimEnd('\r');
                        if (generation == Volatile.Read(ref _diagnosticGeneration)
                            && !overlong && (value is "uia-phase:selection_document" or "uia-phase:selection_prefix"
                                or "uia-phase:selection_through" or "uia-phase:selection_selected" or "uia-phase:selection_suffix"))
                            Volatile.Write(ref _nativePhase, value[10..]);
                        line.Clear();
                        overlong = false;
                    }
                    else if (line.Length < 160) line.Append(character);
                    else overlong = true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }

    private static Process StartWorker()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--composer-worker");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(start) ?? throw new InvalidOperationException("Automation worker unavailable.");
    }

    private void StopWorker()
    {
        var worker = _worker;
        _worker = null;
        _session = Guid.Empty;
        if (worker is null) return;
        try { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
        worker.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
        _lifetime.Cancel();
        lock (_gate)
        {
            _disposed = true;
            StopWorker();
            _lifetime.Dispose();
        }
    }

    private sealed record WorkerElement(Guid Session, long Token);
}
