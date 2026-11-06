using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class IsolatedComposerAccessorTests
{
    [Fact]
    public async Task RealHeadlessWorkerAnswersPingAndExitsOnClosedInput()
    {
        var configuration = typeof(IsolatedComposerAccessorTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var executable = Path.Combine(root, "src", "CodexLanguageFix", "bin", configuration,
            "net8.0-windows", "win-x64", "CodexLanguageFix.exe");
        Assert.True(File.Exists(executable), "The application must be built before its worker is tested.");
        var start = Redirected(executable);
        start.ArgumentList.Add("--composer-worker");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        using var worker = Process.Start(start)!;
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await BrowserProtocol.WriteAsync(worker.StandardInput.BaseStream,
                JsonSerializer.SerializeToUtf8Bytes(new ComposerProcessRequest(1, ComposerOperation.Ping), BrowserProtocol.Json), stop.Token);
            var body = await BrowserProtocol.ReadAsync(worker.StandardOutput.BaseStream, stop.Token).WaitAsync(stop.Token);
            var response = JsonSerializer.Deserialize<ComposerProcessResponse>(body!, BrowserProtocol.Json)!;
            Assert.Equal(1, response.Version);
            Assert.False(response.Failed);
            Assert.Null(response.Snapshot);
            Assert.Equal("not_attempted", response.CaptureStatus);
            worker.StandardInput.Close();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, worker.ExitCode);
        }
        finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void StableEditorTokenKeepsNativeObjectsInChildAndUsesLatestCapture()
    {
        var accessor = new FixtureAccessor { Current = Snapshot("Das ist ein Tset.") };
        var server = new ComposerProcessServer(accessor);
        var first = server.Handle(new(1, ComposerOperation.Capture));
        var firstToken = first.Snapshot!.Token;
        var latestNative = new object();
        accessor.Current = accessor.Current with { NativeElement = latestNative };
        var second = server.Handle(new(1, ComposerOperation.Poll));
        Assert.Equal(firstToken, second.Snapshot!.Token);
        Assert.DoesNotContain("NativeElement", JsonSerializer.Serialize(second), StringComparison.OrdinalIgnoreCase);
        var applied = server.Handle(new(1, ComposerOperation.Replace, firstToken,
            ExpectedText: "Das ist ein Tset.", Replacement: "Das ist ein Test."));
        Assert.True(applied.Replaced);
        Assert.Same(latestNative, accessor.LastWrittenNative);
        Assert.Equal("Das ist ein Test.", accessor.Current.Text);
        var expired = server.Handle(new(1, ComposerOperation.Replace, firstToken + 1,
            ExpectedText: "Das ist ein Test.", Replacement: "Nicht einsetzen"));
        Assert.False(expired.Replaced);
        Assert.Equal("worker_snapshot_expired", expired.WriteStatus);
        Assert.Null(expired.WriteResult);
        Assert.Equal("Das ist ein Test.", accessor.Current.Text);
    }

    [Fact]
    public void MaximumLengthUnicodeMessagesFitBoundedTransportWithoutStaleWritePayload()
    {
        var original = new string('漢', 20_000);
        var corrected = new string('語', 20_000);
        var accessor = new FixtureAccessor { Current = Snapshot(original) };
        var server = new ComposerProcessServer(accessor);
        var capture = server.Handle(new(1, ComposerOperation.Capture));
        var request = new ComposerProcessRequest(1, ComposerOperation.Replace, capture.Snapshot!.Token,
            ExpectedText: original, Replacement: corrected);
        var write = server.Handle(request);
        var later = server.Handle(new(1, ComposerOperation.Poll));
        Assert.True(write.Replaced);
        Assert.Null(later.WriteResult);
        Assert.NotNull(write.WriteResult);
        Assert.Null(write.Snapshot);
        foreach (var message in new object[] { request, write, later })
            Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(message, BrowserProtocol.Json).Length, 1, BrowserProtocol.MaximumBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbruptWorkerExitOrTimeoutNeverReplaysAnUncertainWrite(bool hang)
    {
        var starts = 0;
        Process? child = null;
        using var accessor = new IsolatedComposerAccessor(() =>
        {
            starts++;
            return child = StartFixture(hang);
        }, TimeSpan.FromSeconds(3));
        var snapshot = accessor.TryPollFocusedComposer();
        Assert.NotNull(snapshot);
        var watch = Stopwatch.StartNew();
        Assert.False(accessor.TryReplace(snapshot!, snapshot!.Text, "Das ist ein Test."));
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.Equal(ComposerWriteState.PartiallyApplied, accessor.LastWriteResult!.State);
        Assert.Null(accessor.LastWriteResult.ObservedText);
        Assert.StartsWith("worker_unavailable", accessor.LastWriteStatus);
        Assert.Null(accessor.TryRefresh(snapshot));
        Assert.False(accessor.TryReplace(snapshot, snapshot.Text, "Das ist ein Test."));
        Assert.Null(accessor.TryPollFocusedComposer());
        Assert.Equal(1, starts);
        // The caller is still alive after the child vanished, and the child is disposed.
        Assert.Throws<InvalidOperationException>(() => _ = child!.HasExited);
    }

    [Fact]
    public void InvalidResponseFrameDiscardsSessionWithoutReplayingWriteOrLoggingArbitraryStderr()
    {
        var starts = 0;
        using var accessor = new IsolatedComposerAccessor(() =>
        {
            starts++;
            return StartFixture(hang: false, invalidResponse: true);
        }, TimeSpan.FromSeconds(3));
        var snapshot = accessor.TryPollFocusedComposer();
        Assert.NotNull(snapshot);
        Assert.False(accessor.TryReplace(snapshot!, snapshot!.Text, "Das ist ein Test."));
        Assert.Equal(ComposerWriteState.PartiallyApplied, accessor.LastWriteResult!.State);
        Assert.Equal("worker_unavailable", accessor.LastWriteStatus);
        Assert.Null(accessor.TryRefresh(snapshot));
        Assert.False(accessor.TryReplace(snapshot, snapshot.Text, "Nicht erneut einsetzen"));
        Assert.Null(accessor.TryPollFocusedComposer());
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task DisposalCancelsBlockedIpcWithoutWaitingForRequestTimeout()
    {
        Process? child = null;
        var accessor = new IsolatedComposerAccessor(() => child = StartFixture(hang: true), TimeSpan.FromSeconds(30));
        try
        {
            var snapshot = accessor.TryPollFocusedComposer();
            Assert.NotNull(snapshot);
            var pending = Task.Run(() => accessor.TryReplace(snapshot!, snapshot!.Text, "Das ist ein Test."));
            await Task.Delay(150);
            var watch = Stopwatch.StartNew();
            accessor.Dispose();
            Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        }
        finally { accessor.Dispose(); }
    }

    private static Process StartFixture(bool hang, bool invalidResponse = false)
    {
        var snapshot = ComposerWireSnapshot.From(1, Snapshot("Das ist ein Tset."));
        var response = new ComposerProcessResponse(1, snapshot, false, "captured", "not_attempted",
            ComposerHost.Windows, "prose", true, 1, 1);
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(response, BrowserProtocol.Json));
        var script = """
            $ErrorActionPreference = 'Stop'
            $inputStream = [Console]::OpenStandardInput()
            $outputStream = [Console]::OpenStandardOutput()
            function Read-Frame {
                $header = New-Object byte[] 4
                $offset = 0
                while ($offset -lt 4) {
                    $count = $inputStream.Read($header, $offset, 4 - $offset)
                    if ($count -eq 0) { exit 9 }
                    $offset += $count
                }
                $size = [BitConverter]::ToInt32($header, 0)
                if ($size -le 0 -or $size -gt 262144) { exit 10 }
                $body = New-Object byte[] $size
                $offset = 0
                while ($offset -lt $size) {
                    $count = $inputStream.Read($body, $offset, $size - $offset)
                    if ($count -eq 0) { exit 9 }
                    $offset += $count
                }
            }
            Read-Frame
            $response = [Convert]::FromBase64String('RESPONSE')
            $header = [BitConverter]::GetBytes($response.Length)
            $outputStream.Write($header, 0, 4)
            $outputStream.Write($response, 0, $response.Length)
            $outputStream.Flush()
            Read-Frame
            FINISH
            """;
        var finish = invalidResponse
            ? "[Console]::Error.WriteLine('uia-phase:secretdraft'); Start-Sleep -Milliseconds 250; $outputStream.Write([byte[]]@(0,0,0,0),0,4); $outputStream.Flush(); Start-Sleep -Seconds 30"
            : "[Console]::Error.WriteLine('uia-phase:selection_selected'); " + (hang ? "Start-Sleep -Seconds 30" : "exit 77");
        script = script.Replace("RESPONSE", encoded).Replace("FINISH", finish);
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = Redirected(shell);
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return Process.Start(start)!;
    }

    private static ProcessStartInfo Redirected(string executable) => new(executable)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };

    private static ComposerSnapshot Snapshot(string text) => new(new object(), [1, 2], text,
        new Rect(0, 0, 300, 100), 123, Host: ComposerHost.Windows, HostWindow: 456);

    private sealed class FixtureAccessor : IComposerAccessor
    {
        internal required ComposerSnapshot Current { get; set; }
        internal object? LastWrittenNative { get; private set; }
        public ComposerWriteResult? LastWriteResult { get; private set; }
        public string LastCaptureStatus => "captured";
        public string LastWriteStatus => "ok";
        public ComposerHost? LastCaptureHost => ComposerHost.Windows;
        public string? LastFieldCategory => "prose";
        public bool LastCaptureWasKeyboardFocused => true;
        public ComposerSnapshot? TryCaptureFocusedComposer() => Current;
        public ComposerSnapshot? TryCaptureFocusedComposerWithoutLayout(bool requireKeyboardFocus = false) => Current;
        public ComposerSnapshot? TryPollFocusedComposer() => Current;
        public void InvalidateLayout() { }
        public ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot) => snapshot.SameEditor(Current) ? Current : null;
        public bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement)
        {
            if (!snapshot.SameEditor(Current) || Current.Text != expectedText) return false;
            LastWrittenNative = snapshot.NativeElement;
            Current = Current with { Text = replacement };
            LastWriteResult = new(ComposerWriteState.Applied, expectedText, replacement, 1, "ok");
            return true;
        }
    }
}
