[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Godot,
    [string]$FalloutNewVegasData = "",
    [string]$ExpectedMeshesBsaSha256 = "",
    [string]$RetailLogicalPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$contentRoot = Join-Path $repoRoot "content"
$solution = Join-Path $runtimeRoot "OpenNV.sln"
$exporter = Join-Path $contentRoot "tools\export_static_nif_gltf.py"
$preparer = Join-Path $contentRoot "tools\prepare_legal_assets.py"
$reportValidator = Join-Path $contentRoot "tools\validate_runtime_report.py"
$runtimeConfigurationPath = Join-Path $runtimeRoot "config\open-nv-runtime-v1.json"
$RuntimeConfigurationJsonDepth = 100
$runtimeConfiguration = Get-Content -Raw -LiteralPath $runtimeConfigurationPath |
    ConvertFrom-Json -Depth $RuntimeConfigurationJsonDepth
$ownedData = $runtimeConfiguration.legalAssets.ownedData
$cellRecipe = [string]$runtimeConfiguration.legalAssets.defaultCellRecipe
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

foreach ($path in @($Godot, $solution, $exporter, $preparer, $reportValidator, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing OpenNV Godot gate input: $path"
    }
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
    $fo1Report = Join-Path ([IO.Path]::GetTempPath()) ("opennv-fo1-tactical-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $fo1Save = Join-Path ([IO.Path]::GetTempPath()) ("opennv-fo1-tactical-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $fo1Output = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --fo1-hex-scene $fo1Scene --fo1-tactical-proof --save-path $fo1Save --report $fo1Report 2>&1
        $fo1Text = $fo1Output | Out-String
        if ($LASTEXITCODE -ne 0 -or $fo1Text -notmatch "OPENNV_FO1_TACTICAL_PROOF_PASS" -or
            $fo1Text -match "(?m)^ERROR:") {
            throw "Fallout 1 tactical proof failed:`n$fo1Text"
        }
        $fo1 = Get-Content -Raw -LiteralPath $fo1Report | ConvertFrom-Json
        if ($fo1.schema -ne "opennv-fo1-tactical-proof/v1" -or
            $fo1.status -ne "pass" -or
            [int]$fo1.grid.width -ne 200 -or
            [int]$fo1.grid.height -ne 200 -or
            [double]$fo1.grid.flatToFlatMeters -ne 1.0 -or
            $fo1.grid.layout -ne "fallout-even-column-offset-flat-v1" -or
            [int]$fo1.entryTile -ne 17690 -or
            [int]$fo1.movedToTile -ne 17891 -or
            [int]$fo1.moveDistanceMeters -ne 1 -or
            [int]$fo1.movementCostAp -ne 1 -or
            [int]$fo1.turnAfterEnd -lt 2 -or
            [int]$fo1.actionPointsAfterEnd -ne 10 -or
            $fo1.combat.targetPid -ne "01000030" -or
            [int]$fo1.combat.targetSourceHitPoints -ne 6 -or
            [int]$fo1.combat.targetSourceArmorClass -ne 4 -or
            [int]$fo1.combat.targetSourceMeleeDamage -ne 3 -or
            [int]$fo1.combat.targetSourceSequence -ne 12 -or
            [int]$fo1.combat.targetSourceTeam -ne 1 -or
            [int]$fo1.combat.targetSourceAiPacket -ne 12 -or
            [int]$fo1.combat.playerWeaponApCost -ne 5 -or
            [int]$fo1.combat.playerMeleeApCost -ne 3 -or
            [int]$fo1.combat.attacks -lt 2 -or
            [int]$fo1.combat.rangedAttempts -lt 3 -or
            [int]$fo1.combat.rangedHits -lt 2 -or
            [int]$fo1.combat.meleeAttempts -lt 2 -or
            [int]$fo1.combat.meleeHits -lt 2 -or
            [int]$fo1.combat.reloads -ne 1 -or
            [int]$fo1.combat.magazineRounds -ne 12 -or
            [int]$fo1.combat.reserveRounds -lt 1 -or
            [int]$fo1.combat.kills -ne 3 -or
            $fo1.combat.equippedWeaponSymbol -ne "PID_KNIFE" -or
            -not [bool]$fo1.combat.weaponSwapRoundTrip -or
            [int]$fo1.combat.hostileMarkers -ne 20 -or
            [int]$fo1.combat.hostileHealthLabels -ne 20 -or
            -not [bool]$fo1.combat.targetCycleAndFrame -or
            -not [bool]$fo1.combat.corpseVisible -or
            [double]$fo1.combat.corpseGroundErrorMeters -gt 0.0001 -or
            [int]$fo1.combat.localActivationDistanceHexes -ne 6 -or
            -not [bool]$fo1.combat.wholeCaveAggroPrevented -or
            -not [bool]$fo1.combat.hostileMarkerDepthTested -or
            [int]$fo1.session.livingMobs -ne 17 -or
            [int]$fo1.sourceSpriteAnchoring.sprites -ne 1494 -or
            [int]$fo1.sourceSpriteAnchoring.actorSprites -ne 21 -or
            $fo1.sourceSpriteAnchoring.actorBillboard -ne "fixed-y" -or
            [int]$fo1.sourceSpriteAnchoring.staticWorldSprites -ne 1473 -or
            $fo1.sourceSpriteAnchoring.staticBillboard -ne "disabled-world-locked" -or
            [double]$fo1.sourceSpriteAnchoring.staticWorldYawDegrees -ne -150.0 -or
            [double]$fo1.sourceSpriteAnchoring.maximumAnchorError -gt 0.0001 -or
            [bool]$fo1.sourceSpriteAnchoring.sourceStaticOverlayVisible -or
            -not [bool]$fo1.ownedCreature3d.enabled -or
            -not [bool]$fo1.ownedCreature3d.sourceRatSpritesHidden -or
            [int]$fo1.ownedCreature3d.instances -ne 20 -or
            [int]$fo1.ownedCreature3d.meshesPerInstance -ne 6 -or
            [int]$fo1.ownedCreature3d.skeletons -ne 20 -or
            [int]$fo1.ownedCreature3d.animationPlayers -ne 20 -or
            [int]$fo1.ownedCreature3d.importedAnimations -ne 5 -or
            [int]$fo1.ownedCreature3d.hiddenIntactStateGoreMeshes -ne 80 -or
            -not [bool]$fo1.ownedPlayer3d.enabled -or
            -not [bool]$fo1.ownedPlayer3d.sourceSpriteHidden -or
            $fo1.ownedPlayer3d.formId -ne "00104f09" -or
            [int]$fo1.ownedPlayer3d.meshes -ne 15 -or
            [int]$fo1.ownedPlayer3d.skeletons -ne 1 -or
            [int]$fo1.ownedPlayer3d.animationPlayers -ne 1 -or
            [int]$fo1.ownedPlayer3d.importedAnimations -ne 5 -or
            $fo1.ownedPlayer3d.thirdPersonWeapon.formId -ne "0000434f" -or
            $fo1.ownedPlayer3d.thirdPersonMeleeWeapon.formId -ne "00004326" -or
            $fo1.ownedPlayer3d.thirdPersonMeleeWeapon.gameplayPid -ne "00000004" -or
            [double]$fo1.ownedPlayer3d.heightMeters -lt 1.8 -or
            [int]$fo1.session.playerPresentation.moveAnimationPlaybacks -lt 1 -or
            [int]$fo1.cave3d.boundaryEdges -lt 1 -or
            [int]$fo1.cave3d.obstacles -ne 1048 -or
            [int]$fo1.cave3d.triangles -lt 1 -or
            -not [bool]$fo1.cave3d.fixedWorldGeometry -or
            -not [bool]$fo1.cave3d.defaultVisible -or
            [int]$fo1.cave3d.cutawayCandidates -lt 1 -or
            [int]$fo1.cave3d.combatCutawayOccluders -lt 1 -or
            [int]$fo1.cave3d.meltShaderMaterials -lt [int]$fo1.cave3d.cutawayCandidates -or
            -not [bool]$fo1.cave3d.shaderDrivenCameraMelt -or
            -not [bool]$fo1.cave3d.owned.enabled -or
            [int]$fo1.cave3d.owned.instances -ne 170 -or
            [int]$fo1.cave3d.owned.meshInstances -ne 474 -or
            [int]$fo1.cave3d.owned.surfaceInstances -ne 2142 -or
            [int]$fo1.cave3d.owned.materialBindings -ne 201 -or
            [int]$fo1.cave3d.owned.roles.'terrain-envelope' -ne 1 -or
            [int]$fo1.cave3d.owned.roles.'wall-ribbon' -ne 52 -or
            [int]$fo1.cave3d.owned.roles.'vault-portal' -ne 1 -or
            [int]$fo1.cave3d.owned.roles.'large-rock' -ne 54 -or
            [int]$fo1.cave3d.owned.roles.'small-rock' -ne 53 -or
            [int]$fo1.cave3d.owned.roles.stalagmite -ne 7 -or
            [int]$fo1.cave3d.owned.roles.'vault-frame' -ne 1 -or
            [int]$fo1.cave3d.owned.roles.'entrance-corpse' -ne 1 -or
            -not [bool]$fo1.cave3d.owned.continuousFloorVisible -or
            [int]$fo1.cave3d.owned.continuousFloorHexes -ne 30196 -or
            [int]$fo1.cave3d.owned.continuousFloorTriangles -ne 181176 -or
            [int]$fo1.cave3d.owned.continuousFloorMeshInstances -ne 1 -or
            [bool]$fo1.optionalHexOverlay.defaultVisible -or
            -not [bool]$fo1.optionalHexOverlay.togglePassed -or
            -not [bool]$fo1.optionalHexOverlay.depthTested -or
            -not [bool]$fo1.optionalHexOverlay.opaque -or
            [int]$fo1.optionalHexOverlay.hexes -ne 27664 -or
            [int]$fo1.optionalHexOverlay.uniqueEdges -ne 87903 -or
            [int]$fo1.optionalHexOverlay.presentationFootprintBlockedHexes -ne 1463 -or
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
            [double]$fo1.combatPresentation.impactRadiusMeters -gt 0.015 -or
            [int]$fo1.combatPresentation.audioEvents -lt 10 -or
            @($fo1.combatPresentation.audioRoles).Count -ne 10 -or
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
        [double]$diorama.initialSizeMeters -ne 18.0 -or
        [double]$diorama.minimumSizeMeters -ne 6.0 -or
        [double]$diorama.maximumSizeMeters -ne 64.0 -or
        [double]$diorama.zoomedSizeMeters -ge 18.0 -or
        [math]::Abs([double]$diorama.yawStepDegrees - 60.0) -gt 0.001 -or
        @($diorama.panKeys).Count -ne 4 -or
        @($diorama.panKeys) -notcontains "W" -or
        @($diorama.rotationKeys) -notcontains "Q" -or
        @($diorama.rotationKeys) -notcontains "E" -or
        $diorama.zoomInput -ne "mouse-wheel" -or
        $diorama.resetKey -ne "Home" -or
        $diorama.gameplaySession.schema -ne "opennv-sandbox-save/v1" -or
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
        "--cell-recipe", $cellRecipe
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
