using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CodexLanguageFix.Contracts;
using Microsoft.Win32.SafeHandles;

namespace CodexLanguageFix.Windows;

internal static class ComposerHostProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaximumPathCharacters = 32768;

    internal static ComposerHost? Identify(int processId)
    {
        if (processId <= 0) return null;

        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid) return null;

        var path = new StringBuilder(512);
        var length = (uint)path.Capacity;
        if (!QueryFullProcessImageNameW(process, 0, path, ref length))
        {
            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer) return null;
            path = new StringBuilder(MaximumPathCharacters);
            length = (uint)path.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, path, ref length)) return null;
        }

        return length > 0 && length < path.Capacity ? IdentifyPath(path.ToString()) : null;
    }

    internal static ComposerHost? IdentifyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var fileName = Path.GetFileName(path);
        if (string.Equals(fileName, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase)
            && (path.Contains(@"\WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\OpenAI\Codex\", StringComparison.OrdinalIgnoreCase)))
        {
            return ComposerHost.Codex;
        }

        if (string.Equals(fileName, "Antigravity.exe", StringComparison.OrdinalIgnoreCase)
            && path.Contains(@"\Programs\antigravity\", StringComparison.OrdinalIgnoreCase))
        {
            return ComposerHost.Antigravity;
        }

        if (string.Equals(fileName, "Hermes.exe", StringComparison.OrdinalIgnoreCase)
            && IsKnownHermesPath(path))
        {
            return ComposerHost.Hermes;
        }

        return null;
    }

    private static bool IsKnownHermesPath(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return MatchesKnownRoot(Environment.SpecialFolder.LocalApplicationData,
                   @"Programs\Hermes\Hermes.exe", fullPath)
            || MatchesKnownRoot(Environment.SpecialFolder.LocalApplicationData,
                   @"hermes\hermes-agent\apps\desktop\release\win-unpacked\Hermes.exe", fullPath)
            || MatchesKnownRoot(Environment.SpecialFolder.ProgramFiles,
                   @"Hermes\Hermes.exe", fullPath)
            || MatchesKnownRoot(Environment.SpecialFolder.ProgramFilesX86,
                   @"Hermes\Hermes.exe", fullPath);
    }

    private static bool MatchesKnownRoot(Environment.SpecialFolder folder, string relativePath, string fullPath)
    {
        var root = Environment.GetFolderPath(folder);
        return !string.IsNullOrWhiteSpace(root)
            && string.Equals(fullPath, Path.Combine(root, relativePath), StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process,
        uint flags, StringBuilder executableName, ref uint size);
}
