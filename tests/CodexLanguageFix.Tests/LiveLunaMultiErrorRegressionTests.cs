using System.Diagnostics;
using System.Text.RegularExpressions;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using Xunit.Abstractions;

namespace CodexLanguageFix.Tests;

public sealed class LiveLunaMultiErrorRegressionTests(ITestOutputHelper output)
{
    private sealed record CorrectionCase(string Id, string Original, string[] CorrectWords, string[] IncorrectWords);

    private static readonly CorrectionCase[] Cases =
    [
        new("edge-whitespace", "  Das ist ein fehler. Die korrektur ist korekt.  ",
            ["Fehler", "Korrektur", "korrekt"], ["fehler", "korrektur", "korekt"]),
        new("multiple-bullets", "- Das ist ein fehler.\n- Die korrektur ist korekt.\n- Der test ist einfach.",
            ["Fehler", "Korrektur", "korrekt", "Test"], ["fehler", "korrektur", "korekt", "test"]),
        new("url-and-cli", "Das ist ein fehler. Die korrektur ist korekt. Nutze https://example.com/tset und --output=tset.txt unverändert.",
            ["Fehler", "Korrektur", "korrekt"], ["fehler", "korrektur", "korekt"]),
        new("crlf-paragraphs", "Die korrektur ist korekt.\r\n\r\nDas ist ein fehler. Grüße aus Köln. 🙂\r\n",
            ["Fehler", "Korrektur", "korrekt"], ["fehler", "korrektur", "korekt"])
    ];

    [Fact]
    public async Task RealLuna_CorrectsMultipleErrorsAndPreservesOriginalLayout()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LUNA_MULTI_ERROR_LIVE_TEST") != "1")
            return;

        var localizer = new AppLocalizer("de");
        using var client = new CodexAppServerClient(LunaLiveRuntime.CreateDirectory(), localizer);
        Assert.True((await client.GetAccountAsync(CancellationToken.None)).IsChatGpt,
            "Für diese vier Live-Fälle ist das vorhandene ChatGPT-OAuth-Konto erforderlich.");
        var selection = await client.ResolveLunaProfileAsync(CancellationToken.None);
        Assert.Equal("low", selection.Effort);
        Assert.Equal("priority", selection.ServiceTier);
        var provider = new LunaCorrectionProvider(client, localizer, cacheEnabled: false);
        using var concurrency = new SemaphoreSlim(2);
        var observations = await Task.WhenAll(Cases.Select(RunCaseAsync));
        foreach (var observation in observations)
            output.WriteLine($"{observation.Id}: {observation.ElapsedMs} ms; {observation.Detail}");
        Assert.True(observations.All(observation => observation.Passed),
            string.Join(Environment.NewLine, observations.Where(observation => !observation.Passed)
                .Select(observation => $"{observation.Id}: {observation.Detail}")));

        async Task<(string Id, long ElapsedMs, bool Passed, string Detail)> RunCaseAsync(CorrectionCase testCase)
        {
            await concurrency.WaitAsync();
            var timer = Stopwatch.StartNew();
            try
            {
                var result = await provider.CorrectAsync(testCase.Original, CancellationToken.None);
                var structure = TextStructure.IsPreserved(testCase.Original, result.CorrectedText);
                var protectedOriginal = ProtectedSpanDetector.Detect(testCase.Original)
                    .Select(span => testCase.Original.Substring(span.Start, span.Length));
                var protectedCorrected = ProtectedSpanDetector.Detect(result.CorrectedText)
                    .Select(span => result.CorrectedText.Substring(span.Start, span.Length));
                var protection = protectedOriginal.SequenceEqual(protectedCorrected, StringComparer.Ordinal);
                var corrections = testCase.CorrectWords.All(word => ContainsWord(result.CorrectedText, word))
                    && !testCase.IncorrectWords.Any(word => ContainsWord(result.CorrectedText, word));
                var profile = result.LunaExecution is { } execution
                    && execution.PromptProfile == LunaProductionConfiguration.Qualified.PromptVariant
                    && execution.Model == selection.Model
                    && execution.Effort == "low"
                    && execution.ServiceTier == "priority"
                    && execution.Protocol == LunaProductionConfiguration.Qualified.ProtocolName;
                var changed = result.ChangeCount > 0;
                return (testCase.Id, timer.ElapsedMilliseconds, structure && protection && corrections && profile && changed,
                    $"structure={structure}, protection={protection}, corrections={corrections}, profile={profile}, changed={changed}");
            }
            catch (Exception exception)
            {
                return (testCase.Id, timer.ElapsedMilliseconds, false, $"error={exception.GetType().Name}");
            }
            finally
            {
                concurrency.Release();
            }
        }
    }

    private static bool ContainsWord(string text, string word) => Regex.IsMatch(text,
        @"\b" + Regex.Escape(word) + @"\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
