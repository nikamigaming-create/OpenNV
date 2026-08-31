[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$Report = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\arroyo-arrival-first-beat-v1.json",
    [Parameter(Mandatory)]
    [string]$ClassicHumanoidInstallManifest
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$resolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
$preflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
$transitionResolver = Join-Path $PSScriptRoot 'Resolve-Fo2TempleTransitionOutput.ps1'
foreach ($inputPath in @($Godot, $TempleCache, $ArroyoCache, $PlayerCache, $transitionResolver)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 first-beat input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Report) {
    throw "Refusing to overwrite Fallout 2 first-beat report: $Report"
}
$resolverOutput = @(& $resolver -InstallManifest $ClassicHumanoidInstallManifest)
if ($resolverOutput.Count -ne 1 -or [string]::IsNullOrWhiteSpace($resolverOutput[0])) {
    throw 'Classic humanoid install-manifest resolution did not emit exactly one preview-set path.'
}
$donor = [string]$resolverOutput[0]
if (-not (Test-Path -LiteralPath $donor -PathType Leaf)) {
    throw "Classic humanoid install-manifest resolver emitted a missing preview set: $donor"
}
& $preflight -PreviewSet $donor
$transitionOutput = @(& $transitionResolver -TempleCache $TempleCache)
if ($transitionOutput.Count -ne 1 -or [string]::IsNullOrWhiteSpace($transitionOutput[0])) {
    throw 'Fallout 2 Temple transition resolver did not emit exactly one manifest path.'
}

& $Godot --headless --path $runtime `
    'res://src/Campaigns/Fallout2/Temple/Fo2ArroyoArrivalFirstBeatProof.tscn' -- `
    --fo2-temple-cache $TempleCache `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-player-cache $PlayerCache `
    --classic-humanoid-donor-preview-set $donor `
    --fo2-arroyo-first-beat-report $Report
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 Arroyo first-beat proof failed with exit code $LASTEXITCODE."
}
$proof = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
if ($proof.schema -ne 'opennv-fo2-arroyo-arrival-first-beat-proof/v2' -or
    $proof.status -ne 'pass-source-bound-discrete-arrival-movement-headless-not-rendered' -or
    -not $proof.arrival.exactExitGridPlacement -or
    -not $proof.movement.legalMoveAccepted -or
    -not $proof.movement.invalidMoveRejected -or
    $proof.movement.expectedLegalMoves -le 0 -or
    $proof.movement.legalPathTiles.Count -ne $proof.movement.expectedLegalMoves -or
    $proof.movement.legalPathAccepted.Count -ne $proof.movement.expectedLegalMoves -or
    $proof.movement.completedLegalMoves -ne $proof.movement.expectedLegalMoves -or
    -not $proof.promotion.sharedOwnedHumanoidDonorAdmitted -or
    -not $proof.promotion.exactArrivalSpawnContract -or
    -not $proof.promotion.deterministicLegalPath -or
    -not $proof.promotion.invalidSourceMoveBlocked -or
    $proof.promotion.rendered -or $proof.promotion.interactive -or
    $proof.promotion.playableCampaign) {
    throw 'Fallout 2 Arroyo first-beat report failed its source-bound promotion contract.'
}
Write-Output (
    'OPENNV_FO2_ARROYO_FIRST_BEAT_PROOF_PASS arrival={0} legal={1} rejected={2}' -f
    $proof.arrival.tile,
    $proof.movement.legalDestinationTile,
    $proof.movement.rejectedCandidateTile)
