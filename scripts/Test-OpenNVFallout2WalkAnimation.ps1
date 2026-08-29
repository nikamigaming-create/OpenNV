[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v1\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\owned-walk-animation-v1"
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
        throw "Required Fallout 2 walk-animation input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 walk-animation proof: $Output"
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
    $save = Join-Path $sexRoot "fo2-$key-walk-save.json"
    & $Godot @commonArguments `
        '--fo2-save' $save `
        '--fo2-walk-animation-sex' $sex `
        '--fo2-walk-animation-write-proof' $sexRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Fallout 2 $sex walk-animation write proof failed with exit code $LASTEXITCODE."
    }
    & $Godot '--headless' @commonArguments `
        '--fo2-save' $save `
        '--fo2-walk-animation-sex' $sex `
        '--fo2-walk-animation-restore-proof' $sexRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Fallout 2 $sex walk-animation restore proof failed with exit code $LASTEXITCODE."
    }

    $writePath = Join-Path $sexRoot "walk-$key-write-proof.json"
    $restorePath = Join-Path $sexRoot "walk-$key-restore-proof.json"
    foreach ($reportPath in @($writePath, $restorePath, $save)) {
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            throw "Fallout 2 walk-animation report/save is missing: $reportPath"
        }
    }
    $write = Get-Content -LiteralPath $writePath -Raw | ConvertFrom-Json
    $restore = Get-Content -LiteralPath $restorePath -Raw | ConvertFrom-Json
    $saved = Get-Content -LiteralPath $save -Raw | ConvertFrom-Json
    $expectedPid = if ($sex -eq 'Male') { '01000001' } else { '01000002' }
    $expectedWalk = if ($sex -eq 'Male') {
        'art\critters\hmwarrab.frm'
    } else {
        'art\critters\hfprimab.frm'
    }
    if ($write.schema -ne 'opennv-fo2-walk-animation-write-proof/v1' -or
        $write.status -ne 'pass-owned-pro-linked-aa-ab-two-direction-save' -or
        $restore.schema -ne 'opennv-fo2-walk-animation-restore-proof/v1' -or
        $restore.status -ne 'pass-owned-pro-linked-aa-ab-cold-restore' -or
        $write.sex -ne $sex -or $restore.sex -ne $sex -or
        $write.presentation.PrototypePid -ne $expectedPid -or
        $write.presentation.walkCode -ne 'AB' -or
        $write.presentation.walkLogicalPath -ne $expectedWalk -or
        $write.presentation.walkFps -ne 10 -or
        $write.presentation.walkFramesPerDirection -ne 8 -or
        $write.presentation.walkDirections -ne 6 -or
        -not $write.idleResumed -or
        $write.steps.Count -ne 2 -or
        $write.steps[0].Direction -eq $write.steps[1].Direction -or
        $write.steps[0].StartTile -ne $write.steps[1].EndTile -or
        $write.steps[0].EndTile -ne $write.steps[1].StartTile -or
        ($write.steps | Where-Object {
            -not $_.Passed -or -not $_.SawWalking -or
            -not $_.WalkingAtCapture -or -not $_.DirectionStayedExact -or
            -not $_.IdleResumed -or $_.FrameAdvances -lt 2 -or
            $_.CapturedLogicalPath -ne $expectedWalk -or
            $_.CapturedSourceSha256.Length -ne 64 -or
            $_.CapturedPngSha256.Length -ne 64 -or
            $_.Frame.Width -ne 1280 -or $_.Frame.Height -ne 720 -or
            $_.Frame.Sha256.Length -ne 64
        }).Count -ne 0 -or
        -not $restore.restore.coldProcess -or
        -not $restore.restore.exactInitialPosition -or
        -not $restore.restore.exactInitialTile -or
        -not $restore.restore.exactInitialDirection -or
        -not $restore.restore.idleAtRestore -or
        -not $restore.restore.grounded -or
        $restore.presentation.PrototypePid -ne $expectedPid -or
        $restore.presentation.walkLogicalPath -ne $expectedWalk -or
        $saved.character.Sex -ne $sex -or
        $write.save.Sha256 -ne $restore.save.Sha256) {
        throw "Fallout 2 $sex walk-animation evidence failed its bounded contract."
    }
    Write-Output (
        "OPENNV_FO2_WALK_ANIMATION_PASS sex={0} directions={1},{2} frames={3},{4} saveSha256={5}" -f
        $sex,
        $write.steps[0].Direction,
        $write.steps[1].Direction,
        $write.steps[0].FrameAdvances,
        $write.steps[1].FrameAdvances,
        $write.save.Sha256)
}
