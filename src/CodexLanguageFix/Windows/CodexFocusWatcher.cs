using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace CodexLanguageFix.Windows;

public sealed class CodexFocusWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectReorder = 0x8004;
    private const uint EventObjectFocus = 0x8005;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint WineventOutOfContext = 0x0000;
    private const uint GaRoot = 2;
    private const int ObjIdCaret = -8;
    private const int ObjIdCursor = -9;
    private const uint EventObjectContentScrolled = 0x8015;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _notificationTimer;
    private readonly object _gate = new();
    private readonly WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private DispatcherOperation? _startNotification;
    private bool _notificationPending;
    private bool _focusChangePending;
    private bool _disposed;

    public CodexFocusWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _dispatcher.VerifyAccess();
        _callback = OnWinEvent;
        _notificationTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _notificationTimer.Tick += OnNotificationTick;
        AddHook(EventSystemForeground);
        AddHook(EventObjectDestroy);
        AddHook(EventObjectShow);
        AddHook(EventObjectHide);
        AddHook(EventObjectReorder);
        AddHook(EventObjectFocus);
        AddHook(EventObjectLocationChange);
        AddHook(EventObjectContentScrolled);
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
        // Caret blinking/movement is not an editor move or keyboard-focus transition.
        if (objectId is ObjIdCaret or ObjIdCursor) return;
        if (eventType != EventSystemForeground && window != nint.Zero)
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId) return;

            var eventRoot = GetAncestor(window, GaRoot);
            var foregroundRoot = GetAncestor(GetForegroundWindow(), GaRoot);
            if (eventRoot != nint.Zero && foregroundRoot != nint.Zero && eventRoot != foregroundRoot)
                return;
        }

        lock (_gate)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            _focusChangePending |= IsFocusTransition(eventType);
            if (_notificationPending) return;
            _notificationPending = true;
            _startNotification = _dispatcher.BeginInvoke(DispatcherPriority.Background, StartNotificationTimer);
        }
    }

    private void StartNotificationTimer()
    {
        lock (_gate)
        {
            _startNotification = null;
            if (!_disposed && _notificationPending) _notificationTimer.Start();
        }
    }

    internal static bool IsFocusTransition(uint eventType) => eventType is EventSystemForeground
        or EventObjectFocus or EventSystemMinimizeStart or EventSystemMinimizeEnd;

    private void OnNotificationTick(object? sender, EventArgs args)
    {
        ComposerChangeEventArgs change;
        lock (_gate)
        {
            _notificationTimer.Stop();
            if (_disposed || !_notificationPending) return;
            _notificationPending = false;
            change = new ComposerChangeEventArgs(_focusChangePending);
            _focusChangePending = false;
        }
        Changed?.Invoke(this, change);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _notificationPending = false;
            _startNotification?.Abort();
            _startNotification = null;
        }

        if (_dispatcher.CheckAccess()) DisposeHooks();
        else _dispatcher.Invoke(DisposeHooks);
    }

    private void DisposeHooks()
    {
        _notificationTimer.Stop();
        _notificationTimer.Tick -= OnNotificationTick;
        foreach (var hook in _hooks)
        {
            UnhookWinEvent(hook);
        }

        _hooks.Clear();
        Changed = null;
    }

    private delegate void WinEventDelegate(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

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

internal sealed class ComposerChangeEventArgs(bool focusChanged) : EventArgs
{
    internal bool FocusChanged { get; } = focusChanged;
}
