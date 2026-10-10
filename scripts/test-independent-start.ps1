<#
.SYNOPSIS
Opt-in Windows integration test. Starts the supplied app and leaves it running.
.DESCRIPTION
The executable must not already be running. A disposable job launches a control
process normally and the app through start-independent.ps1. Closing that job
must stop the control and leave the app alive. No app UI is edited.
#>
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [switch]$Child,
    [string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath

if ($Child) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class IndependentStartTestJob
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateJobObjectW(IntPtr attributes, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool member);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static IntPtr Enter()
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("This integration test requires x64.");
        var job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = Marshal.AllocHGlobal(144);
        try
        {
            Marshal.Copy(new byte[144], 0, limits, 144);
            // JOBOBJECT_EXTENDED_LIMIT_INFORMATION: KILL_ON_JOB_CLOSE.
            Marshal.WriteInt32(limits, 16, 0x2000);
            if (!SetInformationJobObject(job, 9, limits, 144) ||
                !AssignProcessToJobObject(job, Process.GetCurrentProcess().Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return job;
        }
        catch { CloseHandle(job); throw; }
        finally { Marshal.FreeHGlobal(limits); }
    }
    public static bool Contains(IntPtr job, int processId)
    {
        using (var process = Process.GetProcessById(processId))
        {
            if (!IsProcessInJob(process.Handle, job, out var member))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return member;
        }
    }
}
'@
    $job = [IndependentStartTestJob]::Enter()
    $control = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 60') -WindowStyle Hidden -PassThru
    $appId = & (Join-Path $PSScriptRoot 'start-independent.ps1') -ExecutablePath $executable
    if (-not [IndependentStartTestJob]::Contains($job, $control.Id)) { throw 'Control did not inherit the test job.' }
    if ([IndependentStartTestJob]::Contains($job, $appId)) { throw 'App inherited the test job.' }
    @{ controlId = $control.Id; appId = $appId } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath
    [IndependentStartTestJob]::CloseHandle($job) | Out-Null
    throw 'Closing the job should have terminated this child.'
}

if (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($executable)) -ErrorAction SilentlyContinue) {
    throw 'The target app is already running. This test requires a stopped app.'
}
$ResultPath = Join-Path ([IO.Path]::GetTempPath()) ('independent-start-' + [Guid]::NewGuid().ToString('N') + '.json')
$shell = (Get-Process -Id $PID).Path
$helper = Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-NonInteractive', '-File', ('"' + $PSCommandPath + '"'), '-Child', '-ExecutablePath', ('"' + $executable + '"'), '-ResultPath', ('"' + $ResultPath + '"')) -WindowStyle Hidden -RedirectStandardError ($ResultPath + '.stderr') -PassThru
try {
    if (-not $helper.WaitForExit(15000)) { throw 'Disposable job test timed out.' }
    if (-not (Test-Path -LiteralPath $ResultPath)) {
        throw "Test helper exited with $($helper.ExitCode): $(Get-Content -LiteralPath ($ResultPath + '.stderr') -Raw)"
    }
    $result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
    Start-Sleep -Seconds 3
    if (Get-Process -Id $result.controlId -ErrorAction SilentlyContinue) { throw 'The control process survived job closure.' }
    $app = Get-Process -Id $result.appId -ErrorAction Stop
    if ($app.Path -ne $executable -or -not $app.Responding) { throw 'The independent app is not healthy.' }
    [pscustomobject]@{ passed = $true; controlTerminated = $true; independentAppId = $app.Id; responding = $app.Responding }
}
finally {
    if (-not $helper.HasExited) { $helper.Kill(); $helper.WaitForExit(5000) | Out-Null }
    Remove-Item -LiteralPath $ResultPath, ($ResultPath + '.stderr') -ErrorAction SilentlyContinue
}
