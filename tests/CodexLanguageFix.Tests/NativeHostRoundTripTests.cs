using System.Diagnostics;
using System.Text.Json;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;
public sealed class NativeHostRoundTripTests
{
    [Fact]
    public async Task RealHostRejectsForeignOriginAndTransportsSyntheticCorrection()
    {
        var executable = Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_NATIVE_HOST_FIXTURE");
        if (string.IsNullOrEmpty(executable)) return;
        Assert.True(File.Exists(executable));
        using var provider = new PrioritizedCorrectionProvider(new FakeProvider());
        using var server = new BrowserBridgeServer(provider, () => Task.FromResult(new BrowserState(true, false, 3, 188)),
            _ => { }, () => true, key => key, BrowserProtocol.PipeName + ".Test");
        using (var rejected = Start(executable, "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/"))
        {
            await rejected.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, rejected.ExitCode);
        }
        using var host = Start(executable, "chrome-extension://pacghgdaoldcojhkbglolhbggdfjbgfb/");
        try
        {
            var message = new BrowserMessage(1, "correct", "native-roundtrip", "synthetic", 0, "fixture", 1, "Das ist ein Tset.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await BrowserProtocol.WriteAsync(host.StandardInput.BaseStream, JsonSerializer.SerializeToUtf8Bytes(message, BrowserProtocol.Json), timeout.Token);
            var response = await BrowserProtocol.ReadAsync(host.StandardOutput.BaseStream, timeout.Token);
            using var json = JsonDocument.Parse(response!);
            Assert.Equal("result", json.RootElement.GetProperty("type").GetString());
            Assert.Equal("Das ist ein Test.", json.RootElement.GetProperty("text").GetString());
            Assert.Equal(message.RequestId, json.RootElement.GetProperty("requestId").GetString());
            host.StandardInput.Close();
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, host.ExitCode);
        }
        finally { if (!host.HasExited) { host.Kill(); await host.WaitForExitAsync(); } }
        server.Dispose();
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static Process Start(string executable, string origin)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(origin);
        return Process.Start(start)!;
    }
    private sealed class FakeProvider : ICorrectionProvider
    {
        public CorrectionProviderKind Kind => CorrectionProviderKind.Luna;
        public Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("Das ist ein Tset.", text);
            return Task.FromResult(new CorrectionProviderResult("Das ist ein Test.", 1, Kind, null, TimeSpan.Zero));
        }
        public Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
