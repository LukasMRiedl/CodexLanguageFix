using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaRuntimeLifecycleTests
{
    private static readonly LunaModelSelection Selection = new("gpt-6-luna", "low", "priority");
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new { type = "object" });
    private const string Instructions = "Correct the text.";

    [Fact]
    public async Task WarmCorrection_ReusesOnlyItsPreparedThreadWithoutAccountOrCatalogRoundTrips()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(client, "first");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await client.WarmUpOnConnectionAsync(transport.Connection, Instructions, "low", timeout.Token);
        var result = await Run(client, transport, timeout.Token);

        Assert.Equal("corrected", result.Response);
        Assert.Equal("first-thread-1", result.ThreadId);
        Assert.Equal(1, transport.Count("account/read"));
        Assert.Equal(1, transport.Count("model/list"));
        Assert.Equal(1, transport.Count("thread/start"));
        var turn = transport.Requests.Single(request => request.Method == "turn/start").Parameters;
        Assert.Equal(Selection.Model, turn.GetProperty("model").GetString());
        Assert.Equal(Selection.Effort, turn.GetProperty("effort").GetString());
        Assert.Equal(Selection.ServiceTier, turn.GetProperty("serviceTier").GetString());
    }

    [Fact]
    public async Task NewConnection_DiscardsPreparedThreadAndCleansUpOnItsOwningConnection()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var first = new Transport(client, "first");
        using var second = new Transport(client, "second");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await client.WarmUpOnConnectionAsync(first.Connection, Instructions, "low", timeout.Token);
        var result = await Run(client, second, timeout.Token);
        var cleaned = await first.Unsubscribed.Task.WaitAsync(timeout.Token);

        Assert.Equal("second-thread-1", result.ThreadId);
        Assert.Equal("first-thread-1", cleaned);
        Assert.DoesNotContain(second.Requests, request => request.Method == "thread/unsubscribe"
            && request.Parameters.GetProperty("threadId").GetString() == "first-thread-1");
    }

    [Fact]
    public async Task AccountChange_DiscardsPreparedThreadEvenWhenTheModelSelectionStaysTheSame()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(client, "same");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await client.WarmUpOnConnectionAsync(transport.Connection, Instructions, "low", timeout.Token);
        client.HandleNotification("account/updated", JsonSerializer.SerializeToElement(new { authMode = "chatgpt" }));
        var result = await Run(client, transport, timeout.Token);

        Assert.Equal("same-thread-2", result.ThreadId);
        Assert.Equal(2, transport.Count("account/read"));
        Assert.Equal(2, transport.Count("model/list"));
        Assert.Equal("same-thread-1", await transport.Unsubscribed.Task.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task AccountChangeDuringWarmup_DoesNotPublishThePreparedThread()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(client, "same");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        transport.OnThreadStart = () => client.HandleNotification("account/updated", default);

        await Assert.ThrowsAsync<CodexAppServerException>(() =>
            client.WarmUpOnConnectionAsync(transport.Connection, Instructions, "low", timeout.Token));
        transport.OnThreadStart = null;
        var result = await Run(client, transport, timeout.Token);

        Assert.Equal("same-thread-2", result.ThreadId);
        Assert.Equal(2, transport.Count("thread/start"));
    }

    [Fact]
    public async Task AccountChangeDuringTurn_RejectsTheCompletedOutput()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(client, "same") { ChangeAccountDuringTurn = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<CodexAppServerException>(() => Run(client, transport, timeout.Token));

        Assert.Equal(1, transport.Count("turn/start"));
    }

    [Fact]
    public async Task DisconnectAfterTurnStart_EndsTheCorrectionWithoutWaitingForCancellationOrRetrying()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(client, "first") { CompleteTurns = false };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var correction = Run(client, transport, CancellationToken.None);
        await transport.TurnStarted.Task.WaitAsync(timeout.Token);

        transport.Disconnect();

        await Assert.ThrowsAsync<AppServerDisconnectedException>(() => correction.WaitAsync(timeout.Token));
        Assert.Equal(1, transport.Count("turn/start"));
        Assert.Equal(1, transport.Count("thread/start"));
        await Assert.ThrowsAsync<AppServerDisconnectedException>(() =>
            client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));
        Assert.Equal(1, transport.Count("model/list"));
    }

    [Fact]
    public async Task DeadConnection_DoesNotProvideItsPreparedThreadToTheNextConnection()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var first = new Transport(client, "first");
        using var second = new Transport(client, "second");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await client.WarmUpOnConnectionAsync(first.Connection, Instructions, "low", timeout.Token);
        first.Disconnect();
        await first.Connection.Disconnected.WaitAsync(timeout.Token);
        var result = await Run(client, second, timeout.Token);

        Assert.Equal("second-thread-1", result.ThreadId);
        Assert.Equal(0, first.Count("turn/start"));
        Assert.Equal(0, first.Count("thread/unsubscribe"));
    }

    [Fact]
    public async Task PackageChange_WaitsForTheRunningOperationThenDiscardsTheOldPreparedThread()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var first = new Transport(client, "first");
        using var second = new Transport(client, "second");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Den vorhandenen Laufzeitstatus injizieren; Prozessstart und Locator gehören
        // nicht zu diesem Test des tatsächlichen Drain-/Reset-Pfades.
        typeof(CodexAppServerClient).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, first.Connection);
        typeof(CodexAppServerClient).GetField("_processExecutable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, "old-package/codex.exe");
        using var running = await client.EnterRuntimeOperationAsync("old-package/codex.exe", timeout.Token);
        await client.WarmUpOnConnectionAsync(first.Connection, Instructions, "low", timeout.Token);

        var enteringUpdatedRuntime = client.EnterRuntimeOperationAsync("new-package/codex.exe", timeout.Token);
        Assert.False(enteringUpdatedRuntime.IsCompleted);
        Assert.False(first.Connection.Disconnected.IsCompleted);

        running.Dispose();
        using var next = await enteringUpdatedRuntime;
        Assert.True(first.Connection.Disconnected.IsCompleted);
        var result = await Run(client, second, timeout.Token);
        Assert.Equal("second-thread-1", result.ThreadId);
        Assert.Equal(1, second.Count("model/list"));
    }

    [Fact]
    public async Task RequestsAfterDisconnect_FailWithoutWritingToTheDeadConnection()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(client, "first");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        transport.Disconnect();
        await transport.Connection.Disconnected.WaitAsync(timeout.Token);

        await Assert.ThrowsAsync<AppServerDisconnectedException>(() =>
            transport.Connection.RequestAsync("account/read", new { }, timeout.Token));

        Assert.Empty(transport.Requests);
    }

    private static Task<LunaTransportExecution> Run(CodexAppServerClient client, Transport transport,
        CancellationToken cancellationToken) => client.RunOnConnectionAsync(transport.Connection,
            "{}", Schema, Instructions, Selection, cancellationToken);

    private sealed class Transport : IDisposable
    {
        private readonly LineReader _reader = new();
        private int _threads;
        private int _turns;
        public JsonRpcLineConnection Connection { get; }
        public ConcurrentQueue<(string Method, JsonElement Parameters)> Requests { get; } = new();
        public TaskCompletionSource TurnStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Unsubscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CompleteTurns { get; init; } = true;
        public bool ChangeAccountDuringTurn { get; init; }
        public Action? OnThreadStart { get; set; }

        public Transport(CodexAppServerClient client, string name)
        {
            var writer = new Responder(line =>
            {
                using var document = JsonDocument.Parse(line);
                var request = document.RootElement;
                var method = request.GetProperty("method").GetString()!;
                var parameters = request.GetProperty("params");
                Requests.Enqueue((method, parameters.Clone()));
                object result;
                string? turnId = null;
                switch (method)
                {
                    case "account/read":
                        result = new { account = new { type = "chatgpt" } };
                        break;
                    case "model/list":
                        result = new { data = new[] { new { model = Selection.Model,
                            supportedReasoningEfforts = new[] { new { reasoningEffort = "low" } },
                            serviceTiers = new[] { new { id = "priority" } } } } };
                        break;
                    case "thread/start":
                        OnThreadStart?.Invoke();
                        result = new { thread = new { id = $"{name}-thread-{++_threads}" } };
                        break;
                    case "turn/start":
                        turnId = $"{name}-turn-{++_turns}";
                        result = new { turn = new { id = turnId } };
                        break;
                    case "thread/unsubscribe":
                        Unsubscribed.TrySetResult(parameters.GetProperty("threadId").GetString()!);
                        result = new { };
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected RPC: {method}");
                }
                _reader.Send(JsonSerializer.Serialize(new { id = request.GetProperty("id").GetInt64(), result }));
                if (turnId is not null)
                {
                    TurnStarted.TrySetResult();
                    if (ChangeAccountDuringTurn) Notify("account/updated", new { authMode = "chatgpt" });
                    if (CompleteTurns)
                    {
                        Notify("item/completed", new { turnId, item = new { type = "agentMessage", text = "corrected" } });
                        Notify("turn/completed", new { turn = new { id = turnId, status = "completed" } });
                    }
                }
            });
            Connection = new JsonRpcLineConnection(_reader, writer);
            Connection.NotificationReceived += client.HandleNotification;
            Connection.Start();
        }

        public int Count(string method) => Requests.Count(request => request.Method == method);
        public void Disconnect() => _reader.Finish();
        private void Notify(string method, object parameters) =>
            _reader.Send(JsonSerializer.Serialize(new { method, @params = parameters }));
        public void Dispose() { Connection.Dispose(); _reader.Dispose(); }
    }

    private sealed class Responder(Action<string> respond) : StringWriter
    {
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            respond(buffer.ToString());
            return Task.CompletedTask;
        }
    }

    private sealed class LineReader : TextReader
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public void Send(string line) => _lines.Writer.TryWrite(line);
        public void Finish() => _lines.Writer.TryComplete();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (await _lines.Reader.WaitToReadAsync(cancellationToken) && _lines.Reader.TryRead(out var line)) return line;
            return null;
        }
        protected override void Dispose(bool disposing) { Finish(); base.Dispose(disposing); }
    }
}
