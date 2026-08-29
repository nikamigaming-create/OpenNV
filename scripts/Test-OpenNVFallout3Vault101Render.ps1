[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Godot,

    [Parameter(Mandatory)]
    [string]$Python,

    [string]$Profile = "",

    [string]$CacheRoot = "",

    [string]$CaptureRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$preparer = Join-Path $repoRoot "content\tools\prepare_fo3_birth_presentation.py"
$proofScene = "res://src/Campaigns/Fallout3/Fo3Vault101BirthProof.tscn"
if ([string]::IsNullOrWhiteSpace($Profile)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $Profile = Join-Path $localData "OpenNV\profiles\fallout3\vanilla\fallout3-profile.json"
}
$runId = [Guid]::NewGuid().ToString("N")
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $env:TEMP "opennv-fo3-vault101-cache-$runId"
}
if ([string]::IsNullOrWhiteSpace($CaptureRoot)) {
    $CaptureRoot = Join-Path $env:TEMP "opennv-fo3-vault101-capture-$runId"
}

foreach ($path in @($Godot, $Python, $Profile, $preparer, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Fallout 3 Vault 101 render-test input: $path"
    }
}
foreach ($path in @($CacheRoot, $CaptureRoot)) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite Fallout 3 Vault 101 render-test output: $path"
    }
}

$prepareOutput = & $Python $preparer --profile ([IO.Path]::GetFullPath($Profile)) `
    --cache-root ([IO.Path]::GetFullPath($CacheRoot)) 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 3 Vault 101 presentation preparation failed:`n$($prepareOutput | Out-String)"
}
$prepareText = $prepareOutput | Out-String
$receiptLine = @(
    $prepareText -split "`r?`n" |
        Where-Object { $_.TrimStart().StartsWith("{") }
)[-1]
$receipt = $receiptLine | ConvertFrom-Json
if ($receipt.schema -ne "opennv-fo3-vault101-birth-presentation/v3" -or
    -not (Test-Path -LiteralPath $receipt.output -PathType Leaf)) {
    throw "Fallout 3 Vault 101 preparation receipt is invalid."
}

$renderOutput = & $Godot --xr-mode off --path $runtimeRoot --windowed `
    --resolution 1280x720 --position 10000,10000 $proofScene -- `
    --fo3-profile ([IO.Path]::GetFullPath($Profile)) `
    --fo3-birth-presentation ([IO.Path]::GetFullPath($receipt.output)) `
    --fo3-birth-capture ([IO.Path]::GetFullPath($CaptureRoot)) 2>&1
$renderText = $renderOutput | Out-String
$expected =
    "OPENNV_FO3_VAULT101_RENDER_PASS cell=00028138 entry=00039562 " +
    "references=29 models=23 surfaces=148 textures=51 materials=118 " +
    "actors=1 actorSurfaces=18 interactive=0"
if ($LASTEXITCODE -ne 0 -or $renderText -notmatch [regex]::Escape($expected)) {
    throw "Fallout 3 Vault 101 native render proof failed:`n$renderText"
}

$reportPath = Join-Path $CaptureRoot "vault101-birth-native-render-proof.json"
$framePath = Join-Path $CaptureRoot "vault101-birth-entry.png"
$actorFramePath = Join-Path $CaptureRoot "doctor-li-owned-actor.png"
$dialogueFramePath = Join-Path $CaptureRoot "stage65-owned-dad-cue.png"
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json -Depth 100
if ($report.schema -ne "opennv-fo3-vault101-birth-native-render-proof/v5" -or
    $report.status -ne "pass-rendered-owned-birth-room-doctor-li-and-explicit-dad-dialogue-cue" -or
    -not $report.promotion.rendered -or
    -not $report.promotion.texturesBound -or
    $report.promotion.interactive -or
    -not $report.promotion.actorsRendered -or
    -not $report.promotion.doctorLiRendered -or
    $report.promotion.questCommandsExecuted -or
    $report.cell.loadedStaticReferences -ne 29 -or
    $report.cell.loadedUniqueModels -ne 23 -or
    $report.materials.resolvedUniqueTextures -ne 51 -or
    $report.materials.materialBindings -ne 118 -or
    $report.doctorActor.referenceFormId -ne "000290a5" -or
    $report.doctorActor.baseFormId -ne "000290a3" -or
    $report.doctorActor.authoredComponents -ne 12 -or
    $report.doctorActor.authoredSkins -ne 10 -or
    $report.doctorActor.authoredSurfaces -ne 18 -or
    $report.doctorActor.runtimeSurfaces -ne 18 -or
    $report.doctorActor.grounding.supportReferenceFormId -ne "0005ed06" -or
    $report.doctorActor.grounding.supportBaseEditorId -ne "UtlRmWall03" -or
    $report.doctorActor.grounding.verticalCorrectionGodotGameUnits -ge 0 -or
    [Math]::Abs(
        $report.doctorActor.grounding.groundedFootMinimumGodotMeters -
        $report.doctorActor.grounding.supportGodotMeters) -gt 0.0002 -or
    -not $report.doctorActor.grounding.preservedAuthoredHorizontalTransform -or
    $report.doctorActor.proofLitMaterials -le 0 -or
    -not $report.doctorActor.frustumIntersection -or
    -not $report.doctorActorFrame.visualGatePassed -or
    $report.doctorActorFrame.runtimeSurfaces -ne 18 -or
    -not (Test-Path -LiteralPath $framePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $actorFramePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $dialogueFramePath -PathType Leaf) -or
    $report.boundedDialogueCue.sourceStage -ne 65 -or
    $report.boundedDialogueCue.targetStage -ne 80 -or
    $report.boundedDialogueCue.infoFormId -ne "0001f380" -or
    -not $report.boundedDialogueCue.explicitAdvanceRequired -or
    -not $report.boundedDialogueCue.audioPlaybackStarted -or
    -not $report.boundedDialogueCue.subtitleRendered -or
    $report.boundedDialogueCue.lipPlayback -or
    $report.boundedDialogueCue.dadRendered -or
    $report.boundedDialogueCue.retailTimingApplied -or
    $report.boundedDialogueCue.stage80Applied -or
    -not $report.promotion.sourceBoundDialogueCue -or
    $report.characterSelectionHandoff.sourceStage -ne 62 -or
    $report.characterSelectionHandoff.packageFormId -ne "0006a818" -or
    $report.characterSelectionHandoff.packageLocationReferenceFormId -ne "00039562" -or
    $report.characterSelectionHandoff.entryReferenceFormId -ne "00039562" -or
    $report.proofCamera.authority -ne "owned-CG00-support-mesh-top-derived-proof-only-not-retail-camera" -or
    $report.proofCamera.supportReferenceFormId -ne "00060c92" -or
    $report.proofCamera.supportBaseEditorId -ne "CG00Gurney" -or
    $report.proofCamera.supportSurfaceGodotGameUnits -le 0 -or
    $report.proofCamera.surfaceClearanceGameUnits -le $report.proofCamera.nearGameUnits -or
    $report.proofCamera.positionGodotGameUnits[1] -le $report.proofCamera.supportSurfaceGodotGameUnits -or
    -not $report.characterSelectionHandoff.boundedPresentationOnly -or
    $report.characterSelectionHandoff.packageExecuted -or
    $report.characterSelectionHandoff.playerIdleExecuted -or
    $report.characterSelectionHandoff.dialoguePlayback -or
    $report.characterSelectionHandoff.retailTimingApplied -or
    -not $report.promotion.characterSelectionJoinedToScene) {
    throw "Fallout 3 Vault 101 render report promotion boundary is invalid."
}

$renderText
"OPENNV_FO3_VAULT101_GATE_PASS cache=$CacheRoot capture=$CaptureRoot"
