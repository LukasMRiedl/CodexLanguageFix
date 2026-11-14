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
function Compare-Fixture($Before, $After, [int]$ExpectedExit, [switch]$AnyReduction, [switch]$PrivateRamReduction) {
    $Before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $baselinePath
    $After | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $candidatePath
    if ($PrivateRamReduction) {
        & $shell -NoProfile -File (Join-Path $PSScriptRoot 'compare-memory.ps1') -BaselinePath $baselinePath -CandidatePath $candidatePath -OutputPath $resultPath -PrivateRamReduction *> $null
    } elseif ($AnyReduction) {
        & $shell -NoProfile -File (Join-Path $PSScriptRoot 'compare-memory.ps1') -BaselinePath $baselinePath -CandidatePath $candidatePath -OutputPath $resultPath -AnyReduction *> $null
    } else {
        & $shell -NoProfile -File (Join-Path $PSScriptRoot 'compare-memory.ps1') -BaselinePath $baselinePath -CandidatePath $candidatePath -OutputPath $resultPath *> $null
    }
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
# Current acceptance is strict reduction in all four statistics, with full 30-minute samples.
$fullBefore = New-Report ([double[]](,100.0 * 1800)) ([double[]](,100.0 * 1800))
$fullAfter = New-Report ([double[]](,99.999 * 1800)) ([double[]](,99.999 * 1800))
Compare-Fixture $fullBefore $fullAfter 0 -AnyReduction
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if ($result.requiredReductionPercent -ne 0 -or -not $result.strictReduction -or $result.gates.Count -ne 4) { throw 'Strict-reduction gate metadata is incorrect.' }
Compare-Fixture $fullBefore $fullBefore 2 -AnyReduction
Compare-Fixture $fullBefore (New-Report ([double[]](,99.0 * 1800)) ([double[]](,100.0 * 1800))) 2 -AnyReduction
Compare-Fixture (New-Report @(100,100) @(100,100)) (New-Report @(99,99) @(99,99)) 1 -AnyReduction
$fullAfter.nativeHostIncluded = $true
Compare-Fixture $fullBefore $fullAfter 1 -AnyReduction
$fullAfter.nativeHostIncluded = $false
Compare-Fixture $fullBefore $fullAfter 0 -AnyReduction # Missing historical field means false.
$fullBefore.nativeHostIncluded = $true
$fullAfter.nativeHostIncluded = $true
Compare-Fixture $fullBefore $fullAfter 0 -AnyReduction
Compare-Fixture $fullBefore $fullAfter 2 # Historical 80-percent gate remains unchanged.
# Private resident memory is distinct from both private commit and total working set.
function New-ResidentReport([double]$Resident) {
    $report = New-Report ([double[]](,100000000.0 * 18000)) ([double[]](,200000000.0 * 18000))
    $report.schemaVersion = 2
    $report.durationSeconds = 1800
    $report.sampleIntervalSeconds = 0.1
    for ($i = 0; $i -lt $report.samples.Count; $i++) {
        $report.samples[$i].elapsedSeconds = [Math]::Round(2 + $i / 10, 3)
        $report.samples[$i].privateWorkingSetBytes = $Resident
    }
    return $report
}
$residentBefore = New-ResidentReport 60000000
$residentAfter = New-ResidentReport 49000000
Compare-Fixture $residentBefore $residentAfter 0 -PrivateRamReduction
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if ($result.gates.Count -ne 2 -or -not $result.privateRamTargetMet -or $result.acceptanceMode -ne 'private-resident-ram' -or
    @($result.summaries | Where-Object { $_.metric -eq 'privateWorkingSetBytes' -and $_.maxBytes -eq 49000000 }).Count -ne 1) { throw 'Private resident statistics or acceptance are incorrect.' }
Compare-Fixture $residentBefore $residentBefore 2 -PrivateRamReduction
Compare-Fixture $residentBefore (New-ResidentReport 55000000) 0 -PrivateRamReduction
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if ($result.privateRamTargetMet) { throw 'A real improvement above 50 MB must not be labelled below target.' }
Compare-Fixture $fullBefore $fullAfter 1 -PrivateRamReduction
$residentAfter.samples[0].Remove('privateWorkingSetBytes')
Compare-Fixture $residentBefore $residentAfter 1 -PrivateRamReduction
# Exercise real Windows scheduling. The child collector measures this test process.
$smokePath = Join-Path $testDirectory 'collector-smoke.json'
& $shell -NoProfile -File (Join-Path $PSScriptRoot 'measure-memory.ps1') -RootProcessId $PID -Scenario collector-smoke -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 1 -DurationSeconds 5 -OutputPath $smokePath
if ($LASTEXITCODE -ne 0) { throw 'Real memory collector smoke failed.' }
$smoke = Get-Content -LiteralPath $smokePath -Raw | ConvertFrom-Json
if (-not $smoke.complete -or $smoke.samples.Count -ne 50 -or $smoke.schemaVersion -ne 2 -or $smoke.sampleIntervalSeconds -ne 0.1) { throw 'Real memory collector report is incomplete.' }
for ($i = 0; $i -lt $smoke.samples.Count; $i++) {
    if ($smoke.samples[$i].elapsedSeconds -lt ($smoke.warmupSeconds + $i / 10 - 0.000001)) {
        throw 'Real memory collector sampled before its deadline.'
    }
    if ($smoke.samples[$i].privateWorkingSetBytes -le 0 -or $smoke.samples[$i].privateWorkingSetBytes -gt $smoke.samples[$i].workingSetBytes) { throw 'Resident private memory must be a nonzero subset of the same working-set snapshot.' }
}
# A suspended/late collector must save its partial report immediately instead of
# producing all scheduled samples in a catch-up loop. Delay the real sleep command.
$deadlinePath = Join-Path $testDirectory 'missed-deadline.json'
$deadlineScript = @'
function Start-Sleep { param([int]$Milliseconds) [Threading.Thread]::Sleep($Milliseconds + 1000) }
& 'MEASURE_SCRIPT' -RootProcessId $PID -Scenario missed-deadline -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 0 -DurationSeconds 5 -OutputPath 'REPORT_PATH'
exit $LASTEXITCODE
'@
$deadlineScript = $deadlineScript.Replace('MEASURE_SCRIPT',
    (Join-Path $PSScriptRoot 'measure-memory.ps1').Replace("'", "''")).Replace('REPORT_PATH', $deadlinePath.Replace("'", "''"))
& $shell -NoProfile -NonInteractive -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($deadlineScript))) *> $null
if ($LASTEXITCODE -ne 2) { throw 'Missed deadline must return an incomplete measurement.' }
$deadline = Get-Content -LiteralPath $deadlinePath -Raw | ConvertFrom-Json
if ($deadline.complete -or $deadline.failure -ne 'sampling-deadline-missed' -or $deadline.samples.Count -ne 1) {
    throw 'A missed deadline must stop and preserve only the already measured sample.'
}
# Descendants can disappear between enumeration, identity lookup and memory read.
# Exercise real exit churn without relaxing the rules for unreadable live processes.
$churnScript = @'
$timer = [Diagnostics.Stopwatch]::StartNew()
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\cmd.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in @('/d','/c','exit 0')) { $start.ArgumentList.Add($argument) }
while ($timer.Elapsed.TotalSeconds -lt 30) {
    $child = [Diagnostics.Process]::Start($start)
    try { $child.WaitForExit() } finally { $child.Dispose() }
    Start-Sleep -Milliseconds 10
}
'@
$churnStart = [Diagnostics.ProcessStartInfo]::new($shell)
$churnStart.UseShellExecute = $false
$churnStart.CreateNoWindow = $true
foreach ($argument in @('-NoProfile','-NonInteractive','-EncodedCommand',
    [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($churnScript)))) { $churnStart.ArgumentList.Add($argument) }
$churn = [Diagnostics.Process]::Start($churnStart)
try {
    $churnPath = Join-Path $testDirectory 'child-exit-churn.json'
    & $shell -NoProfile -File (Join-Path $PSScriptRoot 'measure-memory.ps1') -RootProcessId $churn.Id -Scenario child-exit-churn -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 1 -DurationSeconds 5 -OutputPath $churnPath
    if ($LASTEXITCODE -ne 0) { throw 'Collector rejected normal descendant exits.' }
    $churnReport = Get-Content -LiteralPath $churnPath -Raw | ConvertFrom-Json
    if (-not $churnReport.complete -or $churnReport.samples.Count -ne 50) { throw 'Child-exit churn measurement is incomplete.' }
} finally {
    if (-not $churn.HasExited) { $churn.Kill($true); $churn.WaitForExit(5000) | Out-Null }
    $churn.Dispose()
}
# A bridge launched by Chrome is a sibling of the desktop root, not its descendant.
# Two copies have the same file name; only the exact configured image and its child may count.
$hostName = 'memory-native-host-' + [Guid]::NewGuid().ToString('N') + '.exe'
$allowedDirectory = Join-Path $testDirectory 'allowed'
$decoyDirectory = Join-Path $testDirectory 'decoy'
$null = New-Item -ItemType Directory -Path $allowedDirectory, $decoyDirectory
$allowedExecutable = Join-Path $allowedDirectory $hostName
$decoyExecutable = Join-Path $decoyDirectory $hostName
Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\cmd.exe') -Destination $allowedExecutable
Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\cmd.exe') -Destination $decoyExecutable
$ownedProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()
try {
    $measuredRoot = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\ping.exe') -ArgumentList '127.0.0.1', '-n', '90' -WindowStyle Hidden -PassThru
    $ownedProcesses.Add($measuredRoot)
    $allowedHost = Start-Process -FilePath $allowedExecutable -ArgumentList '/d', '/c', 'ping.exe 127.0.0.1 -n 90 >nul' -WindowStyle Hidden -PassThru
    $ownedProcesses.Add($allowedHost)
    $ownedProcesses.Add((Start-Process -FilePath $decoyExecutable -ArgumentList '/d', '/c', 'ping.exe 127.0.0.1 -n 90 >nul' -WindowStyle Hidden -PassThru))
    Start-Sleep -Milliseconds 500
    # Independent fixture inventory includes Windows-created console helper children.
    $fixtureInventory = @(Get-CimInstance Win32_Process)
    function Count-FixtureTree([int[]]$Roots) {
        $ids = [Collections.Generic.HashSet[int]]::new()
        foreach ($id in $Roots) { $null = $ids.Add($id) }
        do {
            $added = $false
            foreach ($entry in $fixtureInventory) {
                if ($ids.Contains([int]$entry.ParentProcessId) -and $ids.Add([int]$entry.ProcessId)) { $added = $true }
            }
        } while ($added)
        return $ids.Count
    }
    $expectedDefaultCount = Count-FixtureTree @($measuredRoot.Id)
    $expectedBridgeCount = Count-FixtureTree @($measuredRoot.Id, $allowedHost.Id)
    if ($expectedBridgeCount -lt ($expectedDefaultCount + 2)) { throw 'Native-host fixture did not create its expected child.' }
    $defaultScopePath = Join-Path $testDirectory 'default-scope.json'
    & $shell -NoProfile -File (Join-Path $PSScriptRoot 'measure-memory.ps1') -RootProcessId $measuredRoot.Id -Scenario scope-default -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 2 -DurationSeconds 3 -OutputPath $defaultScopePath
    if ($LASTEXITCODE -ne 0) { throw 'Default-scope collector failed.' }
    $defaultScope = Get-Content -LiteralPath $defaultScopePath -Raw | ConvertFrom-Json
    if ($defaultScope.nativeHostIncluded -or @($defaultScope.samples | Where-Object processCount -ne $expectedDefaultCount).Count) { throw 'Default scope unexpectedly counted sibling processes.' }
    $bridgeScopePath = Join-Path $testDirectory 'bridge-scope.json'
    & $shell -NoProfile -File (Join-Path $PSScriptRoot 'measure-memory.ps1') -RootProcessId $measuredRoot.Id -NativeHostExecutablePath $allowedExecutable -Scenario scope-bridge -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 2 -DurationSeconds 3 -OutputPath $bridgeScopePath
    if ($LASTEXITCODE -ne 0) { throw 'Native-host collector failed.' }
    $bridgeRaw = Get-Content -LiteralPath $bridgeScopePath -Raw
    $bridgeScope = $bridgeRaw | ConvertFrom-Json
    if (-not $bridgeScope.nativeHostIncluded -or @($bridgeScope.samples | Where-Object processCount -ne $expectedBridgeCount).Count) { throw 'Expected root, exact-image bridge and its child, excluding same-name decoy.' }
    if ($bridgeRaw.Contains($hostName) -or $bridgeRaw.Contains('processId') -or $bridgeRaw.Contains('executablePath')) { throw 'Collector leaked process identity metadata.' }
    # Native hosts also exit between OpenProcess and image-path lookup. Exercise
    # that branch with many short-lived exact-image matches, not only cmd children.
    $nativeChurnScript = $churnScript.Replace('(Join-Path $env:SystemRoot ''System32\cmd.exe'')',
        ("'" + $allowedExecutable.Replace("'", "''") + "'")).Replace('exit 0', 'exit 259')
    $nativeChurnStart = [Diagnostics.ProcessStartInfo]::new($shell)
    $nativeChurnStart.UseShellExecute = $false
    $nativeChurnStart.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile','-NonInteractive','-EncodedCommand',
        [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($nativeChurnScript)))) { $nativeChurnStart.ArgumentList.Add($argument) }
    $nativeChurn = [Diagnostics.Process]::Start($nativeChurnStart)
    $ownedProcesses.Add($nativeChurn)
    $nativeChurnPath = Join-Path $testDirectory 'native-host-exit-churn.json'
    & $shell -NoProfile -File (Join-Path $PSScriptRoot 'measure-memory.ps1') -RootProcessId $measuredRoot.Id -NativeHostExecutablePath $allowedExecutable -Scenario native-host-exit-churn -BuildLabel test -HostVersion test -Configuration test -WarmupSeconds 1 -DurationSeconds 10 -OutputPath $nativeChurnPath
    if ($LASTEXITCODE -ne 0) { throw 'Collector rejected normal native-host exits.' }
    $nativeChurnReport = Get-Content -LiteralPath $nativeChurnPath -Raw | ConvertFrom-Json
    if (-not $nativeChurnReport.complete -or $nativeChurnReport.samples.Count -ne 100) { throw 'Native-host exit churn measurement is incomplete.' }
} finally {
    foreach ($fixture in $ownedProcesses) {
        try { if (-not $fixture.HasExited) { $fixture.Kill($true); $fixture.WaitForExit() } }
        finally { $fixture.Dispose() }
    }
}
Write-Output "Memory comparison tests passed. Synthetic fixtures: $testDirectory"
