using Microsoft.Win32;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

public sealed class AutostartService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexLanguageFix";
    private readonly AppLocalizer _localizer;

    public AutostartService(AppLocalizer? localizer = null)
    {
        _localizer = localizer ?? new AppLocalizer();
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException(_localizer.Get(AppText.ApplicationPathUnavailable));
            key.SetValue(ValueName, $"\"{executable}\" --autostart", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
