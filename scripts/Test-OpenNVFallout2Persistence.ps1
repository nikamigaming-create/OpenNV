[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v1\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\character-save-v1"
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$save = Join-Path $Output 'fo2-character-arroyo-save.json'
foreach ($inputPath in @(
        $Godot,
        $TempleCache,
        $TempleTransitions,
        $ArroyoCache,
        $PlayerCache,
        $CharacterStartCache)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 persistence input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 persistence proof: $Output"
}

$commonArguments = @(
    '--headless',
    '--path', $runtime,
    'res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn',
    '--',
    '--fo2-temple-cache', $TempleCache,
    '--fo2-temple-transitions', $TempleTransitions,
    '--fo2-arroyo-cache', $ArroyoCache,
    '--fo2-player-cache', $PlayerCache,
    '--fo2-character-start-cache', $CharacterStartCache,
    '--fo2-save', $save
)

& $Godot @commonArguments '--fo2-character-save-write-proof' $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 persistence write phase failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $save -PathType Leaf)) {
    throw "Fallout 2 persistence write phase did not create its save: $save"
}

& $Godot @commonArguments '--fo2-character-save-restore-proof' $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 persistence cold-restore phase failed with exit code $LASTEXITCODE."
}

$writePath = Join-Path $Output 'fo2-character-save-write-proof.json'
$restorePath = Join-Path $Output 'fo2-character-save-restore-proof.json'
foreach ($reportPath in @($writePath, $restorePath)) {
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Fallout 2 persistence report is missing: $reportPath"
    }
}
$write = Get-Content -LiteralPath $writePath -Raw | ConvertFrom-Json
$restore = Get-Content -LiteralPath $restorePath -Raw | ConvertFrom-Json
$saved = Get-Content -LiteralPath $save -Raw | ConvertFrom-Json
if ($write.schema -ne 'opennv-fo2-character-save-write-proof/v1' -or
    $write.status -ne 'pass-owned-premade-map3-state-atomic-save' -or
    $restore.schema -ne 'opennv-fo2-character-save-restore-proof/v1' -or
    $restore.status -ne 'pass-owned-premade-map3-state-cold-restore' -or
    $saved.schema -ne 'opennv-fo2-character-arroyo-save/v8' -or
    $saved.character.Name -ne 'Chitsa' -or
    $saved.character.Sex -ne 'Female' -or
    $saved.world.mapIndex -ne 3 -or
    $saved.world.elevation -ne 0 -or
    $saved.world.arrivalTile -ne 28707 -or
    $saved.world.currentTile -eq 28707 -or
    -not $restore.restore.coldProcess -or
    -not $restore.restore.exactInitialPosition -or
    -not $restore.restore.exactInitialTile -or
    -not $restore.restore.exactInitialRotation -or
    -not $restore.restore.groundedAfterRestore -or
    -not $restore.restore.visibleSexCorrectOwnedFrm -or
    $write.save.sha256 -ne $restore.save.sha256) {
    throw "Fallout 2 persistence reports failed their bounded cold-restore contract."
}
Write-Output (
    "OPENNV_FO2_PERSISTENCE_PASS name={0} map={1} tile={2} rotation={3} saveSha256={4}" -f
    $saved.character.Name,
    $saved.world.mapIndex,
    $saved.world.currentTile,
    $saved.world.rotation,
    $write.save.sha256)
