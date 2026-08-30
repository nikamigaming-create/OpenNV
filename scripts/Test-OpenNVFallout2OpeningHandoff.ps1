[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v2\fo2-character-start-cache.json",
    [string]$Output = "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\opening-handoff-v1",
    [string]$Save = "$env:LOCALAPPDATA\OpenNV\saves\fallout2\opening-handoff-proof-v1.json",
    [Parameter(Mandatory)]
    [string]$ClassicHumanoidInstallManifest
)

$ErrorActionPreference = 'Stop'
$ExpectedPlaybackStartFrame = 1
$ExpectedFadeStartFrame = 1118
$ExpectedTerminalFrame = 1145
$ExpectedPresentedFrameCount = 1145
$ExpectedEvidenceCount = 5
$ExpectedHashLength = 64
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
        throw "Required Fallout 2 opening-handoff input is missing: $inputPath"
    }
}
$classicHumanoidDonorPreviewSet = & $classicHumanoidResolver -InstallManifest $ClassicHumanoidInstallManifest
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid install-manifest resolution failed.' }
& $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid donor preflight failed.' }
if (Test-Path -LiteralPath $Output) {
    throw "Refusing to overwrite Fallout 2 opening-handoff proof: $Output"
}
if (Test-Path -LiteralPath $Save) {
    throw "Fallout 2 opening-handoff proof requires a fresh save path: $Save"
}
$donorArguments = @('--classic-humanoid-donor-preview-set', $classicHumanoidDonorPreviewSet)

& $Godot `
    --path $runtime `
    --resolution 1280x720 `
    'res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn' `
    -- `
    --fo2-temple-cache $TempleCache `
    --fo2-temple-transitions $TempleTransitions `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-player-cache $PlayerCache `
    --fo2-character-start-cache $CharacterStartCache `
    --fo2-save $Save `
    --fo2-opening-handoff-proof $Output `
    @donorArguments
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 opening-handoff proof failed with exit code $LASTEXITCODE."
}

$reportPath = Join-Path $Output 'fo2-opening-handoff-proof.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Fallout 2 opening-handoff report is missing: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$cache = Get-Content -LiteralPath $CharacterStartCache -Raw | ConvertFrom-Json
if ($report.schema -ne 'opennv-fo2-opening-handoff-proof/v1' -or
    $report.status -ne 'pass-owned-elder-full-source-sequence-black-adapted-live-action' -or
    $report.source.MovieSha256 -ne $cache.openingTail.source.movie.sha256 -or
    $report.source.FadeConfigSha256 -ne $cache.openingTail.source.fadeConfig.sha256 -or
    $report.source.PlaybackStartFrame -ne $ExpectedPlaybackStartFrame -or
    $report.source.TailStartFrame -ne $ExpectedFadeStartFrame -or
    $report.source.TerminalFrame -ne $ExpectedTerminalFrame -or
    $report.source.presentedFrames.Count -ne $ExpectedPresentedFrameCount -or
    -not $report.source.exactSourceSequence -or
    -not $report.seam.authoredMovieFromFirstFrame -or
    -not $report.seam.authoredFadeSchedule -or
    -not $report.seam.authoredMovieEndBlack -or
    -not $report.seam.liveRevealAdapted -or
    $report.seam.pixelMatched -or
    -not $report.seam.exactCameraSeam -or
    -not $report.live.controlReleased -or
    -not $report.live.grounded -or
    -not $report.live.firstAction.passed -or
    $report.live.firstAction.endTile -ne $report.live.firstAction.expectedTile -or
    $report.evidence.Count -ne $ExpectedEvidenceCount -or
    ($report.evidence | Where-Object {
        $_.Bytes -le 0 -or $_.Sha256.Length -ne $ExpectedHashLength
    }).Count -ne 0) {
    throw "Fallout 2 opening-handoff evidence failed its bounded contract."
}

Write-Output (
    "OPENNV_FO2_OPENING_HANDOFF_PASS report={0} firstAction={1}:{2}->{3}" -f
    $reportPath,
    $report.live.firstAction.Action,
    $report.live.firstAction.startTile,
    $report.live.firstAction.endTile)
