using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LiveLanguageToolClientTests
{
    [Fact]
    public async Task CheckAsync_LiveEndpointCorrectsProseButPreservesInlineCode()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LIVE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        const string original = "Nutze `eror` und schreibe korekt.";
        var engine = new CorrectionEngine();
        var annotated = engine.Annotate(original);
        using var client = new LanguageToolClient();

        var check = await client.CheckAsync(annotated, CancellationToken.None);
        var outcome = engine.Apply(original, annotated, check.Matches);

        Assert.Contains("`eror`", outcome.CorrectedText, StringComparison.Ordinal);
        Assert.Contains("korrekt", outcome.CorrectedText, StringComparison.Ordinal);
        Assert.DoesNotContain("`error`", outcome.CorrectedText, StringComparison.Ordinal);
    }
}
