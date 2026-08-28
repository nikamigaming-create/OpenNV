[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Fallout3Root,

    [string]$ProfileRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($ProfileRoot)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localData)) {
        throw "No local application-data directory is available."
    }
    $ProfileRoot = Join-Path $localData "OpenNV\profiles\fallout3\vanilla"
}
$ProfileRoot = [IO.Path]::GetFullPath($ProfileRoot)

& python (Join-Path $repoRoot "content\tools\prepare_legal_assets.py") `
    --campaign Fallout3 `
    --data-root $Fallout3Root `
    --cache-root $ProfileRoot
if ($LASTEXITCODE -ne 0) {
    throw "OpenNV Fallout 3 profile registration failed."
}

$profile = Join-Path $ProfileRoot "fallout3-profile.json"
if (-not (Test-Path -LiteralPath $profile -PathType Leaf)) {
    throw "OpenNV Fallout 3 registration produced no profile: $profile"
}
$manifest = Get-Content -Raw -LiteralPath $profile | ConvertFrom-Json
[pscustomobject][ordered]@{
    schema = $manifest.schema
    status = $manifest.status
    campaign = $manifest.campaign
    profileId = $manifest.profileId
    profile = $profile
    dataRoot = $manifest.install.dataRoot
    dlc = @($manifest.install.dlc | Where-Object available | ForEach-Object id)
    runtimeBootReady = $manifest.capabilities.runtimeBootReady
    blockers = @($manifest.blockers)
}
