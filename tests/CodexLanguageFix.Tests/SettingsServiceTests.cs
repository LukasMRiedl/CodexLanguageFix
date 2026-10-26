using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void LegacySettingsWithoutProvider_DisablePreparationAndKeepOtherSettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """{"enabled":false,"startWithWindows":false,"prepareLunaCorrections":true,"language":"de"}""");

            var settings = new SettingsService(directory).Load();

            Assert.False(settings.PrepareLunaCorrections);
            Assert.False(settings.Enabled);
            Assert.False(settings.StartWithWindows);
            Assert.Equal("de", settings.Language);
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
    public void LegacyLunaProvider_PreservesPreparationChoice()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """{"correctionProvider":1,"prepareLunaCorrections":true}""");

            var settings = new SettingsService(directory).Load();

            Assert.True(settings.PrepareLunaCorrections);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LegacyLanguageToolProvider_DisablesPreparationChoice()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """{"correctionProvider":0,"prepareLunaCorrections":true,"settingsVersion":2}""");

            var settings = new SettingsService(directory).Load();

            Assert.False(settings.PrepareLunaCorrections);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("99")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public void LegacyInvalidProvider_DisablesPreparationChoice(string providerJson)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var json = $$"""{"correctionProvider":{{providerJson}},"prepareLunaCorrections":true}""";
            File.WriteAllText(Path.Combine(directory, "settings.json"), json);

            var settings = new SettingsService(directory).Load();

            Assert.False(settings.PrepareLunaCorrections);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CurrentSchemaWithoutLegacyProvider_PreservesPreparationChoice()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """{"settingsVersion":2,"prepareLunaCorrections":true}""");

            var settings = new SettingsService(directory).Load();

            Assert.True(settings.PrepareLunaCorrections);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("""{"prepareLunaCorrections":true}""")]
    [InlineData("""{"settingsVersion":1,"prepareLunaCorrections":true}""")]
    [InlineData("""{"settingsVersion":3,"prepareLunaCorrections":true}""")]
    public void MissingOrUnsupportedSchema_DisablesPreparationChoice(string json)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), json);

            var settings = new SettingsService(directory).Load();

            Assert.False(settings.PrepareLunaCorrections);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PreparationChoice_RoundTripsInCurrentSchemaWithoutLegacyProvider()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SettingsService(directory);
            Assert.False(service.Load().PrepareLunaCorrections);

            service.Save(new AppSettings { PrepareLunaCorrections = true, Language = "de" });

            var savedJson = File.ReadAllText(Path.Combine(directory, "settings.json"));
            Assert.Contains("\"settingsVersion\": 2", savedJson);
            Assert.DoesNotContain("correctionProvider", savedJson.ToLowerInvariant());

            var loaded = service.Load();

            Assert.True(loaded.PrepareLunaCorrections);
            Assert.Equal(AppSettings.CurrentSettingsVersion, loaded.SettingsVersion);
            Assert.Equal("de", loaded.Language);
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
                """{"settingsVersion":2,"shortcutModifiers":8,"shortcutVirtualKey":65}""");

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
