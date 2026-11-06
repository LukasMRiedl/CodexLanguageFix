using System.Collections.Concurrent;

namespace CodexLanguageFix.Windows;

// All UIA objects stay on one apartment for their entire lifetime.
internal sealed class ComposerWorker : IDisposable
{
    private readonly BlockingCollection<Action> _work = new(4);
    private readonly Thread _thread;
    private int _disposed;
    public ComposerWorker()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _work.GetConsumingEnumerable()) action();
            _work.Dispose();
        }) { IsBackground = true, Name = "Text field automation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !_work.TryAdd(() =>
            {
                try { completion.TrySetResult(action()); }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new InvalidOperationException("Automation worker unavailable."));
        }
        catch (InvalidOperationException error) { completion.TrySetException(error); }
        return completion.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _work.CompleteAdding();
    }
}
