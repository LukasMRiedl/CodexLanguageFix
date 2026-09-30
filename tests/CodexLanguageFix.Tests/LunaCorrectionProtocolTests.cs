using System.Text.Json;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaCorrectionProtocolTests
{
    [Fact]
    public async Task SegmentsProtocol_CanBeSelectedForShortTexts()
    {
        var client = new ProtocolClient
        {
            StructuredResponse = _ => "{\"changes\":[{\"id\":0,\"text\":\"Das ist korrekt.\"}]}",
            FullResponse = _ => throw new InvalidOperationException("Volltext darf nicht verwendet werden.")
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: 0,
            outputProtocol: LunaOutputProtocol.Segments);

        var result = await provider.CorrectAsync("Das ist korekt.", CancellationToken.None);

        Assert.Equal("Das ist korrekt.", result.CorrectedText);
        Assert.Equal(1, result.ChangeCount);
        Assert.Equal(1, client.StructuredRunCount);
        Assert.Equal(0, client.FullRunCount);
        Assert.Equal("segments-0", result.LunaExecution?.Protocol);
    }

    [Fact]
    public async Task SpanEditsProtocol_AppliesCompactUnicodeSafeReplacement()
    {
        var client = new ProtocolClient
        {
            StructuredResponse = input =>
            {
                using var document = JsonDocument.Parse(input);
                var source = document.RootElement.GetProperty("source_text").GetString()!;
                var start = source.IndexOf("korekt", StringComparison.Ordinal);
                return JsonSerializer.Serialize(new
                {
                    edits = new[] { new { start, length = "korekt".Length, text = "korrekt" } }
                });
            },
            FullResponse = _ => throw new InvalidOperationException("Volltext darf nicht verwendet werden.")
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: 0,
            outputProtocol: LunaOutputProtocol.SpanEdits);

        var result = await provider.CorrectAsync("🙂 Das ist korekt.\r\n", CancellationToken.None);

        Assert.Equal("🙂 Das ist korrekt.\r\n", result.CorrectedText);
        Assert.Equal(1, result.ChangeCount);
        Assert.Equal(1, client.StructuredRunCount);
        Assert.Equal(0, client.FullRunCount);
        Assert.Equal("span-edits-0", result.LunaExecution?.Protocol);
    }

    [Fact]
    public async Task SpanEditsProtocol_FallsBackExactlyOnceWhenValidationFails()
    {
        var client = new ProtocolClient
        {
            StructuredResponse = _ => "{\"edits\":[{\"start\":999,\"length\":1,\"text\":\"x\"}]}",
            FullResponse = text => JsonSerializer.Serialize(new
            {
                corrected_text = text.Replace("korekt", "korrekt", StringComparison.Ordinal)
            })
        };
        var provider = new LunaCorrectionProvider(
            client,
            new AppLocalizer("de"),
            cacheEnabled: false,
            patchThreshold: 0,
            outputProtocol: LunaOutputProtocol.SpanEdits);

        var result = await provider.CorrectAsync("Das ist korekt.", CancellationToken.None);

        Assert.Equal("Das ist korrekt.", result.CorrectedText);
        Assert.Equal(1, client.StructuredRunCount);
        Assert.Equal(1, client.FullRunCount);
        Assert.True(result.LunaExecution?.FallbackUsed);
    }

    [Fact]
    public void CandidatePatchThresholds_AreThePlannedValues()
    {
        Assert.Equal([500, 1_000, 2_000], LunaCorrectionProvider.CandidatePatchThresholds);
    }

    private sealed class ProtocolClient : ICodexAppServerClient, IStructuredCodexCorrectionClient
    {
        public Func<string, string> StructuredResponse { get; init; } = _ => "{\"changes\":[]}";
        public Func<string, string> FullResponse { get; init; } = text => JsonSerializer.Serialize(new { corrected_text = text });
        public int StructuredRunCount { get; private set; }
        public int FullRunCount { get; private set; }

        public Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CodexAccountState(true, true));

        public Task ConnectChatGptAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LunaModelSelection> ResolveLunaProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LunaModelSelection("gpt-5.6-luna", "low", "priority"));

        public Task<string> RunCorrectionAsync(
            string protectedText,
            LunaModelSelection selection,
            CancellationToken cancellationToken)
        {
            FullRunCount++;
            return Task.FromResult(FullResponse(protectedText));
        }

        public Task<string> RunStructuredCorrectionAsync(
            string inputJson,
            JsonElement outputSchema,
            string developerInstructions,
            LunaModelSelection selection,
            CancellationToken cancellationToken)
        {
            StructuredRunCount++;
            return Task.FromResult(StructuredResponse(inputJson));
        }

        public void Dispose() { }
    }
}
