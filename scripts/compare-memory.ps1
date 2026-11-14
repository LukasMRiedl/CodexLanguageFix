[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselinePath,
    [Parameter(Mandatory)][string]$CandidatePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$AnyReduction,
    [switch]$PrivateRamReduction
)
$ErrorActionPreference = 'Stop'
if ($AnyReduction -and $PrivateRamReduction) { throw 'Choose a single acceptance mode.' }
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
foreach ($name in @('schemaVersion','scenario','hostVersion','configuration','osVersion','osArchitecture','warmupSeconds','durationSeconds','sampleIntervalSeconds')) {
    if ($null -eq $baseline.$name -or $baseline.$name -cne $candidate.$name) {
        throw "Incomparable reports: $name differs or is absent."
    }
}
if ($baseline.schemaVersion -notin @(1,2) -or $baseline.sampleIntervalSeconds -notin @(0.1,1)) { throw 'Unsupported report format.' }
if ($baseline.schemaVersion -eq 1 -and $baseline.sampleIntervalSeconds -ne 1) { throw 'Unsupported historical cadence.' }
if ($PrivateRamReduction -and $baseline.schemaVersion -ne 2) { throw 'Private RAM requires schema-2 reports, not historical private-commit samples.' }
function Get-NativeHostScope($Report) {
    $property = $Report.PSObject.Properties['nativeHostIncluded']
    if ($null -eq $property) { return $false } # Existing schema-1 reports counted descendants only.
    if ($property.Value -isnot [bool]) { throw 'Invalid native-host scope.' }
    return $property.Value
}
$nativeHostIncluded = Get-NativeHostScope $baseline
if ($nativeHostIncluded -ne (Get-NativeHostScope $candidate)) { throw 'Incomparable reports: native-host scopes differ.' }
if (($AnyReduction -or $PrivateRamReduction) -and $baseline.durationSeconds -lt 1800) { throw 'Reduction acceptance requires at least 1800 measured seconds per report.' }
$reportMetrics = @('privateBytes','workingSetBytes')
if ($baseline.schemaVersion -eq 2) { $reportMetrics += 'privateWorkingSetBytes' }
foreach ($report in @($baseline,$candidate)) {
    if ($report.complete -ne $true -or $report.samples.Count -ne ($report.durationSeconds / $report.sampleIntervalSeconds) -or $report.durationSeconds -lt 1) {
        throw 'Incomplete measurement; no acceptance decision is possible.'
    }
    $index = 0
    foreach ($sample in $report.samples) {
        $expectedTime = $report.warmupSeconds + $index * $report.sampleIntervalSeconds
        if ($null -eq $sample.elapsedSeconds -or -not [double]::IsFinite([double]$sample.elapsedSeconds) -or $sample.elapsedSeconds -lt ($expectedTime - 0.000001) -or $sample.elapsedSeconds -gt ($expectedTime + 0.5) -or $sample.processCount -lt 1) {
            throw 'Invalid sampling cadence or process count.'
        }
        foreach ($metric in $reportMetrics) {
            if ($null -eq $sample.$metric -or -not [double]::IsFinite([double]$sample.$metric) -or [double]$sample.$metric -le 0) { throw "Invalid $metric sample." }
        }
        $index++
    }
}
function Get-Statistic($Values, [string]$Statistic) {
    $sorted = @($Values | Sort-Object { [double]$_ })
    if ($Statistic -eq 'p95') { return [double]$sorted[[Math]::Ceiling($sorted.Count * 0.95) - 1] }
    if ($Statistic -eq 'max') { return [double]$sorted[-1] }
    if ($Statistic -eq 'mean') { return [double]($Values | Measure-Object -Average).Average }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}
$gates = @(
    foreach ($metric in $(if ($PrivateRamReduction) { @('privateWorkingSetBytes') } else { @('privateBytes','workingSetBytes') })) {
        foreach ($statistic in @('median','p95')) {
            $before = Get-Statistic @($baseline.samples | ForEach-Object { $_.$metric }) $statistic
            $after = Get-Statistic @($candidate.samples | ForEach-Object { $_.$metric }) $statistic
            $gatePassed = if ($AnyReduction -or $PrivateRamReduction) { $after -lt $before } else { $after -le $before * 0.2 }
            [pscustomobject][ordered]@{
                metric = $metric
                statistic = $statistic
                baselineBytes = $before
                candidateBytes = $after
                baselineMiB = [Math]::Round($before / 1MB, 3)
                candidateMiB = [Math]::Round($after / 1MB, 3)
                reductionPercent = [Math]::Round((1 - $after / $before) * 100, 3)
                passed = $gatePassed
            }
        }
    }
)
$passed = @($gates | Where-Object { -not $_.passed }).Count -eq 0
$summaries = @(
    foreach ($report in @($baseline,$candidate)) {
        foreach ($metric in $reportMetrics) {
            $values = @($report.samples | ForEach-Object { $_.$metric })
            $half = [int][Math]::Floor($values.Count / 2)
            [ordered]@{ build = $report.buildLabel; metric = $metric
                meanBytes = Get-Statistic $values 'mean'; medianBytes = Get-Statistic $values 'median'
                p95Bytes = Get-Statistic $values 'p95'; maxBytes = Get-Statistic $values 'max'
                secondHalfGrowthBytes = $(if ($half -gt 0) {
                    (Get-Statistic $values[$half..($values.Count - 1)] 'median') - (Get-Statistic $values[0..($half - 1)] 'median')
                } else { 0 }) }
        }
    }
)
[ordered]@{
    schemaVersion = $baseline.schemaVersion
    scenario = $baseline.scenario
    baselineBuild = $baseline.buildLabel
    candidateBuild = $candidate.buildLabel
    durationSeconds = $baseline.durationSeconds
    warmupSeconds = $baseline.warmupSeconds
    percentileMethod = 'nearest-rank'
    requiredReductionPercent = $(if ($AnyReduction -or $PrivateRamReduction) { 0 } else { 80 })
    strictReduction = [bool]($AnyReduction -or $PrivateRamReduction)
    acceptanceMode = $(if ($PrivateRamReduction) { 'private-resident-ram' } elseif ($AnyReduction) { 'legacy-any-reduction' } else { 'legacy-80-percent' })
    privateRamTargetBytes = 50000000
    privateRamTargetMet = $(if ($baseline.schemaVersion -eq 2) { (Get-Statistic @($candidate.samples.privateWorkingSetBytes) 'max') -lt 50000000 } else { $null })
    nativeHostIncluded = $nativeHostIncluded
    passed = $passed
    gates = $gates
    summaries = $summaries
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$gates | Format-Table metric,statistic,baselineMiB,candidateMiB,reductionPercent,passed
if (-not $passed) { exit 2 }
exit 0
