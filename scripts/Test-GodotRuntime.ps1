[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Godot,
    [string]$FalloutNewVegasData = "",
    [string]$ExpectedMeshesBsaSha256 = "",
    [string]$RetailLogicalPath = "",
    [string]$Fo1HexScene = "",
    [string]$ClassicHumanoidInstallManifest = ""
)

# Immutable diagnostic/acceptance contracts; runtime policy is configuration-owned.
$TestGodotRuntimeContractNEgativE150Point0 = -150.0
$TestGodotRuntimeContract0Point0001 = 0.0001
$TestGodotRuntimeContract0Point001 = 0.001
$TestGodotRuntimeContract0Point015 = 0.015
$TestGodotRuntimeContract1Point8 = 1.8
$TestGodotRuntimeContract10 = 10
$TestGodotRuntimeContract1048 = 1048
$TestGodotRuntimeContract12 = 12
$TestGodotRuntimeContract1463 = 1463
$TestGodotRuntimeContract1473 = 1473
$TestGodotRuntimeContract1494 = 1494
$TestGodotRuntimeContract15 = 15
$TestGodotRuntimeContract17 = 17
$TestGodotRuntimeContract170 = 170
$TestGodotRuntimeContract17690 = 17690
$TestGodotRuntimeContract17891 = 17891
$TestGodotRuntimeContract18Point0 = 18.0
$TestGodotRuntimeContract181176 = 181176
$TestGodotRuntimeContract20 = 20
$TestGodotRuntimeContract200 = 200
$TestGodotRuntimeContract201 = 201
$TestGodotRuntimeContract21 = 21
$TestGodotRuntimeContract2142 = 2142
$TestGodotRuntimeContract27664 = 27664
$TestGodotRuntimeContract30196 = 30196
$TestGodotRuntimeContract474 = 474
$TestGodotRuntimeContract5 = 5
$TestGodotRuntimeContract52 = 52
$TestGodotRuntimeContract53 = 53
$TestGodotRuntimeContract54 = 54
$TestGodotRuntimeContract6 = 6
$TestGodotRuntimeContract6Point0 = 6.0
$TestGodotRuntimeContract60Point0 = 60.0
$TestGodotRuntimeContract64Point0 = 64.0
$TestGodotRuntimeContract7 = 7
$TestGodotRuntimeContract80 = 80
$TestGodotRuntimeContract87903 = 87903
$DemonstratedCombatKillPaths = 4
$CampaignSaveSchema = "opennv-campaign-save/v7"


$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$contentRoot = Join-Path $repoRoot "content"
$solution = Join-Path $runtimeRoot "OpenNV.sln"
$containerInventoryProbe = Join-Path $repoRoot `
    "contract-tests\ContainerInventoryContractProbe\ContainerInventoryContractProbe.csproj"
$actorAnimationPlaybackProbe = Join-Path $repoRoot `
    "contract-tests\ActorAnimationPlaybackProbe\ActorAnimationPlaybackProbe.csproj"
$actorComplexionProbe = Join-Path $repoRoot `
    "contract-tests\ActorComplexionContractProbe\ActorComplexionContractProbe.csproj"
$gamebryoPackageSelectionProbe = Join-Path $repoRoot `
    "contract-tests\GamebryoPackageSelectionProbe\GamebryoPackageSelectionProbe.csproj"
$gamebryoUiTileProbe = Join-Path $repoRoot `
    "contract-tests\GamebryoUiTileContractProbe\GamebryoUiTileContractProbe.csproj"
$gamebryoPackagePlacementProbe = Join-Path $repoRoot `
    "contract-tests\GamebryoPackagePlacementProbe\GamebryoPackagePlacementProbe.csproj"
$exporter = Join-Path $contentRoot "tools\export_static_nif_gltf.py"
$preparer = Join-Path $contentRoot "tools\prepare_legal_assets.py"
$reportValidator = Join-Path $contentRoot "tools\validate_runtime_report.py"
$classicHumanoidPreflight = Join-Path $PSScriptRoot "Assert-ClassicHumanoidDonorPreviewSet.ps1"
$classicHumanoidResolver = Join-Path $PSScriptRoot "Resolve-ClassicHumanoidDonorPreviewSet.ps1"
$runtimeConfigurationPath = Join-Path $runtimeRoot "config\open-nv-runtime-v1.json"
$RuntimeConfigurationJsonDepth = 100
$runtimeConfiguration = Get-Content -Raw -LiteralPath $runtimeConfigurationPath |
    ConvertFrom-Json -Depth $RuntimeConfigurationJsonDepth
$ownedData = $runtimeConfiguration.legalAssets.ownedData
$defaultCellRecipe = [string]$runtimeConfiguration.legalAssets.defaultCellRecipe
$linkedWorldCellRecipe = [string]$runtimeConfiguration.legalAssets.linkedWorldProofCellRecipe
if ([string]::IsNullOrWhiteSpace($RetailLogicalPath)) {
    $RetailLogicalPath = [string]$runtimeConfiguration.legalAssets.smokeModelLogicalPath
}
$fixtureModel = "res://tests/fixtures/opaque-triangle.gltf"
$fixtureSidecar = "res://tests/fixtures/opaque-triangle.opennv.json"

function Resolve-OwnedDataRoot(
    [string]$SelectedRoot,
    [string]$MasterFile,
    [string]$DataDirectoryName
) {
    $root = [IO.Path]::GetFullPath($SelectedRoot)
    if (Test-Path -LiteralPath (Join-Path $root $MasterFile) -PathType Leaf) {
        return $root
    }
    $data = Join-Path $root $DataDirectoryName
    if (Test-Path -LiteralPath (Join-Path $data $MasterFile) -PathType Leaf) {
        return [IO.Path]::GetFullPath($data)
    }
    throw "Select either the configured game installation folder or its data folder."
}

foreach ($path in @($Godot, $solution, $containerInventoryProbe, $actorAnimationPlaybackProbe, $actorComplexionProbe, $gamebryoPackageSelectionProbe, $gamebryoUiTileProbe, $gamebryoPackagePlacementProbe, $exporter, $preparer, $reportValidator, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing OpenNV Godot gate input: $path"
    }
}
$fo1DonorArguments = @()
if (-not [string]::IsNullOrWhiteSpace($Fo1HexScene)) {
    if ([string]::IsNullOrWhiteSpace($ClassicHumanoidInstallManifest)) {
        throw "Fallout 1 tactical proof requires -ClassicHumanoidInstallManifest; no substitute player body is admitted."
    }
    $classicHumanoidDonorPreviewSet = & $classicHumanoidResolver -InstallManifest $ClassicHumanoidInstallManifest
    if ($LASTEXITCODE -ne 0) { throw "Classic humanoid install-manifest resolution failed." }
    & $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
    if ($LASTEXITCODE -ne 0) { throw "Classic humanoid donor preflight failed." }
    $fo1DonorArguments = @("--classic-humanoid-donor-preview-set", $classicHumanoidDonorPreviewSet)
}

$sourceRoots = @(
    $runtimeRoot,
    $contentRoot,
    (Join-Path $repoRoot ".github"),
    (Join-Path $repoRoot "desktop\src"),
    (Join-Path $repoRoot "release"),
    (Join-Path $repoRoot "scripts")
)
$sourceFiles = @(
    Get-ChildItem -LiteralPath $sourceRoots -Recurse -File |
        Where-Object Extension -in @(".cs", ".csproj", ".gd", ".gdshader", ".json", ".mjs", ".ps1", ".py", ".sln", ".tres", ".yml", ".yaml")
)
$forbiddenPattern = '(?i)open' + 'mw|nif' + 'test|onv' + 'skel'
$forbidden = @($sourceFiles | Select-String -Pattern $forbiddenPattern)
if ($forbidden.Count -gt 0) {
    throw "Quarantined engine dependency found in clean runtime/content source:`n$($forbidden | Out-String)"
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
& (Join-Path $PSScriptRoot "Test-SourceConstantPolicy.ps1")
if ($LASTEXITCODE -ne 0) { throw "OpenNV source constant policy failed." }
& python -m unittest discover -s (Join-Path $contentRoot "tests") -p "test_*.py" -v
if ($LASTEXITCODE -ne 0) { throw "Direct content tests failed." }
& dotnet build $solution --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Godot Release build failed." }
& dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "OpenNV C# format/analyzer gate failed." }
& dotnet build (Join-Path $runtimeRoot "OpenNV.csproj") --configuration Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Godot Debug build failed." }
& dotnet run --project $containerInventoryProbe --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Container inventory contract probe failed." }
& dotnet run --project $actorAnimationPlaybackProbe --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Actor animation playback contract probe failed." }
& dotnet run --project $actorComplexionProbe --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Actor complexion contract probe failed." }
& dotnet run --project $gamebryoPackageSelectionProbe --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Gamebryo package selection contract probe failed." }
& dotnet run --project $gamebryoUiTileProbe --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Gamebryo UI tile contract probe failed." }
& dotnet run --project $gamebryoPackagePlacementProbe --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Gamebryo package placement contract probe failed." }

$startupOutput = & $Godot --headless --xr-mode off --path $runtimeRoot 2>&1
if ($LASTEXITCODE -ne 0 -or ($startupOutput | Out-String) -notmatch "OPENNV_GODOT_EXPERIMENTAL_READY playable=0") {
    throw "OpenNV experimental startup gate failed:`n$($startupOutput | Out-String)"
}

$xrReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-xr-rig-{0}.json" -f [guid]::NewGuid().ToString("N"))
$xrSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-xr-rig-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
try {
    $xrOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
        --xr-rig-proof --save-path $xrSave --report $xrReport 2>&1
    $xrText = $xrOutput | Out-String
    if ($LASTEXITCODE -ne 0 -or $xrText -notmatch "OPENNV_OPENXR_RIG_PASS" -or $xrText -match "(?m)^ERROR:") {
        throw "OpenNV OpenXR rig gate failed:`n$xrText"
    }
    & python $reportValidator --mode xr --report $xrReport
    if ($LASTEXITCODE -ne 0) { throw "OpenNV OpenXR rig report is invalid." }
}
finally {
    foreach ($temporaryPath in @($xrReport, $xrSave)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$fo1TacticalPassed = $false
if (-not [string]::IsNullOrWhiteSpace($Fo1HexScene)) {
    $fo1Scene = [IO.Path]::GetFullPath($Fo1HexScene)
    if (-not (Test-Path -LiteralPath $fo1Scene -PathType Leaf)) {
        throw "Fallout 1 hex scene is missing: $fo1Scene"
    }
    $fo1SceneContract = Get-Content -Raw -LiteralPath $fo1Scene |
        ConvertFrom-Json -Depth $RuntimeConfigurationJsonDepth
    $fo1Report = Join-Path ([IO.Path]::GetTempPath()) ("opennv-fo1-tactical-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $fo1Save = Join-Path ([IO.Path]::GetTempPath()) ("opennv-fo1-tactical-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $fo1Output = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --fo1-hex-scene $fo1Scene @fo1DonorArguments `
            --fo1-tactical-proof --save-path $fo1Save --report $fo1Report 2>&1
        $fo1Text = $fo1Output | Out-String
        if ($LASTEXITCODE -ne 0 -or $fo1Text -notmatch "OPENNV_FO1_TACTICAL_PROOF_PASS" -or
            $fo1Text -match "(?m)^ERROR:") {
            throw "Fallout 1 tactical proof failed:`n$fo1Text"
        }
        $fo1 = Get-Content -Raw -LiteralPath $fo1Report | ConvertFrom-Json
        $fo1ExpectedTargets = @(
            $fo1SceneContract.combat.mobs |
                Where-Object { [int]$_.serial -eq [int]$fo1.combat.targetSerial }
        )
        if ($fo1ExpectedTargets.Count -ne 1) {
            throw "Fallout 1 tactical proof target is not uniquely represented in the supplied scene contract."
        }
        $fo1ExpectedTarget = $fo1ExpectedTargets[0]
        $fo1OwnedPresentation = $fo1SceneContract.combat.player.ownedPresentation
        if ($fo1.schema -ne "opennv-fo1-tactical-proof/v1" -or
            $fo1.status -ne "pass" -or
            [int]$fo1.grid.width -ne $TestGodotRuntimeContract200 -or
            [int]$fo1.grid.height -ne $TestGodotRuntimeContract200 -or
            [double]$fo1.grid.flatToFlatMeters -ne 1.0 -or
            $fo1.grid.layout -ne "fallout-even-column-offset-flat-v1" -or
            [int]$fo1.entryTile -ne $TestGodotRuntimeContract17690 -or
            [int]$fo1.movedToTile -ne $TestGodotRuntimeContract17891 -or
            [int]$fo1.moveDistanceMeters -ne 1 -or
            [int]$fo1.movementCostAp -ne 1 -or
            [int]$fo1.turnAfterEnd -lt 2 -or
            [int]$fo1.actionPointsAfterEnd -ne $TestGodotRuntimeContract10 -or
            $fo1.combat.targetPid -ne $fo1ExpectedTarget.pid -or
            [int]$fo1.combat.targetSourceHitPoints -ne $TestGodotRuntimeContract6 -or
            [int]$fo1.combat.targetSourceArmorClass -ne 4 -or
            [int]$fo1.combat.targetSourceMeleeDamage -ne 3 -or
            [int]$fo1.combat.targetSourceSequence -ne $TestGodotRuntimeContract12 -or
            [int]$fo1.combat.targetSourceTeam -ne 1 -or
            [int]$fo1.combat.targetSourceAiPacket -ne $TestGodotRuntimeContract12 -or
            [int]$fo1.combat.playerWeaponApCost -ne $TestGodotRuntimeContract5 -or
            [int]$fo1.combat.playerMeleeApCost -ne 3 -or
            [int]$fo1.combat.attacks -lt 2 -or
            [int]$fo1.combat.rangedAttempts -lt 3 -or
            [int]$fo1.combat.rangedHits -lt 2 -or
            [int]$fo1.combat.meleeAttempts -lt 2 -or
            [int]$fo1.combat.meleeHits -lt 2 -or
            [int]$fo1.combat.reloads -ne 1 -or
            [int]$fo1.combat.magazineRounds -ne $TestGodotRuntimeContract12 -or
            [int]$fo1.combat.reserveRounds -lt 1 -or
            [int]$fo1.combat.kills -ne $DemonstratedCombatKillPaths -or
            $fo1.combat.equippedWeaponSymbol -ne "PID_KNIFE" -or
            -not [bool]$fo1.combat.weaponSwapRoundTrip -or
            [int]$fo1.combat.hostileMarkers -ne $TestGodotRuntimeContract20 -or
            [int]$fo1.combat.hostileHealthLabels -ne $TestGodotRuntimeContract20 -or
            -not [bool]$fo1.combat.targetCycleAndFrame -or
            -not [bool]$fo1.combat.corpseVisible -or
            [double]$fo1.combat.corpseGroundErrorMeters -gt $TestGodotRuntimeContract0Point0001 -or
            [int]$fo1.combat.localActivationDistanceHexes -ne $TestGodotRuntimeContract6 -or
            -not [bool]$fo1.combat.wholeCaveAggroPrevented -or
            -not [bool]$fo1.combat.hostileMarkerDepthTested -or
            [int]$fo1.session.livingMobs -ne
                (@($fo1SceneContract.combat.mobs).Count - [int]$fo1.combat.kills) -or
            [int]$fo1.sourceSpriteAnchoring.sprites -ne $TestGodotRuntimeContract1494 -or
            [int]$fo1.sourceSpriteAnchoring.actorSprites -ne $TestGodotRuntimeContract21 -or
            $fo1.sourceSpriteAnchoring.actorBillboard -ne "fixed-y" -or
            [int]$fo1.sourceSpriteAnchoring.staticWorldSprites -ne $TestGodotRuntimeContract1473 -or
            $fo1.sourceSpriteAnchoring.staticBillboard -ne "disabled-world-locked" -or
            [double]$fo1.sourceSpriteAnchoring.staticWorldYawDegrees -ne $TestGodotRuntimeContractNEgativE150Point0 -or
            [double]$fo1.sourceSpriteAnchoring.maximumAnchorError -gt $TestGodotRuntimeContract0Point0001 -or
            [bool]$fo1.sourceSpriteAnchoring.sourceStaticOverlayVisible -or
            -not [bool]$fo1.ownedCreature3d.enabled -or
            -not [bool]$fo1.ownedCreature3d.sourceRatSpritesHidden -or
            [int]$fo1.ownedCreature3d.instances -ne $TestGodotRuntimeContract20 -or
            [int]$fo1.ownedCreature3d.meshesPerInstance -ne $TestGodotRuntimeContract6 -or
            [int]$fo1.ownedCreature3d.skeletons -ne $TestGodotRuntimeContract20 -or
            [int]$fo1.ownedCreature3d.animationPlayers -ne $TestGodotRuntimeContract20 -or
            [int]$fo1.ownedCreature3d.importedAnimations -ne $TestGodotRuntimeContract5 -or
            [int]$fo1.ownedCreature3d.hiddenIntactStateGoreMeshes -ne $TestGodotRuntimeContract80 -or
            -not [bool]$fo1.ownedPlayer3d.enabled -or
            -not [bool]$fo1.ownedPlayer3d.sourceSpriteHidden -or
            $fo1.ownedPlayer3d.formId -ne $fo1OwnedPresentation.sourceActor.baseFormId -or
            [int]$fo1.ownedPlayer3d.meshes -ne $TestGodotRuntimeContract15 -or
            [int]$fo1.ownedPlayer3d.skeletons -ne 1 -or
            [int]$fo1.ownedPlayer3d.animationPlayers -ne 1 -or
            [int]$fo1.ownedPlayer3d.importedAnimations -ne $TestGodotRuntimeContract5 -or
            $fo1.ownedPlayer3d.thirdPersonWeapon.formId -ne $fo1OwnedPresentation.thirdPersonWeapon.weaponFormId -or
            $fo1.ownedPlayer3d.thirdPersonMeleeWeapon.formId -ne $fo1OwnedPresentation.thirdPersonMeleeWeapon.weaponFormId -or
            $fo1.ownedPlayer3d.thirdPersonMeleeWeapon.gameplayPid -ne $fo1OwnedPresentation.thirdPersonMeleeWeapon.gameplayPid -or
            [double]$fo1.ownedPlayer3d.heightMeters -lt $TestGodotRuntimeContract1Point8 -or
            [int]$fo1.session.playerPresentation.moveAnimationPlaybacks -lt 1 -or
            [int]$fo1.cave3d.boundaryEdges -lt 1 -or
            [int]$fo1.cave3d.obstacles -ne $TestGodotRuntimeContract1048 -or
            [int]$fo1.cave3d.triangles -lt 1 -or
            -not [bool]$fo1.cave3d.fixedWorldGeometry -or
            -not [bool]$fo1.cave3d.defaultVisible -or
            [int]$fo1.cave3d.cutawayCandidates -lt 1 -or
            [int]$fo1.cave3d.combatCutawayOccluders -lt 1 -or
            [int]$fo1.cave3d.meltShaderMaterials -lt [int]$fo1.cave3d.cutawayCandidates -or
            -not [bool]$fo1.cave3d.shaderDrivenCameraMelt -or
            -not [bool]$fo1.cave3d.owned.enabled -or
            [int]$fo1.cave3d.owned.instances -ne $TestGodotRuntimeContract170 -or
            [int]$fo1.cave3d.owned.meshInstances -ne $TestGodotRuntimeContract474 -or
            [int]$fo1.cave3d.owned.surfaceInstances -ne $TestGodotRuntimeContract2142 -or
            [int]$fo1.cave3d.owned.materialBindings -ne $TestGodotRuntimeContract201 -or
            [int]$fo1.cave3d.owned.roles.'terrain-envelope' -ne 1 -or
            [int]$fo1.cave3d.owned.roles.'wall-ribbon' -ne $TestGodotRuntimeContract52 -or
            [int]$fo1.cave3d.owned.roles.'vault-portal' -ne 1 -or
            [int]$fo1.cave3d.owned.roles.'large-rock' -ne $TestGodotRuntimeContract54 -or
            [int]$fo1.cave3d.owned.roles.'small-rock' -ne $TestGodotRuntimeContract53 -or
            [int]$fo1.cave3d.owned.roles.stalagmite -ne $TestGodotRuntimeContract7 -or
            [int]$fo1.cave3d.owned.roles.'vault-frame' -ne 1 -or
            [int]$fo1.cave3d.owned.roles.'entrance-corpse' -ne 1 -or
            -not [bool]$fo1.cave3d.owned.continuousFloorVisible -or
            [int]$fo1.cave3d.owned.continuousFloorHexes -ne $TestGodotRuntimeContract30196 -or
            [int]$fo1.cave3d.owned.continuousFloorTriangles -ne $TestGodotRuntimeContract181176 -or
            [int]$fo1.cave3d.owned.continuousFloorMeshInstances -ne 1 -or
            [bool]$fo1.optionalHexOverlay.defaultVisible -or
            -not [bool]$fo1.optionalHexOverlay.togglePassed -or
            -not [bool]$fo1.optionalHexOverlay.depthTested -or
            -not [bool]$fo1.optionalHexOverlay.opaque -or
            [int]$fo1.optionalHexOverlay.hexes -ne $TestGodotRuntimeContract27664 -or
            [int]$fo1.optionalHexOverlay.uniqueEdges -ne $TestGodotRuntimeContract87903 -or
            [int]$fo1.optionalHexOverlay.presentationFootprintBlockedHexes -ne $TestGodotRuntimeContract1463 -or
            -not [bool]$fo1.camera.middleMouseOrbit -or
            -not [bool]$fo1.camera.rightMousePan -or
            -not [bool]$fo1.camera.wheelZoomTowardCursor -or
            -not [bool]$fo1.camera.thirdPersonToggle -or
            -not [bool]$fo1.camera.thirdPersonShoulderTacticalOrbit -or
            -not [bool]$fo1.camera.thirdPersonClickMovementUsesHexCenters -or
            -not [bool]$fo1.camera.firstPersonToggle -or
            -not [bool]$fo1.camera.firstPersonContinuousLocomotion -or
            [double]$fo1.camera.firstPersonMoveDistanceMeters -le 0.0 -or
            [bool]$fo1.camera.firstPersonTacticalActionPointsConsumed -or
            -not [bool]$fo1.camera.firstPersonHitscanFire -or
            [int]$fo1.camera.firstPersonMissProofShots -lt 1 -or
            [int]$fo1.camera.firstPersonProofShots -lt 2 -or
            [int]$fo1.camera.firstPersonProofHits -lt 1 -or
            -not [bool]$fo1.camera.firstPersonHitConfirmed -or
            -not [bool]$fo1.camera.firstPersonMeleeConfirmed -or
            -not [bool]$fo1.camera.firstPersonMouseUpLooksUp -or
            [double]$fo1.camera.firstPersonPitchAfterMouseUpDegrees -le [double]$fo1.camera.firstPersonPitchBeforeMouseUpDegrees -or
            [double]$fo1.camera.firstPersonForwardYAfterMouseUp -le 0.0 -or
            -not [bool]$fo1.camera.firstPersonHeldWeaponSuppressed -or
            -not [bool]$fo1.camera.firstPersonHoverSelectorSuppressed -or
            $fo1.camera.selectorHexBasis -ne "authoritative-flat-top" -or
            $fo1.combatPresentation.schema -ne "opennv-fo1-combat-presentation/v1" -or
            [int]$fo1.combatPresentation.tracers -ne [int]$fo1.combat.rangedAttempts -or
            [int]$fo1.combatPresentation.impacts -ne [int]$fo1.combatPresentation.tracers -or
            [int]$fo1.combatPresentation.casings -ne [int]$fo1.combatPresentation.tracers -or
            [int]$fo1.combatPresentation.groundedCasings -ne [int]$fo1.combatPresentation.casings -or
            [int]$fo1.combatPresentation.ricochets -lt 1 -or
            [int]$fo1.combatPresentation.meleeSweeps -ne [int]$fo1.combat.meleeAttempts -or
            [double]$fo1.combatPresentation.impactRadiusMeters -gt $TestGodotRuntimeContract0Point015 -or
            [int]$fo1.combatPresentation.audioEvents -lt $TestGodotRuntimeContract10 -or
            @($fo1.combatPresentation.audioRoles).Count -ne $TestGodotRuntimeContract10 -or
            [bool]$fo1.windowsAppControlUsed -or
            [bool]$fo1.foregroundActivationUsed -or
            [bool]$fo1.foregroundInputInjected) {
            throw "Fallout 1 tactical proof report is invalid."
        }
        $fo1TacticalPassed = $true
    }
    finally {
        foreach ($temporaryPath in @($fo1Report, $fo1Save)) {
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }
}

$dioramaReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-classic-diorama-{0}.json" -f [guid]::NewGuid().ToString("N"))
$dioramaSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-classic-diorama-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
try {
    $dioramaOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
        --classic-diorama-rig-proof --save-path $dioramaSave --report $dioramaReport 2>&1
    $dioramaText = $dioramaOutput | Out-String
    if ($LASTEXITCODE -ne 0 -or $dioramaText -notmatch "OPENNV_CLASSIC_DIORAMA_RIG_PASS" -or
        $dioramaText -match "(?m)^ERROR:") {
        throw "OpenNV Classic Diorama rig gate failed:`n$dioramaText"
    }
    $diorama = Get-Content -Raw -LiteralPath $dioramaReport | ConvertFrom-Json
    if ($diorama.schema -ne "opennv-classic-diorama-rig/v1" -or
        $diorama.status -ne "pass" -or
        $diorama.presentation -ne "classic-diorama" -or
        $diorama.simulation -ne "shared-gameplay-session" -or
        $diorama.cameraType -ne "Camera3D" -or
        $diorama.cameraName -ne "ClassicDioramaCamera" -or
        $diorama.orbitName -ne "ClassicDioramaOrbit" -or
        $diorama.projection -ne "orthogonal" -or
        [double]$diorama.initialSizeMeters -ne $TestGodotRuntimeContract18Point0 -or
        [double]$diorama.minimumSizeMeters -ne $TestGodotRuntimeContract6Point0 -or
        [double]$diorama.maximumSizeMeters -ne $TestGodotRuntimeContract64Point0 -or
        [double]$diorama.zoomedSizeMeters -ge $TestGodotRuntimeContract18Point0 -or
        [math]::Abs([double]$diorama.yawStepDegrees - $TestGodotRuntimeContract60Point0) -gt $TestGodotRuntimeContract0Point001 -or
        @($diorama.panKeys).Count -ne 4 -or
        @($diorama.panKeys) -notcontains "W" -or
        @($diorama.rotationKeys) -notcontains "Q" -or
        @($diorama.rotationKeys) -notcontains "E" -or
        $diorama.zoomInput -ne "mouse-wheel" -or
        $diorama.resetKey -ne "Home" -or
        $diorama.gameplaySession.schema -ne $CampaignSaveSchema -or
        [bool]$diorama.turnSimulationConnected -or
        -not [bool]$diorama.noRetailData) {
        throw "OpenNV Classic Diorama rig report is invalid."
    }
}
finally {
    foreach ($temporaryPath in @($dioramaReport, $dioramaSave)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$retailModel = ""
$retailSidecar = ""
$temporaryCache = ""
$poolPracticeValidated = $false
$flatControlsValidated = $false
$worldPickupValidated = $false
try {
if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
    $resolvedFalloutData = Resolve-OwnedDataRoot `
        $FalloutNewVegasData `
        ([string]$ownedData.masterFile) `
        ([string]$ownedData.dataDirectoryName)
    $temporaryCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-legal-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $prepareArguments = @(
        $preparer,
        "--data-root", $resolvedFalloutData,
        "--cache-root", $temporaryCache,
        "--logical-model", $RetailLogicalPath,
        "--cell-recipe", $linkedWorldCellRecipe
    )
    if (-not [string]::IsNullOrWhiteSpace($ExpectedMeshesBsaSha256)) {
        $prepareArguments += @("--expected-meshes-bsa-sha256", $ExpectedMeshesBsaSha256)
    }
    & python @prepareArguments
    if ($LASTEXITCODE -ne 0) { throw "Direct legal-asset preparation failed." }
    $install = Get-Content -Raw -LiteralPath (Join-Path $temporaryCache "install-manifest.json") | ConvertFrom-Json
    if ($install.schema -ne "opennv-legal-asset-cache/v1" -or
        $install.status -ne "prepared-legal-assets") {
        throw "Legal-asset cache manifest is invalid."
    }
    $retailModel = [string]$install.outputs.model
    $retailSidecar = [string]$install.outputs.sidecar
    $cellReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-linked-cell-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $cellSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-linked-cell-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $cellOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$install.outputs.cellScene) `
            --actor-scenes ([string]$install.outputs.actorScenes) `
            --save-path $cellSave --report $cellReport --portal-proof --quit-after-load 2>&1
        $cellText = $cellOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or $cellText -notmatch "OPENNV_GODOT_CELL_PASS" -or $cellText -match "(?m)^ERROR:") {
            throw "OpenNV linked-cell gate failed:`n$cellText"
        }
        & python $reportValidator --mode cell --report $cellReport `
            --install-manifest (Join-Path $temporaryCache "install-manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "OpenNV linked-cell report is invalid." }
    }
    finally {
        foreach ($path in @($cellReport, $cellSave)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    $routeCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-route-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $routeCheckpointReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-route-opening-checkpoint-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $routeResumeReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-route-opening-resume-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $routeTravelReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-route-travel-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $routeReloadReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-route-reload-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $routeOpeningSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-route-opening-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        & python $preparer --data-root $resolvedFalloutData --cache-root $routeCache `
            --logical-model $RetailLogicalPath --cell-recipe $defaultCellRecipe
        if ($LASTEXITCODE -ne 0) { throw "Default owned route preparation failed." }
        $routeInstall = Join-Path $routeCache "install-manifest.json"
        $preparedRoute = Get-Content -Raw -LiteralPath $routeInstall | ConvertFrom-Json

        $checkpointOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$preparedRoute.outputs.cellScene) `
            --actor-scenes ([string]$preparedRoute.outputs.actorScenes) `
            --opening-manifest ([string]$preparedRoute.outputs.openingManifest) `
            --save-path $routeOpeningSave --new-game --opening-proof checkpoint `
            --opening-proof-name NIKAMI --opening-proof-timeout-seconds 600 `
            --report $routeCheckpointReport 2>&1
        $checkpointText = $checkpointOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $checkpointText -notmatch "OPENNV_OPENING_ACCEPTANCE_PASS mode=checkpoint" -or
            $checkpointText -match "(?m)^ERROR:") {
            throw "Default route opening checkpoint failed:`n$checkpointText"
        }
        $resumeOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$preparedRoute.outputs.cellScene) `
            --actor-scenes ([string]$preparedRoute.outputs.actorScenes) `
            --opening-manifest ([string]$preparedRoute.outputs.openingManifest) `
            --save-path $routeOpeningSave --opening-proof resume `
            --opening-proof-name NIKAMI --opening-proof-timeout-seconds 600 `
            --report $routeResumeReport 2>&1
        $resumeText = $resumeOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $resumeText -notmatch "OPENNV_OPENING_ACCEPTANCE_PASS mode=resume" -or
            $resumeText -match "(?m)^ERROR:") {
            throw "Default route opening resume failed:`n$resumeText"
        }

        $travelOutput = & $Godot --xr-mode off --path $runtimeRoot -- `
            --reuse-cache --cache-root $routeCache --save-path $routeOpeningSave `
            --opening-menu-proof continue --route-travel-proof first-run `
            --report $routeTravelReport 2>&1
        $travelText = $travelOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $travelText -notmatch "OPENNV_OWNED_MENU_ACCEPTANCE action=continue transport=godot-button-signal" -or
            $travelText -notmatch "OPENNV_FLAT_ROUTE_TRAVEL_PASS phase=first-run" -or
            $travelText -match "(?m)^ERROR:") {
            throw "Default route normal-input travel gate failed:`n$travelText"
        }
        & python $reportValidator --mode flat-route-travel --report $routeTravelReport `
            --install-manifest $routeInstall
        if ($LASTEXITCODE -ne 0) { throw "Default route normal-input travel report is invalid." }

        $reloadOutput = & $Godot --xr-mode off --path $runtimeRoot -- `
            --reuse-cache --cache-root $routeCache --save-path $routeOpeningSave `
            --opening-menu-proof continue --route-travel-proof cold-reload `
            --report $routeReloadReport 2>&1
        $reloadText = $reloadOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $reloadText -notmatch "OPENNV_OWNED_MENU_ACCEPTANCE action=continue transport=godot-button-signal" -or
            $reloadText -notmatch "OPENNV_FLAT_ROUTE_TRAVEL_PASS phase=cold-reload" -or
            $reloadText -match "(?m)^ERROR:") {
            throw "Default route cold-reload gate failed:`n$reloadText"
        }
        & python $reportValidator --mode flat-route-reload --report $routeReloadReport `
            --install-manifest $routeInstall --prior-report $routeTravelReport
        if ($LASTEXITCODE -ne 0) { throw "Default route cold-reload report is invalid." }
    }
    finally {
        foreach ($path in @(
            $routeCheckpointReport,
            $routeResumeReport,
            $routeTravelReport,
            $routeReloadReport,
            $routeOpeningSave
        )) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        if (Test-Path -LiteralPath $routeCache) {
            Remove-Item -LiteralPath $routeCache -Recurse -Force
        }
    }

    $flatReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-flat-controls-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $flatSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-flat-controls-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $flatOutput = & $Godot --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$install.outputs.cellScene) `
            --actor-scenes ([string]$install.outputs.actorScenes) `
            --save-path $flatSave --flat-controls-proof --report $flatReport 2>&1
        $flatText = $flatOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $flatText -notmatch "OPENNV_FLAT_CONTROLS_PASS" -or
            $flatText -match "(?m)^ERROR:") {
            throw "OpenNV flat controls gate failed:`n$flatText"
        }
        & python $reportValidator --mode flat-controls --report $flatReport `
            --install-manifest (Join-Path $temporaryCache "install-manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "OpenNV flat controls report is invalid." }
    $flatControlsValidated = $true
    }
    finally {
        foreach ($path in @($flatReport, $flatSave)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    $pickupReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-world-pickup-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $pickupSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-world-pickup-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $pickupOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$install.outputs.cellScene) `
            --save-path $pickupSave --world-interaction-proof --report $pickupReport 2>&1
        $pickupText = $pickupOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $pickupText -notmatch "OPENNV_WORLD_PICKUP_PASS" -or
            $pickupText -match "(?m)^ERROR:") {
            throw "OpenNV world pickup gate failed:`n$pickupText"
        }
        & python $reportValidator --mode world-pickup --report $pickupReport `
            --install-manifest (Join-Path $temporaryCache "install-manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "OpenNV world pickup report is invalid." }
        $worldPickupValidated = $true
    }
    finally {
        foreach ($path in @($pickupReport, $pickupSave)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    function Invoke-PoolPracticeGate([bool]$UseXrLayout, [string]$Label) {
        $poolReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-pool-{0}-{1}.json" -f $Label, [guid]::NewGuid().ToString("N"))
        $poolSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-pool-save-{0}-{1}.json" -f $Label, [guid]::NewGuid().ToString("N"))
        try {
            $poolArguments = @(
                "--headless", "--xr-mode", "off", "--path", $runtimeRoot, "--",
                "--cell-scene", ([string]$install.outputs.cellScene),
                "--save-path", $poolSave,
                "--pool-proof",
                "--report", $poolReport
            )
            if ($UseXrLayout) { $poolArguments += "--vr-layout-proof" }
            $poolOutput = & $Godot @poolArguments 2>&1
            $poolText = $poolOutput | Out-String
            if ($LASTEXITCODE -ne 0 -or
                $poolText -notmatch "OPENNV_POOL_PRACTICE_PASS" -or
                $poolText -match "(?m)^ERROR:") {
                throw "OpenNV pool practice gate failed ($Label):`n$poolText"
            }
            $pool = Get-Content -Raw -LiteralPath $poolReport | ConvertFrom-Json
            $expectedAdapter = if ($UseXrLayout) { "openxr-tracked-cue-layout" } else { "desktop-look-and-power" }
            if ($pool.schema -ne "opennv-pool-practice/v1" -or
                $pool.status -ne "pass" -or
                $pool.inputAdapter -ne $expectedAdapter -or
                -not [bool]$pool.sharedSimulation -or
                -not [bool]$pool.cueMounted -or
                -not [bool]$pool.strikeAccepted -or
                [int]$pool.cueBallBallCollisions -lt 1 -or
                -not [bool]$pool.pocketDetected -or
                -not [bool]$pool.pocketSaveRestored -or
                -not [bool]$pool.liveStateRestoredFromColdSave -or
                -not [bool]$pool.authoredReset -or
                [bool]$pool.hardwareValidated) {
                throw "OpenNV pool practice report is invalid ($Label): $poolReport"
            }
        }
        finally {
            foreach ($path in @($poolReport, $poolSave)) {
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            }
        }
    }

    Invoke-PoolPracticeGate -UseXrLayout $false -Label "flat"
    Invoke-PoolPracticeGate -UseXrLayout $true -Label "xr-layout"
    $poolPracticeValidated = $true
}

function Invoke-StaticModelGate([string]$Model, [string]$Sidecar, [string]$Label) {
    $report = Join-Path ([IO.Path]::GetTempPath()) ("opennv-{0}-{1}.json" -f $Label, [guid]::NewGuid().ToString("N"))
    try {
        $output = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --model $Model --sidecar $Sidecar --report $report --quit-after-load 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output | Out-String
        if ($exitCode -ne 0 -or $text -notmatch "OPENNV_GODOT_STATIC_MODEL_PASS") {
            throw "Godot static-model gate failed ($Label):`n$text"
        }
        $document = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
        if ($document.schema -ne "opennv-godot-static-model/v1" -or
            $document.status -ne "pass" -or
            $document.renderer -ne "forward_plus" -or
            [int]$document.meshes -lt 1 -or
            [int]$document.surfaces -lt 1 -or
            [int]$document.vertices -lt 3) {
            throw "Godot static-model report is invalid ($Label): $report"
        }
        return $document
    }
    finally {
        if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    }
}

$fixture = Invoke-StaticModelGate -Model $fixtureModel -Sidecar $fixtureSidecar -Label "synthetic"
$retail = $null
if (-not [string]::IsNullOrWhiteSpace($retailModel)) {
    $retail = Invoke-StaticModelGate -Model $retailModel -Sidecar $retailSidecar -Label "retail"
}

$result = [pscustomobject][ordered]@{
    schema = "opennv-godot-runtime-gate/v1"
    status = "pass"
    cleanRuntime = $true
    openXrRig = $true
    poolFlatPractice = $poolPracticeValidated
    poolOpenXrLayout = $poolPracticeValidated
    flatControls = $flatControlsValidated
    worldPickup = $worldPickupValidated
    openXrHardwareValidated = $false
    syntheticSourceSha256 = [string]$fixture.sourceSha256
    retailSourceSha256 = if ($null -eq $retail) { "not-requested" } else { [string]$retail.sourceSha256 }
    godot = $Godot
}
    $result
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($temporaryCache) -and
        (Test-Path -LiteralPath $temporaryCache)) {
        $resolvedCache = [IO.Path]::GetFullPath($temporaryCache)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedCache.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove non-temporary cache: $resolvedCache"
        }
        Remove-Item -LiteralPath $resolvedCache -Recurse
    }
}
