namespace CodexLanguageFix.Tests;

internal static class LunaLiveRuntime
{
    // Live checks rely on the installed official Codex runtime and use an isolated app data directory.
    internal static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexLanguageFix-Live", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
