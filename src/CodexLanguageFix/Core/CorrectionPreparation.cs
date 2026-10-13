using CodexLanguageFix.Contracts;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Core;

internal sealed class CorrectionPreparation : IDisposable
{
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(1200);
    private readonly ICorrectionProvider _provider;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ComposerSnapshot? _observed;
    private long _changedAt;
    private bool _attempted;
    private bool _handled;
    private bool _disposed;
    private Operation? _operation;

    public CorrectionPreparation(ICorrectionProvider provider, TimeProvider? time = null)
    {
        if (provider.Kind != CorrectionProviderKind.Luna)
            throw new ArgumentException("Nur Luna darf Korrekturen automatisch vorbereiten.", nameof(provider));
        _provider = provider;
        _time = time ?? TimeProvider.System;
    }

    public event Action<string, int, TimeSpan>? Diagnostic;

    public void Observe(ComposerSnapshot? snapshot, bool eligible, bool allowStart = true)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!eligible || snapshot is null || string.IsNullOrWhiteSpace(snapshot.Text)
                || snapshot.Text.Length > LunaCorrectionProvider.MaximumPromptLength)
            {
                ClearCore();
                return;
            }
            if (_observed is null || !CorrectionCoordinator.IsUnchangedEditor(_observed, snapshot))
            {
                CancelCore();
                _observed = snapshot;
                _changedAt = _time.GetTimestamp();
                _attempted = false;
                _handled = false;
            }
            if (!allowStart || _attempted || _handled || _operation is { Finished: false }
                || _time.GetElapsedTime(_changedAt) < QuietPeriod) return;

            _attempted = true;
            var operation = new Operation(snapshot);
            _operation = operation;
            operation.Task = RunAsync(operation);
        }
    }

    public bool IsReady(ComposerSnapshot snapshot)
    {
        lock (_gate)
            return !_disposed && !_handled && _operation is { Finished: true, Succeeded: true } operation
                && CorrectionCoordinator.IsUnchangedEditor(operation.Snapshot, snapshot)
                && _observed is not null && CorrectionCoordinator.IsUnchangedEditor(_observed, snapshot);
    }

    public Task<bool>? BeginManual(ComposerSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            var matching = _operation is { } operation
                && !operation.Cancelled && CorrectionCoordinator.IsUnchangedEditor(operation.Snapshot, snapshot)
                    ? operation.Task : null;
            if (matching is null) CancelCore();
            _observed = snapshot;
            _handled = true;
            _attempted = true;
            return matching;
        }
    }

    public void Suppress(ComposerSnapshot snapshot)
    {
        lock (_gate)
        {
            CancelCore();
            _observed = snapshot;
            _handled = true;
            _attempted = true;
        }
    }

    public void Clear()
    {
        lock (_gate) ClearCore();
    }

    private void ClearCore()
    {
        CancelCore();
        _observed = null;
        _handled = false;
        _attempted = false;
    }

    private void CancelCore()
    {
        if (_operation is { Finished: false, Cancelled: false } operation)
        {
            operation.Cancelled = true;
            operation.Cancellation.Cancel();
        }
    }

    private async Task<bool> RunAsync(Operation operation)
    {
        var startedAt = _time.GetTimestamp();
        Diagnostic?.Invoke("preparation_started", operation.Snapshot.Text.Length, TimeSpan.Zero);
        try
        {
            await _provider.CorrectAsync(operation.Snapshot.Text, operation.Cancellation.Token);
            lock (_gate)
            {
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                operation.Succeeded = true;
            }
            Diagnostic?.Invoke("preparation_completed", operation.Snapshot.Text.Length, _time.GetElapsedTime(startedAt));
            return true;
        }
        catch (OperationCanceledException)
        {
            Diagnostic?.Invoke("preparation_cancelled", operation.Snapshot.Text.Length, _time.GetElapsedTime(startedAt));
            return false;
        }
        catch (Exception)
        {
            Diagnostic?.Invoke("preparation_failed", operation.Snapshot.Text.Length, _time.GetElapsedTime(startedAt));
            return false;
        }
        finally
        {
            lock (_gate)
            {
                operation.Finished = true;
                operation.Cancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ClearCore();
        }
    }

    private sealed class Operation(ComposerSnapshot snapshot)
    {
        public ComposerSnapshot Snapshot { get; } = snapshot;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task<bool> Task { get; set; } = null!;
        public bool Finished { get; set; }
        public bool Cancelled { get; set; }
        public bool Succeeded { get; set; }
    }
}
