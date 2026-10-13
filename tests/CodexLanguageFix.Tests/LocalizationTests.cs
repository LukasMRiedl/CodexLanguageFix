using CodexLanguageFix.Core;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("de", "Aktiviert", "Wird geprüft …", "Rückgängig", "Luna-Korrekturen vorbereiten", "Korrektur bereit")]
    [InlineData("en", "Enabled", "Checking …", "Undo", "Prepare Luna corrections", "Correction ready")]
    public void ExplicitLanguage_ProvidesCompleteVisibleText(
        string language,
        string enabled,
        string checking,
        string undo,
        string prepareLunaCorrections,
        string correctionReady)
    {
        var localizer = new AppLocalizer(language);

        Assert.Equal(enabled, localizer.Get(AppText.Enabled));
        Assert.Equal(checking, localizer.Get(AppText.Checking));
        Assert.Equal(undo, localizer.Get(AppText.Undo));
        Assert.Equal(prepareLunaCorrections, localizer.Get(AppText.PrepareLunaCorrections));
        Assert.Equal(correctionReady, localizer.Get(AppText.CorrectionReady));
        Assert.All(Enum.GetValues<AppText>(), key => Assert.False(string.IsNullOrWhiteSpace(localizer.Get(key))));
    }

    [Theory]
    [InlineData("de", "1,2 Sekunden", "manuell", "ausgeschaltet")]
    [InlineData("en", "1.2 seconds", "manually", "off")]
    public void FirstRunPrivacy_ExplainsOptInLunaPreparation(
        string language,
        string stabilityDelay,
        string manualInsertion,
        string disabledByDefault)
    {
        var privacyText = new AppLocalizer(language).Get(AppText.FirstRunPrivacy);

        Assert.Contains(stabilityDelay, privacyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(manualInsertion, privacyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(disabledByDefault, privacyText, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("de", "Ein-Klick-Korrektur für Codex", "Verstanden", "Rückgängig", "1,2 Sekunden")]
    [InlineData("en", "One-click correction for Codex", "Got it", "Undo", "1.2 seconds")]
    public void WpfWindows_ApplySelectedLanguage(
        string language,
        string heading,
        string acknowledgement,
        string undo,
        string stabilityDelay)
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
                Assert.Contains(stabilityDelay, firstRun.PrivacyText.Text);

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
