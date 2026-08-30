[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v1\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\character-start-v1",
    [Parameter(Mandatory)]
    [string]$ClassicHumanoidInstallManifest
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$classicHumanoidPreflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
$classicHumanoidResolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
foreach ($inputPath in @($Godot, $TempleCache, $TempleTransitions, $ArroyoCache, $PlayerCache, $CharacterStartCache)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 character-start input is missing: $inputPath"
    }
}
$classicHumanoidDonorPreviewSet = & $classicHumanoidResolver -InstallManifest $ClassicHumanoidInstallManifest
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid install-manifest resolution failed.' }
& $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid donor preflight failed.' }
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 character-start proof: $Output"
}

& $Godot `
    --path $runtime `
    --windowed `
    --resolution 1280x720 `
    'res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn' `
    -- `
    --fo2-temple-cache $TempleCache `
    --fo2-temple-transitions $TempleTransitions `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-player-cache $PlayerCache `
    --fo2-character-start-cache $CharacterStartCache `
    --classic-humanoid-donor-preview-set $classicHumanoidDonorPreviewSet `
    --fo2-character-start-proof $Output
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 character-start proof failed with exit code $LASTEXITCODE."
}

$reportPath = Join-Path $Output 'fo2-character-start-proof.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Fallout 2 character-start report is missing: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.schema -ne 'opennv-fo2-character-start-runtime-proof/v1' -or
    $report.status -ne 'pass-owned-premade-selection-to-arroyo-arrival-no-save' -or
    $report.roster.Count -ne 3 -or
    $report.selected.Name -ne 'Chitsa' -or
    $report.selected.Sex -ne 'Female' -or
    $report.selected.Fid -ne '0100003d' -or
    $report.selected.LogicalPath -ne 'art\critters\hfprimaa.frm' -or
    $report.selected.sourceDirections -ne 6 -or
    $report.handoff.mapIndex -ne 3 -or
    $report.handoff.elevation -ne 0 -or
    $report.handoff.arrivalTile -ne 28707 -or
    -not $report.handoff.grounded -or
    -not $report.handoff.visibleCharacter -or
    -not $report.promotion.ownedPremadeRosterSelectable -or
    -not $report.promotion.selectedStateAppliedToPlayer -or
    -not $report.promotion.immediateArroyoHandoff -or
    -not $report.promotion.humanKeyboardAndMouseEntryAvailable -or
    $report.promotion.modifyRoute -or
    $report.promotion.customCharacterRoute -or
    $report.promotion.playerStatePersistent -or
    $report.promotion.interactive -or
    $report.promotion.playableCampaign -or
    $report.promotion.launcherPlayable) {
    throw "Fallout 2 character-start report failed its bounded promotion contract."
}
Write-Output (
    "OPENNV_FO2_CHARACTER_START_PROOF_PASS name={0} sex={1} fid={2} tile={3}" -f
    $report.selected.Name,
    $report.selected.Sex,
    $report.selected.Fid,
    $report.handoff.arrivalTile)
