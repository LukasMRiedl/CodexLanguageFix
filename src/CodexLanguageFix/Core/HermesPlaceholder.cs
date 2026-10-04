namespace CodexLanguageFix.Core;

/// <summary>
/// Rejects source-known hint text that UI Automation cannot distinguish from
/// an identical user draft in the installed German and English Hermes UI.
/// </summary>
internal static class HermesPlaceholder
{
    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
        // New-session composer placeholders (apps/desktop/src/i18n/en.ts, de.ts).
        "What are we building?",
        "Give Hermes a task",
        "What's on your mind?",
        "Describe what you need",
        "What should we tackle?",
        "Ask anything",
        "Start with a goal",
        "Was bauen wir?",
        "Geben Sie Hermes eine Aufgabe",
        "Was ist Ihnen wichtig?",
        "Beschreiben Sie, was Sie brauchen",
        "Was sollen wir angehen?",
        "Fragen Sie irgendetwas",
        "Beginnen Sie mit einem Ziel",

        // Existing-session composer placeholders (apps/desktop/src/i18n/en.ts, de.ts).
        "Send a follow-up",
        "Add more context",
        "Refine the request",
        "What's next?",
        "Keep it going",
        "Push it further",
        "Adjust or continue",
        "Folge senden",
        "Mehr Kontext hinzufügen",
        "Anfrage verfeinern",
        "Was kommt als Nächstes?",
        "Weiter so",
        "Noch weiter",
        "Anpassen oder fortfahren",

        // Disabled transport states (apps/desktop/src/i18n/en.ts, de.ts).
        "Starting Hermes...",
        "Reconnecting to Hermes…",
        "Hermes wird gestartet…",
        "Verbindung zu Hermes wird wiederhergestellt…",

        // The message-edit composer uses copy.editMessage for data-placeholder.
        // It is a distinct Hermes chat editor but shares the same CSS pseudo-text.
        "Edit message",
        "Nachricht bearbeiten"
    };

    /// <summary>
    /// Returns true only when the entire UIA text value equals a source-known
    /// Hermes chat placeholder, allowing trailing TextPattern line endings.
    /// </summary>
    internal static bool IsAmbiguous(string? text)
    {
        if (text is null)
        {
            return false;
        }

        return KnownPlaceholders.Contains(text.TrimEnd('\r', '\n'));
    }
}
