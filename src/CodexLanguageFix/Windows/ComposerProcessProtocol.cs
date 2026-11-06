using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Windows;

internal enum ComposerOperation { Ping, Capture, CaptureWithoutLayout, Poll, Invalidate, Refresh, Replace }

internal sealed record ComposerProcessRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] ComposerOperation Operation,
    long Token = 0,
    bool RequireKeyboardFocus = false,
    string? ExpectedText = null,
    string? Replacement = null);

internal sealed record ComposerProcessResponse(
    int Version,
    ComposerWireSnapshot? Snapshot,
    bool Replaced,
    string CaptureStatus,
    string WriteStatus,
    ComposerHost? Host,
    string? FieldCategory,
    bool KeyboardFocused,
    long CaptureCount,
    long LayoutCaptureCount,
    ComposerWriteResult? WriteResult = null,
    bool Failed = false);

internal sealed record ComposerWireRect(double X, double Y, double Width, double Height, bool Empty = false)
{
    internal static ComposerWireRect From(Rect rectangle) => rectangle.IsEmpty
        ? new(0, 0, 0, 0, true) : new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    internal Rect ToRect() => Empty ? Rect.Empty : new(X, Y, Width, Height);
}

internal sealed record ComposerWireSnapshot(
    long Token, int[] RuntimeId, string Text, ComposerWireRect Bounds, int ProcessId,
    ComposerWireRect? RightControlBounds, ComposerHost Host, long HostWindow,
    ComposerWireRect? EditorBounds, ComposerWireRect[]? OccupiedBounds, ComposerReadMethod ReadMethod,
    ComposerWireRect? PlacementBounds, string FieldCategory, ComposerWireRect[]? EditorTextBounds)
{
    internal static ComposerWireSnapshot From(long token, ComposerSnapshot snapshot) => new(
        token, snapshot.RuntimeId, snapshot.Text, ComposerWireRect.From(snapshot.Bounds), snapshot.ProcessId,
        snapshot.RightControlBounds is { } control ? ComposerWireRect.From(control) : null,
        snapshot.Host, snapshot.HostWindow.ToInt64(),
        snapshot.EditorBounds is { } editor ? ComposerWireRect.From(editor) : null,
        snapshot.OccupiedBounds?.Select(ComposerWireRect.From).ToArray(), snapshot.ReadMethod,
        snapshot.PlacementBounds is { } placement ? ComposerWireRect.From(placement) : null,
        snapshot.FieldCategory, snapshot.EditorTextBounds?.Select(ComposerWireRect.From).ToArray());

    internal ComposerSnapshot ToSnapshot(object element) => new(
        element, RuntimeId, Text, Bounds.ToRect(), ProcessId, RightControlBounds?.ToRect(), Host,
        checked((nint)HostWindow), EditorBounds?.ToRect(), OccupiedBounds?.Select(rect => rect.ToRect()).ToArray(),
        ReadMethod, PlacementBounds?.ToRect(), FieldCategory, EditorTextBounds?.Select(rect => rect.ToRect()).ToArray());
}

internal sealed class ComposerProcessServer(IComposerAccessor accessor)
{
    // Tokens remain stable while an editor is polled; undo is not invalidated by later captures.
    private readonly Dictionary<long, ComposerSnapshot> _editors = [];
    private long _nextToken;

    internal ComposerProcessResponse Handle(ComposerProcessRequest request)
    {
        if (request.Version != 1 || !Enum.IsDefined(request.Operation)
            || request.ExpectedText?.Length > 20_000 || request.Replacement?.Length > 20_000)
            throw new InvalidDataException("Invalid automation request.");

        ComposerSnapshot? snapshot = null;
        var replaced = false;
        var expired = false;
        switch (request.Operation)
        {
            case ComposerOperation.Ping: break;
            case ComposerOperation.Capture: snapshot = accessor.TryCaptureFocusedComposer(); break;
            case ComposerOperation.CaptureWithoutLayout:
                snapshot = accessor.TryCaptureFocusedComposerWithoutLayout(request.RequireKeyboardFocus); break;
            case ComposerOperation.Poll: snapshot = accessor.TryPollFocusedComposer(); break;
            case ComposerOperation.Invalidate: accessor.InvalidateLayout(); break;
            case ComposerOperation.Refresh:
                if (_editors.TryGetValue(request.Token, out var refresh)) snapshot = accessor.TryRefresh(refresh);
                else expired = true;
                break;
            case ComposerOperation.Replace:
                if (request.ExpectedText is null || request.Replacement is null)
                    throw new InvalidDataException("Missing replacement text.");
                if (_editors.TryGetValue(request.Token, out var target))
                    replaced = accessor.TryReplace(target, request.ExpectedText, request.Replacement);
                else expired = true;
                break;
        }

        ComposerWireSnapshot? wire = null;
        if (snapshot is not null)
        {
            var existing = _editors.FirstOrDefault(pair => pair.Value.SameEditor(snapshot));
            var token = existing.Key != 0 ? existing.Key : ++_nextToken;
            if (_editors.Count >= 16 && !_editors.ContainsKey(token)) _editors.Remove(_editors.Keys.First());
            _editors[token] = snapshot;
            wire = ComposerWireSnapshot.From(token, snapshot);
        }

        return new(1, wire, replaced,
            expired ? "worker_snapshot_expired" : accessor.LastCaptureStatus,
            expired ? "worker_snapshot_expired" : accessor.LastWriteStatus,
            accessor.LastCaptureHost, accessor.LastFieldCategory, accessor.LastCaptureWasKeyboardFocused,
            (accessor as CodexComposerAccessor)?.CaptureCount ?? 0,
            (accessor as CodexComposerAccessor)?.LayoutCaptureCount ?? 0,
            request.Operation == ComposerOperation.Replace && !expired ? accessor.LastWriteResult : null);
    }

    internal static int Run(Stream input, Stream output, IComposerAccessor accessor)
    {
        var server = new ComposerProcessServer(accessor);
        try
        {
            while (BrowserProtocol.ReadAsync(input, CancellationToken.None).GetAwaiter().GetResult() is { } body)
            {
                var request = JsonSerializer.Deserialize<ComposerProcessRequest>(body, BrowserProtocol.Json)
                    ?? throw new InvalidDataException("Missing automation request.");
                ComposerProcessResponse response;
                try { response = server.Handle(request); }
                catch (Exception exception) when (exception is not InvalidDataException)
                {
                    response = new(1, null, false, "worker_operation_failed", "worker_operation_failed", null,
                        null, false, 0, 0, Failed: true);
                }
                BrowserProtocol.WriteAsync(output, JsonSerializer.SerializeToUtf8Bytes(response, BrowserProtocol.Json),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return 1;
        }
    }

    internal static int RunNative(int parentProcessId)
    {
        Process parent;
        try { parent = Process.GetProcessById(parentProcessId); }
        catch (ArgumentException) { return 2; }
        using var parentLifetime = parent;
        using var stopWatching = new CancellationTokenSource();
        _ = WatchParent();
        async Task WatchParent()
        {
            try
            {
                await parent.WaitForExitAsync(stopWatching.Token).ConfigureAwait(false);
                Environment.Exit(1);
            }
            catch (OperationCanceledException) { }
        }
        var exitCode = 1;
        var thread = new Thread(() =>
        {
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            exitCode = Run(input, output, new CodexComposerAccessor(phase => Console.Error.WriteLine("uia-phase:" + phase)));
        }) { IsBackground = true, Name = "Isolated text field automation" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        stopWatching.Cancel();
        return exitCode;
    }
}
