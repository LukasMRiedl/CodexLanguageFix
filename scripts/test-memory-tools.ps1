$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('memory-tools-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testDirectory
$shell = (Get-Command pwsh -ErrorAction Stop).Source
$baselinePath = Join-Path $testDirectory 'baseline.json'
$candidatePath = Join-Path $testDirectory 'candidate.json'
$resultPath = Join-Path $testDirectory 'result.json'
function New-Report([double[]]$Private, [double[]]$WorkingSet) {
    return @{
        schemaVersion = 1; scenario = 'synthetic'; hostVersion = 'test'; configuration = 'Release'
        buildLabel = 'fixture'; osVersion = 'test'; osArchitecture = 'X64'
        warmupSeconds = 2; durationSeconds = $Private.Count; sampleIntervalSeconds = 1; complete = $true
        samples = @(for ($i = 0; $i -lt $Private.Count; $i++) {
            @{ elapsedSeconds = 2 + $i; processCount = 2; privateBytes = $Private[$i]; workingSetBytes = $WorkingSet[$i] }
        })
    }
}
function Compare-Fixture($Before, $After, [int]$ExpectedExit) {
    $Before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $baselinePath
    $After | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $candidatePath
    & $shell -NoProfile -File (Join-Path $PSScriptRoot 'compare-memory.ps1') -BaselinePath $baselinePath -CandidatePath $candidatePath -OutputPath $resultPath *> $null
    if ($LASTEXITCODE -ne $ExpectedExit) { throw "Expected exit $ExpectedExit, got $LASTEXITCODE." }
}
$before = New-Report @(100,200,300,400) @(100,200,300,400)
$after = New-Report @(20,40,60,80) @(20,40,60,80)
Compare-Fixture $before $after 0
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if ($result.gates[0].baselineBytes -ne 250 -or $result.gates[1].baselineBytes -ne 400 -or $result.gates.Count -ne 4) { throw 'Median or nearest-rank p95 is incorrect.' }
# Working set must independently pass even when private memory meets the goal.
Compare-Fixture $before (New-Report @(20,40,60,80) @(21,42,63,84)) 2
# A good median cannot conceal a failing tail.
Compare-Fixture $before (New-Report @(20,40,60,81) @(20,40,60,80)) 2
$after.complete = $false
Compare-Fixture $before $after 1
$after.complete = $true
$after.hostVersion = 'different'
Compare-Fixture $before $after 1
$after.hostVersion = 'test'
$after.samples[0].privateBytes = 0
Compare-Fixture $before $after 1
$after.samples[0].privateBytes = 20
$after.samples[1].elapsedSeconds = 3.6
Compare-Fixture $before $after 1
# Exercise real Windows scheduling. The child collector measures this test process.
$smokePath = Join-Path $testDirectory 'collector-smoke.json'
& $shell -NoProfile -File (Join-Path $PSScriptRoot 'measure-memory.ps1') -RootProcessId $PID -Scenario collector-smoke -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 1 -DurationSeconds 5 -OutputPath $smokePath
if ($LASTEXITCODE -ne 0) { throw 'Real memory collector smoke failed.' }
$smoke = Get-Content -LiteralPath $smokePath -Raw | ConvertFrom-Json
if (-not $smoke.complete -or $smoke.samples.Count -ne 5) { throw 'Real memory collector report is incomplete.' }
for ($i = 0; $i -lt $smoke.samples.Count; $i++) {
    if ($smoke.samples[$i].elapsedSeconds -lt ($smoke.warmupSeconds + $i)) {
        throw 'Real memory collector sampled before its deadline.'
    }
}
Write-Output "Memory comparison tests passed. Synthetic fixtures: $testDirectory"
