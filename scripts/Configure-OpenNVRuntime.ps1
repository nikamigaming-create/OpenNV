[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$FalloutNewVegasData,

    [string]$CacheRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localData)) { throw "No local application-data directory is available." }
    $CacheRoot = Join-Path $localData "OpenNV\cache\static-nif-v1"
}
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot)

& python (Join-Path $repoRoot "content\tools\prepare_legal_assets.py") `
    --data-root $FalloutNewVegasData `
    --cache-root $CacheRoot
if ($LASTEXITCODE -ne 0) { throw "OpenNV legal-asset preparation failed." }

$manifest = Join-Path $CacheRoot "install-manifest.json"
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "OpenNV cache preparation produced no install manifest: $manifest"
}
[pscustomobject][ordered]@{
    schema = "opennv-runtime-configuration/v1"
    status = "prepared-experimental-slice"
    cacheRoot = $CacheRoot
    manifest = $manifest
}
