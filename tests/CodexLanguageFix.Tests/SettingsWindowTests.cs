using System.Windows;
using System.Windows.Controls;
using CodexLanguageFix.Core;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

public sealed class SettingsWindowTests
{
    [Theory]
    [InlineData("de", "Einstellungen", "Tastenkürzel aufnehmen", "Standard wiederherstellen", "Letzter Status")]
    [InlineData("en", "Settings", "Record shortcut", "Restore default", "Last status")]
    public void Window_LocalizesControlsAndShowsMultilineReadOnlyStatus(
        string language,
        string title,
        string recordShortcut,
        string restoreDefault,
        string lastStatus)
    {
        RunOnSta(() =>
        {
            var window = new SettingsWindow(
                new AppLocalizer(language),
                CorrectionShortcut.Default,
                "First line\nSecond line",
                _ => null);

            Assert.Equal(title, window.Title);
            Assert.Equal(recordShortcut, ((Button)window.FindName("RecordButton")!).Content);
            Assert.Equal(restoreDefault, ((Button)window.FindName("RestoreButton")!).Content);

            var statusLabel = (TextBlock)window.FindName("LastStatusLabel")!;
            var statusText = (TextBox)window.FindName("StatusTextBox")!;
            Assert.Equal(lastStatus, statusLabel.Text);
            Assert.True(statusText.IsReadOnly);
            Assert.True(statusText.AcceptsReturn);
            Assert.Contains("First line", statusText.Text);
            Assert.Contains("Second line", statusText.Text);

            window.UpdateStatus(string.Empty);
            Assert.Equal(new AppLocalizer(language).Get(AppText.NoStatus), statusText.Text);
            window.Close();
        });
    }

    [Fact]
    public void SaveFailure_IsShownInlineAndKeepsTheDialogOpen()
    {
        RunOnSta(() =>
        {
            var localizer = new AppLocalizer("en");
            var expectedError = localizer.Get(AppText.ShortcutConflict);
            var window = new SettingsWindow(
                localizer,
                CorrectionShortcut.Default,
                "Ready",
                _ => expectedError);

            window.Loaded += (_, _) =>
            {
                ((Button)window.FindName("SaveButton")!)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var message = (TextBlock)window.FindName("InlineMessageText")!;
                Assert.Equal(expectedError, message.Text);
                Assert.Equal(Visibility.Visible, message.Visibility);
                Assert.True(window.IsVisible);
                window.Close();
            };

            window.ShowDialog();
        });
    }

    [Fact]
    public void SaveSuccess_SetsDialogResultAndCloses()
    {
        RunOnSta(() =>
        {
            var window = new SettingsWindow(
                new AppLocalizer("en"),
                CorrectionShortcut.Default,
                "Ready",
                _ => null);

            window.Loaded += (_, _) =>
                ((Button)window.FindName("SaveButton")!)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(window.ShowDialog());
        });
    }

    [Fact]
    public void SettingsDialog_CanRenderWithoutTakingOverTheDesktop()
    {
        var outputPath = Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_SETTINGS_SCREENSHOT");
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        RunOnSta(() =>
        {
            var window = new SettingsWindow(new AppLocalizer("de"), CorrectionShortcut.Default,
                "Prompt korrigiert" + Environment.NewLine + "Eine Korrektur wurde angewendet.", _ => null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000
            };
            window.Loaded += (_, _) =>
            {
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(outputPath)) encoder.Save(stream);
                window.Close();
            };
            window.ShowDialog();
            Assert.True(new FileInfo(outputPath).Length > 0);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF settings test timed out.");
        if (failure is not null)
        {
            throw failure;
        }
    }
}