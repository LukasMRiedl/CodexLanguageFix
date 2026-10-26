using System.Reflection;
using System.Runtime.ExceptionServices;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

public sealed class TraySettingsTests
{
    [Fact]
    public void FeedbackRemainsAvailableWithoutOpeningAnotherWindow()
    {
        RunSta(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "CodexLanguageFix.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var localizer = new AppLocalizer("en");
                using var tray = new TrayController(new AppSettings(), new SettingsService(folder), new AutostartService(localizer), localizer);
                HideIcon(tray);
                var updates = 0;
                tray.LastStatusChanged += (_, _) => updates++;
                tray.SetStatus("Prompt corrected", "One correction applied.");
                Assert.Contains("One correction applied.", tray.LastStatus);
                tray.SetStatus("Correction unavailable", "Connection failed.");
                Assert.Equal("Correction unavailable" + Environment.NewLine + "Connection failed.", tray.LastStatus);
                Assert.Equal(2, updates);
                Assert.False(Directory.Exists(folder));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
    }

    [Fact]
    public void FailedSettingsWriteRetainsPreviousShortcutAndDoesNotChangeSavedData()
    {
        RunSta(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "CodexLanguageFix.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var localizer = new AppLocalizer("en");
                var settings = new AppSettings();
                var service = new SettingsService(folder);
                using var tray = new TrayController(settings, service, new AutostartService(localizer), localizer);
                HideIcon(tray);
                Directory.CreateDirectory(Path.Combine(folder, "settings.json"));
                var failure = Record.Exception(() => tray.SaveShortcut(new CorrectionShortcut(2, 0x4A)));
                Assert.True(failure is IOException or UnauthorizedAccessException);
                Assert.Equal(CorrectionShortcut.Default, tray.Shortcut);
                Assert.True(Directory.Exists(Path.Combine(folder, "settings.json")));
            }
            finally { Directory.Delete(folder, true); }
        });
    }

    [Fact]
    public void SavingShortcutPersistsOtherSettingsAndResetHintIsReadable()
    {
        RunSta(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "CodexLanguageFix.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var localizer = new AppLocalizer("en");
                var settings = new AppSettings { CorrectionProvider = CorrectionProviderKind.Luna, PrepareLunaCorrections = true, ShortcutResetOnLoad = true };
                var service = new SettingsService(folder);
                using var tray = new TrayController(settings, service, new AutostartService(localizer), localizer);
                HideIcon(tray);
                Assert.Equal(localizer.Get(AppText.ShortcutSettingsReset, CorrectionShortcut.Default.GetDisplayText(localizer)), tray.LastStatus);
                var shortcut = new CorrectionShortcut(2 | 4, 0x4A);
                tray.SaveShortcut(shortcut);
                var loaded = service.Load();
                Assert.Equal(shortcut, new CorrectionShortcut(loaded.ShortcutModifiers, loaded.ShortcutVirtualKey));
                Assert.Equal(CorrectionProviderKind.Luna, loaded.CorrectionProvider);
                Assert.True(loaded.PrepareLunaCorrections);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
    }

    private static void HideIcon(TrayController tray) =>
        ((System.Windows.Forms.NotifyIcon)typeof(TrayController).GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!).Visible = false;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
