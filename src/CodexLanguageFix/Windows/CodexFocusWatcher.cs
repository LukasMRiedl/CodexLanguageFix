using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace CodexLanguageFix.Windows;

public sealed class CodexFocusWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectFocus = 0x8005;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint WineventOutOfContext = 0x0000;

    private readonly Dispatcher _dispatcher;
    private readonly WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private int _notificationPending;
    private bool _disposed;

    public CodexFocusWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnWinEvent;
        AddHook(EventSystemForeground);
        AddHook(EventObjectFocus);
        AddHook(EventObjectLocationChange);
        AddHook(EventSystemMinimizeStart);
        AddHook(EventSystemMinimizeEnd);
    }

    public event EventHandler? Changed;

    private void AddHook(uint eventId)
    {
        var hook = SetWinEventHook(eventId, eventId, nint.Zero, _callback, 0, 0, WineventOutOfContext);
        if (hook != nint.Zero)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_disposed) return;
        if (eventType == EventObjectLocationChange && window != GetForegroundWindow())
            return;
        if (Interlocked.Exchange(ref _notificationPending, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _notificationPending, 0);
            if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var hook in _hooks)
        {
            UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private delegate void WinEventDelegate(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
