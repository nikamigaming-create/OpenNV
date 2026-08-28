[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\arroyo-native-render-v1"
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$inputs = @($Godot, $TempleCache, $TempleTransitions, $ArroyoCache)
foreach ($inputPath in $inputs) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 render-proof input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 render proof: $Output"
}

& $Godot `
    --path $runtime `
    --windowed `
    --resolution 1280x720 `
    'res://src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesRenderProof.tscn' `
    -- `
    --fo2-temple-cache $TempleCache `
    --fo2-temple-transitions $TempleTransitions `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-arroyo-render-proof $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 Arroyo Caves native render proof failed with exit code $LASTEXITCODE."
}

$reportPath = Join-Path $Output 'arroyo-caves-native-render-proof.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Fallout 2 Arroyo Caves native render report is missing: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.schema -ne 'opennv-fo2-arroyo-caves-native-render-proof/v1' -or
    $report.status -ne 'pass-rendered-owned-map-presentation-no-player-or-gameplay' -or
    $report.arrival.mapIndex -ne 3 -or
    $report.arrival.elevation -ne 0 -or
    $report.arrival.tile -ne 28707 -or
    $report.arrival.rotation -ne 0 -or
    -not $report.promotion.rendered -or
    $report.promotion.interactive -or
    $report.promotion.playerSpawned -or
    $report.promotion.launcherPlayable) {
    throw "Fallout 2 Arroyo Caves native render report failed its honest promotion contract."
}
Write-Output (
    "OPENNV_FO2_ARROYO_RENDER_PROOF_PASS map={0} elevation={1} arrival={2} floors={3} objects={4} frame={5}" -f
    $report.arrival.mapIndex,
    $report.arrival.elevation,
    $report.arrival.tile,
    $report.construction.floorPatches,
    $report.construction.topLevelObjects,
    $report.frame.sha256)
