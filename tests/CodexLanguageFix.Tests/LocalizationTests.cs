using CodexLanguageFix.Core;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("de", "Aktiviert", "Wird geprüft …", "Rückgängig", "Codex-Korrekturen vorbereiten", "Korrektur bereit")]
    [InlineData("en", "Enabled", "Checking …", "Undo", "Prepare Codex corrections", "Correction ready")]
    public void ExplicitLanguage_ProvidesCompleteVisibleText(
        string language,
        string enabled,
        string checking,
        string undo,
        string prepareCorrections,
        string correctionReady)
    {
        var localizer = new AppLocalizer(language);

        Assert.Equal(enabled, localizer.Get(AppText.Enabled));
        Assert.Equal(checking, localizer.Get(AppText.Checking));
        Assert.Equal(undo, localizer.Get(AppText.Undo));
        Assert.Equal(prepareCorrections, localizer.Get(AppText.PrepareLunaCorrections));
        Assert.Equal(correctionReady, localizer.Get(AppText.CorrectionReady));
        Assert.All(Enum.GetValues<AppText>(), key => Assert.False(string.IsNullOrWhiteSpace(localizer.Get(key))));
    }

    [Theory]
    [InlineData("de", "1,2 Sekunden", "manuell", "ausgeschaltet")]
    [InlineData("en", "1.2 seconds", "manually", "off")]
    public void FirstRunPrivacy_ExplainsOptInCodexPreparation(
        string language,
        string stabilityDelay,
        string manualInsertion,
        string disabledByDefault)
    {
        var localizer = new AppLocalizer(language);
        var privacyText = localizer.Get(AppText.FirstRunPrivacy);

        Assert.Contains(stabilityDelay, privacyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(manualInsertion, privacyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(disabledByDefault, privacyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex", privacyText, StringComparison.OrdinalIgnoreCase);
        Assert.False(privacyText.Contains("LanguageTool", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Codex", localizer.Get(AppText.FirstRunExplanation), StringComparison.OrdinalIgnoreCase);
        Assert.False(localizer.Get(AppText.FirstRunExplanation).Contains("LanguageTool", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("de", "Codex-Verbindung prüfen", "Mit Codex verbinden")]
    [InlineData("en", "Test Codex connection", "Connect to Codex")]
    public void ConnectionMenu_UsesDirectCodexLabels(string language, string testConnection, string connect)
    {
        var localizer = new AppLocalizer(language);

        Assert.Equal(testConnection, localizer.Get(AppText.TestConnection));
        Assert.Equal(connect, localizer.Get(AppText.ConnectOpenAi));
    }

    [Theory]
    [InlineData("de", "Tastenkürzel", "Speichern", "Abbrechen")]
    [InlineData("en", "shortcut", "Save", "Cancel")]
    public void ShortcutText_UsesTheSelectedShortcutDynamically(
        string language,
        string shortcut,
        string save,
        string cancel)
    {
        var localizer = new AppLocalizer(language);

        Assert.Contains(shortcut, localizer.Get(AppText.CorrectTooltip, shortcut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(shortcut, localizer.Get(AppText.FirstRunExplanation, shortcut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(shortcut, localizer.Get(AppText.ShortcutUnavailable, shortcut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(shortcut, localizer.Get(AppText.ShortcutSaved, shortcut), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(save, localizer.Get(AppText.Save));
        Assert.Equal(cancel, localizer.Get(AppText.Cancel));
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
                Assert.Equal(localizer.Get(AppText.CorrectTooltip, CorrectionShortcut.Default.GetDisplayText(localizer)), overlay.CorrectButton.ToolTip);
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
