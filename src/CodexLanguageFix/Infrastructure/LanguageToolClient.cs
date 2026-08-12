using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

public sealed class LanguageToolClient : ILanguageToolClient, IDisposable
{
    public const int MaximumPromptLength = 20_000;
    public static readonly Uri Endpoint = new("https://api.languagetool.org/v2/check");

    private readonly HttpClient _httpClient;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly bool _ownsClient;
    private readonly AppLocalizer _localizer;

    public LanguageToolClient(
        HttpClient? httpClient = null,
        SlidingWindowRateLimiter? rateLimiter = null,
        AppLocalizer? localizer = null)
    {
        _localizer = localizer ?? new AppLocalizer();
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CodexLanguageFix/1.0 (+https://languagetool.org)");
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(localizer: _localizer);
    }

    public async Task<LanguageToolCheckResult> CheckAsync(AnnotatedPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Original.Length == 0)
        {
            return new LanguageToolCheckResult([], (int)HttpStatusCode.OK, TimeSpan.Zero);
        }

        if (prompt.Original.Length > MaximumPromptLength)
        {
            throw new LanguageFixException(_localizer.Get(AppText.PromptTooLong, prompt.Original.Length, MaximumPromptLength));
        }

        _rateLimiter.Reserve(prompt.Original.Length);
        var data = JsonSerializer.Serialize(new AnnotationDocument(prompt.Annotations), SerializerOptions);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["data"] = data,
            ["language"] = "auto",
            ["preferredVariants"] = "de-DE,en-US",
            ["motherTongue"] = "de-DE",
            ["level"] = "picky"
        });

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(Endpoint, form, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageFixException(_localizer.Get(AppText.LanguageToolTimeout));
        }
        catch (HttpRequestException exception)
        {
            throw new LanguageFixException(_localizer.Get(AppText.LanguageToolNetworkFailure), exception);
        }

        using (response)
        {
            stopwatch.Stop();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
                throw new RateLimitException(_localizer.Get(AppText.LanguageToolRateLimit), retryAfter);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new LanguageFixException(_localizer.Get(AppText.LanguageToolHttpFailure, (int)response.StatusCode));
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var payload = await JsonSerializer.DeserializeAsync<ApiResponse>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new JsonException("Leere Antwort");
                var matches = payload.Matches.Select((match, index) => new LanguageToolMatch(
                    match.Offset,
                    match.Length,
                    match.Replacements?.Select(replacement => replacement.Value).Where(value => value is not null).Cast<string>().ToArray() ?? [],
                    match.Rule?.Id ?? string.Empty,
                    match.Rule?.Category?.Id ?? string.Empty,
                    match.Rule?.IssueType ?? string.Empty,
                    match.Rule?.Confidence,
                    index)).ToArray();
                return new LanguageToolCheckResult(matches, (int)response.StatusCode, stopwatch.Elapsed);
            }
            catch (JsonException exception)
            {
                throw new LanguageFixException(_localizer.Get(AppText.LanguageToolInvalidResponse), exception);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record AnnotationDocument(
        [property: JsonPropertyName("annotation")] IReadOnlyList<PromptAnnotation> Annotation);

    private sealed class ApiResponse
    {
        [JsonPropertyName("matches")]
        public ApiMatch[] Matches { get; init; } = [];
    }

    private sealed class ApiMatch
    {
        [JsonPropertyName("offset")]
        public int Offset { get; init; }

        [JsonPropertyName("length")]
        public int Length { get; init; }

        [JsonPropertyName("replacements")]
        public ApiReplacement[]? Replacements { get; init; }

        [JsonPropertyName("rule")]
        public ApiRule? Rule { get; init; }
    }

    private sealed class ApiReplacement
    {
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    private sealed class ApiRule
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("issueType")]
        public string? IssueType { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }

        [JsonPropertyName("category")]
        public ApiCategory? Category { get; init; }
    }

    private sealed class ApiCategory
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
