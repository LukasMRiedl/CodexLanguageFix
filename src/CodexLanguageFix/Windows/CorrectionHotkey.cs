using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexLanguageFix.Windows;

internal sealed class CorrectionHotkey : IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint Modifiers = 0x0001 | 0x0002;
    private const uint NoRepeat = 0x4000;
    private const uint Comma = 0xBC;
    private readonly HwndSource _source;
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

    public bool SetEnabled(bool enabled)
    {
        if (_disposed) return false;
        if (enabled == _registered) return true;
        if (enabled)
        {
            _registered = RegisterHotKey(_source.Handle, HotkeyId, Modifiers | NoRepeat, Comma);
            return _registered;
        }

        if (!UnregisterHotKey(_source.Handle, HotkeyId)) return false;
        _registered = false;
        return true;
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_disposed && _registered && message == WmHotkey && wParam == HotkeyId
            && ((long)lParam & 0xFFFF) == Modifiers && (((long)lParam >> 16) & 0xFFFF) == Comma)
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
