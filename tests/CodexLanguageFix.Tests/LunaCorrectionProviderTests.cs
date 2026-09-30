using System.Collections.Concurrent;
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
        Assert.Equal("low", result.LunaExecution.Effort);
        Assert.Equal("priority", result.LunaExecution.ServiceTier);
        Assert.Equal("full-v1", result.LunaExecution.Protocol);
    }

    [Fact]
    public async Task CorrectAsync_SerializesReadableModelInputAndRoundTripsItExactly()
    {
        const string original = "Grüße > \"Zitat\" \\ literal\r\nLink: https://example.test/a";
        string? capturedInputJson = null;
        var client = new FakeClient(_ => "{\"corrected_text\":\"\"}")
        {
            StructuredResponse = inputJson =>
            {
                capturedInputJson = inputJson;
                return "{\"edits\":[]}";
            }
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: 0,
            outputProtocol: LunaOutputProtocol.SpanEdits);

        var result = await provider.CorrectAsync(original, CancellationToken.None);

        Assert.Equal(original, result.CorrectedText);
        Assert.NotNull(capturedInputJson);
        Assert.Contains("Grüße >", capturedInputJson, StringComparison.Ordinal);
        Assert.Contains("⟦CLF_PROTECTED_", capturedInputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003E", capturedInputJson, StringComparison.OrdinalIgnoreCase);

        using var inputDocument = JsonDocument.Parse(capturedInputJson);
        var modelText = inputDocument.RootElement.GetProperty("source_text").GetString();
        Assert.NotNull(modelText);
        Assert.Contains("Grüße >", modelText, StringComparison.Ordinal);
        Assert.Contains("\"Zitat\" \\ literal\r\nLink: ⟦CLF_PROTECTED_", modelText, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedProductionProfile_MatchesTheLiveBenchmarkWinner()
    {
        var profile = LunaProductionConfiguration.Qualified;

        Assert.Equal("baseline", profile.PromptVariant);
        Assert.Equal(LunaPromptCatalog.Baseline, profile.DeveloperInstructions);
        Assert.Equal("low", profile.Effort);
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
    public async Task CorrectAsync_PropagatesProfileResolutionFailureBeforeReadingCache()
    {
        var client = new FakeClient(text => JsonSerializer.Serialize(new { corrected_text = text + "!" }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));

        var first = await provider.CorrectAsync("Das ist korrekt.", CancellationToken.None);
        client.HasCompatibleProfile = false;

        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            provider.CorrectAsync("Das ist korrekt.", CancellationToken.None));

        Assert.Equal("Das ist korrekt.!", first.CorrectedText);
        Assert.Equal(1, client.RunCount);
        Assert.Equal(2, client.ProfileResolveCount);
    }

    [Fact]
    public async Task CorrectAsync_SeparatesCacheEntriesByEveryResolvedModelProfileField()
    {
        var client = new FakeClient(_ => JsonSerializer.Serialize(new { corrected_text = "Der Text." }));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));
        client.Selection = new LunaModelSelection("gpt-5.6-luna", "low", "priority");

        var first = await provider.CorrectAsync("Der Tset.", CancellationToken.None);
        client.Selection = new LunaModelSelection("gpt-6-luna", "low", "priority");
        var second = await provider.CorrectAsync("Der Tset.", CancellationToken.None);
        client.Selection = new LunaModelSelection("gpt-6-luna", "low", "standard");
        var third = await provider.CorrectAsync("Der Tset.", CancellationToken.None);
        client.Selection = new LunaModelSelection("gpt-6-luna", "medium", "standard");
        var fourth = await provider.CorrectAsync("Der Tset.", CancellationToken.None);
        var cached = await provider.CorrectAsync("Der Tset.", CancellationToken.None);

        Assert.Equal(4, client.RunCount);
        Assert.Equal(5, client.ProfileResolveCount);
        Assert.Equal("gpt-5.6-luna", first.LunaExecution!.Model);
        Assert.Equal("gpt-6-luna", second.LunaExecution!.Model);
        Assert.Equal("standard", third.LunaExecution!.ServiceTier);
        Assert.Equal("medium", fourth.LunaExecution!.Effort);
        Assert.Equal("gpt-6-luna", cached.LunaExecution!.Model);
        Assert.Equal("medium", cached.LunaExecution.Effort);
        Assert.Equal("standard", cached.LunaExecution.ServiceTier);
    }

    [Fact]
    public async Task CorrectAsync_KeepsOneImmutableProfileAcrossParallelRuns()
    {
        var firstSelection = new LunaModelSelection("gpt-5.6-luna", "low", "priority");
        var secondSelection = new LunaModelSelection("gpt-6-luna", "low", "priority");
        var firstRunStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRun = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient(text => JsonSerializer.Serialize(new { corrected_text = text }))
        {
            BeforeRunAsync = async (selection, cancellationToken) =>
            {
                if (selection == firstSelection)
                {
                    firstRunStarted.TrySetResult(true);
                    await releaseFirstRun.Task.WaitAsync(cancellationToken);
                }
            }
        };
        client.Selection = firstSelection;
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"), cacheEnabled: false);

        var firstTask = provider.CorrectAsync("Erster Text.", CancellationToken.None);
        await firstRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.Selection = secondSelection;
        var second = await provider.CorrectAsync("Zweiter Text.", CancellationToken.None);
        releaseFirstRun.TrySetResult(true);
        var first = await firstTask;

        Assert.Equal(firstSelection, client.RunSelections[0]);
        Assert.Equal(secondSelection, client.RunSelections[1]);
        Assert.Equal(firstSelection.Model, first.LunaExecution!.Model);
        Assert.Equal(secondSelection.Model, second.LunaExecution!.Model);
    }

    [Fact]
    public async Task CorrectAsync_UsesTheSameResolvedProfileForStructuredAttemptAndFallback()
    {
        var selection = new LunaModelSelection("gpt-5.6-luna", "low", "priority");
        var client = new FakeClient(_ => JsonSerializer.Serialize(new { corrected_text = "Das ist korrekt." }))
        {
            StructuredResponse = _ => "{\"edits\":[{\"start\":999,\"length\":0,\"text\":\"ungueltig\"}]}"
        };
        client.Selection = selection;
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: 0,
            outputProtocol: LunaOutputProtocol.SpanEdits);

        var result = await provider.CorrectAsync("Das ist korekt.", CancellationToken.None);

        Assert.Equal("Das ist korrekt.", result.CorrectedText);
        Assert.Equal(selection, Assert.Single(client.StructuredSelections));
        Assert.Equal(selection, Assert.Single(client.RunSelections));
        Assert.Equal(selection.Model, result.LunaExecution!.Model);
        Assert.True(result.LunaExecution.FallbackUsed);
    }

    [Fact]
    public async Task CorrectAsync_PreservesResolvedProfileAcrossTimedProtocolAndFallback()
    {
        var selection = new LunaModelSelection("gpt-5.6-luna", "low", "priority");
        var client = new TimedFakeClient(
            selection,
            "{\"edits\":[{\"start\":999,\"length\":0,\"text\":\"ungueltig\"}]}",
            "{\"corrected_text\":\"Das ist korrekt.\"}");
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: 0,
            outputProtocol: LunaOutputProtocol.SpanEdits);

        var result = await provider.CorrectAsync("Das ist korekt.", CancellationToken.None);

        Assert.Equal("Das ist korrekt.", result.CorrectedText);
        Assert.Equal(new[] { selection, selection }, client.Selections);
        Assert.Equal(selection.Model, result.LunaExecution!.Model);
        Assert.True(result.LunaExecution.FallbackUsed);
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
    public async Task TestAsync_RequiresChatGptAndCompatibleLunaProfile()
    {
        var noLogin = new FakeClient(text => text) { Account = new CodexAccountState(true, false) };
        var noProfile = new FakeClient(text => text) { HasCompatibleProfile = false };

        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            new LunaCorrectionProvider(noLogin, new AppLocalizer("en")).TestAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            new LunaCorrectionProvider(noProfile, new AppLocalizer("en")).TestAsync(CancellationToken.None));
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
        private readonly ConcurrentQueue<LunaModelSelection> _runSelections = new();
        private readonly ConcurrentQueue<LunaModelSelection> _structuredSelections = new();
        private int _runCount;
        private int _structuredRunCount;
        private int _profileResolveCount;

        public CodexAccountState Account { get; set; } = new(true, true);
        public bool HasCompatibleProfile { get; set; } = true;
        public LunaModelSelection Selection { get; set; } = new("gpt-5.6-luna", "low", "priority");
        public string? LastProtectedText { get; private set; }
        public int RunCount => Volatile.Read(ref _runCount);
        public int StructuredRunCount => Volatile.Read(ref _structuredRunCount);
        public int ProfileResolveCount => Volatile.Read(ref _profileResolveCount);
        public IReadOnlyList<LunaModelSelection> RunSelections => _runSelections.ToArray();
        public IReadOnlyList<LunaModelSelection> StructuredSelections => _structuredSelections.ToArray();
        public Func<LunaModelSelection, CancellationToken, Task>? BeforeRunAsync { get; init; }
        public Func<string, string>? StructuredResponse { get; init; }

        public Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken) => Task.FromResult(Account);
        public Task ConnectChatGptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LunaModelSelection> ResolveLunaProfileAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _profileResolveCount);
            if (!HasCompatibleProfile)
                throw new CodexAppServerException("No supported Luna profile.");
            return Task.FromResult(Selection);
        }
        public async Task<string> RunCorrectionAsync(
            string protectedText,
            LunaModelSelection selection,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runCount);
            _runSelections.Enqueue(selection);
            LastProtectedText = protectedText;
            if (BeforeRunAsync is { } beforeRun)
                await beforeRun(selection, cancellationToken).ConfigureAwait(false);
            return response(protectedText);
        }
        public Task<string> RunStructuredCorrectionAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            LunaModelSelection selection,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _structuredRunCount);
            _structuredSelections.Enqueue(selection);
            return Task.FromResult((StructuredResponse ?? throw new InvalidOperationException())(inputJson));
        }
        public void Dispose() { }
    }

    private sealed class TimedFakeClient(
        LunaModelSelection selection,
        params string[] responses) : ICodexAppServerClient, IStructuredCodexCorrectionClient, ILunaTimedTransportClient
    {
        private readonly ConcurrentQueue<LunaModelSelection> _selections = new();
        private readonly ConcurrentQueue<string> _responses = new(responses);

        public IReadOnlyList<LunaModelSelection> Selections => _selections.ToArray();

        public Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CodexAccountState(true, true));

        public Task ConnectChatGptAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LunaModelSelection> ResolveLunaProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(selection);

        public Task<string> RunCorrectionAsync(
            string protectedText,
            LunaModelSelection resolvedSelection,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The timed transport should be used.");

        public Task<string> RunStructuredCorrectionAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            LunaModelSelection resolvedSelection,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The timed transport should be used.");

        public Task<LunaTransportExecution> RunStructuredCorrectionWithTimingAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            LunaModelSelection resolvedSelection,
            CancellationToken cancellationToken,
            bool replenishPreparedThread = false)
        {
            _selections.Enqueue(resolvedSelection);
            if (!_responses.TryDequeue(out var response))
                throw new InvalidOperationException("No fake response remains.");

            var profile = new LunaCorrectionProfile(
                resolvedSelection.Model,
                resolvedSelection.Effort,
                resolvedSelection.ServiceTier,
                "test",
                "runtime",
                developerInstructions);
            var timing = new LunaProtocolTiming(
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero);
            return Task.FromResult(new LunaTransportExecution(
                response,
                profile,
                timing,
                "thread",
                "turn",
                false));
        }

        public void Dispose() { }
    }
}
