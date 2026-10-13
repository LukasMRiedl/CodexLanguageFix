[CmdletBinding(DefaultParameterSetName = 'Live')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Build')]
    [string]$BaselineProductionProject,

    [Parameter(Mandatory, ParameterSetName = 'Build')]
    [string]$CandidateProductionProject,

    [Parameter(Mandatory, ParameterSetName = 'Live')]
    [switch]$ExecuteLiveProbe,

    [Parameter(Mandatory, ParameterSetName = 'Build')]
    [switch]$BuildOnly,

    [Parameter(ParameterSetName = 'Build')]
    [Parameter(Mandatory, ParameterSetName = 'Live')]
    [string]$PreparedRunDirectory,

    [Parameter(ParameterSetName = 'Live')]
    [string]$ReportPath,

    [Parameter(Mandatory, ParameterSetName = 'Live')]
    [long]$TargetWindowId
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$probeProject = Join-Path $repoRoot 'tests\SpeedProbe\SpeedProbe.csproj'
$tempBase = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'CodexLanguageFix-SpeedProbe'))

function Resolve-ProjectFile([string]$Path) {
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ([IO.Path]::GetExtension($resolved) -ne '.csproj') {
        throw 'Both production project arguments must name existing .csproj files.'
    }
    return $resolved
}

function Assert-SessionPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PreparedRunDirectory must be a generated child of the SpeedProbe temp directory.'
    }
    return $full
}

function Publish-Probe([string]$ProductionProject, [string]$VariantDirectory) {
    $publishDirectory = Join-Path $VariantDirectory 'publish'
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    & dotnet publish $probeProject --configuration Release --runtime win-x64 --self-contained false `
        "-p:ProductionProjectPath=$ProductionProject" -p:PublishSingleFile=false -p:SelfContained=false `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "SpeedProbe publish failed with exit code $LASTEXITCODE."
    }
    $exe = Join-Path $publishDirectory 'CodexLanguageFix.SpeedProbe.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw 'SpeedProbe publish completed without the expected executable.'
    }
}

if ($BuildOnly) {
    $baselineProject = Resolve-ProjectFile $BaselineProductionProject
    $candidateProject = Resolve-ProjectFile $CandidateProductionProject
    if ([string]::Equals($baselineProject, $candidateProject, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Baseline and candidate must be separate production project paths.'
    }

    New-Item -ItemType Directory -Path $tempBase -Force | Out-Null
    if ([string]::IsNullOrWhiteSpace($PreparedRunDirectory)) {
        $PreparedRunDirectory = Join-Path $tempBase ([Guid]::NewGuid().ToString('N'))
    }
    $prepared = Assert-SessionPath $PreparedRunDirectory
    New-Item -ItemType Directory -Path $prepared -Force | Out-Null
    Publish-Probe $baselineProject (Join-Path $prepared 'baseline')
    Publish-Probe $candidateProject (Join-Path $prepared 'candidate')
    $manifest = [ordered]@{
        schemaVersion = 1
        baselineProductionProject = $baselineProject
        candidateProductionProject = $candidateProject
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
        harnessProject = $probeProject
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $prepared 'manifest.json') -Encoding utf8
    Write-Output $prepared
    return
}

$prepared = Assert-SessionPath $PreparedRunDirectory
$manifestPath = Join-Path $prepared 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'PreparedRunDirectory does not contain a SpeedProbe manifest.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$baselineExe = Join-Path $prepared 'baseline\publish\CodexLanguageFix.SpeedProbe.exe'
$candidateExe = Join-Path $prepared 'candidate\publish\CodexLanguageFix.SpeedProbe.exe'
if (-not (Test-Path -LiteralPath $baselineExe -PathType Leaf) -or -not (Test-Path -LiteralPath $candidateExe -PathType Leaf)) {
    throw 'Both prepared probe executables must exist before live execution.'
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repoRoot (Join-Path 'benchmark-results' ('shortcut-speed-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json'))
}
$resolvedReportPath = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null
$resultsDirectory = Join-Path $prepared 'results'
$dataDirectory = Join-Path $prepared 'data'
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

$samples = [Collections.Generic.List[object]]::new()
$failureCode = $null
$failurePair = $null
$lengths = @('Short', 'Medium', 'Long')

try {
    for ($pair = 0; $pair -lt 30; $pair++) {
        $length = $lengths[$pair % $lengths.Count]
        $order = if (($pair % 2) -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($variant in $order) {
            $exe = if ($variant -eq 'baseline') { $baselineExe } else { $candidateExe }
            $resultFile = Join-Path $resultsDirectory ("pair-{0:D2}-{1}.json" -f $pair, $variant)
            $dataPath = Join-Path $dataDirectory ("pair-{0:D2}-{1}" -f $pair, $variant)
            $arguments = @(
                '--pair', $pair.ToString([Globalization.CultureInfo]::InvariantCulture),
                '--variant', $variant,
                '--length', $length,
                '--target-window', $TargetWindowId.ToString([Globalization.CultureInfo]::InvariantCulture),
                '--result', $resultFile,
                '--data-directory', $dataPath
            )

            & $exe @arguments
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                $failureCode = if ($exitCode -eq 20) { 'hotkey_conflict' } elseif ($exitCode -eq 21) { 'composer_guard_failed' } else { "probe_exit_$exitCode" }
                $failurePair = $pair
                throw "Probe failed for pair $pair, variant $variant ($failureCode)."
            }
            if (-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
                $failureCode = 'probe_result_missing'
                $failurePair = $pair
                throw "Probe result missing for pair $pair, variant $variant."
            }

            $sample = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            $samples.Add($sample)
            if ($sample.pair -ne $pair -or $sample.variant -ne $variant -or $sample.lengthKind -ne $length -or
                -not $sample.writeVerified -or -not $sample.undoVerified -or -not $sample.cleanupVerified -or
                $sample.cacheEnabled -ne $false -or $sample.lastInputToShortcutUpperBoundMilliseconds -gt 300) {
                $failureCode = 'sample_validation_failed'
                $failurePair = $pair
                throw "Probe validation failed for pair $pair, variant $variant."
            }
        }
    }

    if ($samples.Count -ne 60) {
        $failureCode = 'incomplete_pair_set'
        throw 'A complete report requires 60 successful samples across 30 pairs.'
    }
    $profiles = @($samples | ForEach-Object { "$($_.model)|$($_.effort)|$($_.serviceTier)" } | Sort-Object -Unique)
    if ($profiles.Count -ne 1) {
        $failureCode = 'luna_profile_mismatch'
        throw 'Baseline and candidate did not use the same Luna profile.'
    }
}
catch {
    if ($null -eq $failureCode) { $failureCode = 'orchestrator_failure' }
}

function Get-Statistics([double[]]$Values) {
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $p95Index = [Math]::Max(0, [Math]::Ceiling(0.95 * $sorted.Count) - 1)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2 -eq 0) { ($sorted[$middle - 1] + $sorted[$middle]) / 2 } else { $sorted[$middle] }
    return [ordered]@{
        count = $sorted.Count
        meanMilliseconds = [Math]::Round(($sorted | Measure-Object -Average).Average, 2)
        medianMilliseconds = [Math]::Round($median, 2)
        p95Milliseconds = [Math]::Round($sorted[[int]$p95Index], 2)
    }
}

$byLength = [ordered]@{}
foreach ($length in $lengths) {
    $baselineSamples = @($samples | Where-Object { $_.lengthKind -eq $length -and $_.variant -eq 'baseline' })
    $candidateSamples = @($samples | Where-Object { $_.lengthKind -eq $length -and $_.variant -eq 'candidate' })
    $deltas = @()
    foreach ($base in $baselineSamples) {
        $candidate = $candidateSamples | Where-Object { $_.pair -eq $base.pair } | Select-Object -First 1
        if ($null -ne $candidate) {
            $deltas += [double]$candidate.triggerToVerifiedWriteMilliseconds - [double]$base.triggerToVerifiedWriteMilliseconds
        }
    }
    $byLength[$length] = [ordered]@{
        baseline = Get-Statistics @($baselineSamples | ForEach-Object { [double]$_.triggerToVerifiedWriteMilliseconds })
        candidate = Get-Statistics @($candidateSamples | ForEach-Object { [double]$_.triggerToVerifiedWriteMilliseconds })
        pairedCandidateMinusBaseline = Get-Statistics $deltas
        candidateWins = @($deltas | Where-Object { $_ -lt 0 }).Count
        ties = @($deltas | Where-Object { $_ -eq 0 }).Count
    }
}

$baselineStatistics = Get-Statistics @($samples | Where-Object variant -eq 'baseline' | ForEach-Object { [double]$_.triggerToVerifiedWriteMilliseconds })
$candidateStatistics = Get-Statistics @($samples | Where-Object variant -eq 'candidate' | ForEach-Object { [double]$_.triggerToVerifiedWriteMilliseconds })
$qualityPassed = $samples.Count -eq 60 -and @($samples | Where-Object { -not $_.exactExpected }).Count -eq 0
foreach ($base in ($samples | Where-Object variant -eq 'baseline')) {
    $candidate = $samples | Where-Object { $_.variant -eq 'candidate' -and $_.pair -eq $base.pair } | Select-Object -First 1
    if ($null -eq $candidate -or $candidate.correctedHash -ne $base.correctedHash) { $qualityPassed = $false }
}
$absoluteGain = $null
$relativeGain = $null
$speedPassed = $false
if ($null -ne $baselineStatistics -and $null -ne $candidateStatistics) {
    $absoluteGain = $baselineStatistics.medianMilliseconds - $candidateStatistics.medianMilliseconds
    $relativeGain = $absoluteGain / $baselineStatistics.medianMilliseconds
    $speedPassed = $null -eq $failureCode -and $samples.Count -eq 60 -and $absoluteGain -ge 500 -and $relativeGain -ge 0.2 -and
        $candidateStatistics.p95Milliseconds -le ($baselineStatistics.p95Milliseconds * 1.1)
}
$report = [ordered]@{
    schemaVersion = 1
    status = if ($null -eq $failureCode -and $samples.Count -eq 60) { 'complete' } else { 'incomplete' }
    failureCode = $failureCode
    failurePair = $failurePair
    createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    baselineProductionProject = $manifest.baselineProductionProject
    candidateProductionProject = $manifest.candidateProductionProject
    pairCount = [int](@($samples | Select-Object -ExpandProperty pair -Unique).Count)
    sampleCount = $samples.Count
    samples = $samples.ToArray()
    baseline = $baselineStatistics
    candidate = $candidateStatistics
    gate = [ordered]@{ speedPassed=$speedPassed; qualityPassed=$qualityPassed; medianSavedMilliseconds=$absoluteGain; medianSavedFraction=$relativeGain }
    latencyByLength = $byLength
    measurement = [ordered]@{
        triggerToVerifiedWrite = 'Real RegisterHotKey AltGr VK_RMENU + VK_OEM_COMMA to same-editor changed text and coordinator undo state.'
        inputToShortcut = 'Upper bound from start of final synthetic space SendInput call to start of AltGr shortcut SendInput call; must be at most 300 ms.'
        cacheEnabled = $false
        warmUpPerProcess = $true
        cleanup = 'Coordinator Undo was verified against the exact fixture, then the exact synthetic fixture was cleared; no message was sent.'
        ordering = '30 pairs, baseline/candidate order alternates; one probe process per sample.'
    }
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolvedReportPath -Encoding utf8

if ($null -ne $failureCode -or $samples.Count -ne 60) {
    Write-Error "Shortcut speed probe stopped: $failureCode at pair $failurePair. Partial aggregate: $resolvedReportPath"
    exit 1
}

Write-Output $resolvedReportPath
if (-not $speedPassed -or -not $qualityPassed) { exit 2 }
