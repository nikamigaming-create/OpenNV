[CmdletBinding()]
param(
    [string]$Godot = "D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
    [string]$FalloutNewVegasData = "",
    [string]$ExpectedMeshesBsaSha256 = "",
    [string]$RetailLogicalPath = "meshes\landscape\nv_rocks\nvn_rockcanyon12.nif",
    [string]$Fo1HexScene = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$contentRoot = Join-Path $repoRoot "content"
$solution = Join-Path $runtimeRoot "OpenNV.sln"
$exporter = Join-Path $contentRoot "tools\export_static_nif_gltf.py"
$preparer = Join-Path $contentRoot "tools\prepare_legal_assets.py"
$fixtureModel = "res://tests/fixtures/opaque-triangle.gltf"
$fixtureSidecar = "res://tests/fixtures/opaque-triangle.opennv.json"

foreach ($path in @($Godot, $solution, $exporter, $preparer, (Join-Path $runtimeRoot "project.godot"))) {
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
    $xr = Get-Content -Raw -LiteralPath $xrReport | ConvertFrom-Json
    if ($xr.schema -ne "opennv-openxr-rig/v2" -or
        $xr.status -ne "pass" -or
        [bool]$xr.viewportXrEnabledDuringProof -or
        [int]$xr.actionSets -ne 1 -or
        [int]$xr.actions -ne 8 -or
        @($xr.actionNames).Count -ne 8 -or
        @($xr.actionNames) -notcontains "reload" -or
        @($xr.testedInteractionProfiles).Count -ne 2 -or
        @($xr.testedInteractionProfiles) -notcontains "/interaction_profiles/khr/generic_controller" -or
        @($xr.testedInteractionProfiles) -notcontains "/interaction_profiles/oculus/touch_controller" -or
        $xr.originType -ne "XROrigin3D" -or
        $xr.cameraType -ne "XRCamera3D" -or
        $xr.controllerRenderModelManagerType -ne "OpenXRRenderModelManager" -or
        $xr.leftTracker -ne "left_hand" -or
        $xr.rightTracker -ne "right_hand" -or
        [double]$xr.worldScale -ne 1.0 -or
        [double]$xr.desiredEyeHeightMeters -ne 1.68 -or
        [int]$xr.physicsTicksPerSecond -ne 90 -or
        -not [bool]$xr.worldSpaceHud -or
        $xr.sharedSaveSchema.schema -ne "opennv-sandbox-save/v1" -or
        $xr.sharedSaveSchema.equippedWeaponFormId -ne "0000434f" -or
        $xr.sharedSaveSchema.weaponAmmoFormId -ne "00004241" -or
        [int]$xr.sharedSaveSchema.weaponDamage -ne 22 -or
        [int]$xr.sharedSaveSchema.weaponClipSize -ne 12 -or
        [int]$xr.sharedSaveSchema.ammoInMagazine -ne 12 -or
        [int]$xr.sharedSaveSchema.reserveAmmo -ne 11 -or
        [int]$xr.sharedSaveSchema.shotsFired -ne 1) {
        throw "OpenNV OpenXR rig report is invalid."
    }
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
            $fo1.grid.layout -ne "odd-row-offset-pointy" -or
            [int]$fo1.entryTile -ne 17690 -or
            [int]$fo1.moveDistanceMeters -ne 1 -or
            [int]$fo1.movementCostAp -ne 1 -or
            [int]$fo1.turnAfterEnd -ne 2 -or
            [int]$fo1.actionPointsAfterEnd -ne 10 -or
            $fo1.combat.targetPid -ne "01000030" -or
            [int]$fo1.combat.targetSourceHitPoints -ne 6 -or
            [int]$fo1.combat.targetSourceArmorClass -ne 4 -or
            [int]$fo1.combat.targetSourceMeleeDamage -ne 3 -or
            [int]$fo1.combat.targetSourceSequence -ne 12 -or
            [int]$fo1.combat.targetSourceTeam -ne 1 -or
            [int]$fo1.combat.targetSourceAiPacket -ne 12 -or
            [int]$fo1.combat.playerWeaponApCost -ne 5 -or
            [int]$fo1.combat.attacks -ne 1 -or
            [int]$fo1.combat.hostileMarkers -ne 20 -or
            [int]$fo1.combat.hostileHealthLabels -ne 20 -or
            -not [bool]$fo1.combat.targetCycleAndFrame -or
            -not [bool]$fo1.combat.screenTargetReticle -or
            [int]$fo1.session.livingMobs -ne 19 -or
            [int]$fo1.sourceSpriteAnchoring.sprites -ne 1494 -or
            [int]$fo1.sourceSpriteAnchoring.actorSprites -ne 21 -or
            $fo1.sourceSpriteAnchoring.actorBillboard -ne "fixed-y" -or
            [int]$fo1.sourceSpriteAnchoring.staticWorldSprites -ne 1473 -or
            $fo1.sourceSpriteAnchoring.staticBillboard -ne "disabled-world-locked" -or
            [double]$fo1.sourceSpriteAnchoring.staticWorldYawDegrees -ne -45.0 -or
            [double]$fo1.sourceSpriteAnchoring.maximumAnchorError -gt 0.0001 -or
            -not [bool]$fo1.sourceSpriteAnchoring.sourceStaticOverlayVisible -or
            [int]$fo1.cave3d.boundaryEdges -lt 1 -or
            [int]$fo1.cave3d.obstacles -ne 1048 -or
            [int]$fo1.cave3d.triangles -lt 1 -or
            -not [bool]$fo1.cave3d.fixedWorldGeometry -or
            [bool]$fo1.cave3d.defaultVisible -or
            -not [bool]$fo1.camera.middleMouseOrbit -or
            -not [bool]$fo1.camera.rightMousePan -or
            -not [bool]$fo1.camera.wheelZoomTowardCursor -or
            [bool]$fo1.windowsAppControlUsed -or
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
if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
    $temporaryCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-legal-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $prepareArguments = @(
        $preparer,
        "--data-root", $FalloutNewVegasData,
        "--cache-root", $temporaryCache,
        "--logical-model", $RetailLogicalPath
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
    classicDioramaRig = $true
    fo1TacticalHex = $fo1TacticalPassed
    openXrHardwareValidated = $false
    syntheticSourceSha256 = [string]$fixture.sourceSha256
    retailSourceSha256 = if ($null -eq $retail) { "not-requested" } else { [string]$retail.sourceSha256 }
    godot = $Godot
}
if (-not [string]::IsNullOrWhiteSpace($temporaryCache)) {
    $resolvedCache = [IO.Path]::GetFullPath($temporaryCache)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedCache.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove non-temporary cache: $resolvedCache"
    }
    Remove-Item -LiteralPath $resolvedCache -Recurse
}
$result
