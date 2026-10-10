<#
.SYNOPSIS
Starts the desktop app through Windows Explorer, independently of the terminal.
#>
param([Parameter(Mandatory)][string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
if ([IO.Path]::GetExtension($executable) -ne '.exe') { throw 'Expected an executable file.' }

$processName = [IO.Path]::GetFileNameWithoutExtension($executable)
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    throw 'The app is already running. Stop it safely before restarting.'
}

# Use the desktop's out-of-process Shell object. A Shell object created in this
# terminal alone would not establish Explorer as the app's launcher.
$shell = New-Object -ComObject Shell.Application
$desktopHandle = 0
# SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1.
$desktop = $shell.Windows().FindWindowSW(0, 0, 8, [ref]$desktopHandle, 1)
if ($null -eq $desktop) { throw 'The interactive Windows desktop is unavailable.' }
$explorerIds = @(Get-Process explorer | Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId } | Select-Object -ExpandProperty Id)
# SW_SHOWNOACTIVATE = 4; the tray app controls when its overlay becomes visible.
$desktop.Document.Application.ShellExecute($executable, '', [IO.Path]::GetDirectoryName($executable), 'open', 4)

for ($attempt = 0; $attempt -lt 50; $attempt++) {
    $app = @(Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable })
    if ($app.Count -eq 1) {
        $created = Get-CimInstance Win32_Process -Filter "ProcessId=$($app[0].Id)"
        if ($created.ParentProcessId -notin $explorerIds) {
            throw "The app started, but its Explorer parent could not be verified (PID $($app[0].Id))."
        }
        return $app[0].Id
    }
    Start-Sleep -Milliseconds 100
}
throw 'The independent app did not start.'
