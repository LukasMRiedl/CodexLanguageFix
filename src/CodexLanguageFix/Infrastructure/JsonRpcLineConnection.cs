using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

internal sealed class JsonRpcLineConnection : IDisposable
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private long _nextId;
    private Task? _readerTask;

    public JsonRpcLineConnection(TextReader reader, TextWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public event Action<string, JsonElement>? NotificationReceived;

    public void Start()
    {
        _readerTask ??= Task.Run(ReadLoopAsync);
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Eine JSON-RPC-Anfrage-ID wurde doppelt vergeben.");
        }

        try
        {
            await WriteAsync(new { method, id, @params = parameters }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(message, JsonOptions);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new AppServerDisconnectedException("Die Verbindung zum Codex App Server wurde beendet.", exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null)
                {
                    failure = new AppServerDisconnectedException("Der Codex App Server wurde unerwartet beendet.");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.TryGetInt64(out var id)
                    && _pending.TryGetValue(id, out var pending))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var errorMessage)
                            ? errorMessage.GetString()
                            : null;
                        pending.TrySetException(new CodexAppServerException(message ?? "Der Codex App Server hat die Anfrage abgelehnt."));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        pending.TrySetResult(result.Clone());
                    }
                    else
                    {
                        pending.TrySetException(new CodexAppServerException("Der Codex App Server hat eine unvollständige Antwort geliefert."));
                    }

                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement)
                    && methodElement.GetString() is { Length: > 0 } method)
                {
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : default;
                    NotificationReceived?.Invoke(method, parameters);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            failure ??= new AppServerDisconnectedException("Die Verbindung zum Codex App Server wurde beendet.");
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(failure);
            }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _writeGate.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class AppServerDisconnectedException(string message, Exception? innerException = null) : IOException(message, innerException);
