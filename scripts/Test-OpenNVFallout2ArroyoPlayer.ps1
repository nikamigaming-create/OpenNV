[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\arroyo-player-runtime-v1"
)

$ErrorActionPreference = 'Stop'
$ExpectedArrivalTile = 28707
$ExpectedFinalTile = 31907
$ExpectedRejectedTile = 32107
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
foreach ($inputPath in @($Godot, $TempleCache, $TempleTransitions, $ArroyoCache)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 player-proof input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 player proof: $Output"
}

& $Godot `
    --path $runtime `
    --windowed `
    --resolution 1280x720 `
    'res://src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesPlayProof.tscn' `
    -- `
    --fo2-temple-cache $TempleCache `
    --fo2-temple-transitions $TempleTransitions `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-arroyo-player-proof $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 Arroyo player runtime proof failed with exit code $LASTEXITCODE."
}

$reportPath = Join-Path $Output 'arroyo-player-runtime-proof.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Fallout 2 Arroyo player runtime report is missing: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.schema -ne 'opennv-fo2-arroyo-player-runtime-proof/v1' -or
    $report.status -ne 'pass-input-driven-source-gated-player-runtime-no-character-art-or-save' -or
    $report.arrival.mapIndex -ne 3 -or
    $report.arrival.elevation -ne 0 -or
    $report.arrival.tile -ne $ExpectedArrivalTile -or
    $report.movement.finalTile -ne $ExpectedFinalTile -or
    $report.movement.rejectedCandidateTile -ne $ExpectedRejectedTile -or
    -not $report.promotion.inputDrivenMovement -or
    -not $report.promotion.physicalFloorSupport -or
    -not $report.promotion.sourceMaskCollisionGate -or
    $report.promotion.characterArtLoaded -or
    $report.promotion.playerStatePersistent -or
    $report.promotion.interactive -or
    $report.promotion.playableCampaign -or
    $report.promotion.launcherPlayable) {
    throw "Fallout 2 Arroyo player runtime report failed its honest promotion contract."
}
Write-Output (
    "OPENNV_FO2_ARROYO_PLAYER_PROOF_PASS arrival={0} final={1} transitions={2} rejected={3} distance={4:N3}" -f
    $report.arrival.tile,
    $report.movement.finalTile,
    $report.movement.completedTileTransitions,
    $report.movement.rejectedCandidateTile,
    $report.movement.horizontalDistanceMeters)
