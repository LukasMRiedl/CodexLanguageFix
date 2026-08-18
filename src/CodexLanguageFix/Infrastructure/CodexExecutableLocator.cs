using System.IO;

namespace CodexLanguageFix.Infrastructure;

internal static class CodexExecutableLocator
{
    public static string? Find()
    {
        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.Combine(directory, "codex.exe"));
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "codex.exe"));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                var info = new FileInfo(candidate);
                var resolved = info.ResolveLinkTarget(true);
                return resolved?.FullName ?? info.FullName;
            }
            catch (IOException)
            {
                // Den nächsten offiziellen Installationspfad prüfen.
            }
            catch (UnauthorizedAccessException)
            {
                // Den nächsten offiziellen Installationspfad prüfen.
            }
        }

        return null;
    }
}
