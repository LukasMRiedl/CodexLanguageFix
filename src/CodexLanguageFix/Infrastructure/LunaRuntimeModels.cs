namespace CodexLanguageFix.Infrastructure;

/// <summary>
/// Das Produktionsprofil für die automatische Luna-Auswahl.
/// Alle produktiven Luna-Komponenten beziehen Prompt, Denkstufe und Protokoll
/// aus dieser einen unveränderlichen Quelle.
/// </summary>
internal sealed record LunaProductionConfiguration(
    string PromptVariant,
    string DeveloperInstructions,
    string Effort,
    string ServiceTier,
    LunaOutputProtocol OutputProtocol,
    string ProtocolName,
    int PatchThreshold)
{
    internal static LunaProductionConfiguration Qualified { get; } = new(
        PromptVariant: "baseline",
        DeveloperInstructions: LunaPromptCatalog.Baseline,
        Effort: "low",
        ServiceTier: "priority",
        OutputProtocol: LunaOutputProtocol.FullText,
        ProtocolName: "full-v1",
        PatchThreshold: int.MaxValue);
}

internal interface ILunaTimedTransportClient
{
    Task<LunaTransportExecution> RunStructuredCorrectionWithTimingAsync(
        string inputJson,
        System.Text.Json.JsonElement outputSchema,
        string developerInstructions,
        CodexLanguageFix.Core.LunaModelSelection selection,
        CancellationToken cancellationToken,
        bool replenishPreparedThread = false);
}

/// <summary>
/// Die unveränderlichen Laufzeitparameter eines einzelnen Luna-App-Server-Threads.
/// Ein vorbereiteter Thread darf nur mit exakt demselben Profil verwendet werden.
/// </summary>
internal sealed record LunaCorrectionProfile(
    string Model,
    string Effort,
    string ServiceTier,
    string OutputProtocol,
    string RuntimeDirectory,
    string DeveloperInstructions);

/// <summary>
/// Technische Teilzeiten eines letzten Luna-Protokolllaufs.
/// Die Werte enthalten keine Prompt- oder Antwortdaten.
/// </summary>
internal sealed record LunaProtocolTiming(
    TimeSpan EnsureServer,
    TimeSpan CapabilityValidation,
    TimeSpan ThreadStart,
    TimeSpan TurnStart,
    TimeSpan CompletionWait,
    TimeSpan CleanupEnqueue)
{
    /// <summary>
    /// Zeit vom Absenden von <c>turn/start</c> bis zum ersten Textdelta. Null, wenn der
    /// App Server kein Textdelta gesendet hat. Deltas werden ausschließlich gemessen.
    /// </summary>
    public TimeSpan? TimeToFirstTextDelta { get; init; }

    /// <summary>
    /// Zeit vom Absenden von <c>turn/start</c> bis zu <c>turn/completed</c>.
    /// </summary>
    public TimeSpan ModelCompletion { get; init; }

    public TimeSpan Total => EnsureServer
        + CapabilityValidation
        + ThreadStart
        + ModelCompletion
        + CleanupEnqueue;
}

/// <summary>
/// Unveränderliches Transportergebnis genau eines Luna-App-Server-Laufs. Damit bleiben
/// Antwort und Teilzeiten auch bei parallelen Anfragen eindeutig miteinander verknüpft.
/// </summary>
internal sealed record LunaTransportExecution(
    string Response,
    LunaCorrectionProfile Profile,
    LunaProtocolTiming Timing,
    string ThreadId,
    string TurnId,
    bool ToolObserved);

internal sealed record PreparedLunaThread(
    LunaCorrectionProfile Profile,
    string ThreadId,
    JsonRpcLineConnection Connection,
    int Generation);
