[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Godot,

    [Parameter(Mandatory)]
    [string]$Python,

    [string]$Profile = "",

    [string]$CacheRoot = "",

    [string]$CaptureRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$preparer = Join-Path $repoRoot "content\tools\prepare_fo3_birth_presentation.py"
$proofScene = "res://src/Campaigns/Fallout3/Fo3Vault101BirthProof.tscn"
if ([string]::IsNullOrWhiteSpace($Profile)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $Profile = Join-Path $localData "OpenNV\profiles\fallout3\vanilla\fallout3-profile.json"
}
$runId = [Guid]::NewGuid().ToString("N")
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $env:TEMP "opennv-fo3-vault101-cache-$runId"
}
if ([string]::IsNullOrWhiteSpace($CaptureRoot)) {
    $CaptureRoot = Join-Path $env:TEMP "opennv-fo3-vault101-capture-$runId"
}

foreach ($path in @($Godot, $Python, $Profile, $preparer, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Fallout 3 Vault 101 render-test input: $path"
    }
}
foreach ($path in @($CacheRoot, $CaptureRoot)) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite Fallout 3 Vault 101 render-test output: $path"
    }
}

$prepareOutput = & $Python $preparer --profile ([IO.Path]::GetFullPath($Profile)) `
    --cache-root ([IO.Path]::GetFullPath($CacheRoot)) 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 3 Vault 101 presentation preparation failed:`n$($prepareOutput | Out-String)"
}
$prepareText = $prepareOutput | Out-String
$receiptLine = @(
    $prepareText -split "`r?`n" |
        Where-Object { $_.TrimStart().StartsWith("{") }
)[-1]
$receipt = $receiptLine | ConvertFrom-Json
if ($receipt.schema -ne "opennv-fo3-vault101-birth-presentation/v1" -or
    -not (Test-Path -LiteralPath $receipt.output -PathType Leaf)) {
    throw "Fallout 3 Vault 101 preparation receipt is invalid."
}

$renderOutput = & $Godot --xr-mode off --path $runtimeRoot --windowed `
    --resolution 1280x720 --position 10000,10000 $proofScene -- `
    --fo3-profile ([IO.Path]::GetFullPath($Profile)) `
    --fo3-birth-presentation ([IO.Path]::GetFullPath($receipt.output)) `
    --fo3-birth-capture ([IO.Path]::GetFullPath($CaptureRoot)) 2>&1
$renderText = $renderOutput | Out-String
$expected =
    "OPENNV_FO3_VAULT101_RENDER_PASS cell=00028138 entry=00039562 " +
    "references=29 models=23 surfaces=148 actors=0 interactive=0"
if ($LASTEXITCODE -ne 0 -or $renderText -notmatch [regex]::Escape($expected)) {
    throw "Fallout 3 Vault 101 native render proof failed:`n$renderText"
}

$reportPath = Join-Path $CaptureRoot "vault101-birth-native-render-proof.json"
$framePath = Join-Path $CaptureRoot "vault101-birth-entry.png"
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json -Depth 100
if ($report.status -ne "pass-rendered-owned-birth-room-no-actors-scripts-or-gameplay" -or
    -not $report.promotion.rendered -or
    $report.promotion.interactive -or
    $report.promotion.actorsRendered -or
    $report.promotion.questCommandsExecuted -or
    $report.cell.loadedStaticReferences -ne 29 -or
    $report.cell.loadedUniqueModels -ne 23 -or
    -not (Test-Path -LiteralPath $framePath -PathType Leaf)) {
    throw "Fallout 3 Vault 101 render report promotion boundary is invalid."
}

$renderText
"OPENNV_FO3_VAULT101_GATE_PASS cache=$CacheRoot capture=$CaptureRoot"
