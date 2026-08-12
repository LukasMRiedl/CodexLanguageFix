using CodexLanguageFix.Core;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("de", "Aktiviert", "Wird geprüft …", "Rückgängig")]
    [InlineData("en", "Enabled", "Checking …", "Undo")]
    public void ExplicitLanguage_ProvidesCompleteVisibleText(
        string language,
        string enabled,
        string checking,
        string undo)
    {
        var localizer = new AppLocalizer(language);

        Assert.Equal(enabled, localizer.Get(AppText.Enabled));
        Assert.Equal(checking, localizer.Get(AppText.Checking));
        Assert.Equal(undo, localizer.Get(AppText.Undo));
        Assert.All(Enum.GetValues<AppText>(), key => Assert.False(string.IsNullOrWhiteSpace(localizer.Get(key))));
    }

    [Theory]
    [InlineData("DE", "de")]
    [InlineData("en-US", "auto")]
    [InlineData("unknown", "auto")]
    [InlineData(null, "auto")]
    public void LanguageMode_IsNormalized(string? input, string expected)
    {
        Assert.Equal(expected, AppLocalizer.NormalizeLanguage(input));
    }

    [Fact]
    public void LanguageSwitch_RaisesOneEventAndChangesFormatting()
    {
        var localizer = new AppLocalizer("de");
        var changes = 0;
        localizer.LanguageChanged += (_, _) => changes++;

        localizer.SetLanguage("en");
        localizer.SetLanguage("en");

        Assert.Equal(1, changes);
        Assert.Equal("2 changes", localizer.Get(AppText.ChangeCountMany, 2));
    }

    [Theory]
    [InlineData("de", "Ein-Klick-Korrektur für Codex", "Verstanden", "Rückgängig")]
    [InlineData("en", "One-click correction for Codex", "Got it", "Undo")]
    public void WpfWindows_ApplySelectedLanguage(
        string language,
        string heading,
        string acknowledgement,
        string undo)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var localizer = new AppLocalizer(language);
                var firstRun = new FirstRunWindow(localizer);
                var overlay = new OverlayWindow(localizer);

                Assert.Equal(heading, firstRun.HeadingText.Text);
                Assert.Equal(acknowledgement, firstRun.AcceptButton.Content);
                Assert.Equal(undo, overlay.UndoText.Text);
                Assert.Equal(localizer.Get(AppText.CorrectTooltip), overlay.CorrectButton.ToolTip);

                firstRun.Close();
                overlay.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The localized WPF test timed out.");
        if (failure is not null)
        {
            throw failure;
        }
    }
}
