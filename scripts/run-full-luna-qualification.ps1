param(
    [string]$ReportDirectory = "benchmark-results/full-qualification",
    [int]$Seed = 42
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

$env:CODEX_LANGUAGE_FIX_LUNA_BENCHMARK = "1"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_AUTO_SEARCH = "0"
$env:CODEX_LANGUAGE_FIX_PROMPT_VARIANTS = "baseline"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_EFFORTS = "low"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_PROTOCOLS = "full"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_PARALLELISM = "2"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_WARMUP = "0"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_SCHEMA_WARMUP = "0"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_COLD_STARTS = "0"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_COMPARE_LANGUAGETOOL = "1"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_REQUIRE_PERFORMANCE_GATE = "0"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_WARM_ROUNDS = "1"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_WARM_CASES = "8"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_SEED = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:CODEX_LANGUAGE_FIX_BENCHMARK_TIMEOUT_SECONDS = "180"
$env:CODEX_LANGUAGE_FIX_BENCHMARK_REPORT = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $ReportDirectory))

dotnet test .\CodexLanguageFix.sln `
    -c Release `
    --no-restore `
    --filter FullyQualifiedName~LiveLunaPromptBenchmarkTests

exit $LASTEXITCODE
