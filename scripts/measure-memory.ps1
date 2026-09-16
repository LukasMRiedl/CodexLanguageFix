<#
.SYNOPSIS
Measures an existing Windows process and its descendants once per second.
.DESCRIPTION
DurationSeconds is the measured duration AFTER WarmupSeconds. Use identical host,
scenario, warmup, duration and build configuration for baseline and candidate.
Only aggregate bytes/counts and explicit run labels are saved: no process names,
IDs, command lines, editor text, credentials or environment variables.
Short-lived processes between samples cannot be observed. Root exit, an unreadable
live process or a sampling interval over 1.5 seconds makes the run incomplete.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateRange(1,2147483647)][int]$RootProcessId,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Scenario,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$BuildLabel,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$HostVersion,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Configuration,
    [Parameter(Mandatory)][ValidateRange(0,86400)][int]$WarmupSeconds,
    [Parameter(Mandatory)][ValidateRange(1,172800)][int]$DurationSeconds,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'Windows is required.' }
if (-not ('MemoryProcessSnapshot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class MemoryProcessSnapshot {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry {
        public uint Size, Usage, ProcessId;
        public UIntPtr Heap;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static Dictionary<int, int> Read() {
        var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) throw new Win32Exception();
        try {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
            var result = new Dictionary<int, int>();
            if (!Process32FirstW(handle, ref entry)) throw new Win32Exception();
            do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
            while (Process32NextW(handle, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception();
            return result;
        } finally { CloseHandle(handle); }
    }
}
'@
}
$root = [Diagnostics.Process]::GetProcessById($RootProcessId)
$rootStarted = $root.StartTime.ToUniversalTime().Ticks
$root.Dispose()
$known = @{ $RootProcessId = $rootStarted }
$samples = [Collections.Generic.List[object]]::new()
$runStarted = [DateTime]::UtcNow.ToString('o')
$timer = [Diagnostics.Stopwatch]::StartNew()
$complete = $true
$failure = $null
try {
    for ($index = 0; $index -lt ($WarmupSeconds + $DurationSeconds); $index++) {
        # Windows may return from Sleep slightly early; measure only after the deadline.
        while (($remainingMs = $index * 1000 - $timer.Elapsed.TotalMilliseconds) -gt 0) {
            Start-Sleep -Milliseconds ([int][Math]::Ceiling($remainingMs))
        }
        $sampleTime = $timer.Elapsed.TotalSeconds
        if ($sampleTime - $index -gt 0.5) { $complete = $false; $failure = 'sampling-deadline-missed' }
        $parents = [MemoryProcessSnapshot]::Read()
        $members = [Collections.Generic.Dictionary[int, object]]::new()
        try {
            # Retain descendants across samples, but reject PID reuse by start time.
            foreach ($knownId in @($known.Keys)) {
                if (-not $parents.ContainsKey([int]$knownId)) { $known.Remove($knownId); continue }
                $process = [Diagnostics.Process]::GetProcessById([int]$knownId)
                if ($process.StartTime.ToUniversalTime().Ticks -ne $known[$knownId]) {
                    $process.Dispose(); $known.Remove($knownId); continue
                }
                $members.Add([int]$knownId, $process)
            }
            if (-not $members.ContainsKey($RootProcessId)) { throw 'root-exited' }
            do {
                $added = $false
                foreach ($entry in $parents.GetEnumerator()) {
                    if ($members.ContainsKey($entry.Key) -or -not $members.ContainsKey($entry.Value)) { continue }
                    try { $process = [Diagnostics.Process]::GetProcessById($entry.Key) }
                    catch [ArgumentException] { continue } # Exited after the snapshot.
                    $start = $process.StartTime.ToUniversalTime().Ticks
                    if ($start -lt $known[$entry.Value]) { $process.Dispose(); continue }
                    $known[$entry.Key] = $start
                    $members.Add($entry.Key, $process)
                    $added = $true
                }
            } while ($added)
            [long]$privateBytes = 0
            [long]$workingSetBytes = 0
            $count = 0
            foreach ($process in $members.Values) {
                $process.Refresh()
                if ($process.HasExited) { continue }
                $privateBytes += $process.PrivateMemorySize64
                $workingSetBytes += $process.WorkingSet64
                $count++
            }
            if ($index -ge $WarmupSeconds) {
                $samples.Add([ordered]@{
                    elapsedSeconds = [Math]::Round($sampleTime, 3)
                    processCount = $count
                    privateBytes = $privateBytes
                    workingSetBytes = $workingSetBytes
                })
            }
        } finally { foreach ($process in $members.Values) { $process.Dispose() } }
    }
} catch {
    $complete = $false
    # Avoid serializing exception text containing paths or other local details.
    $failure = 'process-enumeration-or-memory-read-failed'
    Write-Warning 'The run is incomplete: a process exited or process memory could not be read.'
}
$report = [ordered]@{
    schemaVersion = 1
    startedUtc = $runStarted
    scenario = $Scenario
    buildLabel = $BuildLabel
    hostVersion = $HostVersion
    configuration = $Configuration
    osVersion = [Environment]::OSVersion.Version.ToString()
    osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    warmupSeconds = $WarmupSeconds
    durationSeconds = $DurationSeconds
    sampleIntervalSeconds = 1
    complete = ($complete -and $samples.Count -eq $DurationSeconds)
    failure = $failure
    samples = $samples.ToArray()
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
if (-not $report.complete) { exit 2 }
