using System.Net;
using System.Text;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LanguageToolClientTests
{
    [Fact]
    public async Task CheckAsync_SendsExpectedSettingsAndParsesResponse()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            const string json = """
                {"matches":[{"offset":8,"length":6,"replacements":[{"value":"korrekt"}],"rule":{"id":"SPELL","issueType":"misspelling","confidence":0.81,"category":{"id":"TYPOS"}}}]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new LanguageToolClient(http);
        var prompt = new CorrectionEngine().Annotate("Das ist korekt.");

        var result = await client.CheckAsync(prompt, CancellationToken.None);

        Assert.NotNull(requestBody);
        Assert.Contains("language=auto", requestBody);
        Assert.Contains("preferredVariants=de-DE%2Cen-US", requestBody);
        Assert.Contains("motherTongue=de-DE", requestBody);
        Assert.Contains("level=picky", requestBody);
        var match = Assert.Single(result.Matches);
        Assert.Equal("korrekt", Assert.Single(match.Replacements));
        Assert.Equal(0.81, match.Confidence);
    }

    [Fact]
    public async Task CheckAsync_RejectsOversizedPromptBeforeNetworkCall()
    {
        var called = false;
        var handler = new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new LanguageToolClient(new HttpClient(handler), localizer: new AppLocalizer("de"));
        var prompt = new CorrectionEngine().Annotate(new string('a', 20_001));

        await Assert.ThrowsAsync<LanguageFixException>(() => client.CheckAsync(prompt, CancellationToken.None));

        Assert.False(called);
    }

    [Fact]
    public async Task CheckAsync_RejectsInvalidJson()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        }));
        using var client = new LanguageToolClient(new HttpClient(handler), localizer: new AppLocalizer("de"));

        var exception = await Assert.ThrowsAsync<LanguageFixException>(() =>
            client.CheckAsync(new CorrectionEngine().Annotate("Test"), CancellationToken.None));

        Assert.Contains("ungültige Antwort", exception.Message);
    }

    [Fact]
    public async Task CheckAsync_MapsHttp429ToRateLimit()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var client = new LanguageToolClient(new HttpClient(handler));

        await Assert.ThrowsAsync<RateLimitException>(() =>
            client.CheckAsync(new CorrectionEngine().Annotate("Test"), CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responseFactory(request);
    }
}
