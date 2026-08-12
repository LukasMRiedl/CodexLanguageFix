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
    ComposerUnavailable,
    PromptChanged,
    WriteRejected,
    PromptCorrected,
    SuggestionAppliedOne,
    SuggestionsAppliedMany,
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
    ChangeCountOne,
    ChangeCountMany,
    PromptTooLong,
    LanguageToolTimeout,
    LanguageToolNetworkFailure,
    LanguageToolRateLimit,
    LanguageToolHttpFailure,
    LanguageToolInvalidResponse,
    PublicRateLimit
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
        [AppText.FirstRunExplanation] = "When a supported prompt field is active, a small Aa button appears inside it. Only after you click it is the current prompt sent securely to the free LanguageTool API and corrected.",
        [AppText.FirstRunPrivacy] = "The app uses neither a Codex plugin nor the clipboard. Prompt text and API responses are not logged.",
        [AppText.Acknowledge] = "Got it",
        [AppText.Enabled] = "Enabled",
        [AppText.StartWithWindows] = "Start with Windows",
        [AppText.TestConnection] = "Test LanguageTool connection",
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
        [AppText.CorrectAutomationName] = "Correct prompt with LanguageTool",
        [AppText.NoPromptDetected] = "No supported prompt detected",
        [AppText.Checking] = "Checking …",
        [AppText.NoChangesFound] = "No changes found",
        [AppText.ComposerUnavailable] = "The prompt field is no longer available. Nothing was overwritten.",
        [AppText.PromptChanged] = "The prompt changed during the check. Nothing was overwritten.",
        [AppText.WriteRejected] = "The app rejected direct text replacement. The prompt was not changed.",
        [AppText.PromptCorrected] = "Prompt corrected",
        [AppText.SuggestionAppliedOne] = "1 LanguageTool suggestion was applied.",
        [AppText.SuggestionsAppliedMany] = "{0:N0} LanguageTool suggestions were applied.",
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
        [AppText.ChangeCountOne] = "1 change",
        [AppText.ChangeCountMany] = "{0:N0} changes",
        [AppText.PromptTooLong] = "The prompt contains {0:N0} characters. The free API allows no more than {1:N0} characters per check.",
        [AppText.LanguageToolTimeout] = "LanguageTool did not respond within ten seconds.",
        [AppText.LanguageToolNetworkFailure] = "LanguageTool is currently unavailable. The prompt was not changed.",
        [AppText.LanguageToolRateLimit] = "LanguageTool has reached its request limit. Please try again later.",
        [AppText.LanguageToolHttpFailure] = "LanguageTool responded with HTTP {0}. The prompt was not changed.",
        [AppText.LanguageToolInvalidResponse] = "LanguageTool returned an invalid response. The prompt was not changed.",
        [AppText.PublicRateLimit] = "The public LanguageTool limit has been reached. Please try again in {0:N0} seconds."
    };

    private static readonly IReadOnlyDictionary<AppText, string> GermanTexts = new Dictionary<AppText, string>
    {
        [AppText.FirstRunHeading] = "Ein-Klick-Korrektur für Codex",
        [AppText.FirstRunExplanation] = "Wenn ein unterstütztes Eingabefeld aktiv ist, erscheint darin ein kleiner Aa-Knopf. Erst nach deinem Klick wird der aktuelle Prompt sicher an die kostenlose LanguageTool-API übertragen und korrigiert.",
        [AppText.FirstRunPrivacy] = "Die App verwendet weder ein Codex-Plugin noch die Zwischenablage. Prompttexte und API-Antworten werden nicht protokolliert.",
        [AppText.Acknowledge] = "Verstanden",
        [AppText.Enabled] = "Aktiviert",
        [AppText.StartWithWindows] = "Mit Windows starten",
        [AppText.TestConnection] = "LanguageTool-Verbindung testen",
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
        [AppText.CorrectAutomationName] = "Prompt mit LanguageTool korrigieren",
        [AppText.NoPromptDetected] = "Kein unterstützter Prompt erkannt",
        [AppText.Checking] = "Wird geprüft …",
        [AppText.NoChangesFound] = "Keine Änderungen gefunden",
        [AppText.ComposerUnavailable] = "Das Eingabefeld ist nicht mehr verfügbar. Es wurde nichts überschrieben.",
        [AppText.PromptChanged] = "Der Prompt wurde während der Prüfung verändert. Es wurde nichts überschrieben.",
        [AppText.WriteRejected] = "Die App hat das direkte Zurückschreiben abgelehnt. Der Prompt wurde nicht verändert.",
        [AppText.PromptCorrected] = "Prompt korrigiert",
        [AppText.SuggestionAppliedOne] = "1 LanguageTool-Vorschlag wurde übernommen.",
        [AppText.SuggestionsAppliedMany] = "{0:N0} LanguageTool-Vorschläge wurden übernommen.",
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
        [AppText.ChangeCountOne] = "1 Änderung",
        [AppText.ChangeCountMany] = "{0:N0} Änderungen",
        [AppText.PromptTooLong] = "Der Prompt enthält {0:N0} Zeichen. Die kostenlose API erlaubt höchstens {1:N0} Zeichen pro Prüfung.",
        [AppText.LanguageToolTimeout] = "LanguageTool hat nicht innerhalb von zehn Sekunden geantwortet.",
        [AppText.LanguageToolNetworkFailure] = "LanguageTool ist derzeit nicht erreichbar. Der Prompt wurde nicht verändert.",
        [AppText.LanguageToolRateLimit] = "LanguageTool hat das Anfragelimit erreicht. Bitte später erneut versuchen.",
        [AppText.LanguageToolHttpFailure] = "LanguageTool hat mit HTTP {0} geantwortet. Der Prompt wurde nicht verändert.",
        [AppText.LanguageToolInvalidResponse] = "LanguageTool hat eine ungültige Antwort geliefert. Der Prompt wurde nicht verändert.",
        [AppText.PublicRateLimit] = "Das öffentliche LanguageTool-Limit ist erreicht. Bitte in {0:N0} Sekunden erneut versuchen."
    };
}
