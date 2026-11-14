<#
.SYNOPSIS
Measures an existing Windows process and its descendants, including private resident RAM.
.DESCRIPTION
DurationSeconds is the measured duration AFTER WarmupSeconds. Use identical host,
scenario, warmup, duration and build configuration for baseline and candidate.
Only aggregate bytes/counts and explicit run labels are saved: no process names,
IDs, command lines, editor text, credentials or environment variables.
Short-lived processes between samples cannot be observed. Root exit, an unreadable
live process or a missed sampling deadline by more than 500 ms makes the run incomplete.
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
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateNotNullOrEmpty()][string]$NativeHostExecutablePath,
    [ValidateSet(100,1000)][int]$SampleIntervalMilliseconds = 100
)
$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'Windows is required.' }
if (-not [Environment]::Is64BitProcess) { throw '64-bit PowerShell is required for working-set snapshots.' }
$nativeHostPath = $null
if ($PSBoundParameters.ContainsKey('NativeHostExecutablePath')) {
    if (-not [IO.Path]::IsPathFullyQualified($NativeHostExecutablePath)) { throw 'Native host requires a full executable path.' }
    $nativeHostPath = [IO.Path]::GetFullPath($NativeHostExecutablePath)
    if (-not [IO.File]::Exists($nativeHostPath)) { throw 'Native host executable does not exist.' }
}
if (-not ('MemoryProcessSnapshot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
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
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool QueryWorkingSet(IntPtr process, [In, Out] ulong[] buffer, uint bytes);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters {
        public uint Size, Faults;
        public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool,
            PageFile, PeakPageFile, PrivateUsage;
    }
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint bytes);
    private static ulong[] workingSetBuffer = new ulong[8192];
    private static Win32Exception NativeError(string operation, int? code = null) {
        var exception = new Win32Exception(code ?? Marshal.GetLastWin32Error());
        exception.Data["MemoryOperation"] = operation; // Fixed technical label, never a path or PID.
        return exception;
    }
    // Image queries can fail during teardown before the handle becomes signaled.
    // Only a confirmed exit may be ignored; an unreadable live process still fails.
    private static bool HasExited(IntPtr process) => WaitForSingleObject(process, 50) == 0; // WAIT_OBJECT_0, including exit code 259.
    public static long[] ReadWorkingSet(int id, long expectedStarted) {
        var process = OpenProcess(0x410, false, id); // QUERY_INFORMATION | VM_READ; does not change the target.
        if (process == IntPtr.Zero) throw NativeError("open-working-set-process");
        try {
            if (!GetProcessTimes(process, out var created, out _, out _, out _)) throw NativeError("read-process-times");
            if (DateTime.FromFileTimeUtc(created).Ticks != expectedStarted) throw new InvalidOperationException("Process identity changed.");
            var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
            if (!GetProcessMemoryInfo(process, ref counters, counters.Size)) throw NativeError("read-private-commit");
            while (!QueryWorkingSet(process, workingSetBuffer, checked((uint)(workingSetBuffer.Length * 8)))) {
                var error = Marshal.GetLastWin32Error();
                if ((error != 24 && error != 122) || workingSetBuffer.Length >= 8388608) throw NativeError("query-working-set", error);
                workingSetBuffer = new ulong[workingSetBuffer.Length * 2];
            }
            var count = checked((int)workingSetBuffer[0]);
            if (count < 0 || count >= workingSetBuffer.Length) throw new InvalidOperationException("Invalid working set.");
            long privatePages = 0;
            for (var i = 1; i <= count; i++)
                if ((workingSetBuffer[i] & 0x100UL) == 0) privatePages++; // PSAPI_WORKING_SET_BLOCK.Shared
            return new[] { (long)count * Environment.SystemPageSize, privatePages * Environment.SystemPageSize,
                checked((long)counters.PrivateUsage.ToUInt64()) };
        } finally { CloseHandle(process); }
    }
    public static Dictionary<int, long> MatchingNativeHosts { get; private set; } = new();
    private static bool MatchesPath(int id, string expected, out long started) {
        started = 0;
        var process = OpenProcess(0x101000, false, id); // QUERY_LIMITED_INFORMATION | SYNCHRONIZE
        if (process == IntPtr.Zero) {
            var error = Marshal.GetLastWin32Error();
            if (error == 87) return false; // Process exited after the Toolhelp snapshot.
            throw NativeError("open-native-host", error);
        }
        try {
            uint length = 32768;
            var path = new StringBuilder((int)length);
            if (!QueryFullProcessImageNameW(process, 0, path, ref length)) {
                var error = Marshal.GetLastWin32Error();
                if (HasExited(process)) return false;
                throw NativeError("native-host-image", error);
            }
            if (!string.Equals(Path.GetFullPath(path.ToString()), expected, StringComparison.OrdinalIgnoreCase)) return false;
            if (!GetProcessTimes(process, out var creation, out _, out _, out _)) {
                var error = Marshal.GetLastWin32Error();
                if (HasExited(process)) return false;
                throw NativeError("native-host-start-time", error);
            }
            started = DateTime.FromFileTimeUtc(creation).Ticks;
            return true;
        } finally { CloseHandle(process); }
    }
    public static Dictionary<int, int> Read(string nativeHostExecutablePath = null) {
        MatchingNativeHosts = new Dictionary<int, long>();
        var handle = CreateToolhelp32Snapshot(2, 0);
        // Toolhelp can report a changing snapshot length. Retry only this specific
        // transient error, with a fixed bound; other live-read failures remain fatal.
        for (var attempt = 1; handle == new IntPtr(-1) && Marshal.GetLastWin32Error() == 24 && attempt < 3; attempt++)
            handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) throw NativeError("create-process-snapshot");
        try {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
            var result = new Dictionary<int, int>();
            if (!Process32FirstW(handle, ref entry)) throw NativeError("first-process-entry");
            do {
                var id = (int)entry.ProcessId;
                result[id] = (int)entry.ParentProcessId;
                if (nativeHostExecutablePath != null
                    && string.Equals(entry.Exe, Path.GetFileName(nativeHostExecutablePath), StringComparison.OrdinalIgnoreCase)
                    && MatchesPath(id, nativeHostExecutablePath, out var started)) MatchingNativeHosts.Add(id, started);
            } while (Process32NextW(handle, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw NativeError("next-process-entry");
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
$failureDetails = $null
$samplesPerSecond = 1000 / $SampleIntervalMilliseconds
$warmupSamples = $WarmupSeconds * $samplesPerSecond
$measuredSamples = $DurationSeconds * $samplesPerSecond
$maximumLagSeconds = 0.0
$maximumSampleGapSeconds = 0.0
$previousMeasuredTime = $null
$failureStage = 'initialization'
function Get-SnapshotProcess([int]$Id) {
    try { $process = [Diagnostics.Process]::GetProcessById($Id) }
    catch [ArgumentException] { return $null } # Exited after the Toolhelp snapshot.
    try {
        # StartTime also races process exit. Do not confuse an exited child with an
        # unreadable live process; the latter must still invalidate the measurement.
        $null = $process.StartTime
        return $process
    } catch {
        $exited = $process.HasExited
        $process.Dispose()
        if ($exited) { return $null }
        throw
    }
}
try {
    for ($index = 0; $index -lt ($warmupSamples + $measuredSamples); $index++) {
        # Windows may return from Sleep slightly early; measure only after the deadline.
        while (($remainingMs = $index * $SampleIntervalMilliseconds - $timer.Elapsed.TotalMilliseconds) -gt 0) {
            Start-Sleep -Milliseconds ([int][Math]::Ceiling($remainingMs))
        }
        $sampleTime = $timer.Elapsed.TotalSeconds
        $maximumLagSeconds = [Math]::Max($maximumLagSeconds, $sampleTime - $index * $SampleIntervalMilliseconds / 1000)
        if ($sampleTime - $index * $SampleIntervalMilliseconds / 1000 -gt 0.5) {
            $failureStage = 'sampling-deadline-missed'
            throw 'sampling-deadline-missed'
        }
        $failureStage = 'process-enumeration'
        $parents = [MemoryProcessSnapshot]::Read($nativeHostPath)
        $members = [Collections.Generic.Dictionary[int, object]]::new()
        try {
            # Retain descendants across samples, but reject PID reuse by start time.
            $failureStage = 'known-process-identity'
            foreach ($knownId in @($known.Keys)) {
                if (-not $parents.ContainsKey([int]$knownId)) { $known.Remove($knownId); continue }
                $process = Get-SnapshotProcess ([int]$knownId)
                if ($null -eq $process) { $known.Remove($knownId); continue }
                if ($process.StartTime.ToUniversalTime().Ticks -ne $known[$knownId]) {
                    $process.Dispose(); $known.Remove($knownId); continue
                }
                $members.Add([int]$knownId, $process)
            }
            if (-not $members.ContainsKey($RootProcessId)) { $failureStage = 'root-exited'; throw 'root-exited' }
            # Chrome launches the bridge outside the desktop app's descendant tree.
            # Exact image matching is repeated each sample; known instances retain start-time guards.
            $failureStage = 'native-host-identity'
            foreach ($nativeId in [MemoryProcessSnapshot]::MatchingNativeHosts.Keys) {
                if ($members.ContainsKey($nativeId)) { continue }
                $process = Get-SnapshotProcess $nativeId
                if ($null -eq $process) { continue }
                try {
                    $start = $process.StartTime.ToUniversalTime().Ticks
                    if ($start -ne [MemoryProcessSnapshot]::MatchingNativeHosts[$nativeId]) { $process.Dispose(); continue }
                    $known[$nativeId] = $start
                    $members.Add($nativeId, $process)
                } catch { $process.Dispose(); throw }
            }
            $failureStage = 'descendant-identity'
            do {
                $added = $false
                foreach ($entry in $parents.GetEnumerator()) {
                    if ($members.ContainsKey($entry.Key) -or -not $members.ContainsKey($entry.Value)) { continue }
                    $process = Get-SnapshotProcess $entry.Key
                    if ($null -eq $process) { continue }
                    $start = $process.StartTime.ToUniversalTime().Ticks
                    if ($start -lt $known[$entry.Value]) { $process.Dispose(); continue }
                    $known[$entry.Key] = $start
                    $members.Add($entry.Key, $process)
                    $added = $true
                }
            } while ($added)
            [long]$privateBytes = 0
            [long]$workingSetBytes = 0
            [long]$privateWorkingSetBytes = 0
            $count = 0
            $failureStage = 'working-set-read'
            foreach ($process in $members.Values) {
                $process.Refresh()
                if ($process.HasExited) { continue }
                try { $resident = [MemoryProcessSnapshot]::ReadWorkingSet($process.Id, $known[$process.Id]) }
                catch { if ($process.HasExited -and $process.Id -ne $RootProcessId) { continue }; throw }
                $privateBytes += $resident[2]
                $workingSetBytes += $resident[0]
                $privateWorkingSetBytes += $resident[1]
                $count++
            }
            if ($members[$RootProcessId].HasExited) { $failureStage = 'root-exited'; throw 'root-exited' }
            if ($index -ge $warmupSamples) {
                if ($null -ne $previousMeasuredTime) { $maximumSampleGapSeconds = [Math]::Max($maximumSampleGapSeconds, $sampleTime - $previousMeasuredTime) }
                $previousMeasuredTime = $sampleTime
                $samples.Add([ordered]@{
                    elapsedSeconds = [Math]::Round($sampleTime, 3)
                    processCount = $count
                    privateBytes = $privateBytes
                    workingSetBytes = $workingSetBytes
                    privateWorkingSetBytes = $privateWorkingSetBytes
                })
            }
        } finally { foreach ($process in $members.Values) { $process.Dispose() } }
    }
} catch {
    $complete = $false
    # Avoid serializing exception text containing paths or other local details.
    $failure = $failureStage
    $cause = $_.Exception.GetBaseException()
    $failureDetails = [ordered]@{
        exceptionType = $cause.GetType().Name
        nativeOperation = $cause.Data['MemoryOperation']
        nativeErrorCode = $(if ($cause -is [ComponentModel.Win32Exception]) { $cause.NativeErrorCode } else { $null })
    }
    Write-Warning 'The run is incomplete: a process exited or process memory could not be read.'
}
$summaries = [ordered]@{}
if ($samples.Count -gt 0) {
    foreach ($metric in @('privateBytes','workingSetBytes','privateWorkingSetBytes')) {
        $values = [double[]]@($samples | ForEach-Object { $_[$metric] })
        $sorted = [double[]]$values.Clone()
        [Array]::Sort($sorted)
        $middle = [int][Math]::Floor($sorted.Length / 2)
        $median = if ($sorted.Length % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
        $half = [int][Math]::Floor($values.Length / 2)
        $growth = 0.0
        if ($half -gt 0) {
            $first = [double[]]$values[0..($half - 1)]
            $last = [double[]]$values[$half..($values.Length - 1)]
            [Array]::Sort($first); [Array]::Sort($last)
            $firstMiddle = [int][Math]::Floor($first.Length / 2)
            $lastMiddle = [int][Math]::Floor($last.Length / 2)
            $firstMedian = if ($first.Length % 2) { $first[$firstMiddle] } else { ($first[$firstMiddle - 1] + $first[$firstMiddle]) / 2 }
            $lastMedian = if ($last.Length % 2) { $last[$lastMiddle] } else { ($last[$lastMiddle - 1] + $last[$lastMiddle]) / 2 }
            $growth = $lastMedian - $firstMedian
        }
        $summaries[$metric] = [ordered]@{
            meanBytes = ($values | Measure-Object -Average).Average
            medianBytes = $median
            p95Bytes = $sorted[[int][Math]::Ceiling($sorted.Length * 0.95) - 1]
            maxBytes = $sorted[-1]
            secondHalfGrowthBytes = $growth
        }
    }
}
$report = [ordered]@{
    schemaVersion = 2
    nativeHostIncluded = ($null -ne $nativeHostPath)
    startedUtc = $runStarted
    scenario = $Scenario
    buildLabel = $BuildLabel
    hostVersion = $HostVersion
    configuration = $Configuration
    osVersion = [Environment]::OSVersion.Version.ToString()
    osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    warmupSeconds = $WarmupSeconds
    durationSeconds = $DurationSeconds
    sampleIntervalSeconds = $SampleIntervalMilliseconds / 1000
    samplingToleranceSeconds = 0.5
    maximumSampleLagSeconds = [Math]::Round($maximumLagSeconds, 6)
    maximumSampleGapSeconds = [Math]::Round($maximumSampleGapSeconds, 6)
    complete = ($complete -and $samples.Count -eq $measuredSamples)
    failure = $failure
    failureDetails = $failureDetails
    summaries = $summaries
    samples = $samples.ToArray()
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
if (-not $report.complete) { exit 2 }
exit 0
