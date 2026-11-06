using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexLanguageFix.Infrastructure;
internal static class BrowserForeground
{
    internal static bool IsChrome()
    {
        try
        {
            _ = GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
