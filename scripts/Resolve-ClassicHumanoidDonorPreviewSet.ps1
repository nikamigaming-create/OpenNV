[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallManifest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$InstallSchema = 'opennv-legal-asset-cache/v1'
$InstallStatus = 'prepared-legal-assets'
$Sha256Pattern = '^[0-9a-fA-F]{64}$'

function Require-String([object]$Value, [string]$Label) {
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Classic humanoid install-manifest field is empty: $Label"
    }
    return $text
}

function Require-Hash([object]$Value, [string]$Label) {
    $hash = Require-String $Value $Label
    if ($hash -notmatch $Sha256Pattern) {
        throw "Classic humanoid install-manifest hash is invalid: $Label"
    }
    return $hash.ToLowerInvariant()
}

function Assert-HashBoundFile([string]$Path, [string]$ExpectedHash, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Classic humanoid install-manifest $Label is missing: $Path"
    }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedHash) {
        throw "Classic humanoid install-manifest $Label hash mismatch: $Path"
    }
}

$resolvedInstallManifest = [IO.Path]::GetFullPath($InstallManifest)
if (-not (Test-Path -LiteralPath $resolvedInstallManifest -PathType Leaf)) {
    throw "Classic humanoid install manifest is missing: $resolvedInstallManifest"
}
$install = Get-Content -LiteralPath $resolvedInstallManifest -Raw | ConvertFrom-Json -Depth 32
if ($install.schema -ne $InstallSchema -or $install.status -ne $InstallStatus) {
    throw "Unexpected classic humanoid install manifest: $resolvedInstallManifest"
}
$outputs = $install.outputs
$previewPath = [IO.Path]::GetFullPath((Require-String $outputs.openingPlayerFaceGenPreviewSet 'outputs.openingPlayerFaceGenPreviewSet'))
$previewSha256 = Require-Hash $outputs.openingPlayerFaceGenPreviewSetSha256 'outputs.openingPlayerFaceGenPreviewSetSha256'
$openingPath = [IO.Path]::GetFullPath((Require-String $outputs.openingManifest 'outputs.openingManifest'))
$openingSha256 = Require-Hash $outputs.openingManifestSha256 'outputs.openingManifestSha256'
Assert-HashBoundFile $openingPath $openingSha256 'opening manifest'
$opening = Get-Content -LiteralPath $openingPath -Raw | ConvertFrom-Json -Depth 32
$openingPreview = $opening.outputs.playerFaceGenPreviewSet
if ($null -eq $openingPreview -or
    [IO.Path]::GetFullPath((Require-String $openingPreview.path 'opening.outputs.playerFaceGenPreviewSet.path')) -ne $previewPath -or
    (Require-Hash $openingPreview.sha256 'opening.outputs.playerFaceGenPreviewSet.sha256') -ne $previewSha256) {
    throw 'Classic humanoid install-manifest preview output does not match the opening manifest.'
}
Assert-HashBoundFile $previewPath $previewSha256 'player FaceGen preview set'
Write-Output $previewPath
