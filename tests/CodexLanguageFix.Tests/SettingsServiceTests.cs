using System.Text.Json;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void ExistingSettingsWithoutProviderKeepLanguageToolDefault()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"enabled\":true,\"language\":\"de\"}");

            var settings = new SettingsService(directory).Load();

            Assert.Equal(CorrectionProviderKind.LanguageTool, settings.CorrectionProvider);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void InvalidProviderFallsBackToLanguageTool()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"correctionProvider\":99}");

            var settings = new SettingsService(directory).Load();

            Assert.Equal(CorrectionProviderKind.LanguageTool, settings.CorrectionProvider);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexLanguageFix.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
