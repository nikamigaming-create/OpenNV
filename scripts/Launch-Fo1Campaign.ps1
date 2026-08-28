[CmdletBinding()]
param(
    [string]$Godot = "D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
    [string]$CampaignPresentation = "",
    [string]$ExpectedPresentationSha256 = "61965ae5fe3971618904d63719c190178595110a2b750de30572e2e90234eca7",
    [string]$Map = "",
    [Nullable[int]]$Elevation = $null,
    [switch]$Smoke
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
if ([string]::IsNullOrWhiteSpace($CampaignPresentation)) {
    $CampaignPresentation = Join-Path $repoRoot `
        "dist\fo1-campaign-presentation-20260826-r5\campaign-presentation.json"
}
$CampaignPresentation = [IO.Path]::GetFullPath($CampaignPresentation)
foreach ($path in @($Godot, $CampaignPresentation, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Fallout campaign viewer input: $path"
    }
}
$actualSha256 = (Get-FileHash -LiteralPath $CampaignPresentation -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $ExpectedPresentationSha256.ToLowerInvariant()) {
    throw "Fallout campaign presentation hash drift: $actualSha256"
}
$contract = Get-Content -Raw -LiteralPath $CampaignPresentation | ConvertFrom-Json
if ($contract.schema -ne "opennv-fo1-campaign-presentation/v1" -or
    $contract.status -ne "prepared-source-reference-not-rendered" -or
    [bool]$contract.retailOrDerivedAssetsPackaged) {
    throw "Unexpected Fallout campaign presentation contract."
}
if ([string]::IsNullOrWhiteSpace($Map)) {
    $Map = [string]$contract.viewer.defaultMapId
}
$selected = @($contract.maps | Where-Object id -EQ $Map)
if ($selected.Count -ne 1) {
    throw "Fallout campaign map is absent or duplicated: $Map"
}
if ($null -ne $Elevation -and ($Elevation -lt 0 -or $Elevation -gt 2)) {
    throw "Fallout campaign elevation must be 0, 1, or 2."
}

$engineArguments = if ($Smoke) {
    @("--headless", "--xr-mode", "off")
}
else {
    @("--xr-mode", "off")
}
$runtimeArguments = @(
    "--fo1-campaign-presentation", $CampaignPresentation,
    "--fo1-map", $Map
)
if ($null -ne $Elevation) {
    $runtimeArguments += @("--fo1-elevation", [string]$Elevation)
}
if ($Smoke) {
    $runtimeArguments += "--quit-after-load"
}
& $Godot @engineArguments --path $runtimeRoot -- @runtimeArguments
if ($LASTEXITCODE -ne 0) {
    throw "Fallout campaign viewer exited with code $LASTEXITCODE."
}
