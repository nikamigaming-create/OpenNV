[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v1\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\exit-transition-v1"
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
foreach ($inputPath in @(
        $Godot,
        $TempleCache,
        $TempleTransitions,
        $ArroyoCache,
        $PlayerCache,
        $CharacterStartCache)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 exit-transition input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 exit-transition proof: $Output"
}

$save = Join-Path $Output 'fo2-exit-transition-save.json'
$commonArguments = @(
    '--path', $runtime,
    '--resolution', '1280x720',
    'res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn',
    '--',
    '--fo2-temple-cache', $TempleCache,
    '--fo2-temple-transitions', $TempleTransitions,
    '--fo2-arroyo-cache', $ArroyoCache,
    '--fo2-player-cache', $PlayerCache,
    '--fo2-character-start-cache', $CharacterStartCache,
    '--fo2-save', $save
)

& $Godot @commonArguments '--fo2-exit-transition-write-proof' $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 exit-transition write proof failed with exit code $LASTEXITCODE."
}
& $Godot '--headless' @commonArguments '--fo2-exit-transition-restore-proof' $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 exit-transition cold proof failed with exit code $LASTEXITCODE."
}

$writePath = Join-Path $Output 'fo2-exit-transition-write-proof.json'
$restorePath = Join-Path $Output 'fo2-exit-transition-restore-proof.json'
foreach ($reportPath in @($writePath, $restorePath, $save)) {
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Fallout 2 exit-transition report/save is missing: $reportPath"
    }
}
$write = Get-Content -LiteralPath $writePath -Raw | ConvertFrom-Json
$restore = Get-Content -LiteralPath $restorePath -Raw | ConvertFrom-Json
$saved = Get-Content -LiteralPath $save -Raw | ConvertFrom-Json
if ($write.schema -ne 'opennv-fo2-exit-transition-write-proof/v1' -or
    $write.status -ne 'pass-source-exit-ordinary-movement-map126-save' -or
    $restore.schema -ne 'opennv-fo2-exit-transition-restore-proof/v1' -or
    $restore.status -ne 'pass-map126-source-transition-cold-restore' -or
    $write.source.exitSerial -ne 1738 -or
    $write.source.tile -ne 31307 -or
    $write.source.pathSha256 -ne '9895a6b2cffcfe36bfad927bb28377ea69edc732afc7a7a66fbb357c97d57413' -or
    ($write.source.path -join ',') -ne ($write.source.observedPath -join ',') -or
    $write.destination.mapIndex -ne 126 -or
    $write.destination.tile -ne 16486 -or
    $write.destination.elevation -ne 0 -or
    $write.destination.rotation -ne 0 -or
    -not $write.destination.playerGrounded -or
    -not $write.destination.playerVisible -or
    $write.destination.animation -ne 'AA' -or
    $write.frame.width -ne 1280 -or $write.frame.height -ne 720 -or
    $write.frame.sha256.Length -ne 64 -or
    $write.visual.wallProxyMeshes -ne 0 -or
    $write.visual.sourceWallSprites -le 0 -or
    -not $write.visual.sourceWallsVisible -or
    $write.visual.cameraSizeMeters -ne 12.0 -or
    $write.visual.cameraProfileSizeMeters -ne 12.0 -or
    -not $write.visual.sourceFrmSpritesRetained -or
    $write.visual.hiddenSourceGeometry -or
    -not $restore.restore.coldProcess -or
    -not $restore.restore.exactInitialPosition -or
    -not $restore.restore.exactInitialTile -or
    -not $restore.restore.exactInitialFacing -or
    -not $restore.restore.grounded -or
    -not $restore.restore.ownedPresentationVisible -or
    -not $restore.restore.idleAa -or
    $saved.schema -ne 'opennv-fo2-character-arroyo-save/v9' -or
    $saved.world.mapIndex -ne 126 -or $saved.world.currentTile -ne 16486 -or
    $saved.lastTransition.exitSerial -ne 1738 -or
    $write.save.Sha256 -ne $restore.save.Sha256) {
    throw "Fallout 2 exit-transition evidence failed its bounded contract."
}
Write-Output (
    "OPENNV_FO2_EXIT_TRANSITION_PASS exit={0}:{1} target={2}:{3} saveSha256={4} frameSha256={5}" -f
    $write.source.exitSerial,
    $write.source.tile,
    $write.destination.mapIndex,
    $write.destination.tile,
    $write.save.Sha256,
    $write.frame.sha256)
