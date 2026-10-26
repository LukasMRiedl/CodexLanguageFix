using System.IO;
using System.Text.Json;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

public sealed class SettingsService
{
    private readonly string _directory;
    private readonly string _path;

    public SettingsService(string? baseDirectory = null)
    {
        _directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexLanguageFix");
        _path = Path.Combine(_directory, "settings.json");
    }

    public string ApplicationDirectory => _directory;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            if (!CanPreservePreparationChoice(document.RootElement))
            {
                settings.PrepareLunaCorrections = false;
            }

            var shortcut = new CorrectionShortcut(settings.ShortcutModifiers, settings.ShortcutVirtualKey);
            if (!shortcut.IsValid)
            {
                settings.ShortcutModifiers = CorrectionShortcut.Default.Modifiers;
                settings.ShortcutVirtualKey = CorrectionShortcut.Default.VirtualKey;
                settings.ShortcutResetOnLoad = true;
            }

            return settings;
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SettingsVersion = AppSettings.CurrentSettingsVersion;
        Directory.CreateDirectory(_directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    private static bool CanPreservePreparationChoice(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetPropertyIgnoreCase(root, "correctionProvider", out var legacyProvider))
        {
            return legacyProvider.ValueKind == JsonValueKind.Number
                && legacyProvider.TryGetInt32(out var provider)
                && provider == 1;
        }

        return TryGetPropertyIgnoreCase(root, "settingsVersion", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var settingsVersion)
            && settingsVersion == AppSettings.CurrentSettingsVersion;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
