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
    public async Task RealLuna_RepeatedCorrectionsDoNotStartToolProcesses()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LUNA_RESOURCE_LIVE_TEST") != "1")
            return;

        using var client = new CodexAppServerClient(LunaLiveRuntime.CreateDirectory());
        await client.WarmUpAsync(CancellationToken.None);
        var process = Assert.IsType<Process>(typeof(CodexAppServerClient)
            .GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        var provider = new LunaCorrectionProvider(client, new AppLocalizer(), cacheEnabled: false);
        var observations = new List<object>();
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var result = await provider.CorrectAsync("Das ist ein fehler.", CancellationToken.None);
            Assert.Equal("Das ist ein Fehler.", result.CorrectedText);
            await client.WarmUpAsync(CancellationToken.None);
            var children = ChildProcesses(process.Id);
            process.Refresh();
            observations.Add(new
            {
                iteration,
                serverWorkingSetBytes = process.WorkingSet64,
                serverPrivateBytes = process.PrivateMemorySize64,
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
