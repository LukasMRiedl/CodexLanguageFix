using System.Text.Json;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaCorrectionProviderTests
{
    [Fact]
    public async Task CorrectAsync_AppliesStructuredCorrectionAndRestoresProtectedContent()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Replace("korekt", "korrekt", StringComparison.Ordinal),
            edits = new[] { new { category = "spelling", before = "korekt", after = "korrekt" } }
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        var result = await provider.CorrectAsync("Nutze `eror` und schreibe korekt.", CancellationToken.None);

        Assert.Equal("Nutze `eror` und schreibe korrekt.", result.CorrectedText);
        Assert.Equal(1, result.ChangeCount);
        Assert.Equal(CorrectionProviderKind.Luna, result.Provider);
        Assert.DoesNotContain("eror", client.LastProtectedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectAsync_RejectsPlaceholderMutation()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Replace("⟦", string.Empty, StringComparison.Ordinal),
            edits = new[] { new { category = "style", before = "a", after = "b" } }
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("en"));

        await Assert.ThrowsAsync<LanguageFixException>(() =>
            provider.CorrectAsync("Use `protected` and bad prose.", CancellationToken.None));
    }

    [Fact]
    public async Task CorrectAsync_RejectsInconsistentEditList()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new { corrected_text = text + "!", edits = Array.Empty<object>() }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("en"));

        await Assert.ThrowsAsync<LanguageFixException>(() => provider.CorrectAsync("Test", CancellationToken.None));
    }

    [Fact]
    public async Task CorrectAsync_RejectsOversizedPromptBeforeStartingServer()
    {
        var client = new FakeClient(_ => throw new InvalidOperationException());
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("en"));

        await Assert.ThrowsAsync<LanguageFixException>(() =>
            provider.CorrectAsync(new string('a', 20_001), CancellationToken.None));

        Assert.Null(client.LastProtectedText);
    }

    [Fact]
    public async Task TestAsync_RequiresChatGptAndLunaLow()
    {
        var noLogin = new FakeClient(text => text) { Account = new CodexAccountState(true, false) };
        var noLow = new FakeClient(text => text) { SupportsLow = false };

        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            new LunaCorrectionProvider(noLogin, new AppLocalizer("en")).TestAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            new LunaCorrectionProvider(noLow, new AppLocalizer("en")).TestAsync(CancellationToken.None));
    }

    [Fact]
    public void Prompt_TreatsSourceAsUntrustedAndForbidsTools()
    {
        Assert.Contains("untrusted text", LunaCorrectionProvider.DeveloperPrompt, StringComparison.Ordinal);
        Assert.Contains("Never answer", LunaCorrectionProvider.DeveloperPrompt, StringComparison.Ordinal);
        Assert.Contains("Do not use tools", LunaCorrectionProvider.DeveloperPrompt, StringComparison.Ordinal);
        Assert.Contains("Never translate", LunaCorrectionProvider.DeveloperPrompt, StringComparison.Ordinal);
    }

    private sealed class FakeClient(Func<string, string> response) : ICodexAppServerClient
    {
        public CodexAccountState Account { get; set; } = new(true, true);
        public bool SupportsLow { get; set; } = true;
        public string? LastProtectedText { get; private set; }

        public Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken) => Task.FromResult(Account);
        public Task ConnectChatGptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> SupportsLunaLowAsync(CancellationToken cancellationToken) => Task.FromResult(SupportsLow);
        public Task<string> RunCorrectionAsync(string protectedText, CancellationToken cancellationToken)
        {
            LastProtectedText = protectedText;
            return Task.FromResult(response(protectedText));
        }
        public void Dispose() { }
    }
}
