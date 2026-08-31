[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\arroyo-native-render-v1"
)

$ErrorActionPreference = 'Stop'
$ExpectedFloorPatches = 4595
$ExpectedTopLevelObjects = 1842
$ExpectedWallObjects = 1145
$ExpectedWallTiles = 1112
$ExpectedWallComponents = 13
$ExpectedCaveShellComponents = 3
$ExpectedStonePostInstances = 10
$ExpectedVisibleProps = 1028
$ExpectedHiddenSourceMarkers = 24
$ExpectedFloorBoundaryEdges = 316
$ExpectedWallMaterialArtifacts = 102
$ExpectedOpaqueWallMaterialArtifacts = 101
$ExpectedFloorMaterialArtifacts = 20
$ExpectedSourceTorchProps = 22
$ExpectedSourceMapLights = 33
$ExpectedSha256Characters = 64
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$inputs = @($Godot, $TempleCache, $TempleTransitions, $ArroyoCache)
foreach ($inputPath in $inputs) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 render-proof input is missing: $inputPath"
    }
}
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 render proof: $Output"
}

& $Godot `
    --path $runtime `
    --windowed `
    --resolution 1280x720 `
    'res://src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesRenderProof.tscn' `
    -- `
    --fo2-temple-cache $TempleCache `
    --fo2-temple-transitions $TempleTransitions `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-arroyo-render-proof $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 Arroyo Caves native render proof failed with exit code $LASTEXITCODE."
}

$reportPath = Join-Path $Output 'arroyo-caves-native-render-proof.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Fallout 2 Arroyo Caves native render report is missing: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.schema -ne 'opennv-fo2-arroyo-caves-native-render-proof/v3' -or
    $report.status -ne 'pass-source-bound-molded-3d-construction-frame-presentation-unaccepted' -or
    $report.arrival.mapIndex -ne 3 -or
    $report.arrival.elevation -ne 0 -or
    $report.arrival.tile -ne 28707 -or
    $report.arrival.rotation -ne 0 -or
    -not $report.promotion.constructionFrameRendered -or
    $report.promotion.presentationAccepted -or
    $report.promotion.interactive -or
    $report.promotion.playerSpawned -or
    $report.promotion.launcherPlayable -or
    $report.promotion.pairReady -or
    $report.promotion.fo1QualityParity -or
    $report.source.exactElevationZeroCoverage.nonDefaultFloorPatches -ne $ExpectedFloorPatches -or
    $report.source.exactElevationZeroCoverage.floorBoundaryEdges -ne $ExpectedFloorBoundaryEdges -or
    $report.source.exactElevationZeroCoverage.topLevelObjects -ne $ExpectedTopLevelObjects -or
    $report.source.exactElevationZeroCoverage.wallObjects -ne $ExpectedWallObjects -or
    $report.source.exactElevationZeroCoverage.uniqueWallTiles -ne $ExpectedWallTiles -or
    $report.source.exactElevationZeroCoverage.wallComponents -ne $ExpectedWallComponents -or
    $report.construction.moldedFloorPatches -ne $ExpectedFloorPatches -or
    $report.construction.sourceWallComponents -ne $ExpectedWallComponents -or
    $report.construction.fusedCaveShellComponents -ne $ExpectedCaveShellComponents -or
    $report.construction.fusedWallMeshInstances -ne $ExpectedCaveShellComponents -or
    $report.construction.sourceFrmStonePostInstances -ne $ExpectedStonePostInstances -or
    $report.construction.hiddenWallSpriteCards -ne $ExpectedWallObjects -or
    $report.construction.hiddenSourceMarkerCards -ne $ExpectedHiddenSourceMarkers -or
    $report.construction.visibleSourceProps -ne $ExpectedVisibleProps -or
    $report.construction.groundedSourceProps -ne $ExpectedVisibleProps -or
    $report.construction.visibleSourceTorchProps -ne $ExpectedSourceTorchProps -or
    $report.construction.sourceMapLightRecords -ne $ExpectedSourceMapLights -or
    $report.construction.sourceMapLights -ne $ExpectedSourceMapLights -or
    $report.construction.sourceTorchMotivatedMapLights -ne $ExpectedSourceTorchProps -or
    -not $report.construction.ceilingClosure -or
    -not $report.construction.sourceWalkMaskUnchanged -or
    $report.presentation.generatedAssetLane.used -or
    $report.presentation.generatedAssetLane.ownedOrGeneratedMeshesPackaged -or
    $report.presentation.ownedFrmSurfaces.sourceWallArtifacts -ne $ExpectedWallMaterialArtifacts -or
    $report.presentation.ownedFrmSurfaces.opaqueSourceWallArtifacts -ne $ExpectedOpaqueWallMaterialArtifacts -or
    $report.presentation.ownedFrmSurfaces.sourceFloorArtifacts -ne $ExpectedFloorMaterialArtifacts -or
    $report.presentation.ownedFrmSurfaces.normalTextureSha256.Length -ne
        $ExpectedSha256Characters -or
    $report.presentation.ownedFrmSurfaces.floorNormalTextureSha256.Length -ne
        $ExpectedSha256Characters -or
    $report.presentation.ownedFrmSurfaces.distributionAllowed -or
    -not $report.frame.frameIntegrityGatePassed -or
    $report.frame.presentationVisualGatePassed -or
    -not $report.frame.presentationVisualBlockers -or
    $report.cinematicHandoff.reviewed) {
    throw "Fallout 2 Arroyo Caves native render report failed its honest promotion contract."
}
Write-Output (
    "OPENNV_FO2_ARROYO_RENDER_PROOF_PASS map={0} elevation={1} arrival={2} floors={3} objects={4} wallShells={5} props={6} pairReady={7} frame={8}" -f
    $report.arrival.mapIndex,
    $report.arrival.elevation,
    $report.arrival.tile,
    $report.construction.floorPatches,
    $report.construction.topLevelObjects,
    $report.construction.fusedWallMeshInstances,
    $report.construction.groundedSourceProps,
    $report.promotion.pairReady,
    $report.frame.sha256)
