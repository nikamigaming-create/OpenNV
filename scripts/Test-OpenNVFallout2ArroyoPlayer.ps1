[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v2\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\arroyo-player-runtime-v1",
    [string]$ClassicHumanoidInstallManifest,
    [string]$PresentationDonorPreviewSet
)

$ErrorActionPreference = 'Stop'
$ExpectedArrivalTile = 28707
$ExpectedFinalTile = 31907
$ExpectedRejectedTile = 32107
$ExpectedClassicHudSourceAssets = 15
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$classicHumanoidPreflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
$classicHumanoidResolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
foreach ($inputPath in @(
        $Godot,
        $TempleCache,
        $TempleTransitions,
        $ArroyoCache,
        $PlayerCache,
        $CharacterStartCache)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 player-proof input is missing: $inputPath"
    }
}
$classicHumanoidDonorPreviewSet = if (
    -not [string]::IsNullOrWhiteSpace($PresentationDonorPreviewSet)) {
    [IO.Path]::GetFullPath($PresentationDonorPreviewSet)
} else {
    if ([string]::IsNullOrWhiteSpace($ClassicHumanoidInstallManifest)) {
        throw 'Fallout 2 player proof requires an owned presentation donor preview set or install manifest.'
    }
    & $classicHumanoidResolver -InstallManifest $ClassicHumanoidInstallManifest
    if (-not $?) { throw 'Classic humanoid install-manifest resolution failed.' }
}
& $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
if (-not $?) { throw 'Classic humanoid donor preflight failed.' }
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
    --fo2-player-cache $PlayerCache `
    --fo2-character-start-cache $CharacterStartCache `
    --classic-humanoid-donor-preview-set $classicHumanoidDonorPreviewSet `
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
    $report.status -ne 'pass-input-driven-source-gated-player-runtime-owned-hmwarr-bound-3d-donor-no-save' -or
    $report.arrival.mapIndex -ne 3 -or
    $report.arrival.elevation -ne 0 -or
    $report.arrival.tile -ne $ExpectedArrivalTile -or
    $report.movement.finalTile -ne $ExpectedFinalTile -or
    $report.movement.rejectedCandidateTile -ne $ExpectedRejectedTile -or
    -not $report.promotion.inputDrivenMovement -or
    -not $report.promotion.physicalFloorSupport -or
    -not $report.promotion.sourceMaskCollisionGate -or
    -not $report.promotion.characterArtLoaded -or
    -not $report.promotion.sourceWalkAnimationPlayed -or
    -not $report.promotion.humanInteractiveEntryAvailable -or
    $report.playerPresentation.fid -ne '0100003e' -or
    $report.playerPresentation.logicalPath -ne 'art\critters\hmwarraa.frm' -or
    $report.playerPresentation.prototypePid -ne '01000001' -or
    $report.playerPresentation.prototypeLogicalPath -ne 'proto\critters\00000001.pro' -or
    $report.playerPresentation.walkLogicalPath -ne 'art\critters\hmwarrab.frm' -or
    $report.playerPresentation.walkFps -ne 10 -or
    $report.playerPresentation.walkFramesPerDirection -ne 8 -or
    $report.playerPresentation.sourceDirections -ne 6 -or
    $report.playerPresentation.admittedFrame -ne 0 -or
    -not $report.playerPresentation.animationPlayback -or
    $report.playerPresentation.walkFrameAdvances -le 0 -or
    $report.playerPresentation.completedWalkCycles -le 0 -or
    -not $report.playerPresentation.idleResumedAtEnd -or
    -not $report.playerPresentation.visible -or
    $report.playerPresentation.geometryMode -ne 'owned-fnv-full-body-presentation-donor-non-parity' -or
    $report.playerPresentation.sourceStateGeometryMode -ne 'exact-owned-fo2-frm-alpha-island-molded-relief-v2' -or
    $report.playerPresentation.sourceStateReliefVisible -or
    -not $report.playerPresentation.usesOwnedDonor -or
    $report.playerPresentation.roleDonorOutfitFormId -ne '0003307c' -or
    $report.playerPresentation.loadedDonorOutfitFormId -ne '0003307c' -or
    $report.playerPresentation.meshInstances -le 0 -or
    $report.playerPresentation.visibleAnimation.firstWalkClip -notmatch 'forward' -or
    $report.playerPresentation.visibleAnimation.secondWalkClip -notmatch 'forward' -or
    $report.playerPresentation.visibleAnimation.firstWalkClipSeconds -le 0 -or
    $report.playerPresentation.visibleAnimation.secondWalkClipSeconds -le 0 -or
    $report.playerPresentation.visibleAnimation.endClip -notmatch 'idle' -or
    $report.playerPresentation.skinJoin.mode -ne
        'owned-shaderskin-detail-with-facegen-neck-and-cheek-complexion-v9' -or
    $report.frames.Count -ne 5 -or
    [IO.Path]::GetFileName($report.frames[4].Path) -ne 'player-close-final.png' -or
    -not $report.classicHud.visible -or
    -not $report.classicHud.ownedFallout2ClassicInterface -or
    -not $report.classicHud.sourcePixelLayout -or
    $report.classicHud.sourceAssetCount -ne $ExpectedClassicHudSourceAssets -or
    $report.classicHud.retailBehaviorParity -or
    $report.classicHud.startBlockedSourceLight -or
    -not $report.classicHud.endBlockedSourceLight -or
    $report.classicHud.sourceStateTile -ne $ExpectedFinalTile -or
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
