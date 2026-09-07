namespace CodexLanguageFix.Tests;

internal static class LunaLiveRuntime
{
    // Live checks use the installed app's existing catalog, but never modify its runtime directory.
    internal static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexLanguageFix-Live", Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(directory, "luna-runtime");
        Directory.CreateDirectory(runtime);
        var installedCatalog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexLanguageFix", "luna-runtime", "luna-model-catalog.json");
        if (File.Exists(installedCatalog))
            File.Copy(installedCatalog, Path.Combine(runtime, "luna-model-catalog.json"));
        return directory;
    }
}
