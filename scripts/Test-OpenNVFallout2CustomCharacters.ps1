[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v1\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\custom-character-v1"
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
        throw "Required Fallout 2 custom-character input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 custom-character proof: $Output"
}

$commonArguments = @(
    '--path', $runtime,
    '--resolution', '1280x720',
    'res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn',
    '--',
    '--fo2-temple-cache', $TempleCache,
    '--fo2-temple-transitions', $TempleTransitions,
    '--fo2-arroyo-cache', $ArroyoCache,
    '--fo2-player-cache', $PlayerCache,
    '--fo2-character-start-cache', $CharacterStartCache
)

foreach ($sex in @('Male', 'Female')) {
    $key = $sex.ToLowerInvariant()
    $sexRoot = Join-Path $Output $key
    $save = Join-Path $sexRoot "fo2-custom-$key-save.json"
    & $Godot @commonArguments `
        '--fo2-save' $save `
        '--fo2-custom-character-sex' $sex `
        '--fo2-custom-character-write-proof' $sexRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Fallout 2 $sex custom-character write proof failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $save -PathType Leaf)) {
        throw "Fallout 2 $sex custom-character write proof did not create its save: $save"
    }

    & $Godot '--headless' @commonArguments `
        '--fo2-save' $save `
        '--fo2-custom-character-sex' $sex `
        '--fo2-custom-character-restore-proof' $sexRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Fallout 2 $sex custom-character restore proof failed with exit code $LASTEXITCODE."
    }

    $writePath = Join-Path $sexRoot "custom-$key-write-proof.json"
    $restorePath = Join-Path $sexRoot "custom-$key-restore-proof.json"
    foreach ($reportPath in @($writePath, $restorePath)) {
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            throw "Fallout 2 custom-character report is missing: $reportPath"
        }
    }
    $write = Get-Content -LiteralPath $writePath -Raw | ConvertFrom-Json
    $restore = Get-Content -LiteralPath $restorePath -Raw | ConvertFrom-Json
    $saved = Get-Content -LiteralPath $save -Raw | ConvertFrom-Json
    $expectedMode = if ($sex -eq 'Male') {
        'modified-owned-premade'
    } else {
        'custom-created-from-owned-rules'
    }
    if ($write.schema -ne 'opennv-fo2-custom-character-write-proof/v1' -or
        $write.status -ne "pass-$expectedMode-map3-atomic-save" -or
        $restore.schema -ne 'opennv-fo2-custom-character-restore-proof/v1' -or
        $restore.status -ne "pass-$expectedMode-map3-cold-restore" -or
        $saved.schema -ne 'opennv-fo2-character-arroyo-save/v3' -or
        $saved.character.Mode -ne $expectedMode -or
        $saved.character.Sex -ne $sex -or
        ($saved.character.special | Measure-Object -Sum).Sum -ne 40 -or
        $saved.world.mapIndex -ne 3 -or
        $saved.world.elevation -ne 0 -or
        $saved.world.arrivalTile -ne 28707 -or
        -not $write.cancelPathPreservedState -or
        -not $restore.restore.coldProcess -or
        -not $restore.restore.exactInitialPosition -or
        -not $restore.restore.exactInitialTile -or
        -not $restore.restore.exactInitialRotation -or
        -not $restore.restore.grounded -or
        -not $restore.restore.visibleSexCorrectOwnedFrm -or
        $write.save.sha256 -ne $restore.save.sha256) {
        throw "Fallout 2 $sex custom-character proof failed its bounded contract."
    }
    if (($sex -eq 'Male' -and $write.tagsAndTraits -ne 'source-unchanged') -or
        ($sex -eq 'Female' -and
            ($write.tagsAndTraits -ne 'unselected' -or
             $saved.character.taggedSkills.Count -ne 0 -or
             $saved.character.traits.Count -ne 0))) {
        throw "Fallout 2 $sex custom-character tag/trait policy drifted."
    }
    Write-Output (
        "OPENNV_FO2_CUSTOM_CHARACTER_PASS sex={0} mode={1} name={2} saveSha256={3}" -f
        $sex,
        $expectedMode,
        $saved.character.Name,
        $write.save.sha256)
}
