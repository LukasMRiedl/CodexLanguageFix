[CmdletBinding()]
param([Parameter(Mandatory)][string]$HostExecutable)
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $HostExecutable).Path
if ([IO.Path]::GetFileName($resolved) -ne 'CodexLanguageFix.NativeHost.exe') { throw 'Expected CodexLanguageFix.NativeHost.exe.' }
$manifestPath = Join-Path (Split-Path -Parent $resolved) 'com.codexlanguagefix.chrome.json'
$key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.codexlanguagefix.chrome'
if ((Test-Path -LiteralPath $key) -and (Get-Item -LiteralPath $key).GetValue('') -ne $manifestPath) { throw 'A different installation already owns this host registration.' }
$manifest = [ordered]@{
    name = 'com.codexlanguagefix.chrome'
    description = 'Codex Language Fix Chrome bridge'
    path = $resolved
    type = 'stdio'
    allowed_origins = @('chrome-extension://pacghgdaoldcojhkbglolhbggdfjbgfb/')
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifestPath -Encoding utf8
New-Item -Path $key -Force | Out-Null
Set-Item -LiteralPath $key -Value $manifestPath
if ((Get-Item -LiteralPath $key).GetValue('') -ne $manifestPath) { throw 'Native host registration readback failed.' }
[pscustomobject]@{ Manifest = $manifestPath; Executable = $resolved; ExtensionId = 'pacghgdaoldcojhkbglolhbggdfjbgfb'; Registered = $true } | ConvertTo-Json
