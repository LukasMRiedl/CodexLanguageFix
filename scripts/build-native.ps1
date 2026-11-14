[CmdletBinding()]
param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\native\build'),
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\dist\native-win-x64')
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\native'))
$cmakeCommand = Get-Command cmake.exe -ErrorAction SilentlyContinue
if ($cmakeCommand) { $cmakePath = $cmakeCommand.Source }
else {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Install Visual Studio Build Tools 2022 with Desktop development with C++, CMake and Windows SDK.' }
    $installation = & $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmakePath = Join-Path $installation 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if (-not (Test-Path -LiteralPath $cmakePath)) { throw 'The installed C++ toolchain does not include CMake.' }
}
& $cmakePath -S $source -B $BuildDirectory -G 'Visual Studio 17 2022' -A x64 -T v143
if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
& $cmakePath --build $BuildDirectory --config Release --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
$ctestPath = Join-Path (Split-Path -Parent $cmakePath) 'ctest.exe'
& $ctestPath --test-dir $BuildDirectory -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'Native tests failed. No product package was generated.' }
& $cmakePath --install $BuildDirectory --config Release --prefix $PackageDirectory
if ($LASTEXITCODE -ne 0) { throw 'Native package generation failed.' }
Get-ChildItem -LiteralPath $PackageDirectory -Recurse -File | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($PackageDirectory,$_.FullName);Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
}
