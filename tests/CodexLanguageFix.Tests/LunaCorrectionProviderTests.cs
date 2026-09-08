using System.Text.Json;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaCorrectionProviderTests
{
    [Theory]
    [InlineData("full", false)]
    [InlineData("segments", false)]
    [InlineData("spans", false)]
    [InlineData("full", true)]
    [InlineData("segments", true)]
    [InlineData("spans", true)]
    public async Task CorrectAsync_RestoresLayoutBeforeCountingAndCachingForEveryProtocol(string protocol, bool whitespaceOnly)
    {
        const string original = "  korekt  ";
        var output = whitespaceOnly ? "korekt" : "korrekt";
        var client = new FakeClient(_ => JsonSerializer.Serialize(new { corrected_text = output }))
        {
            StructuredResponse = _ => protocol == "spans"
                ? JsonSerializer.Serialize(new { edits = new[] { new { start = 0, length = original.Length, text = output } } })
                : JsonSerializer.Serialize(new { changes = new[] { new { id = 0, text = output } } })
        };
        var outputProtocol = protocol switch
        {
            "segments" => LunaOutputProtocol.Segments,
            "spans" => LunaOutputProtocol.SpanEdits,
            _ => LunaOutputProtocol.FullText
        };
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"), true, 0,
            outputProtocol, fallbackEnabled: false);

        var first = await provider.CorrectAsync(original, CancellationToken.None);
        var cached = await provider.CorrectAsync(original, CancellationToken.None);

        Assert.Equal("  " + output + "  ", first.CorrectedText);
        Assert.Equal(whitespaceOnly ? 0 : 1, first.ChangeCount);
        Assert.Equal(first.CorrectedText, cached.CorrectedText);
        Assert.Equal(first.ChangeCount, cached.ChangeCount);
        Assert.Equal(first.ChangeCount, first.LunaExecution!.ChangeCount);
        Assert.Equal(1, client.RunCount + client.StructuredRunCount);
        Assert.Equal(protocol == "full" ? 1 : 0, client.RunCount);
    }

    [Fact]
    public async Task CorrectAsync_RestoresParagraphWhitespaceAndLineEndingsLocally()
    {
        var client = new FakeClient(_ => JsonSerializer.Serialize(new
        {
            corrected_text = "- korrekt\n- Zweiter"
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        var result = await provider.CorrectAsync("  - korekt  \r\n \t\r\n- Zweiter\r\n", CancellationToken.None);

        Assert.Equal("  - korrekt  \r\n \t\r\n- Zweiter\r\n", result.CorrectedText);
        Assert.Equal(1, result.ChangeCount);
    }

    [Fact]
    public async Task CorrectAsync_LayoutRecoveryCannotRestoreInvalidControlCharacters()
    {
        var client = new FakeClient(_ => JsonSerializer.Serialize(new { corrected_text = "Text" }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        await Assert.ThrowsAsync<LanguageFixException>(() => provider.CorrectAsync("\vText", CancellationToken.None));
    }

    [Fact]
    public async Task CorrectAsync_LayoutRecoveryCannotHidePlaceholderMutation()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Trim().Replace("⟦", string.Empty, StringComparison.Ordinal)
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        await Assert.ThrowsAsync<LanguageFixException>(() =>
            provider.CorrectAsync("  Use `protected` and bad prose.  ", CancellationToken.None));
    }

    [Fact]
    public async Task CorrectAsync_AppliesStructuredCorrectionAndRestoresProtectedContent()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Replace("korekt", "korrekt", StringComparison.Ordinal),
            edit_count = 1
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        var result = await provider.CorrectAsync("Nutze `eror` und schreibe korekt.", CancellationToken.None);

        Assert.Equal("Nutze `eror` und schreibe korrekt.", result.CorrectedText);
        Assert.Equal(1, result.ChangeCount);
        Assert.Equal(CorrectionProviderKind.Luna, result.Provider);
        Assert.DoesNotContain("eror", client.LastProtectedText, StringComparison.Ordinal);
        Assert.NotNull(result.LunaExecution);
        Assert.Equal("baseline", result.LunaExecution.PromptProfile);
        Assert.Equal("none", result.LunaExecution.Effort);
        Assert.Equal("priority", result.LunaExecution.ServiceTier);
        Assert.Equal("full-v1", result.LunaExecution.Protocol);
    }

    [Fact]
    public void QualifiedProductionProfile_MatchesTheLiveBenchmarkWinner()
    {
        var profile = LunaProductionConfiguration.Qualified;

        Assert.Equal("baseline", profile.PromptVariant);
        Assert.Equal(LunaPromptCatalog.Baseline, profile.DeveloperInstructions);
        Assert.Equal("none", profile.Effort);
        Assert.Equal("priority", profile.ServiceTier);
        Assert.Equal(LunaOutputProtocol.FullText, profile.OutputProtocol);
        Assert.Equal("full-v1", profile.ProtocolName);
        Assert.Equal(int.MaxValue, profile.PatchThreshold);
        Assert.Equal(profile.DeveloperInstructions, LunaCorrectionProvider.DeveloperPrompt);
        Assert.Equal(profile.Effort, CodexAppServerClient.LunaEffort);
        Assert.Equal(profile.ServiceTier, CodexAppServerClient.LunaServiceTier);
    }

    [Fact]
    public async Task CorrectAsync_RejectsPlaceholderMutation()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Replace("⟦", string.Empty, StringComparison.Ordinal),
            edit_count = 1
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("en"));

        await Assert.ThrowsAsync<LanguageFixException>(() =>
            provider.CorrectAsync("Use `protected` and bad prose.", CancellationToken.None));
    }

    [Fact]
    public async Task CorrectAsync_CountsAChangedFullTextResultLocally()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new { corrected_text = text + "!", edit_count = 0 }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("en"));

        var result = await provider.CorrectAsync("Test", CancellationToken.None);

        Assert.Equal("Test!", result.CorrectedText);
        Assert.Equal(1, result.ChangeCount);
    }

    [Fact]
    public async Task CorrectAsync_ReusesOnlyValidatedResultsFromTheExactMemoryCache()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Replace("korekt", "korrekt", StringComparison.Ordinal)
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        var first = await provider.CorrectAsync("Das ist korekt.", CancellationToken.None);
        var second = await provider.CorrectAsync("Das ist korekt.", CancellationToken.None);

        Assert.Equal("Das ist korrekt.", first.CorrectedText);
        Assert.Equal(first.CorrectedText, second.CorrectedText);
        Assert.Equal(1, client.RunCount);
        Assert.Equal(TimeSpan.Zero, second.Elapsed);
    }

    [Fact]
    public async Task CorrectAsync_UsesValidatedPatchForLongSegmentedText()
    {
        var client = new FakeClient(_ => throw new InvalidOperationException())
        {
            StructuredResponse = _ => "{\"changes\":[{\"id\":0,\"text\":\"Das ist korrekt.\"}]}"
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: LunaCorrectionProvider.DefaultPatchThreshold);
        var input = "Das ist korekt. " + new string('a', 1_000) + ".";

        var result = await provider.CorrectAsync(input, CancellationToken.None);

        Assert.StartsWith("Das ist korrekt. ", result.CorrectedText, StringComparison.Ordinal);
        Assert.Equal(1, result.ChangeCount);
        Assert.Equal(1, client.StructuredRunCount);
        Assert.Equal(0, client.RunCount);
    }

    [Fact]
    public async Task CorrectAsync_RetriesInvalidPatchOnceWithFullTextProtocol()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new
        {
            corrected_text = text.Replace("korekt", "korrekt", StringComparison.Ordinal)
        }))
        {
            StructuredResponse = _ => "{\"changes\":[{\"id\":999,\"text\":\"ungültig\"}]}"
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: LunaCorrectionProvider.DefaultPatchThreshold);
        var input = "Das ist korekt. " + new string('a', 1_000) + ".";

        var result = await provider.CorrectAsync(input, CancellationToken.None);

        Assert.StartsWith("Das ist korrekt. ", result.CorrectedText, StringComparison.Ordinal);
        Assert.Equal(1, client.StructuredRunCount);
        Assert.Equal(1, client.RunCount);
    }

    [Fact]
    public async Task CorrectAsync_CanForceFullTextForIsolatedPromptBenchmarks()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new { corrected_text = text }))
        {
            StructuredResponse = _ => throw new InvalidOperationException("Patch darf nicht verwendet werden.")
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: int.MaxValue);
        var input = "Absatz eins. " + new string('a', 1_200) + ".";

        var result = await provider.CorrectAsync(input, CancellationToken.None);

        Assert.Equal(input, result.CorrectedText);
        Assert.Equal(1, client.RunCount);
        Assert.Equal(0, client.StructuredRunCount);
    }

    [Fact]
    public async Task CorrectAsync_RejectsUnexpectedControlCharacters()
    {
        var client = new FakeClient(_ => JsonSerializer.Serialize(new
        {
            corrected_text = "Der Text enth\0E4lt ein Nullzeichen.",
            edit_count = 1
        }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        await Assert.ThrowsAsync<LanguageFixException>(() =>
            provider.CorrectAsync("Der Text enthält kein Nullzeichen.", CancellationToken.None));
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
    public async Task TestAsync_RequiresChatGptAndLunaFast()
    {
        var noLogin = new FakeClient(text => text) { Account = new CodexAccountState(true, false) };
        var noLow = new FakeClient(text => text) { SupportsLow = false };

        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            new LunaCorrectionProvider(noLogin, new AppLocalizer("en")).TestAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            new LunaCorrectionProvider(noLow, new AppLocalizer("en")).TestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("audit")]
    [InlineData("safe")]
    [InlineData("balanced")]
    [InlineData("ultra")]
    public void PromptVariants_PreserveTheSecurityAndMinimalEditContract(string variant)
    {
        var prompt = LunaPromptCatalog.Get(variant);

        Assert.Contains("untrusted", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answer", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("translate", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("byte-for-byte", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLF_PROTECTED", prompt, StringComparison.Ordinal);
    }

    private sealed class FakeClient(Func<string, string> response) : ICodexAppServerClient, IStructuredCodexCorrectionClient
    {
        public CodexAccountState Account { get; set; } = new(true, true);
        public bool SupportsLow { get; set; } = true;
        public string? LastProtectedText { get; private set; }
        public int RunCount { get; private set; }
        public int StructuredRunCount { get; private set; }
        public Func<string, string>? StructuredResponse { get; init; }

        public Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken) => Task.FromResult(Account);
        public Task ConnectChatGptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> SupportsLunaAsync(CancellationToken cancellationToken) => Task.FromResult(SupportsLow);
        public Task<string> RunCorrectionAsync(string protectedText, CancellationToken cancellationToken)
        {
            RunCount++;
            LastProtectedText = protectedText;
            return Task.FromResult(response(protectedText));
        }
        public Task<string> RunStructuredCorrectionAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            string effort,
            CancellationToken cancellationToken)
        {
            StructuredRunCount++;
            return Task.FromResult((StructuredResponse ?? throw new InvalidOperationException())(inputJson));
        }
        public void Dispose() { }
    }
}
