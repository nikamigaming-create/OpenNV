[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Fallout2Root,

    [string]$ProfilePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localData)) {
        throw "No local application-data directory is available."
    }
    $ProfilePath = Join-Path $localData "OpenNV\profiles\fallout2\fallout2-profile.json"
}
$ProfilePath = [IO.Path]::GetFullPath($ProfilePath)

& python (Join-Path $repoRoot "content\tools\fo2_profile.py") `
    --install-root ([IO.Path]::GetFullPath($Fallout2Root)) `
    --output $ProfilePath
if ($LASTEXITCODE -ne 0) {
    throw "OpenNV Fallout 2 profile registration failed."
}

$manifest = Get-Content -Raw -LiteralPath $ProfilePath | ConvertFrom-Json
[pscustomobject][ordered]@{
    schema = $manifest.schema
    status = $manifest.status
    campaign = $manifest.campaign
    sourceProfileId = $manifest.sourceProfileId
    profile = $ProfilePath
    archives = @($manifest.install.archives | ForEach-Object file)
    members = ($manifest.install.archives.formatIdentity.entries | Measure-Object -Sum).Sum
    runtimeReady = $manifest.runtimeCompatibility.ready
    firstSliceBlocker = $manifest.runtimeCompatibility.firstSliceBlocker
}
