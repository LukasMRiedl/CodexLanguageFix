using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void ExistingSettingsWithoutProviderOrShortcutKeepTheirDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"enabled\":true,\"language\":\"de\"}");

            var settings = new SettingsService(directory).Load();

            Assert.Equal(CorrectionProviderKind.LanguageTool, settings.CorrectionProvider);
            Assert.False(settings.PrepareLunaCorrections);
            Assert.Equal(CorrectionShortcut.Default.Modifiers, settings.ShortcutModifiers);
            Assert.Equal(CorrectionShortcut.Default.VirtualKey, settings.ShortcutVirtualKey);
            Assert.False(settings.ShortcutResetOnLoad);
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

    [Fact]
    public void LunaPreparation_IsDisabledByDefaultAndPersistsWhenEnabled()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SettingsService(directory);
            Assert.False(service.Load().PrepareLunaCorrections);

            service.Save(new AppSettings { PrepareLunaCorrections = true });

            Assert.True(service.Load().PrepareLunaCorrections);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ShortcutSettingsPersistButResetOnLoadFlagDoesNot()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SettingsService(directory);
            service.Save(new AppSettings
            {
                ShortcutModifiers = 6,
                ShortcutVirtualKey = 0x51,
                ShortcutResetOnLoad = true
            });

            var savedJson = File.ReadAllText(Path.Combine(directory, "settings.json"));
            Assert.DoesNotContain("shortcutResetOnLoad", savedJson.ToLowerInvariant());

            var loaded = service.Load();

            Assert.Equal(6u, loaded.ShortcutModifiers);
            Assert.Equal(0x51u, loaded.ShortcutVirtualKey);
            Assert.False(loaded.ShortcutResetOnLoad);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void InvalidStoredShortcutResetsToDefaultAndSetsResetOnLoadFlag()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                "{\"shortcutModifiers\":8,\"shortcutVirtualKey\":65}");

            var settings = new SettingsService(directory).Load();

            Assert.Equal(CorrectionShortcut.Default.Modifiers, settings.ShortcutModifiers);
            Assert.Equal(CorrectionShortcut.Default.VirtualKey, settings.ShortcutVirtualKey);
            Assert.True(settings.ShortcutResetOnLoad);
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
