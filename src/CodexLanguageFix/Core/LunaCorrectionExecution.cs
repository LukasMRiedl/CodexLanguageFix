namespace CodexLanguageFix.Core;

/// <summary>
/// Teilzeiten einer vollständig validierten Luna-Korrektur. Die Werte enthalten
/// ausschließlich technische Metadaten und keine Prompt- oder Antwortinhalte.
/// </summary>
public sealed record LunaCorrectionTimings(
    TimeSpan ServerAndAccount,
    TimeSpan ThreadProvision,
    TimeSpan TurnStart,
    TimeSpan? TimeToFirstTextDelta,
    TimeSpan ModelCompletion,
    TimeSpan Validation,
    TimeSpan Restoration,
    TimeSpan ComposerWrite,
    TimeSpan Cleanup)
{
    public static LunaCorrectionTimings Empty { get; } = new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        null,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);
}

/// <summary>
/// Transport-unabhängiges, unveränderliches Ergebnis genau einer Luna-Ausführung.
/// Es darf in Diagnose- und Benchmarkberichten verwendet werden, ohne Rohtext zu
/// protokollieren.
/// </summary>
public sealed record LunaCorrectionExecution(
    string CorrectedText,
    int ChangeCount,
    string PromptProfile,
    string PromptHash,
    string Effort,
    string ServiceTier,
    string Protocol,
    bool FallbackUsed,
    int OutputCharacters,
    LunaCorrectionTimings Timings)
{
    public string Model { get; init; } = string.Empty;
}
