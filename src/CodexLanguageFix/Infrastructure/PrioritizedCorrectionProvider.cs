using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

// One running provider invocation and one replaceable request, shared by every editor backend.
internal sealed class PrioritizedCorrectionProvider(ICorrectionProvider inner) : IPreparationCorrectionProvider, IDisposable
{
    private readonly object _gate = new();
    private Operation? _active;
    private Operation? _pending;
    private bool _pumping;
    private bool _disposed;
    public CorrectionProviderKind Kind => inner.Kind;
    public Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellation) => inner.TestAsync(cancellation);
    public Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellation) => Enqueue(text, false, cancellation);
    public Task<CorrectionProviderResult> PrepareAsync(string text, CancellationToken cancellation) => Enqueue(text, true, cancellation);
    private Task<CorrectionProviderResult> Enqueue(string text, bool preparation, CancellationToken cancellation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (cancellation.IsCancellationRequested) return Task.FromCanceled<CorrectionProviderResult>(cancellation);
            if (preparation && ((_active is { Preparation: false } && !_active.Cancellation.IsCancellationRequested) || _pending is { Preparation: false }))
                return Task.FromCanceled<CorrectionProviderResult>(new CancellationToken(true));
            _active?.Cancel();
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new Operation(text, preparation, cancellation);
            var result = _pending.Result.Task;
            if (!_pumping) { _pumping = true; _ = Task.Run(PumpAsync); }
            return result;
        }
    }
    private async Task PumpAsync()
    {
        while (true)
        {
            Operation operation;
            lock (_gate)
            {
                if (_pending is null) { _pumping = false; return; }
                operation = _pending; _pending = null; _active = operation;
            }
            try
            {
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                var result = await inner.CorrectAsync(operation.Text, operation.Cancellation.Token).ConfigureAwait(false);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                operation.Result.TrySetResult(result);
            }
            catch (OperationCanceledException) { operation.Result.TrySetCanceled(); }
            catch (Exception exception) { operation.Result.TrySetException(exception); }
            finally
            {
                lock (_gate) { _active = null; operation.Dispose(); }
            }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _active?.Cancel(); _pending?.Cancel(); _pending?.Dispose(); _pending = null;
        }
    }
    private sealed class Operation : IDisposable
    {
        internal readonly string Text;
        internal readonly bool Preparation;
        internal readonly CancellationTokenSource Cancellation;
        internal readonly TaskCompletionSource<CorrectionProviderResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        internal Operation(string text, bool preparation, CancellationToken cancellation)
        {
            Text = text; Preparation = preparation;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _registration = Cancellation.Token.Register(() => Result.TrySetCanceled());
        }
        internal void Cancel() => Cancellation.Cancel();
        public void Dispose() { _registration.Dispose(); Cancellation.Dispose(); }
    }
}
