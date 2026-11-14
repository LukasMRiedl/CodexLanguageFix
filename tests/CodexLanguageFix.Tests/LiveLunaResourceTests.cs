using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using Xunit.Abstractions;

namespace CodexLanguageFix.Tests;

public sealed class LiveLunaResourceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealLuna_CanceledTurnUnloadsAndNextCorrectionRemainsUsable()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LUNA_RESOURCE_LIVE_TEST") != "1")
            return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var client = new CodexAppServerClient(LunaLiveRuntime.CreateDirectory());
        await client.WarmUpAsync(timeout.Token);
        var connection = Assert.IsType<JsonRpcLineConnection>(typeof(CodexAppServerClient)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer(), cacheEnabled: false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var pending = provider.CorrectAsync("Das ist ein fehler.", cancellation.Token);
        var turns = (System.Collections.ICollection)typeof(CodexAppServerClient)
            .GetField("_turns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        // Cancel an actual started turn, rather than a request that never left warmup.
        while (turns.Count == 0)
        {
            Assert.False(pending.IsCompleted, "The synthetic turn finished before cancellation could be tested.");
            await Task.Delay(10, timeout.Token);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await client.WarmUpAsync(timeout.Token);
        var unloadDeadline = Stopwatch.StartNew();
        var loadedThreadCount = int.MaxValue;
        while (unloadDeadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            var loaded = await connection.RequestAsync("thread/loaded/list", new { }, timeout.Token);
            loadedThreadCount = loaded.GetProperty("data").GetArrayLength();
            if (loadedThreadCount == 1) break;
            await Task.Delay(50, timeout.Token);
        }
        Assert.Equal(1, loadedThreadCount);
        var next = await provider.CorrectAsync("Das ist ein fehler.", timeout.Token);
        Assert.Equal("Das ist ein Fehler.", next.CorrectedText);
        output.WriteLine("Started synthetic turn canceled; only next prepared thread retained; next correction exact.");
    }

    [Fact]
    public async Task RealLuna_RepeatedCorrectionsDoNotStartToolProcesses()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LUNA_RESOURCE_LIVE_TEST") != "1")
            return;

        using var client = new CodexAppServerClient(LunaLiveRuntime.CreateDirectory());
        await client.WarmUpAsync(CancellationToken.None);
        var process = Assert.IsType<Process>(typeof(CodexAppServerClient)
            .GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        Assert.Contains("thread_unload_delay_secs=0", process.StartInfo.ArgumentList);
        var connection = Assert.IsType<JsonRpcLineConnection>(typeof(CodexAppServerClient)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        var config = await connection.RequestAsync("config/read", new { includeLayers = false }, CancellationToken.None);
        Assert.Equal(0, config.GetProperty("config").GetProperty("thread_unload_delay_secs").GetInt32());
        var provider = new LunaCorrectionProvider(client, new AppLocalizer(), cacheEnabled: false);
        var observations = new List<object>();
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var result = await provider.CorrectAsync("Das ist ein fehler.", CancellationToken.None);
            Assert.Equal("Das ist ein Fehler.", result.CorrectedText);
            await client.WarmUpAsync(CancellationToken.None);
            var unloadDeadline = Stopwatch.StartNew();
            var loadedThreadCount = int.MaxValue;
            while (unloadDeadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                var loaded = await connection.RequestAsync("thread/loaded/list", new { }, CancellationToken.None);
                loadedThreadCount = loaded.GetProperty("data").GetArrayLength();
                if (loadedThreadCount <= 1) break;
                await Task.Delay(50);
            }
            Assert.Equal(1, loadedThreadCount); // The prepared next correction must remain usable.
            var children = ChildProcesses(process.Id);
            process.Refresh();
            observations.Add(new
            {
                iteration,
                serverWorkingSetBytes = process.WorkingSet64,
                serverPrivateBytes = process.PrivateMemorySize64,
                loadedThreadCount,
                childCount = children.Count,
                toolProcessCount = children.Count(name => name != "conhost.exe")
            });
            Assert.All(children, name => Assert.Equal("conhost.exe", name));
        }
        var report = JsonSerializer.Serialize(new { measuredUtc = DateTime.UtcNow, observations });
        output.WriteLine(report);
        var reportDirectory = Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_RESOURCE_REPORT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(reportDirectory))
        {
            Directory.CreateDirectory(reportDirectory);
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, $"resource-{Guid.NewGuid():N}.json"), report);
        }
    }

    private static List<string> ChildProcesses(int parentId)
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        Assert.NotEqual(new nint(-1), snapshot);
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            var children = new List<string>();
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (entry.ParentProcessId == parentId)
                        children.Add(entry.Executable);
                } while (Process32Next(snapshot, ref entry));
            }
            return children;
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
