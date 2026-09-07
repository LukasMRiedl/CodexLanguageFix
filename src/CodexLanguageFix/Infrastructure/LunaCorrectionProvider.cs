using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

public sealed class LunaCorrectionProvider : ICorrectionProvider, IOpenAiConnection
{
    public const int MaximumPromptLength = 20_000;
    internal const int DefaultPatchThreshold = 1_000;
    internal static IReadOnlyList<int> CandidatePatchThresholds { get; } = [500, 1_000, 2_000];

    private readonly ICodexAppServerClient client;
    private readonly AppLocalizer localizer;
    private readonly LunaCorrectionCache? _cache;
    private readonly int _patchThreshold;
    private readonly LunaOutputProtocol _outputProtocol;
    private readonly bool _fallbackEnabled;
    private readonly bool _usePreparedThreads;

    public LunaCorrectionProvider(
        ICodexAppServerClient client,
        AppLocalizer localizer,
        bool cacheEnabled = true,
        int patchThreshold = int.MaxValue)
        : this(
            client,
            localizer,
            cacheEnabled,
            patchThreshold,
            patchThreshold == int.MaxValue ? LunaOutputProtocol.FullText : LunaOutputProtocol.Segments,
            fallbackEnabled: true,
            usePreparedThreads: true)
    {
    }

    internal LunaCorrectionProvider(
        ICodexAppServerClient client,
        AppLocalizer localizer,
        bool cacheEnabled,
        int patchThreshold,
        LunaOutputProtocol outputProtocol,
        bool fallbackEnabled = true,
        bool usePreparedThreads = true)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _cache = cacheEnabled ? new LunaCorrectionCache() : null;
        _patchThreshold = patchThreshold >= 0
            ? patchThreshold
            : throw new ArgumentOutOfRangeException(nameof(patchThreshold));
        _outputProtocol = outputProtocol;
        _fallbackEnabled = fallbackEnabled;
        _usePreparedThreads = usePreparedThreads;
    }

    public CorrectionProviderKind Kind => CorrectionProviderKind.Luna;

    public async Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return new CorrectionProviderResult(text, 0, Kind, null, TimeSpan.Zero);
        }

        if (text.Length > MaximumPromptLength)
        {
            throw new LanguageFixException(localizer.Get(AppText.PromptTooLongGeneric, text.Length, MaximumPromptLength));
        }

        var cacheKey = LunaCorrectionCache.CreateKey(
            text,
            $"{CodexAppServerClient.LunaModel}\0{CodexAppServerClient.LunaEffort}\0adaptive-v2-{_outputProtocol}-{_patchThreshold}\0{DeveloperPrompt}");
        if (_cache?.TryGet(cacheKey, out var cached) == true)
        {
            return new CorrectionProviderResult(
                cached.CorrectedText,
                cached.ChangeCount,
                Kind,
                null,
                TimeSpan.Zero)
            {
                LunaExecution = CreateExecution(
                    cached.CorrectedText,
                    cached.ChangeCount,
                    fallbackUsed: false,
                    LunaCorrectionTimings.Empty)
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var protectedText = LunaProtectedText.Create(text);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        string restored;
        int changeCount;
        var fallbackUsed = false;
        var validation = TimeSpan.Zero;
        var restoration = TimeSpan.Zero;
        LunaProtocolTiming? transportTiming = null;
        try
        {
            if (_outputProtocol is not LunaOutputProtocol.FullText
                && text.Length >= _patchThreshold
                && client is IStructuredCodexCorrectionClient structuredClient)
            {
                var structuredAttempt = _outputProtocol == LunaOutputProtocol.SpanEdits
                    ? await TryCorrectWithSpanEditsAsync(
                        structuredClient,
                        protectedText,
                        text.Length,
                        timeout.Token).ConfigureAwait(false)
                    : await TryCorrectWithSegmentsAsync(
                        structuredClient,
                        protectedText,
                        text.Length,
                        timeout.Token).ConfigureAwait(false);
                if (structuredAttempt.Correction is { } structuredCorrection)
                {
                    restored = structuredCorrection.Restored;
                    changeCount = structuredCorrection.ChangeCount;
                    validation = structuredCorrection.Validation;
                    restoration = structuredCorrection.Restoration;
                    transportTiming = structuredAttempt.TransportTiming;
                }
                else
                {
                    if (!_fallbackEnabled)
                    {
                        throw new LanguageFixException(localizer.Get(AppText.LunaInvalidResponse));
                    }

                    fallbackUsed = true;
                    var full = await CorrectFullTextAsync(protectedText, text, timeout.Token).ConfigureAwait(false);
                    restored = full.Restored;
                    changeCount = full.ChangeCount;
                    validation = full.Validation;
                    restoration = full.Restoration;
                    transportTiming = CombineTransportTimings(structuredAttempt.TransportTiming, full.TransportTiming);
                }
            }
            else
            {
                var full = await CorrectFullTextAsync(protectedText, text, timeout.Token).ConfigureAwait(false);
                restored = full.Restored;
                changeCount = full.ChangeCount;
                validation = full.Validation;
                restoration = full.Restoration;
                transportTiming = full.TransportTiming;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageFixException(localizer.Get(AppText.LunaTimeout));
        }
        var structureValidation = Stopwatch.StartNew();
        if (!TextStructure.IsPreserved(text, restored))
        {
            throw new LanguageFixException(localizer.Get(AppText.LunaInvalidResponse));
        }

        structureValidation.Stop();
        validation += structureValidation.Elapsed;
        stopwatch.Stop();
        _cache?.Set(cacheKey, new LunaCachedCorrection(restored, changeCount));
        var timings = CreateTransportIndependentTimings(transportTiming, validation, restoration);
        return new CorrectionProviderResult(restored, changeCount, Kind, null, stopwatch.Elapsed)
        {
            LunaExecution = CreateExecution(restored, changeCount, fallbackUsed, timings)
        };
    }

    private async Task<CorrectionPayload> CorrectFullTextAsync(
        LunaProtectedText protectedText,
        string original,
        CancellationToken cancellationToken)
    {
        string raw;
        LunaProtocolTiming? transportTiming = null;
        if (client is ILunaTimedTransportClient timedClient)
        {
            var inputJson = JsonSerializer.Serialize(new { source_text = protectedText.Text });
            var execution = await timedClient.RunStructuredCorrectionWithTimingAsync(
                inputJson,
                OutputSchema,
                DeveloperPrompt,
                CodexAppServerClient.LunaEffort,
                cancellationToken,
                replenishPreparedThread: _usePreparedThreads).ConfigureAwait(false);
            raw = execution.Response;
            transportTiming = execution.Timing;
        }
        else
        {
            raw = await client.RunCorrectionAsync(protectedText.Text, cancellationToken).ConfigureAwait(false);
        }
        LunaResponse response;
        var validationStopwatch = Stopwatch.StartNew();
        try
        {
            response = JsonSerializer.Deserialize<LunaResponse>(raw, SerializerOptions)
                ?? throw new JsonException("Leere Antwort");
        }
        catch (JsonException exception)
        {
            throw new LanguageFixException(localizer.Get(AppText.LunaInvalidResponse), exception);
        }

        if (response.CorrectedText is null
            || response.CorrectedText.Length > Math.Max(1_000, original.Length * 2 + 500))
        {
            throw new LanguageFixException(localizer.Get(AppText.LunaInvalidResponse));
        }

        validationStopwatch.Stop();
        var restorationStopwatch = Stopwatch.StartNew();
        if (!protectedText.TryRestore(response.CorrectedText, out var restored)
            || HasUnexpectedControlCharacters(restored))
        {
            throw new LanguageFixException(localizer.Get(AppText.LunaInvalidResponse));
        }

        restorationStopwatch.Stop();
        return new CorrectionPayload(
            restored,
            string.Equals(restored, original, StringComparison.Ordinal) ? 0 : 1,
            validationStopwatch.Elapsed,
            restorationStopwatch.Elapsed,
            transportTiming);
    }

    private async Task<PatchAttempt> TryCorrectWithSegmentsAsync(
        IStructuredCodexCorrectionClient structuredClient,
        LunaProtectedText protectedText,
        int originalLength,
        CancellationToken cancellationToken)
    {
        var document = LunaSegmentedDocument.Create(protectedText.Text);
        return await TryCorrectWithSegmentsAsync(
            structuredClient,
            document,
            protectedText,
            originalLength,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PatchAttempt> TryCorrectWithSegmentsAsync(
        IStructuredCodexCorrectionClient structuredClient,
        LunaSegmentedDocument document,
        LunaProtectedText protectedText,
        int originalLength,
        CancellationToken cancellationToken)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            segments = document.Segments.Select(segment => new { id = segment.Id, text = segment.Text })
        });
        var (raw, transportTiming) = await RunStructuredAsync(
            structuredClient,
            inputJson,
            PatchOutputSchema,
            DeveloperPrompt + LunaPromptCatalog.PatchProtocol,
            cancellationToken).ConfigureAwait(false);
        try
        {
            var response = JsonSerializer.Deserialize<LunaPatchResponse>(raw, SerializerOptions);
            if (response?.Changes is null)
            {
                return new PatchAttempt(null, transportTiming);
            }

            var validationStopwatch = Stopwatch.StartNew();
            if (!document.TryApply(response.Changes, out var correctedProtectedText, out var changeCount)
                || correctedProtectedText.Length > Math.Max(1_000, protectedText.Text.Length * 2 + 500))
            {
                return new PatchAttempt(null, transportTiming);
            }

            validationStopwatch.Stop();
            var restorationStopwatch = Stopwatch.StartNew();
            if (!protectedText.TryRestore(correctedProtectedText, out var restored)
                || restored.Length > Math.Max(1_000, originalLength * 2 + 500)
                || HasUnexpectedControlCharacters(restored))
            {
                return new PatchAttempt(null, transportTiming);
            }

            restorationStopwatch.Stop();
            return new PatchAttempt(
                new PatchCorrection(restored, changeCount, validationStopwatch.Elapsed, restorationStopwatch.Elapsed),
                transportTiming);
        }
        catch (JsonException)
        {
            return new PatchAttempt(null, transportTiming);
        }
    }

    private async Task<PatchAttempt> TryCorrectWithSpanEditsAsync(
        IStructuredCodexCorrectionClient structuredClient,
        LunaProtectedText protectedText,
        int originalLength,
        CancellationToken cancellationToken)
    {
        var inputJson = JsonSerializer.Serialize(new { source_text = protectedText.Text });
        var (raw, transportTiming) = await RunStructuredAsync(
            structuredClient,
            inputJson,
            SpanEditOutputSchema,
            DeveloperPrompt + LunaPromptCatalog.SpanEditProtocol,
            cancellationToken).ConfigureAwait(false);
        try
        {
            var response = JsonSerializer.Deserialize<LunaSpanEditResponse>(raw, SerializerOptions);
            if (response?.Edits is null)
            {
                return new PatchAttempt(null, transportTiming);
            }

            var validationStopwatch = Stopwatch.StartNew();
            if (!LunaSpanEditDocument.TryApply(
                    protectedText.Text,
                    response.Edits,
                    out var correctedProtectedText,
                    out var changeCount)
                || correctedProtectedText.Length > Math.Max(1_000, protectedText.Text.Length * 2 + 500))
            {
                return new PatchAttempt(null, transportTiming);
            }

            validationStopwatch.Stop();
            var restorationStopwatch = Stopwatch.StartNew();
            if (!protectedText.TryRestore(correctedProtectedText, out var restored)
                || restored.Length > Math.Max(1_000, originalLength * 2 + 500)
                || HasUnexpectedControlCharacters(restored))
            {
                return new PatchAttempt(null, transportTiming);
            }

            restorationStopwatch.Stop();
            return new PatchAttempt(
                new PatchCorrection(restored, changeCount, validationStopwatch.Elapsed, restorationStopwatch.Elapsed),
                transportTiming);
        }
        catch (JsonException)
        {
            return new PatchAttempt(null, transportTiming);
        }
    }

    private async Task<(string Raw, LunaProtocolTiming? Timing)> RunStructuredAsync(
        IStructuredCodexCorrectionClient structuredClient,
        string inputJson,
        JsonElement outputSchema,
        string developerInstructions,
        CancellationToken cancellationToken)
    {
        if (client is ILunaTimedTransportClient timedClient)
        {
            var execution = await timedClient.RunStructuredCorrectionWithTimingAsync(
                inputJson,
                outputSchema,
                developerInstructions,
                CodexAppServerClient.LunaEffort,
                cancellationToken,
                replenishPreparedThread: _usePreparedThreads).ConfigureAwait(false);
            return (execution.Response, execution.Timing);
        }

        var raw = await structuredClient.RunStructuredCorrectionAsync(
            inputJson,
            outputSchema,
            developerInstructions,
            CodexAppServerClient.LunaEffort,
            cancellationToken).ConfigureAwait(false);
        return (raw, null);
    }

    private static bool HasUnexpectedControlCharacters(string text) =>
        text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));

    private LunaCorrectionTimings CreateTransportIndependentTimings(
        LunaProtocolTiming? transport,
        TimeSpan validation,
        TimeSpan restoration)
    {
        if (transport is null)
        {
            return new LunaCorrectionTimings(
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                null,
                TimeSpan.Zero,
                validation,
                restoration,
                TimeSpan.Zero,
                TimeSpan.Zero);
        }

        return new LunaCorrectionTimings(
            transport.EnsureServer + transport.CapabilityValidation,
            transport.ThreadStart,
            transport.TurnStart,
            transport.TimeToFirstTextDelta,
            transport.ModelCompletion,
            validation,
            restoration,
            TimeSpan.Zero,
            transport.CleanupEnqueue);
    }

    private static LunaProtocolTiming? CombineTransportTimings(
        LunaProtocolTiming? first,
        LunaProtocolTiming? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return new LunaProtocolTiming(
            first.EnsureServer + second.EnsureServer,
            first.CapabilityValidation + second.CapabilityValidation,
            first.ThreadStart + second.ThreadStart,
            first.TurnStart + second.TurnStart,
            first.CompletionWait + second.CompletionWait,
            first.CleanupEnqueue + second.CleanupEnqueue)
        {
            TimeToFirstTextDelta = first.TimeToFirstTextDelta ?? second.TimeToFirstTextDelta,
            ModelCompletion = first.ModelCompletion + second.ModelCompletion
        };
    }

    private LunaCorrectionExecution CreateExecution(
        string correctedText,
        int changeCount,
        bool fallbackUsed,
        LunaCorrectionTimings timings)
    {
        var protocolName = _outputProtocol switch
        {
            LunaOutputProtocol.FullText => LunaProductionConfiguration.Qualified.ProtocolName,
            LunaOutputProtocol.Segments => $"segments-{_patchThreshold}",
            LunaOutputProtocol.SpanEdits => $"span-edits-{_patchThreshold}",
            _ => _outputProtocol.ToString()
        };
        var developerInstructions = _outputProtocol switch
        {
            LunaOutputProtocol.Segments => DeveloperPrompt + LunaPromptCatalog.PatchProtocol,
            LunaOutputProtocol.SpanEdits => DeveloperPrompt + LunaPromptCatalog.SpanEditProtocol,
            _ => DeveloperPrompt
        };
        var promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(developerInstructions)));
        return new LunaCorrectionExecution(
            correctedText,
            changeCount,
            LunaProductionConfiguration.Qualified.PromptVariant,
            promptHash,
            CodexAppServerClient.LunaEffort,
            CodexAppServerClient.LunaServiceTier,
            protocolName,
            fallbackUsed,
            correctedText.Length,
            timings);
    }

    public async Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var account = await client.GetAccountAsync(cancellationToken).ConfigureAwait(false);
        if (!account.IsChatGpt)
        {
            throw new CodexAppServerException(localizer.Get(AppText.OpenAiLoginRequired));
        }

        if (!await client.SupportsLunaAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CodexAppServerException(localizer.Get(AppText.LunaUnavailable));
        }

        stopwatch.Stop();
        return new CorrectionProviderHealth(Kind, stopwatch.Elapsed);
    }

    public Task ConnectAsync(CancellationToken cancellationToken) => client.ConnectChatGptAsync(cancellationToken);

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        client is CodexAppServerClient appServer
            ? appServer.WarmUpAsync(cancellationToken)
            : Task.CompletedTask;

    internal static string DeveloperPrompt => LunaProductionConfiguration.Qualified.DeveloperInstructions;

    internal static JsonElement OutputSchema { get; } = CreateOutputSchema();
    internal static JsonElement PatchOutputSchema { get; } = CreatePatchOutputSchema();
    internal static JsonElement SpanEditOutputSchema { get; } = CreateSpanEditOutputSchema();

    private static JsonElement CreateOutputSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["corrected_text"],
              "properties": {
                "corrected_text": { "type": "string" }
              }
            }
            """);
        return document.RootElement.Clone();
    }

    private static JsonElement CreatePatchOutputSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["changes"],
              "properties": {
                "changes": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["id", "text"],
                    "properties": {
                      "id": { "type": "integer", "minimum": 0 },
                      "text": { "type": "string" }
                    }
                  }
                }
              }
            }
            """);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateSpanEditOutputSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["edits"],
              "properties": {
                "edits": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["start", "length", "text"],
                    "properties": {
                      "start": { "type": "integer", "minimum": 0 },
                      "length": { "type": "integer", "minimum": 0 },
                      "text": { "type": "string" }
                    }
                  }
                }
              }
            }
            """);
        return document.RootElement.Clone();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private sealed class LunaResponse
    {
        [JsonPropertyName("corrected_text")]
        public string? CorrectedText { get; init; }
    }

    private sealed class LunaPatchResponse
    {
        [JsonPropertyName("changes")]
        public LunaSegmentChange[]? Changes { get; init; }
    }

    private sealed class LunaSpanEditResponse
    {
        [JsonPropertyName("edits")]
        public LunaSpanEdit[]? Edits { get; init; }
    }

    private sealed record CorrectionPayload(
        string Restored,
        int ChangeCount,
        TimeSpan Validation,
        TimeSpan Restoration,
        LunaProtocolTiming? TransportTiming);

    private sealed record PatchCorrection(
        string Restored,
        int ChangeCount,
        TimeSpan Validation,
        TimeSpan Restoration);

    private sealed record PatchAttempt(PatchCorrection? Correction, LunaProtocolTiming? TransportTiming);
}
