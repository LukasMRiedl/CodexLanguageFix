using System.Diagnostics;
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
    public async Task Codex_PreservesTwelveRepresentativeFormats()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_FORMAT_LIVE_TEST") != "1")
            return;

        var localizer = new AppLocalizer("de");
        using var client = new CodexAppServerClient(LunaLiveRuntime.CreateDirectory(), localizer);
        Assert.True((await client.GetAccountAsync(CancellationToken.None)).IsChatGpt,
            "Für den Codex-Live-Test ist eine ChatGPT-Anmeldung erforderlich.");
        var selection = await client.ResolveLunaProfileAsync(CancellationToken.None);
        Assert.Equal("low", selection.Effort);
        Assert.Equal("priority", selection.ServiceTier);
        var provider = new LunaCorrectionProvider(client, localizer, cacheEnabled: false);
        var failures = new List<string>();
        foreach (var testCase in Cases)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var result = await provider.CorrectAsync(testCase.Original, CancellationToken.None);
                var structure = TextStructure.IsPreserved(testCase.Original, result.CorrectedText);
                var accepted = string.Equals(testCase.Expected, result.CorrectedText, StringComparison.Ordinal);
                var profileMatches = result.LunaExecution is { } execution
                    && execution.Model == selection.Model
                    && execution.Effort == "low"
                    && execution.ServiceTier == "priority";
                if (!structure || !accepted || !profileMatches)
                    failures.Add($"Codex/{testCase.Id}: structure={structure}, accepted={accepted}, profile={profileMatches}");
                output.WriteLine($"Codex/{testCase.Id}: {timer.Elapsed.TotalMilliseconds:F0} ms, passed={structure && accepted && profileMatches}");
            }
            catch (Exception exception)
            {
                // Fixtures contain only synthetic text; never record provider responses.
                output.WriteLine($"Codex/{testCase.Id}: {exception.GetType().Name}");
                failures.Add($"Codex/{testCase.Id}: {exception.GetType().Name}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
