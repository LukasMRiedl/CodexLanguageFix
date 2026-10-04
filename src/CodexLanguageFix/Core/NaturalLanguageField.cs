using System.Text;
using System.Text.RegularExpressions;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Core;

internal static class NaturalLanguageField
{
    internal static bool IsAmbiguousComposerHint(ComposerHost host, string? name, string text) =>
        host == ComposerHost.Hermes ? HermesPlaceholder.IsAmbiguous(text)
        : host == ComposerHost.Codex && !string.IsNullOrWhiteSpace(name)
            && string.Equals(name, text.TrimEnd('\r', '\n'), StringComparison.Ordinal);

    private static readonly HashSet<string> CodexAutomationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "artifact-feedback-details",
        "chatgpt-custom-instructions",
        "chatgpt-personalization-more-about-you"
    };

    private static readonly HashSet<string> HermesAutomationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cron-prompt",
        "new-profile-soul",
        "webhook-prompt"
    };

    // Exact labels are taken from the installed Codex webview assets. IDs are
    // preferred because labels can be absent from UI Automation in embedded webviews.
    private static readonly HashSet<string> CodexLabels = BuildLabels(
        "How else could we improve? (optional)", "Custom instructions", "More about you",
        "Was könnten wir sonst noch verbessern? (optional)", "Benutzerdefinierte Anweisungen",
        "Mehr über dich", "Edit message", "Nachricht bearbeiten");

    // These are exact labels used by Hermes' localized prose fields. Keep them
    // exact: accessible names can otherwise contain arbitrary field or document text.
    private static readonly HashSet<string> HermesLabels = BuildLabels(
        "Prompt", "Idea", "Idee", "Criterion", "Kriterium", "Description", "Beschreibung",
        "SOUL.md", "Edit SOUL.md…", "SOUL.md bearbeiten…",
        "SOUL.md (optional — replaces the generated persona)",
        "What should this bot help with?", "Other", "Sonstiges", "Andere",
        "Edit message", "Nachricht bearbeiten");

    private static readonly HashSet<string> ExcludedLabels = BuildLabels(
        "Search", "Search…", "Name", "Title", "Nickname", "Occupation", "Project name",
        "Path", "File path", "Folder path", "URL", "URI",
        "API key", "Access token", "Token", "Password", "Authentication", "Auth",
        "Code", "JSON", "JSON Schema", "Settings", "Configuration", "Config", "Endpoint",
        "Command", "Shell", "Terminal", "Model", "Workspace", "Directory", "File name",
        "Suchen", "Name", "Titel", "Spitzname", "Beruf", "Projektname",
        "Pfad", "Dateipfad", "Ordnerpfad", "API-Schlüssel",
        "Zugriffstoken", "Passwort", "Authentifizierung", "Einstellungen", "Konfiguration",
        "Befehl", "Endpunkt", "Modell", "Arbeitsbereich", "Verzeichnis", "Dateiname");

    private static readonly HashSet<string> TechnicalIdTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "name", "title", "path", "url", "uri", "auth", "password", "secret", "token",
        "key", "code", "json", "setting", "settings", "config", "configuration", "endpoint",
        "command", "shell", "terminal", "model", "workspace", "directory", "filename", "query",
        "sql", "regex", "expression", "nickname", "occupation"
    };

    private static readonly HashSet<string> CodeEditorClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CodeMirror", "cm-editor", "Monaco", "monaco-editor", "ace_editor", "jsoneditor"
    };

    internal static bool IsSupported(
        ComposerHost host,
        string? className,
        string? automationId,
        string? name,
        string? associatedLabel)
    {
        if (HasCodeEditorClass(className)
            || HasTechnicalId(automationId)
            || IsExcludedLabel(name)
            || IsExcludedLabel(associatedLabel))
        {
            return false;
        }

        var normalizedId = Normalize(automationId);
        var normalizedName = Normalize(name);
        var normalizedLabel = Normalize(associatedLabel);
        return host switch
        {
            ComposerHost.Codex => CodexAutomationIds.Contains(normalizedId)
                || CodexLabels.Contains(normalizedName)
                || CodexLabels.Contains(normalizedLabel),
            ComposerHost.Hermes => HermesAutomationIds.Contains(normalizedId)
                || HermesLabels.Contains(normalizedName)
                || HermesLabels.Contains(normalizedLabel),
            _ => false
        };
    }

    internal static bool IsSupported(
        ComposerHost host,
        string? automationId,
        string? name,
        string? associatedLabel) =>
        IsSupported(host, null, automationId, name, associatedLabel);

    private static HashSet<string> BuildLabels(params string[] labels) =>
        labels.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsExcludedLabel(string? value) =>
        ExcludedLabels.Contains(Normalize(value));

    private static bool HasCodeEditorClass(string? className) =>
        (className ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(CodeEditorClasses.Contains);

    private static bool HasTechnicalId(string? automationId)
    {
        var value = automationId ?? string.Empty;
        if (value.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || value.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separated = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");
        var tokens = Regex.Split(separated, "[^\\p{L}\\p{N}]+")
            .Where(token => token.Length > 0);
        return tokens.Any(TechnicalIdTokens.Contains);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
