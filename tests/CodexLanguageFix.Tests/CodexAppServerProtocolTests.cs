using System.Diagnostics;
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

    [Fact]
    public void TextDeltaTiming_RemainsAssignedToItsTurnDuringParallelRuns()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        var now = Stopwatch.GetTimestamp();
        client.RegisterTurnStart("turn-a", now - (2 * Stopwatch.Frequency));
        client.RegisterTurnStart("turn-b", now - Stopwatch.Frequency);

        client.ObserveFirstTextDelta("turn-b");
        client.ObserveFirstTextDelta("turn-a");

        var first = Assert.IsType<TimeSpan>(client.GetTimeToFirstTextDelta("turn-a"));
        var second = Assert.IsType<TimeSpan>(client.GetTimeToFirstTextDelta("turn-b"));
        Assert.True(first > second);
        Assert.InRange(first.TotalSeconds, 1.9, 2.2);
        Assert.InRange(second.TotalSeconds, 0.9, 1.2);
    }

    [Fact]
    public void TransportTiming_IsImmutableAndDoesNotDoubleCountTurnStart()
    {
        var timing = new LunaProtocolTiming(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(3),
            TimeSpan.FromMilliseconds(4),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(6))
        {
            TimeToFirstTextDelta = TimeSpan.FromMilliseconds(7),
            ModelCompletion = TimeSpan.FromMilliseconds(8)
        };

        var changed = timing with { ModelCompletion = TimeSpan.FromMilliseconds(9) };

        Assert.Equal(TimeSpan.FromMilliseconds(20), timing.Total);
        Assert.Equal(TimeSpan.FromMilliseconds(21), changed.Total);
        Assert.Equal(TimeSpan.FromMilliseconds(8), timing.ModelCompletion);
    }

    [Fact]
    public void DisconnectReset_DoesNotReplaceAConnectionRecoveredByAParallelRequest()
    {
        using var disconnected = new JsonRpcLineConnection(new StringReader(string.Empty), new StringWriter());
        using var recovered = new JsonRpcLineConnection(new StringReader(string.Empty), new StringWriter());

        Assert.False(CodexAppServerClient.ShouldResetObservedConnection(recovered, disconnected));
        Assert.True(CodexAppServerClient.ShouldResetObservedConnection(disconnected, disconnected));
    }

    private sealed class ChannelLineReader : TextReader
    {
        private readonly Channel<string?> _channel = Channel.CreateUnbounded<string?>();

        public ValueTask WriteAsync(string line) => _channel.Writer.WriteAsync(line);

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAsync(cancellationToken);
    }
}
