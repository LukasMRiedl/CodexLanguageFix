using System.Text.Json;
using System.Threading.Channels;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class CodexAppServerProtocolTests
{
    [Fact]
    public void ModelList_DetectsOnlyExactLunaFastCapability()
    {
        using var supported = JsonDocument.Parse("""
            {"data":[{"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"none","description":"No reasoning"}],"serviceTiers":[{"id":"priority","name":"Fast","description":"1.5x speed"}]}]}
            """);
        using var wrongEffort = JsonDocument.Parse("""
            {"data":[{"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"max","description":"Deep"}],"serviceTiers":[{"id":"priority","name":"Fast","description":"1.5x speed"}]}]}
            """);
        using var wrongModel = JsonDocument.Parse("""
            {"data":[{"model":"gpt-5.6-sol","supportedReasoningEfforts":[{"reasoningEffort":"none","description":"No reasoning"}],"serviceTiers":[{"id":"priority","name":"Fast","description":"1.5x speed"}]}]}
            """);
        using var missingFastTier = JsonDocument.Parse("""
            {"data":[{"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"none","description":"No reasoning"}],"serviceTiers":[]}]}
            """);

        Assert.True(CodexAppServerClient.ModelListContainsLuna(supported.RootElement));
        Assert.False(CodexAppServerClient.ModelListContainsLuna(wrongEffort.RootElement));
        Assert.False(CodexAppServerClient.ModelListContainsLuna(wrongModel.RootElement));
        Assert.False(CodexAppServerClient.ModelListContainsLuna(missingFastTier.RootElement));
    }

    [Fact]
    public async Task JsonRpcConnection_WritesRequestAndMatchesResponse()
    {
        var reader = new ChannelLineReader();
        var writer = new StringWriter();
        using var connection = new JsonRpcLineConnection(reader, writer);
        connection.Start();

        var request = connection.RequestAsync("account/read", new { refreshToken = true }, CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => writer.ToString().Contains("account/read", StringComparison.Ordinal), TimeSpan.FromSeconds(2)));
        await reader.WriteAsync("{\"id\":1,\"result\":{\"requiresOpenaiAuth\":true,\"account\":null}}");
        var response = await request;

        Assert.True(response.GetProperty("requiresOpenaiAuth").GetBoolean());
        Assert.Contains("\"refreshToken\":true", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonRpcConnection_ForwardsNotificationsWithoutMixingPromptDataIntoErrors()
    {
        var reader = new ChannelLineReader();
        var writer = new StringWriter();
        using var connection = new JsonRpcLineConnection(reader, writer);
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (method, parameters) =>
        {
            if (method == "account/login/completed" && parameters.GetProperty("success").GetBoolean())
            {
                received.TrySetResult(true);
            }
        };
        connection.Start();

        await reader.WriteAsync("{\"method\":\"account/login/completed\",\"params\":{\"loginId\":\"x\",\"success\":true,\"error\":null}}");

        Assert.True(await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private sealed class ChannelLineReader : TextReader
    {
        private readonly Channel<string?> _channel = Channel.CreateUnbounded<string?>();

        public ValueTask WriteAsync(string line) => _channel.Writer.WriteAsync(line);

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAsync(cancellationToken);
    }
}
