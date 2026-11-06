using System.Globalization;

namespace CodexLanguageFix.Core;

public enum AppText
{
    BrowserCorrectionReady,
    BrowserCorrectionFailed,
    BrowserCorrectionDisabled,
    FirstRunHeading,
    FirstRunExplanation,
    FirstRunPrivacy,
    Acknowledge,
    Enabled,
    StartWithWindows,
    PrepareLunaCorrections,
    CorrectionReady,
    TestConnection,
    ConnectOpenAi,
    Language,
    LanguageAutomatic,
    LanguageGerman,
    LanguageEnglish,
    Exit,
    AutostartChangeFailed,
    ApplicationPathUnavailable,
    Undo,
    UndoTooltip,
    CorrectTooltip,
    ShortcutUnavailable,
    Settings,
    RecordShortcut,
    RecordShortcutPrompt,
    RestoreDefaultShortcut,
    Save,
    Cancel,
    ShortcutHint,
    InvalidShortcut,
    ShortcutConflict,
    ShortcutSaveFailed,
    ShortcutReset,
    ShortcutSettingsReset,
    LastStatus,
    NoStatus,
    ShortcutSaved,
    CorrectAutomationName,
    NoPromptDetected,
    Checking,
    NoChangesFound,
    NoChangesFoundProvider,
    ComposerUnavailable,
    PromptChanged,
    WriteRejected,
    PromptCorrected,
    CorrectionAppliedOne,
    CorrectionsAppliedMany,
    CheckCancelled,
    CorrectionUnavailable,
    UnexpectedCorrectionFailure,
    UndoUnavailable,
    UndoCompleted,
    UndoFailed,
    ResponseTime,
    UnexpectedConnectionFailure,
    ProviderAvailable,
    ChangeCountOne,
    ChangeCountMany,
    PromptTooLongGeneric,
    CodexNotInstalled,
    CodexAppServerStartFailed,
    OpenAiBrowserFailed,
    OpenAiLoginTimeout,
    OpenAiLoginFailed,
    OpenAiLoginRequired,
    OpenAiConnected,
    LunaUnavailable,
    LunaRequestFailed,
    LunaUnexpectedTool,
    LunaInvalidResponse,
    LunaTimeout
}

public sealed class AppLocalizer
{
    public const string Automatic = "auto";
    public const string German = "de";
    public const string English = "en";

    private string _languageMode;

    public AppLocalizer(string? languageMode = null)
    {
        _languageMode = NormalizeLanguage(languageMode);
    }

    public event EventHandler? LanguageChanged;

    public string LanguageMode => _languageMode;

    public CultureInfo Culture => ResolveCulture(_languageMode);

    public bool IsGerman => Culture.TwoLetterISOLanguageName.Equals(German, StringComparison.OrdinalIgnoreCase);

    public void SetLanguage(string? languageMode)
    {
        var normalized = NormalizeLanguage(languageMode);
        if (string.Equals(_languageMode, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _languageMode = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(AppText text, params object[] arguments)
    {
        var value = (IsGerman ? GermanTexts : EnglishTexts)[text];
        return arguments.Length == 0 ? value : string.Format(Culture, value, arguments);
    }

    public static string NormalizeLanguage(string? languageMode) => languageMode?.ToLowerInvariant() switch
    {
        German => German,
        English => English,
        _ => Automatic
    };

    private static CultureInfo ResolveCulture(string languageMode)
    {
        if (languageMode == German)
        {
            return CultureInfo.GetCultureInfo("de-DE");
        }

        if (languageMode == English)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(German, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("de-DE")
            : CultureInfo.GetCultureInfo("en-US");
    }

    private static readonly IReadOnlyDictionary<AppText, string> EnglishTexts = new Dictionary<AppText, string>
    {
        [AppText.FirstRunHeading] = "One-click correction for Codex",
        [AppText.FirstRunExplanation] = "When a supported prompt field with text is active, a small Aa button appears inside it. Click it or press {0} to correct the current prompt with Codex.",
        [AppText.FirstRunPrivacy] = "No prompt is sent automatically by default. The optional “Prepare Codex corrections” setting is off. When enabled, the unsent prompt, including intermediate drafts, is sent to Codex after 1.2 seconds of observed text stability to prepare a correction draft. You insert the draft manually; it is never applied automatically. Requests use the ChatGPT account connected through the official Codex Desktop runtime and may count toward that account's usage limits. The app does not use a Codex plugin or the clipboard, and never logs prompts, drafts, responses, or OAuth tokens.",
        [AppText.Acknowledge] = "Got it",
        [AppText.Enabled] = "Enabled",
        [AppText.StartWithWindows] = "Start with Windows",
        [AppText.PrepareLunaCorrections] = "Prepare Codex corrections",
        [AppText.CorrectionReady] = "Correction ready",
        [AppText.TestConnection] = "Test Codex connection",
        [AppText.ConnectOpenAi] = "Connect to Codex",
        [AppText.Language] = "Language",
        [AppText.LanguageAutomatic] = "Automatic (Windows)",
        [AppText.LanguageGerman] = "Deutsch",
        [AppText.LanguageEnglish] = "English",
        [AppText.Exit] = "Exit",
        [AppText.AutostartChangeFailed] = "Could not change automatic startup",
        [AppText.ApplicationPathUnavailable] = "The application path could not be determined.",
        [AppText.Undo] = "Undo",
        [AppText.UndoTooltip] = "Undo the last correction",
        [AppText.CorrectTooltip] = "Correct spelling and grammar ({0})",
        [AppText.ShortcutUnavailable] = "The {0} shortcut could not be registered. You can still click the Aa button.",
        [AppText.Settings] = "Settings",
        [AppText.RecordShortcut] = "Record shortcut",
        [AppText.RecordShortcutPrompt] = "Press the desired shortcut now. Escape cancels recording.",
        [AppText.RestoreDefaultShortcut] = "Restore default",
        [AppText.Save] = "Save",
        [AppText.Cancel] = "Cancel",
        [AppText.ShortcutHint] = "Click “Record shortcut”, then press the desired combination. AltGr is Ctrl+Alt. Escape cancels recording.",
        [AppText.InvalidShortcut] = "That shortcut cannot be used. Choose a different key combination.",
        [AppText.ShortcutConflict] = "That shortcut is already in use by another application.",
        [AppText.ShortcutSaveFailed] = "The shortcut could not be saved. Try again.",
        [AppText.ShortcutReset] = "Default shortcut restored. Save to apply it.",
        [AppText.ShortcutSettingsReset] = "The stored shortcut was invalid. Using {0}; choose and save a shortcut in Settings.",
        [AppText.LastStatus] = "Last status",
        [AppText.NoStatus] = "No status available.",
        [AppText.ShortcutSaved] = "Shortcut {0} saved.",
        [AppText.CorrectAutomationName] = "Correct prompt",
        [AppText.NoPromptDetected] = "No supported prompt detected",
        [AppText.Checking] = "Checking …",
        [AppText.NoChangesFound] = "No changes found",
        [AppText.NoChangesFoundProvider] = "No changes · {0}",
        [AppText.ComposerUnavailable] = "The prompt field is no longer available. Nothing was overwritten.",
        [AppText.PromptChanged] = "The prompt changed during the check. Nothing was overwritten.",
        [AppText.WriteRejected] = "The correction could not be inserted safely. Please check the input field.",
        [AppText.BrowserCorrectionReady] = "Chrome correction ready.",
        [AppText.BrowserCorrectionFailed] = "Codex correction failed. Your text was not changed.",
        [AppText.BrowserCorrectionDisabled] = "Correction is paused or automatic preparation is disabled.",
        [AppText.PromptCorrected] = "Prompt corrected",
        [AppText.CorrectionAppliedOne] = "1 correction was applied with {0}.",
        [AppText.CorrectionsAppliedMany] = "{0:N0} corrections were applied with {1}.",
        [AppText.CheckCancelled] = "Check cancelled",
        [AppText.CorrectionUnavailable] = "Correction unavailable",
        [AppText.UnexpectedCorrectionFailure] = "The correction failed unexpectedly. The prompt was not changed.",
        [AppText.UndoUnavailable] = "Undo is no longer available",
        [AppText.UndoCompleted] = "Correction undone",
        [AppText.UndoFailed] = "Undo failed",
        [AppText.ResponseTime] = "Response in {0:N0} ms.",
        [AppText.UnexpectedConnectionFailure] = "The connection test failed unexpectedly.",
        [AppText.ProviderAvailable] = "{0} is available",
        [AppText.ChangeCountOne] = "1 change",
        [AppText.ChangeCountMany] = "{0:N0} changes",
        [AppText.PromptTooLongGeneric] = "The prompt contains {0:N0} characters. Codex accepts no more than {1:N0} characters per correction.",
        [AppText.CodexNotInstalled] = "The official Codex Desktop runtime was not found. Install or update Codex Desktop, then try again.",
        [AppText.CodexAppServerStartFailed] = "The official Codex App Server could not be started.",
        [AppText.OpenAiBrowserFailed] = "The Codex sign-in page could not be opened.",
        [AppText.OpenAiLoginTimeout] = "Codex sign-in was not completed within five minutes.",
        [AppText.OpenAiLoginFailed] = "Codex sign-in was not completed successfully.",
        [AppText.OpenAiLoginRequired] = "Connect to Codex from the tray menu before requesting a correction.",
        [AppText.OpenAiConnected] = "Codex is connected",
        [AppText.LunaUnavailable] = "No compatible Codex correction model with low reasoning and Fast mode is available. Update the official Codex Desktop installation and try again.",
        [AppText.LunaRequestFailed] = "Codex could not complete the correction. The prompt was not changed.",
        [AppText.LunaUnexpectedTool] = "Codex attempted an operation outside text correction. The response was rejected.",
        [AppText.LunaInvalidResponse] = "Codex returned an invalid or unsafe correction. The prompt was not changed.",
        [AppText.LunaTimeout] = "Codex did not complete the correction within 60 seconds."
    };

    private static readonly IReadOnlyDictionary<AppText, string> GermanTexts = new Dictionary<AppText, string>
    {
        [AppText.FirstRunHeading] = "Ein-Klick-Korrektur für Codex",
        [AppText.FirstRunExplanation] = "Wenn ein unterstütztes Eingabefeld mit Text aktiv ist, erscheint darin ein kleiner Aa-Knopf. Klicke darauf oder drücke {0}, um den aktuellen Prompt mit Codex zu korrigieren.",
        [AppText.FirstRunPrivacy] = "Standardmäßig wird kein Prompt automatisch übertragen. Die optionale Einstellung „Codex-Korrekturen vorbereiten“ ist ausgeschaltet. Ist sie eingeschaltet, sendet die App den ungesendeten Prompt einschließlich Zwischenständen nach 1,2 Sekunden beobachteter Textstabilität an Codex, um einen Korrekturentwurf vorzubereiten. Du fügst den Entwurf manuell ein; er wird nie automatisch eingesetzt. Anfragen verwenden das ChatGPT-Konto, das über die offizielle Codex-Desktop-Laufzeit verbunden ist, und können auf dessen Nutzungslimits angerechnet werden. Die App verwendet weder ein Codex-Plugin noch die Zwischenablage und protokolliert niemals Prompts, Entwürfe, Antworten oder OAuth-Tokens.",
        [AppText.Acknowledge] = "Verstanden",
        [AppText.Enabled] = "Aktiviert",
        [AppText.StartWithWindows] = "Mit Windows starten",
        [AppText.PrepareLunaCorrections] = "Codex-Korrekturen vorbereiten",
        [AppText.CorrectionReady] = "Korrektur bereit",
        [AppText.TestConnection] = "Codex-Verbindung prüfen",
        [AppText.ConnectOpenAi] = "Mit Codex verbinden",
        [AppText.Language] = "Sprache",
        [AppText.LanguageAutomatic] = "Automatisch (Windows)",
        [AppText.LanguageGerman] = "Deutsch",
        [AppText.LanguageEnglish] = "English",
        [AppText.Exit] = "Beenden",
        [AppText.AutostartChangeFailed] = "Autostart konnte nicht geändert werden",
        [AppText.ApplicationPathUnavailable] = "Der Anwendungspfad konnte nicht bestimmt werden.",
        [AppText.Undo] = "Rückgängig",
        [AppText.UndoTooltip] = "Letzte Korrektur zurücknehmen",
        [AppText.CorrectTooltip] = "Rechtschreibung und Grammatik korrigieren ({0})",
        [AppText.ShortcutUnavailable] = "Das Tastenkürzel {0} konnte nicht registriert werden. Du kannst weiterhin auf den Aa-Knopf klicken.",
        [AppText.Settings] = "Einstellungen",
        [AppText.RecordShortcut] = "Tastenkürzel aufnehmen",
        [AppText.RecordShortcutPrompt] = "Drücke jetzt das gewünschte Tastenkürzel. Escape bricht die Aufnahme ab.",
        [AppText.RestoreDefaultShortcut] = "Standard wiederherstellen",
        [AppText.Save] = "Speichern",
        [AppText.Cancel] = "Abbrechen",
        [AppText.ShortcutHint] = "Klicke auf „Tastenkürzel aufnehmen“ und drücke die gewünschte Kombination. AltGr entspricht Strg+Alt. Mit Esc brichst du die Aufnahme ab.",
        [AppText.InvalidShortcut] = "Dieses Tastenkürzel kann nicht verwendet werden. Wähle eine andere Tastenkombination.",
        [AppText.ShortcutConflict] = "Dieses Tastenkürzel wird bereits von einer anderen Anwendung verwendet.",
        [AppText.ShortcutSaveFailed] = "Das Tastenkürzel konnte nicht gespeichert werden. Versuche es erneut.",
        [AppText.ShortcutReset] = "Das Standard-Tastenkürzel wurde wiederhergestellt. Speichere, um es zu übernehmen.",
        [AppText.ShortcutSettingsReset] = "Das gespeicherte Tastenkürzel war ungültig. Es gilt {0}; wähle und speichere ein Kürzel in den Einstellungen.",
        [AppText.LastStatus] = "Letzter Status",
        [AppText.NoStatus] = "Kein Status verfügbar.",
        [AppText.ShortcutSaved] = "Tastenkürzel {0} gespeichert.",
        [AppText.CorrectAutomationName] = "Prompt korrigieren",
        [AppText.NoPromptDetected] = "Kein unterstützter Prompt erkannt",
        [AppText.Checking] = "Wird geprüft …",
        [AppText.NoChangesFound] = "Keine Änderungen gefunden",
        [AppText.NoChangesFoundProvider] = "Keine Änderungen · {0}",
        [AppText.ComposerUnavailable] = "Das Eingabefeld ist nicht mehr verfügbar. Es wurde nichts überschrieben.",
        [AppText.PromptChanged] = "Der Prompt wurde während der Prüfung verändert. Es wurde nichts überschrieben.",
        [AppText.WriteRejected] = "Die Korrektur konnte nicht sicher eingesetzt werden. Bitte prüfe das Eingabefeld.",
        [AppText.BrowserCorrectionReady] = "Chrome-Korrektur bereit.",
        [AppText.BrowserCorrectionFailed] = "Die Codex-Korrektur ist fehlgeschlagen. Dein Text wurde nicht geändert.",
        [AppText.BrowserCorrectionDisabled] = "Die Korrektur ist pausiert oder die automatische Vorbereitung deaktiviert.",
        [AppText.PromptCorrected] = "Prompt korrigiert",
        [AppText.CorrectionAppliedOne] = "1 Korrektur wurde mit {0} übernommen.",
        [AppText.CorrectionsAppliedMany] = "{0:N0} Korrekturen wurden mit {1} übernommen.",
        [AppText.CheckCancelled] = "Prüfung abgebrochen",
        [AppText.CorrectionUnavailable] = "Korrektur nicht möglich",
        [AppText.UnexpectedCorrectionFailure] = "Die Korrektur ist unerwartet fehlgeschlagen. Der Prompt wurde nicht verändert.",
        [AppText.UndoUnavailable] = "Rückgängig nicht mehr möglich",
        [AppText.UndoCompleted] = "Korrektur zurückgenommen",
        [AppText.UndoFailed] = "Rückgängig fehlgeschlagen",
        [AppText.ResponseTime] = "Antwort in {0:N0} ms.",
        [AppText.UnexpectedConnectionFailure] = "Der Verbindungstest ist unerwartet fehlgeschlagen.",
        [AppText.ProviderAvailable] = "{0} ist verfügbar",
        [AppText.ChangeCountOne] = "1 Änderung",
        [AppText.ChangeCountMany] = "{0:N0} Änderungen",
        [AppText.PromptTooLongGeneric] = "Der Prompt enthält {0:N0} Zeichen. Codex akzeptiert höchstens {1:N0} Zeichen pro Korrektur.",
        [AppText.CodexNotInstalled] = "Die offizielle Codex-Desktop-Laufzeit wurde nicht gefunden. Installiere oder aktualisiere Codex Desktop und versuche es erneut.",
        [AppText.CodexAppServerStartFailed] = "Der offizielle Codex App Server konnte nicht gestartet werden.",
        [AppText.OpenAiBrowserFailed] = "Die Codex-Anmeldeseite konnte nicht geöffnet werden.",
        [AppText.OpenAiLoginTimeout] = "Die Codex-Anmeldung wurde nicht innerhalb von fünf Minuten abgeschlossen.",
        [AppText.OpenAiLoginFailed] = "Die Codex-Anmeldung wurde nicht erfolgreich abgeschlossen.",
        [AppText.OpenAiLoginRequired] = "Verbinde Codex zuerst über das Menü im Infobereich, bevor du eine Korrektur anforderst.",
        [AppText.OpenAiConnected] = "Codex ist verbunden",
        [AppText.LunaUnavailable] = "Es ist kein kompatibles Codex-Korrekturmodell mit niedriger Denkstufe und Fast Mode verfügbar. Aktualisiere die offizielle Codex-Desktop-Installation und versuche es erneut.",
        [AppText.LunaRequestFailed] = "Codex konnte die Korrektur nicht abschließen. Der Prompt wurde nicht verändert.",
        [AppText.LunaUnexpectedTool] = "Codex hat eine Aktion außerhalb der Textkorrektur versucht. Die Antwort wurde verworfen.",
        [AppText.LunaInvalidResponse] = "Codex hat eine ungültige oder unsichere Korrektur geliefert. Der Prompt wurde nicht verändert.",
        [AppText.LunaTimeout] = "Codex hat die Korrektur nicht innerhalb von 60 Sekunden abgeschlossen."
    };
}
