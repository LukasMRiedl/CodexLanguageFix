[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselinePath,
    [Parameter(Mandatory)][string]$CandidatePath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
foreach ($name in @('schemaVersion','scenario','hostVersion','configuration','osVersion','osArchitecture','warmupSeconds','durationSeconds','sampleIntervalSeconds')) {
    if ($null -eq $baseline.$name -or $baseline.$name -cne $candidate.$name) {
        throw "Incomparable reports: $name differs or is absent."
    }
}
if ($baseline.schemaVersion -ne 1 -or $baseline.sampleIntervalSeconds -ne 1) { throw 'Unsupported report format.' }
foreach ($report in @($baseline,$candidate)) {
    if ($report.complete -ne $true -or $report.samples.Count -ne $report.durationSeconds -or $report.durationSeconds -lt 1) {
        throw 'Incomplete measurement; no acceptance decision is possible.'
    }
    $index = 0
    foreach ($sample in $report.samples) {
        $expectedTime = $report.warmupSeconds + $index
        if ($null -eq $sample.elapsedSeconds -or $sample.elapsedSeconds -lt $expectedTime -or $sample.elapsedSeconds -gt ($expectedTime + 0.5) -or $sample.processCount -lt 1) {
            throw 'Invalid sampling cadence or process count.'
        }
        foreach ($metric in @('privateBytes','workingSetBytes')) {
            if ($null -eq $sample.$metric -or [double]$sample.$metric -le 0) { throw "Invalid $metric sample." }
        }
        $index++
    }
}
function Get-Statistic($Values, [string]$Statistic) {
    $sorted = @($Values | Sort-Object { [double]$_ })
    if ($Statistic -eq 'p95') { return [double]$sorted[[Math]::Ceiling($sorted.Count * 0.95) - 1] }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}
$gates = @(
    foreach ($metric in @('privateBytes','workingSetBytes')) {
        foreach ($statistic in @('median','p95')) {
            $before = Get-Statistic @($baseline.samples | ForEach-Object { $_.$metric }) $statistic
            $after = Get-Statistic @($candidate.samples | ForEach-Object { $_.$metric }) $statistic
            [pscustomobject][ordered]@{
                metric = $metric
                statistic = $statistic
                baselineBytes = $before
                candidateBytes = $after
                baselineMiB = [Math]::Round($before / 1MB, 3)
                candidateMiB = [Math]::Round($after / 1MB, 3)
                reductionPercent = [Math]::Round((1 - $after / $before) * 100, 3)
                passed = ($after -le $before * 0.2)
            }
        }
    }
)
$passed = @($gates | Where-Object { -not $_.passed }).Count -eq 0
[ordered]@{
    schemaVersion = 1
    scenario = $baseline.scenario
    baselineBuild = $baseline.buildLabel
    candidateBuild = $candidate.buildLabel
    durationSeconds = $baseline.durationSeconds
    warmupSeconds = $baseline.warmupSeconds
    percentileMethod = 'nearest-rank'
    requiredReductionPercent = 80
    passed = $passed
    gates = $gates
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$gates | Format-Table metric,statistic,baselineMiB,candidateMiB,reductionPercent,passed
if (-not $passed) { exit 2 }
