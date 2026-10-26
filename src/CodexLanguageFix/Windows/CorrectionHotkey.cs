using System.Runtime.InteropServices;
using System.Windows.Interop;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Windows;

internal sealed class CorrectionHotkey : IDisposable
{
    private const int HotkeyId = 1;
    private const int ProbeHotkeyId = 2;
    private const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorInvalidParameter = 87;
    private readonly HwndSource _source;
    private CorrectionShortcut _shortcut = CorrectionShortcut.Default;
    private bool _registered;
    private bool _disposed;

    public CorrectionHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("CodexLanguageFix.Shortcut")
        {
            ParentWindow = new nint(-3), // HWND_MESSAGE
            WindowStyle = 0
        });
        _source.AddHook(OnMessage);
    }

    public event EventHandler? CorrectRequested;

    public CorrectionShortcut Shortcut => _shortcut;

    public bool SetShortcut(CorrectionShortcut shortcut)
    {
        if (_disposed || !shortcut.IsValid)
        {
            return false;
        }

        if (shortcut == _shortcut)
        {
            return true;
        }

        if (!_registered)
        {
            _shortcut = shortcut;
            return true;
        }

        var previousShortcut = _shortcut;
        if (!UnregisterHotKey(_source.Handle, HotkeyId))
        {
            return false;
        }

        _registered = false;
        _shortcut = shortcut;
        if (RegisterHotKey(_source.Handle, HotkeyId, _shortcut.Modifiers | NoRepeat, _shortcut.VirtualKey))
        {
            _registered = true;
            return true;
        }

        _shortcut = previousShortcut;
        _registered = RegisterHotKey(
            _source.Handle,
            HotkeyId,
            previousShortcut.Modifiers | NoRepeat,
            previousShortcut.VirtualKey);
        return false;
    }

    public bool TryProbe(CorrectionShortcut shortcut, out int error)
    {
        error = 0;
        if (_disposed)
        {
            error = ErrorInvalidHandle;
            return false;
        }

        if (!shortcut.IsValid)
        {
            error = ErrorInvalidParameter;
            return false;
        }

        HwndSource? probeSource = null;
        try
        {
            probeSource = new HwndSource(new HwndSourceParameters("CodexLanguageFix.ShortcutProbe")
            {
                ParentWindow = new nint(-3), // HWND_MESSAGE
                WindowStyle = 0
            });

            if (!RegisterHotKey(
                    probeSource.Handle,
                    ProbeHotkeyId,
                    shortcut.Modifiers | NoRepeat,
                    shortcut.VirtualKey))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            if (!UnregisterHotKey(probeSource.Handle, ProbeHotkeyId))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
        finally
        {
            probeSource?.Dispose();
        }
    }

    public bool SetEnabled(bool enabled)
    {
        if (_disposed) return false;
        if (enabled == _registered) return true;
        if (enabled)
        {
            _registered = RegisterHotKey(
                _source.Handle,
                HotkeyId,
                _shortcut.Modifiers | NoRepeat,
                _shortcut.VirtualKey);
            return _registered;
        }

        if (!UnregisterHotKey(_source.Handle, HotkeyId)) return false;
        _registered = false;
        return true;
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_disposed && _registered && message == WmHotkey && wParam == HotkeyId
            && ((long)lParam & 0xFFFF) == _shortcut.Modifiers
            && (((long)lParam >> 16) & 0xFFFF) == _shortcut.VirtualKey)
        {
            handled = true;
            CorrectRequested?.Invoke(this, EventArgs.Empty);
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetEnabled(false);
        _disposed = true;
        CorrectRequested = null;
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
