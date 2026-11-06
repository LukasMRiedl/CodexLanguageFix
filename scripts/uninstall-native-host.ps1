[CmdletBinding()]
param([Parameter(Mandatory)][string]$HostExecutable)
$ErrorActionPreference = 'Stop'
$expectedExe = [IO.Path]::GetFullPath($HostExecutable)
if ([IO.Path]::GetFileName($expectedExe) -ne 'CodexLanguageFix.NativeHost.exe') { throw 'Expected CodexLanguageFix.NativeHost.exe.' }
$expectedManifest = Join-Path (Split-Path -Parent $expectedExe) 'com.codexlanguagefix.chrome.json'
$key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.codexlanguagefix.chrome'
if (Test-Path -LiteralPath $expectedManifest) {
    $manifest = Get-Content -Raw -LiteralPath $expectedManifest | ConvertFrom-Json
    if ($manifest.name -ne 'com.codexlanguagefix.chrome' -or $manifest.path -ne $expectedExe) { throw 'Manifest ownership mismatch; nothing removed.' }
}
if (Test-Path -LiteralPath $key) {
    if ((Get-Item -LiteralPath $key).GetValue('') -ne $expectedManifest) { throw 'Registration belongs to another installation; nothing removed.' }
    Remove-Item -LiteralPath $key
}
if (Test-Path -LiteralPath $expectedManifest) { Remove-Item -LiteralPath $expectedManifest }
