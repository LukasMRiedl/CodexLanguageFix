using System.Globalization;

namespace CodexLanguageFix.Core;

public enum AppText
{
    FirstRunHeading,
    FirstRunExplanation,
    FirstRunPrivacy,
    Acknowledge,
    Enabled,
    StartWithWindows,
    TestConnection,
    CorrectionProvider,
    ProviderLanguageTool,
    ProviderLuna,
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
    CorrectAutomationName,
    NoPromptDetected,
    Checking,
    NoChangesFound,
    NoChangesFoundProvider,
    ComposerUnavailable,
    PromptChanged,
    WriteRejected,
    PromptCorrected,
    SuggestionAppliedOne,
    SuggestionsAppliedMany,
    CorrectionAppliedOne,
    CorrectionsAppliedMany,
    CheckCancelled,
    CorrectionUnavailable,
    UnexpectedCorrectionFailure,
    UndoUnavailable,
    UndoCompleted,
    UndoFailed,
    LanguageToolAvailable,
    ResponseTime,
    LanguageToolUnavailable,
    UnexpectedConnectionFailure,
    ProviderAvailable,
    ChangeCountOne,
    ChangeCountMany,
    PromptTooLong,
    LanguageToolTimeout,
    LanguageToolNetworkFailure,
    LanguageToolRateLimit,
    LanguageToolHttpFailure,
    LanguageToolInvalidResponse,
    PublicRateLimit,
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
        [AppText.FirstRunExplanation] = "When a supported prompt field is active, a small Aa button appears inside it. After you click it, the selected provider corrects the current prompt. LanguageTool is used by default; OpenAI Luna with ChatGPT OAuth can be selected in the tray menu.",
        [AppText.FirstRunPrivacy] = "The app uses neither a Codex plugin nor the clipboard. Text is transmitted only after a click, and prompt text, provider responses, and OAuth tokens are never logged.",
        [AppText.Acknowledge] = "Got it",
        [AppText.Enabled] = "Enabled",
        [AppText.StartWithWindows] = "Start with Windows",
        [AppText.TestConnection] = "Test selected provider",
        [AppText.CorrectionProvider] = "Correction provider",
        [AppText.ProviderLanguageTool] = "LanguageTool",
        [AppText.ProviderLuna] = "OpenAI Luna · Fast",
        [AppText.ConnectOpenAi] = "Connect OpenAI",
        [AppText.Language] = "Language",
        [AppText.LanguageAutomatic] = "Automatic (Windows)",
        [AppText.LanguageGerman] = "Deutsch",
        [AppText.LanguageEnglish] = "English",
        [AppText.Exit] = "Exit",
        [AppText.AutostartChangeFailed] = "Could not change automatic startup",
        [AppText.ApplicationPathUnavailable] = "The application path could not be determined.",
        [AppText.Undo] = "Undo",
        [AppText.UndoTooltip] = "Undo the last correction",
        [AppText.CorrectTooltip] = "Correct spelling and grammar",
        [AppText.CorrectAutomationName] = "Correct prompt",
        [AppText.NoPromptDetected] = "No supported prompt detected",
        [AppText.Checking] = "Checking …",
        [AppText.NoChangesFound] = "No changes found",
        [AppText.NoChangesFoundProvider] = "No changes · {0}",
        [AppText.ComposerUnavailable] = "The prompt field is no longer available. Nothing was overwritten.",
        [AppText.PromptChanged] = "The prompt changed during the check. Nothing was overwritten.",
        [AppText.WriteRejected] = "The correction could not be inserted safely. Please check the input field.",
        [AppText.PromptCorrected] = "Prompt corrected",
        [AppText.SuggestionAppliedOne] = "1 LanguageTool suggestion was applied.",
        [AppText.SuggestionsAppliedMany] = "{0:N0} LanguageTool suggestions were applied.",
        [AppText.CorrectionAppliedOne] = "1 correction was applied with {0}.",
        [AppText.CorrectionsAppliedMany] = "{0:N0} corrections were applied with {1}.",
        [AppText.CheckCancelled] = "Check cancelled",
        [AppText.CorrectionUnavailable] = "Correction unavailable",
        [AppText.UnexpectedCorrectionFailure] = "The correction failed unexpectedly. The prompt was not changed.",
        [AppText.UndoUnavailable] = "Undo is no longer available",
        [AppText.UndoCompleted] = "Correction undone",
        [AppText.UndoFailed] = "Undo failed",
        [AppText.LanguageToolAvailable] = "LanguageTool is available",
        [AppText.ResponseTime] = "Response in {0:N0} ms.",
        [AppText.LanguageToolUnavailable] = "LanguageTool is unavailable",
        [AppText.UnexpectedConnectionFailure] = "The connection test failed unexpectedly.",
        [AppText.ProviderAvailable] = "{0} is available",
        [AppText.ChangeCountOne] = "1 change",
        [AppText.ChangeCountMany] = "{0:N0} changes",
        [AppText.PromptTooLong] = "The prompt contains {0:N0} characters. The free API allows no more than {1:N0} characters per check.",
        [AppText.LanguageToolTimeout] = "LanguageTool did not respond within ten seconds.",
        [AppText.LanguageToolNetworkFailure] = "LanguageTool is currently unavailable. The prompt was not changed.",
        [AppText.LanguageToolRateLimit] = "LanguageTool has reached its request limit. Please try again later.",
        [AppText.LanguageToolHttpFailure] = "LanguageTool responded with HTTP {0}. The prompt was not changed.",
        [AppText.LanguageToolInvalidResponse] = "LanguageTool returned an invalid response. The prompt was not changed.",
        [AppText.PublicRateLimit] = "The public LanguageTool limit has been reached. Please try again in {0:N0} seconds.",
        [AppText.PromptTooLongGeneric] = "The prompt contains {0:N0} characters. The selected provider accepts no more than {1:N0} characters per correction.",
        [AppText.CodexNotInstalled] = "The official Codex runtime was not found. Install or update Codex, then try again.",
        [AppText.CodexAppServerStartFailed] = "The official Codex App Server could not be started.",
        [AppText.OpenAiBrowserFailed] = "The OpenAI sign-in page could not be opened.",
        [AppText.OpenAiLoginTimeout] = "OpenAI sign-in was not completed within five minutes.",
        [AppText.OpenAiLoginFailed] = "OpenAI sign-in was not completed successfully.",
        [AppText.OpenAiLoginRequired] = "Connect OpenAI from the tray menu before using Luna.",
        [AppText.OpenAiConnected] = "OpenAI is connected",
        [AppText.LunaUnavailable] = "GPT-5.6 Luna without reasoning and with Fast mode is unavailable. Update Codex and try again; the app will not fall back to another model or mode.",
        [AppText.LunaRequestFailed] = "Luna could not complete the correction. The prompt was not changed.",
        [AppText.LunaUnexpectedTool] = "Luna attempted an operation outside text correction. The response was rejected.",
        [AppText.LunaInvalidResponse] = "Luna returned an invalid or unsafe correction. The prompt was not changed.",
        [AppText.LunaTimeout] = "Luna did not complete the correction within 60 seconds."
    };

    private static readonly IReadOnlyDictionary<AppText, string> GermanTexts = new Dictionary<AppText, string>
    {
        [AppText.FirstRunHeading] = "Ein-Klick-Korrektur für Codex",
        [AppText.FirstRunExplanation] = "Wenn ein unterstütztes Eingabefeld aktiv ist, erscheint darin ein kleiner Aa-Knopf. Nach deinem Klick korrigiert der ausgewählte Anbieter den aktuellen Prompt. Standardmäßig wird LanguageTool verwendet; OpenAI Luna mit ChatGPT-OAuth kann im Infobereich ausgewählt werden.",
        [AppText.FirstRunPrivacy] = "Die App verwendet weder ein Codex-Plugin noch die Zwischenablage. Text wird nur nach einem Klick übertragen; Prompttexte, Anbieterantworten und OAuth-Tokens werden niemals protokolliert.",
        [AppText.Acknowledge] = "Verstanden",
        [AppText.Enabled] = "Aktiviert",
        [AppText.StartWithWindows] = "Mit Windows starten",
        [AppText.TestConnection] = "Ausgewählten Anbieter testen",
        [AppText.CorrectionProvider] = "Korrekturanbieter",
        [AppText.ProviderLanguageTool] = "LanguageTool",
        [AppText.ProviderLuna] = "OpenAI Luna · Schnell",
        [AppText.ConnectOpenAi] = "OpenAI verbinden",
        [AppText.Language] = "Sprache",
        [AppText.LanguageAutomatic] = "Automatisch (Windows)",
        [AppText.LanguageGerman] = "Deutsch",
        [AppText.LanguageEnglish] = "English",
        [AppText.Exit] = "Beenden",
        [AppText.AutostartChangeFailed] = "Autostart konnte nicht geändert werden",
        [AppText.ApplicationPathUnavailable] = "Der Anwendungspfad konnte nicht bestimmt werden.",
        [AppText.Undo] = "Rückgängig",
        [AppText.UndoTooltip] = "Letzte Korrektur zurücknehmen",
        [AppText.CorrectTooltip] = "Rechtschreibung und Grammatik korrigieren",
        [AppText.CorrectAutomationName] = "Prompt korrigieren",
        [AppText.NoPromptDetected] = "Kein unterstützter Prompt erkannt",
        [AppText.Checking] = "Wird geprüft …",
        [AppText.NoChangesFound] = "Keine Änderungen gefunden",
        [AppText.NoChangesFoundProvider] = "Keine Änderungen · {0}",
        [AppText.ComposerUnavailable] = "Das Eingabefeld ist nicht mehr verfügbar. Es wurde nichts überschrieben.",
        [AppText.PromptChanged] = "Der Prompt wurde während der Prüfung verändert. Es wurde nichts überschrieben.",
        [AppText.WriteRejected] = "Die Korrektur konnte nicht sicher eingesetzt werden. Bitte prüfe das Eingabefeld.",
        [AppText.PromptCorrected] = "Prompt korrigiert",
        [AppText.SuggestionAppliedOne] = "1 LanguageTool-Vorschlag wurde übernommen.",
        [AppText.SuggestionsAppliedMany] = "{0:N0} LanguageTool-Vorschläge wurden übernommen.",
        [AppText.CorrectionAppliedOne] = "1 Korrektur wurde mit {0} übernommen.",
        [AppText.CorrectionsAppliedMany] = "{0:N0} Korrekturen wurden mit {1} übernommen.",
        [AppText.CheckCancelled] = "Prüfung abgebrochen",
        [AppText.CorrectionUnavailable] = "Korrektur nicht möglich",
        [AppText.UnexpectedCorrectionFailure] = "Die Korrektur ist unerwartet fehlgeschlagen. Der Prompt wurde nicht verändert.",
        [AppText.UndoUnavailable] = "Rückgängig nicht mehr möglich",
        [AppText.UndoCompleted] = "Korrektur zurückgenommen",
        [AppText.UndoFailed] = "Rückgängig fehlgeschlagen",
        [AppText.LanguageToolAvailable] = "LanguageTool erreichbar",
        [AppText.ResponseTime] = "Antwort in {0:N0} ms.",
        [AppText.LanguageToolUnavailable] = "LanguageTool nicht erreichbar",
        [AppText.UnexpectedConnectionFailure] = "Der Verbindungstest ist unerwartet fehlgeschlagen.",
        [AppText.ProviderAvailable] = "{0} ist verfügbar",
        [AppText.ChangeCountOne] = "1 Änderung",
        [AppText.ChangeCountMany] = "{0:N0} Änderungen",
        [AppText.PromptTooLong] = "Der Prompt enthält {0:N0} Zeichen. Die kostenlose API erlaubt höchstens {1:N0} Zeichen pro Prüfung.",
        [AppText.LanguageToolTimeout] = "LanguageTool hat nicht innerhalb von zehn Sekunden geantwortet.",
        [AppText.LanguageToolNetworkFailure] = "LanguageTool ist derzeit nicht erreichbar. Der Prompt wurde nicht verändert.",
        [AppText.LanguageToolRateLimit] = "LanguageTool hat das Anfragelimit erreicht. Bitte später erneut versuchen.",
        [AppText.LanguageToolHttpFailure] = "LanguageTool hat mit HTTP {0} geantwortet. Der Prompt wurde nicht verändert.",
        [AppText.LanguageToolInvalidResponse] = "LanguageTool hat eine ungültige Antwort geliefert. Der Prompt wurde nicht verändert.",
        [AppText.PublicRateLimit] = "Das öffentliche LanguageTool-Limit ist erreicht. Bitte in {0:N0} Sekunden erneut versuchen.",
        [AppText.PromptTooLongGeneric] = "Der Prompt enthält {0:N0} Zeichen. Der ausgewählte Anbieter akzeptiert höchstens {1:N0} Zeichen pro Korrektur.",
        [AppText.CodexNotInstalled] = "Die offizielle Codex-Laufzeit wurde nicht gefunden. Installiere oder aktualisiere Codex und versuche es erneut.",
        [AppText.CodexAppServerStartFailed] = "Der offizielle Codex App Server konnte nicht gestartet werden.",
        [AppText.OpenAiBrowserFailed] = "Die OpenAI-Anmeldeseite konnte nicht geöffnet werden.",
        [AppText.OpenAiLoginTimeout] = "Die OpenAI-Anmeldung wurde nicht innerhalb von fünf Minuten abgeschlossen.",
        [AppText.OpenAiLoginFailed] = "Die OpenAI-Anmeldung wurde nicht erfolgreich abgeschlossen.",
        [AppText.OpenAiLoginRequired] = "Verbinde OpenAI zuerst über das Menü im Infobereich, bevor du Luna verwendest.",
        [AppText.OpenAiConnected] = "OpenAI ist verbunden",
        [AppText.LunaUnavailable] = "GPT-5.6 Luna ohne Denkmodus und mit Fast Mode ist nicht verfügbar. Aktualisiere Codex und versuche es erneut; die App wechselt nicht auf ein anderes Modell oder einen anderen Modus.",
        [AppText.LunaRequestFailed] = "Luna konnte die Korrektur nicht abschließen. Der Prompt wurde nicht verändert.",
        [AppText.LunaUnexpectedTool] = "Luna hat eine Aktion außerhalb der Textkorrektur versucht. Die Antwort wurde verworfen.",
        [AppText.LunaInvalidResponse] = "Luna hat eine ungültige oder unsichere Korrektur geliefert. Der Prompt wurde nicht verändert.",
        [AppText.LunaTimeout] = "Luna hat die Korrektur nicht innerhalb von 60 Sekunden abgeschlossen."
    };
}
