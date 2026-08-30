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
    [string]$Report,
    [Parameter(Mandatory = $true)]
    [string]$SavePath,
    [string]$ExitGridTransition,
    [string]$DestinationPresentation,
    [string]$ColdRestoreReport
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$resolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
$preflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
foreach ($inputPath in @($Godot, $HexScene, $CharacterStart, $resolver, $preflight)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 1 native first-beat input is missing: $inputPath"
    }
}
if (-not [string]::IsNullOrWhiteSpace($ExitGridTransition) -and
    -not (Test-Path -LiteralPath $ExitGridTransition -PathType Leaf)) {
    throw "Fallout 1 exit-grid transition descriptor is missing: $ExitGridTransition"
}
if (-not [string]::IsNullOrWhiteSpace($DestinationPresentation) -and
    -not (Test-Path -LiteralPath $DestinationPresentation -PathType Leaf)) {
    throw "Fallout 1 destination presentation is missing: $DestinationPresentation"
}
if (-not [string]::IsNullOrWhiteSpace($DestinationPresentation) -and
    [string]::IsNullOrWhiteSpace($ExitGridTransition)) {
    throw 'Fallout 1 destination presentation requires an explicit exit-grid transition descriptor.'
}
if (-not [string]::IsNullOrWhiteSpace($DestinationPresentation) -and
    [string]::IsNullOrWhiteSpace($ColdRestoreReport)) {
    throw 'Fallout 1 destination presentation proof requires an explicit cold-restore report path.'
}
if ([string]::IsNullOrWhiteSpace($DestinationPresentation) -and
    -not [string]::IsNullOrWhiteSpace($ColdRestoreReport)) {
    throw 'Fallout 1 cold-restore report requires an explicit destination presentation.'
}
$outputPaths = @($Report, $SavePath)
if (-not [string]::IsNullOrWhiteSpace($ColdRestoreReport)) {
    $outputPaths += $ColdRestoreReport
}
foreach ($outputPath in $outputPaths) {
    if (Test-Path -LiteralPath $outputPath) {
        throw "Refusing to overwrite Fallout 1 native first-beat output: $outputPath"
    }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($outputPath))
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Fallout 1 native first-beat output needs a parent directory: $outputPath"
    }
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

$scene = Get-Content -LiteralPath $HexScene -Raw | ConvertFrom-Json -Depth 64
if ($scene.schema -ne 'opennv-fo1-hex-scene/v1' -or
    $scene.status -ne 'interactive-hex-topology-proof') {
    throw "FO1 first-beat requires an explicit valid hex-scene cache: $HexScene"
}
$character = Get-Content -LiteralPath $CharacterStart -Raw | ConvertFrom-Json -Depth 64
if ($character.schema -ne 'opennv-fo1-character-start/v1' -or
    $character.status -ne 'prepared-owned-data') {
    throw "FO1 first-beat requires an explicit valid character-start cache: $CharacterStart"
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

$characterSha256 = (Get-FileHash -LiteralPath $CharacterStart -Algorithm SHA256).Hash.ToLowerInvariant()
$exitArguments = @()
if (-not [string]::IsNullOrWhiteSpace($ExitGridTransition)) {
    $exitArguments = @('--fo1-exit-grid-transition', $ExitGridTransition)
}
$destinationArguments = @()
if (-not [string]::IsNullOrWhiteSpace($DestinationPresentation)) {
    $destinationArguments = @('--fo1-destination-presentation', $DestinationPresentation)
}
& $Godot --headless --path $runtime -- `
    --fo1-hex-scene $HexScene `
    --fo1-new-game-demo `
    --fo1-native-first-beat-proof `
    --fo1-character-start $CharacterStart `
    --fo1-character-start-sha256 $characterSha256 `
    --classic-humanoid-donor-preview-set $donor `
    --demo-report $Report `
    --save-path $SavePath `
    --fo1-demo-fast-opening `
    --fo1-demo-skip-opening `
    @exitArguments `
    @destinationArguments
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 1 native first-beat proof failed with exit code $LASTEXITCODE."
}

$proof = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json -Depth 64
$pickup = $proof.mapInventoryPickup
$classicInventoryHud = $proof.classicInventoryHud
$engagement = $proof.adjacentRatEngagement
$invalidPickupStack = $null -ne $pickup -and @($pickup.pickup.collectedItems | Where-Object {
    $_.inventoryAfter -ne ($_.inventoryBefore + $_.objects)
}).Count -gt 0
if ($proof.schema -ne 'opennv-fo1-native-first-beat-headless-proof/v1' -or
    $proof.status -ne 'pass-source-bound-pickup-equip-use-combat-save-restore-headless-not-rendered' -or
    $proof.rendered -or $proof.interactive -or
    $proof.files.Count -ne 0 -or
    -not $proof.playerPresentation.usesOwnedDonor -or
    -not $engagement.approach.sourceWalkMaskOnly -or
    -not $engagement.approach.contactIsAdjacent -or
    $engagement.approach.pathTiles.Count -lt 1 -or
    -not $engagement.result.attempted -or
    -not $engagement.result.hit -or
    $engagement.result.appliedDamage -le 0 -or
    $engagement.target.hitPointsAfter -ne
        ($engagement.target.hitPointsBefore - $engagement.result.appliedDamage) -or
    $engagement.actionPointsAfter -ne
        ($engagement.actionPointsBefore - $engagement.weapon.actionPointCost) -or
    [string]::IsNullOrWhiteSpace($engagement.target.prototypeSha256) -or
    [string]::IsNullOrWhiteSpace($engagement.weapon.prototypeSha256) -or
    $null -eq $pickup -or
    $null -eq $classicInventoryHud -or
    -not $classicInventoryHud.matched -or
    $classicInventoryHud.sequence.Count -ne 6 -or
    $classicInventoryHud.sequence[0].action -ne 'open' -or
    $classicInventoryHud.sequence[1].action -ne 'select' -or
    $classicInventoryHud.sequence[2].action -ne 'equip' -or
    $classicInventoryHud.sequence[3].action -ne 'select' -or
    $classicInventoryHud.sequence[4].action -ne 'equip' -or
    $classicInventoryHud.sequence[5].action -ne 'close' -or
    $null -eq $classicInventoryHud.sourceInventory.source.items -or
    [string]::IsNullOrWhiteSpace($classicInventoryHud.sourceHud.equippedWeaponSymbol) -or
    $classicInventoryHud.restored.equippedWeaponSymbol -ne
        $classicInventoryHud.sequence[4].symbol -or
    $classicInventoryHud.restored.hudEquippedWeaponSymbol -ne
        $classicInventoryHud.sequence[4].symbol -or
    -not $pickup.pickup.sourceWalkMaskOnly -or
    -not $pickup.pickup.approach.contactIsAdjacent -or
    $pickup.pickup.approach.pathTiles.Count -lt 1 -or
    $pickup.pickup.collectedItems.Count -lt 1 -or
    $invalidPickupStack -or
    $pickup.pickup.equippedWeaponSymbol -ne $pickup.WeaponSymbol -or
    $pickup.use.weapon.pid -ne $pickup.WeaponPid -or
    -not $pickup.pickup.persistence.matched -or
    -not $engagement.persistence.matched) {
    throw 'Fallout 1 native first-beat report failed its source-bound pickup/equip/use/combat/save/restore contract.'
}
if (-not [string]::IsNullOrWhiteSpace($ExitGridTransition)) {
    $exit = $proof.caveExitGridTransition
    if ($null -eq $exit -or -not $exit.sourceWalkMaskOnly -or $exit.pathTiles.Count -lt 1 -or
        $null -eq $exit.doorActivation -or -not $exit.doorActivation.sourceDoor -or
        $exit.doorActivation.doorApproach.Count -lt 1 -or
        $null -eq $exit.contract.activatedTile -or -not $exit.contract.transitionCommitted -or -not $exit.persistence.matched -or
        [string]::IsNullOrWhiteSpace($exit.contract.destination.mapSha256) -or
        ([string]::IsNullOrWhiteSpace($DestinationPresentation) -and $exit.contract.destinationSceneLoaded) -or
        (-not [string]::IsNullOrWhiteSpace($DestinationPresentation) -and -not $exit.contract.destinationSceneLoaded)) {
        throw 'Fallout 1 native first-beat report failed its source-bound cave exit-grid transition contract.'
    }
}
if (-not [string]::IsNullOrWhiteSpace($DestinationPresentation)) {
    $destination = $proof.caveExitGridTransition.destinationPresentation
    $move = $proof.caveExitGridTransition.firstControllableDestinationMove
    if ($null -eq $destination -or $destination.sourcePlayerFallback -or
        [string]::IsNullOrWhiteSpace($destination.sha256) -or $null -eq $move -or
        -not $move.sourceWalkMaskOnly -or $move.destinationMove -eq $proof.caveExitGridTransition.contract.activatedTile) {
        throw 'Fallout 1 native first-beat report failed its loaded destination presentation/control contract.'
    }
    & $Godot --headless --path $runtime -- `
        --fo1-hex-scene $HexScene `
        --fo1-destination-cold-restore-proof `
        --classic-humanoid-donor-preview-set $donor `
        --fo1-exit-grid-transition $ExitGridTransition `
        --fo1-destination-presentation $DestinationPresentation `
        --report $ColdRestoreReport `
        --save-path $SavePath
    if ($LASTEXITCODE -ne 0) {
        throw "Fallout 1 destination cold-restore proof failed with exit code $LASTEXITCODE."
    }
    $cold = Get-Content -LiteralPath $ColdRestoreReport -Raw | ConvertFrom-Json -Depth 64
    if ($cold.schema -ne 'opennv-fo1-destination-cold-restore-proof/v1' -or
        $cold.status -ne 'pass-source-bound-vault13-cold-restore-headless-not-rendered' -or
        -not $cold.coldProcess -or $cold.rendered -or $cold.interactive -or
        $cold.files.Count -ne 0 -or $cold.sourceScene.visible -or
        -not $cold.transition.destinationSceneLoaded -or
        $cold.transition.activatedTile -ne $proof.caveExitGridTransition.contract.activatedTile -or
        $cold.destinationPresentation.sha256 -ne $destination.sha256 -or
        $cold.destinationPresentation.sourceMapSha256 -ne $destination.sourceMapSha256 -or
        -not $cold.restored.sourceWalkMaskOnly -or
        -not $cold.firstControllableDestinationMove.sourceWalkMaskOnly -or
        $cold.firstControllableDestinationMove.destinationMove -eq $cold.transition.activatedTile) {
        throw 'Fallout 1 destination cold-restore report failed its explicit active-map/hash contract.'
    }
}
Write-Output (
    'OPENNV_FO1_NATIVE_FIRST_BEAT_HEADLESS_PASS loot={0} rat={1} contact={2} damage={3} apCost={4}' -f
        $pickup.HostPid,
        $engagement.target.pid,
        $engagement.approach.contactTile,
        $engagement.result.appliedDamage,
        $engagement.weapon.actionPointCost)
