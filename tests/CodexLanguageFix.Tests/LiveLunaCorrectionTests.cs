using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LiveLunaCorrectionTests
{
    [Fact]
    public async Task LunaFast_LiveOAuthCorrectsGermanAndPreservesTechnicalText()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LUNA_LIVE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "CodexLanguageFix-Luna-Live");
        var localizer = new AppLocalizer("de");
        using var client = new CodexAppServerClient(directory, localizer);
        var account = await client.GetAccountAsync(CancellationToken.None);
        Assert.True(account.IsChatGpt, "Für den Live-Test muss Codex mit ChatGPT-OAuth angemeldet sein.");
        Assert.True(await client.SupportsLunaAsync(CancellationToken.None), "Luna ohne Denkmodus und mit Fast Mode muss verfügbar sein.");

        var provider = new LunaCorrectionProvider(client, localizer);
        var result = await provider.CorrectAsync(
            "Nutze `var eror = 1;` und schreibe diesen satz korekt.",
            CancellationToken.None);

        Assert.Contains("`var eror = 1;`", result.CorrectedText, StringComparison.Ordinal);
        Assert.Contains("Satz", result.CorrectedText, StringComparison.Ordinal);
        Assert.Contains("korrekt", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.ChangeCount >= 1);
    }
}
