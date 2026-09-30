using System.Text.Json;
using System.Threading.Channels;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaRuntimeSnapshotTests
{
    [Fact]
    public async Task Snapshot_ReusesAccountAndCatalogWithoutClickRoundTrips()
    {
        using var transport = new Transport(_ => Catalog("gpt-6-luna"));
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var first = await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token);
        var second = await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token);

        Assert.Equal(new LunaModelSelection("gpt-6-luna", "low", "priority"), first);
        Assert.Same(first, second);
        Assert.Equal(1, transport.AccountRequests);
        Assert.Equal(1, transport.ModelRequests);
    }

    [Fact]
    public async Task AccountNotification_InvalidatesPreparedCatalogSnapshot()
    {
        using var transport = new Transport(_ => Catalog("gpt-6-luna"));
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var first = await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token);

        client.HandleNotification("account/updated", JsonSerializer.SerializeToElement(new { authMode = "chatgpt" }));

        var second = await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token);
        Assert.NotSame(first, second);
        Assert.Equal(2, transport.AccountRequests);
        Assert.Equal(2, transport.ModelRequests);
    }

    [Fact]
    public async Task AccountChangeDuringLookup_RereadsAccountAndEveryPageWithoutPublishingStaleSelection()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        var catalogReads = 0;
        using var transport = new Transport(request =>
        {
            catalogReads++;
            if (catalogReads == 2)
                client.HandleNotification("account/updated", JsonSerializer.SerializeToElement(new { authMode = "chatgpt" }));
            var firstPage = !request.GetProperty("params").TryGetProperty("cursor", out _);
            return Catalog(catalogReads <= 2 ? "gpt-9-luna" : firstPage ? "gpt-5.6-luna" : "gpt-6-luna",
                firstPage ? "page-2" : null);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var selection = await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token);

        Assert.Equal("gpt-6-luna", selection.Model);
        Assert.Equal(2, transport.AccountRequests);
        Assert.Equal(4, transport.ModelRequests);
        Assert.Same(selection, await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));
        Assert.Equal(2, transport.AccountRequests);
    }

    [Fact]
    public async Task InitialAccountReadNotification_RetriesOnceAndPublishesTheFreshSnapshot()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var transport = new Transport(_ => Catalog("gpt-6-luna"), notifyOnAccountRead: count => count == 1);
        transport.Connection.NotificationReceived += client.HandleNotification;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var selection = await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token);

        Assert.Equal(new LunaModelSelection("gpt-6-luna", "low", "priority"), selection);
        Assert.Equal(2, transport.AccountRequests);
        Assert.Equal(2, transport.ModelRequests);
        Assert.Same(selection, await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));
        Assert.Equal(2, transport.AccountRequests);
    }

    [Fact]
    public async Task RepeatedAccountChanges_StopAfterOneRetryAndDoNotPublishASnapshot()
    {
        using var client = new CodexAppServerClient(Path.GetTempPath());
        var notify = true;
        using var transport = new Transport(_ => Catalog("gpt-6-luna"), notifyOnAccountRead: _ => notify);
        transport.Connection.NotificationReceived += client.HandleNotification;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<CodexAppServerException>(() => client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));

        Assert.Equal(2, transport.AccountRequests);
        Assert.Equal(2, transport.ModelRequests);
        notify = false;
        Assert.Equal("gpt-6-luna", (await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token)).Model);
        Assert.Equal(3, transport.AccountRequests);
        Assert.Equal(3, transport.ModelRequests);
    }

    [Fact]
    public async Task NewConnection_RefreshesModelSelection()
    {
        using var first = new Transport(_ => Catalog("gpt-5.6-luna"));
        using var second = new Transport(_ => Catalog("gpt-6-luna"));
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        Assert.Equal("gpt-5.6-luna", (await client.ResolveOnConnectionAsync(first.Connection, timeout.Token)).Model);
        Assert.Equal("gpt-6-luna", (await client.ResolveOnConnectionAsync(second.Connection, timeout.Token)).Model);
        Assert.Equal(1, second.AccountRequests);
        Assert.Equal(1, second.ModelRequests);
    }

    [Fact]
    public async Task Pagination_ReadsAllPagesBeforeSelectingAndIncludesHidden()
    {
        using var transport = new Transport(request =>
            !request.GetProperty("params").TryGetProperty("cursor", out var cursor) || cursor.ValueKind == JsonValueKind.Null
                ? Catalog("gpt-5.6-luna", "page-2") : Catalog("gpt-6-luna"));
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        Assert.Equal("gpt-6-luna", (await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token)).Model);
        Assert.Equal(2, transport.ModelRequests);
        Assert.All(transport.ModelParameters, parameters => Assert.True(parameters.GetProperty("includeHidden").GetBoolean()));
        Assert.Equal("page-2", transport.ModelParameters[1].GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task UnsupportedCatalog_IsCachedAsUnavailableWithoutInventingLow()
    {
        using var transport = new Transport(_ => Catalog("gpt-6-luna", effort: "medium"));
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<CodexAppServerException>(() => client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));
        await Assert.ThrowsAsync<CodexAppServerException>(() => client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));
        Assert.Equal(1, transport.ModelRequests);
    }

    [Fact]
    public async Task RepeatedCursor_FailsWithoutPublishingPartialSelection()
    {
        var repeat = true;
        using var transport = new Transport(_ => Catalog("gpt-6-luna", repeat ? "loop" : null));
        using var client = new CodexAppServerClient(Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<JsonException>(() => client.ResolveOnConnectionAsync(transport.Connection, timeout.Token));
        repeat = false;
        Assert.Equal("gpt-6-luna", (await client.ResolveOnConnectionAsync(transport.Connection, timeout.Token)).Model);
        Assert.Equal(2, transport.AccountRequests);
    }

    private static JsonElement Catalog(string model, string? cursor = null, string effort = "low") =>
        JsonSerializer.SerializeToElement(new
        {
            data = new[] { new { model, supportedReasoningEfforts = new[] { new { reasoningEffort = effort } },
                serviceTiers = new[] { new { id = "priority" } } } }, nextCursor = cursor
        });

    private sealed class Transport : IDisposable
    {
        private readonly ChannelReaderText _reader = new();
        public JsonRpcLineConnection Connection { get; }
        public int AccountRequests { get; private set; }
        public int ModelRequests { get; private set; }
        public List<JsonElement> ModelParameters { get; } = [];

        public Transport(Func<JsonElement, JsonElement> catalog, Func<int, bool>? notifyOnAccountRead = null)
        {
            var writer = new ResponderWriter(line =>
            {
                using var request = JsonDocument.Parse(line);
                var root = request.RootElement;
                JsonElement result;
                if (root.GetProperty("method").GetString() == "account/read")
                {
                    AccountRequests++;
                    if (notifyOnAccountRead?.Invoke(AccountRequests) == true)
                        _reader.Send(JsonSerializer.Serialize(new { method = "account/updated", @params = new { authMode = "chatgpt" } }));
                    result = JsonSerializer.SerializeToElement(new { account = new { type = "chatgpt" } });
                }
                else
                {
                    ModelRequests++;
                    ModelParameters.Add(root.GetProperty("params").Clone());
                    result = catalog(root);
                }
                _reader.Send(JsonSerializer.Serialize(new { id = root.GetProperty("id").GetInt64(), result }));
            });
            Connection = new JsonRpcLineConnection(_reader, writer);
            Connection.Start();
        }

        public void Dispose() { Connection.Dispose(); _reader.Dispose(); }
    }

    private sealed class ResponderWriter(Action<string> respond) : StringWriter
    {
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            respond(buffer.ToString());
            return Task.CompletedTask;
        }
    }

    private sealed class ChannelReaderText : TextReader
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public void Send(string line) => _lines.Writer.TryWrite(line);
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await _lines.Reader.ReadAsync(cancellationToken);
        protected override void Dispose(bool disposing) { _lines.Writer.TryComplete(); base.Dispose(disposing); }
    }
}
