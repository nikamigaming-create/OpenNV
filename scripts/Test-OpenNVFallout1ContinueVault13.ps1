[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [Parameter(Mandatory = $true)]
    [string]$HexScene,
    [Parameter(Mandatory = $true)]
    [string]$CharacterStart,
    [Parameter(Mandatory = $true)]
    [string]$ClassicHumanoidInstallManifest,
    [Parameter(Mandatory = $true)]
    [string]$ExitGridTransition,
    [Parameter(Mandatory = $true)]
    [string]$DestinationPresentation,
    [string]$DestinationInventoryInteraction,
    [string]$DestinationFlareUse,
    [string]$DestinationGenericDoor,
    [Parameter(Mandatory = $true)]
    [string]$SavePath,
    [Parameter(Mandatory = $true)]
    [string]$Report
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$resolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
$preflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
foreach ($inputPath in @(
    $Godot, $HexScene, $CharacterStart, $ClassicHumanoidInstallManifest,
    $ExitGridTransition, $DestinationPresentation, $SavePath, $resolver, $preflight
)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 1 Continue input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Report) {
    throw "Refusing to overwrite Fallout 1 Continue proof: $Report"
}
if (($PSBoundParameters.ContainsKey('DestinationInventoryInteraction')) -ne
    ($PSBoundParameters.ContainsKey('DestinationFlareUse'))) {
    throw 'Fallout 1 flare use proof requires both explicit interaction and flare-use descriptors.'
}
if ($PSBoundParameters.ContainsKey('DestinationGenericDoor') -and
    -not $PSBoundParameters.ContainsKey('DestinationFlareUse')) {
    throw 'Fallout 1 generic-door proof requires the restored explicit flare-use contract.'
}
foreach ($optionalPath in @($DestinationInventoryInteraction, $DestinationFlareUse, $DestinationGenericDoor)) {
    if (-not [string]::IsNullOrWhiteSpace($optionalPath) -and
        -not (Test-Path -LiteralPath $optionalPath -PathType Leaf)) {
        throw "Required Fallout 1 flare-use input is missing: $optionalPath"
    }
}
$reportParent = Split-Path -Parent ([IO.Path]::GetFullPath($Report))
if ([string]::IsNullOrWhiteSpace($reportParent)) {
    throw "Fallout 1 Continue proof needs a parent directory: $Report"
}
New-Item -ItemType Directory -Force -Path $reportParent | Out-Null

$save = Get-Content -LiteralPath $SavePath -Raw | ConvertFrom-Json -Depth 64
if ($save.schema -ne 'opennv-fo1-hex-save/v1' -or
    $null -eq $save.activeMap -or $save.activeMap.schema -ne 'opennv-fo1-active-map/v1' -or
    $save.activeMap.kind -ne 'destination' -or
    [string]::IsNullOrWhiteSpace($save.activeMap.presentation.path) -or
    [string]::IsNullOrWhiteSpace($save.activeMap.presentation.sha256)) {
    throw 'Fallout 1 Continue requires a saved explicit active destination-map contract.'
}
$resolvedDestination = [IO.Path]::GetFullPath($DestinationPresentation)
if ($save.activeMap.presentation.path -ne $resolvedDestination -or
    $save.activeMap.presentation.sha256 -ne
        (Get-FileHash -LiteralPath $DestinationPresentation -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Fallout 1 Continue destination presentation does not match the saved explicit path/hash join.'
}
$scene = Get-Content -LiteralPath $HexScene -Raw | ConvertFrom-Json -Depth 64
$character = Get-Content -LiteralPath $CharacterStart -Raw | ConvertFrom-Json -Depth 64
if ($scene.schema -ne 'opennv-fo1-hex-scene/v1' -or
    $scene.status -ne 'interactive-hex-topology-proof' -or
    $character.schema -ne 'opennv-fo1-character-start/v1' -or
    $character.status -ne 'prepared-owned-data') {
    throw 'Fallout 1 Continue requires explicit valid FO1 scene and character-start caches.'
}

$resolverOutput = @(& $resolver -InstallManifest $ClassicHumanoidInstallManifest)
if ($resolverOutput.Count -ne 1 -or [string]::IsNullOrWhiteSpace($resolverOutput[0])) {
    throw 'Classic humanoid install-manifest resolution did not emit exactly one preview-set path.'
}
$donor = [string]$resolverOutput[0]
& $preflight -PreviewSet $donor
$characterSha256 = (Get-FileHash -LiteralPath $CharacterStart -Algorithm SHA256).Hash.ToLowerInvariant()

$godotArgs = @(
    '--headless', '--path', $runtime, '--',
    '--fo1-hex-scene', $HexScene,
    '--fo1-new-game',
    '--fo1-start-presentation', 'hex-tactical',
    '--fo1-continue-menu-proof',
    '--fo1-character-start', $CharacterStart,
    '--fo1-character-start-sha256', $characterSha256,
    '--classic-humanoid-donor-preview-set', $donor,
    '--fo1-exit-grid-transition', $ExitGridTransition,
    '--fo1-destination-presentation', $DestinationPresentation,
    '--save-path', $SavePath,
    '--report', $Report
)
if ($PSBoundParameters.ContainsKey('DestinationFlareUse')) {
    $godotArgs += @(
        '--fo1-destination-inventory-interaction', $DestinationInventoryInteraction,
        '--fo1-destination-flare-use', $DestinationFlareUse,
        '--fo1-continue-flare-use-proof'
    )
}
if ($PSBoundParameters.ContainsKey('DestinationGenericDoor')) {
    $godotArgs += @(
        '--fo1-destination-generic-door', $DestinationGenericDoor,
        '--fo1-continue-generic-door-proof'
    )
}
& $Godot @godotArgs
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 1 launcher Continue proof failed with exit code $LASTEXITCODE."
}

$proof = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json -Depth 64
if ($proof.schema -ne 'opennv-fo1-launcher-continue-destination-proof/v1' -or
    $proof.status -ne 'pass-source-bound-launcher-menu-continue-vault13-headless-not-rendered' -or
    $proof.rendered -or $proof.interactive -or $proof.files.Count -ne 0 -or
    $proof.launcher.route -ne 'fo1-new-game' -or $proof.launcher.menuAction -ne 'continue' -or
    $proof.launcher.eventContract -ne 'Fo1MainMenu.ContinueRequested' -or
    $proof.restored.sourceRootVisible -or -not $proof.restored.sourceWalkMaskOnly -or
    -not $proof.transition.destinationSceneLoaded -or
    $proof.transition.activatedTile -ne $save.exitGridTransition.activatedTile -or
    $proof.destinationPresentation.sha256 -ne $save.activeMap.presentation.sha256 -or
    $proof.destinationPresentation.sourceMapSha256 -ne $save.activeMap.sourceMapSha256 -or
    -not $proof.firstControllableDestinationMove.sourceWalkMaskOnly -or
    $proof.firstControllableDestinationMove.destinationMove -eq $proof.transition.activatedTile) {
    throw 'Fallout 1 launcher Continue proof failed its saved destination-map contract.'
}
if ($PSBoundParameters.ContainsKey('DestinationFlareUse') -and
    ($null -eq $proof.flareUse -or -not $proof.flareUse.lit -or
     $proof.flareUse.selectedSymbol -ne 'PID_FLARE' -or
     $proof.flareUse.activeHand -ne 'not-proven-by-script' -or
     $proof.flareUse.expiry -ne 'unimplemented-fail-closed')) {
    throw 'Fallout 1 launcher Continue proof did not preserve the bounded source-script flare state.'
}
if ($PSBoundParameters.ContainsKey('DestinationGenericDoor') -and
    ($null -eq $proof.genericDoor -or -not $proof.genericDoor.opened -or
     -not $proof.genericDoor.movedThroughOpenedBlocker -or
     -not $proof.genericDoor.approach.sourceWalkMaskOnly -or
     $proof.genericDoor.interactionActionPoints -ne 'not-source-backed' -or
     [string]::IsNullOrWhiteSpace($proof.genericDoor.sound) -or
     $proof.genericDoor.framesPerSecond -le 0 -or
     $proof.genericDoor.frameCount -le 1 -or
     $proof.genericDoor.sourceFrame -ne ($proof.genericDoor.frameCount - 1))) {
    throw 'Fallout 1 launcher Continue proof did not preserve the bounded generic-door contract.'
}

Write-Output (
    'OPENNV_FO1_LAUNCHER_CONTINUE_VAULT13_PASS restored={0} move={1}' -f
        $proof.restored.playerTile,
        $proof.firstControllableDestinationMove.destinationMove)
