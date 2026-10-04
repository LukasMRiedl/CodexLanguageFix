using System.Runtime.InteropServices;

namespace CodexLanguageFix.Tests;

// UIA and Win32 must use physical coordinates on mixed-DPI desktops.
internal sealed class LiveDpiScope : IDisposable
{
    private readonly nint _previous = SetThreadDpiAwarenessContext(new nint(-4));

    public LiveDpiScope()
    {
        if (_previous == nint.Zero) throw new InvalidOperationException("PerMonitorV2 konnte nicht aktiviert werden.");
    }

    public void Dispose() => SetThreadDpiAwarenessContext(_previous);

    internal static bool IsPerMonitorV2(nint window) =>
        AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(window), new nint(-4));

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}
