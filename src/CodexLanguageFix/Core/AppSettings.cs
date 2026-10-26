using System.Text.Json.Serialization;

namespace CodexLanguageFix.Core;

public sealed class AppSettings
{
    public const int CurrentSettingsVersion = 2;

    public int SettingsVersion { get; set; } = CurrentSettingsVersion;
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool PrepareLunaCorrections { get; set; }
    public bool FirstRunNoticeShown { get; set; }
    public string Language { get; set; } = AppLocalizer.Automatic;
    public uint ShortcutModifiers { get; set; } = 3;
    public uint ShortcutVirtualKey { get; set; } = 0xBC;

    [JsonIgnore]
    public bool ShortcutResetOnLoad { get; set; }
}
