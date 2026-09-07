using System.Diagnostics;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using Xunit.Abstractions;

namespace CodexLanguageFix.Tests;

public sealed class LiveFormatRegressionTests(ITestOutputHelper output)
{
    private sealed record FormatCase(string Id, string Original, string Expected);

    private static readonly FormatCase[] Cases =
    [
        new("lf-paragraphs", "Das ist ein fehler.\n\nDas ist ein Satz.", "Das ist ein Fehler.\n\nDas ist ein Satz."),
        new("crlf-paragraphs", "Das ist ein fehler.\r\n\r\nDas ist ein Satz.\r\n", "Das ist ein Fehler.\r\n\r\nDas ist ein Satz.\r\n"),
        new("lf-bullets", "- This is an test.\n- This is a sentence.", "- This is a test.\n- This is a sentence."),
        new("crlf-nested", "- Das ist ein fehler.\r\n  - Das ist ein Satz.\r\n", "- Das ist ein Fehler.\r\n  - Das ist ein Satz.\r\n"),
        new("numbered", "1. This is an test.\n2. This is a sentence.", "1. This is a test.\n2. This is a sentence."),
        new("checkboxes", "- [ ] Das ist ein fehler.\n- [x] Das ist ein Satz.", "- [ ] Das ist ein Fehler.\n- [x] Das ist ein Satz."),
        new("markdown-break", "## Test\n\nDas ist ein fehler.  \nDas ist ein Satz.", "## Test\n\nDas ist ein Fehler.  \nDas ist ein Satz."),
        new("technical", "Das ist ein fehler.\n\n`var eror = 1;`\nhttps://example.com/tset\nC:\\Temp\\tset.txt\n--tset", "Das ist ein Fehler.\n\n`var eror = 1;`\nhttps://example.com/tset\nC:\\Temp\\tset.txt\n--tset"),
        new("control-unicode", "Grüße aus Köln. 😊\n\nDie Tür ist geöffnet.", "Grüße aus Köln. 😊\n\nDie Tür ist geöffnet."),
        new("control-quote", "> This is a test.\n> This is a sentence.\n", "> This is a test.\n> This is a sentence.\n"),
        new("control-code", "Das ist ein Test.\n\n```text\neror\n  tset\n```\n", "Das ist ein Test.\n\n```text\neror\n  tset\n```\n"),
        new("control-crlf-list", "1. Das ist ein Test.\r\n2. Das ist ein Satz.\r\n\r\n", "1. Das ist ein Test.\r\n2. Das ist ein Satz.\r\n\r\n")
    ];

    [Fact]
    public async Task RealProviders_PreserveTwelveRepresentativeFormats()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_FORMAT_LIVE_TEST") != "1")
            return;

        var localizer = new AppLocalizer("de");
        using var lunaClient = new CodexAppServerClient(
            LunaLiveRuntime.CreateDirectory(), localizer);
        string? lunaUnavailable = null;
        try
        {
            if (!(await lunaClient.GetAccountAsync(CancellationToken.None)).IsChatGpt)
                lunaUnavailable = "ChatGPT-OAuth unavailable";
            else if (!await lunaClient.SupportsLunaAsync(CancellationToken.None))
                lunaUnavailable = "production Luna/none/priority capability unavailable";
        }
        catch (Exception exception)
        {
            lunaUnavailable = exception.GetType().Name;
        }
        using var languageToolClient = new LanguageToolClient(localizer: localizer);
        var engine = new CorrectionEngine();
        ICorrectionProvider[] providers =
        [
            new LanguageToolCorrectionProvider(languageToolClient, engine),
            new LunaCorrectionProvider(lunaClient, localizer, cacheEnabled: false)
        ];
        var failures = new List<string>();
        var sinceLanguageToolStart = Stopwatch.StartNew();
        var hasStartedLanguageTool = false;
        foreach (var testCase in Cases)
        {
            // Keep public LanguageTool starts at least 3.1 seconds apart (under 20/minute).
            if (hasStartedLanguageTool)
            {
                var remaining = TimeSpan.FromSeconds(3.1) - sinceLanguageToolStart.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);
            }
            sinceLanguageToolStart.Restart();
            hasStartedLanguageTool = true;
            var observations = await Task.WhenAll(providers.Select(async provider =>
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    if (provider.Kind == CorrectionProviderKind.Luna && lunaUnavailable is not null)
                        return (provider.Kind, timer.Elapsed, Failure: $"Luna/{testCase.Id}: not executed ({lunaUnavailable})");
                    var result = await provider.CorrectAsync(testCase.Original, CancellationToken.None);
                    var structure = TextStructure.IsPreserved(testCase.Original, result.CorrectedText);
                    var accepted = string.Equals(testCase.Expected, result.CorrectedText, StringComparison.Ordinal);
                    // Exact outputs include every protected span, multiplicity, order and control character.
                    return (provider.Kind, timer.Elapsed, Failure: structure && accepted ? null
                        : $"{provider.Kind}/{testCase.Id}: structure={structure}, accepted={accepted}");
                }
                catch (Exception exception)
                {
                    return (provider.Kind, timer.Elapsed,
                        Failure: $"{provider.Kind}/{testCase.Id}: {exception.GetType().Name}");
                }
            }));
            foreach (var observation in observations)
            {
                output.WriteLine($"{observation.Kind}/{testCase.Id}: {observation.Elapsed.TotalMilliseconds:F0} ms, passed={observation.Failure is null}");
                if (observation.Failure is not null)
                    failures.Add(observation.Failure);
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
