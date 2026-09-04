[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TempleCache
)

$ErrorActionPreference = 'Stop'

function Require-String([object]$Value, [string]$Label) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "Fallout 2 Temple transition output has no $Label."
    }
    return [string]$Value
}

function Require-Hash([object]$Value, [string]$Label) {
    $hash = Require-String $Value $Label
    if ($hash -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Fallout 2 Temple transition output has an invalid $Label."
    }
    return $hash.ToLowerInvariant()
}

function Resolve-ExactPath([string]$Value, [string]$Root, [string]$Label) {
    if ([System.IO.Path]::IsPathRooted($Value)) {
        throw "Fallout 2 Temple transition output $Label must be cache-relative."
    }
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $rootPath $Value))
    $rootPrefix = $rootPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Fallout 2 Temple transition output $Label escapes its cache root."
    }
    return $candidate
}

function Assert-ExactFileHash([string]$Path, [string]$Expected, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Fallout 2 Temple transition output $Label is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "Fallout 2 Temple transition output $Label hash mismatch."
    }
}

$cachePath = [System.IO.Path]::GetFullPath($TempleCache)
if (-not (Test-Path -LiteralPath $cachePath -PathType Leaf)) {
    throw "Fallout 2 Temple cache is missing: $cachePath"
}
$cacheRoot = Split-Path -Parent $cachePath
$cache = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
$cacheSource = $cache.sourceManifest
$cacheProfile = $cache.sourceProfile
$descriptor = $cache.outputs.templeTransitions
if ($null -eq $descriptor) {
    throw 'Fallout 2 Temple cache has no outputs.templeTransitions descriptor.'
}
$transitionPath = Resolve-ExactPath (Require-String $descriptor.file 'descriptor file') $cacheRoot 'descriptor file'
$transitionHash = Require-Hash $descriptor.sha256 'descriptor sha256'
$sourceHash = Require-Hash $descriptor.sourceManifestSha256 'descriptor sourceManifestSha256'
$profileHash = Require-Hash $descriptor.sourceProfileSha256 'descriptor sourceProfileSha256'
$profileId = Require-String $descriptor.sourceProfileId 'descriptor sourceProfileId'
if ($sourceHash -ne (Require-Hash $cacheSource.sha256 'cache sourceManifest sha256') -or
    $profileHash -ne (Require-Hash $cacheProfile.sha256 'cache sourceProfile sha256') -or
    $profileId -ne (Require-String $cacheProfile.sourceProfileId 'cache sourceProfileId')) {
    throw 'Fallout 2 Temple transition descriptor does not join the cache source/profile.'
}
Assert-ExactFileHash $transitionPath $transitionHash 'manifest'
$transition = Get-Content -LiteralPath $transitionPath -Raw | ConvertFrom-Json
if ($transition.schema -ne 'opennv-fo2-temple-transitions/v1' -or
    $transition.status -ne 'compiled-owned-transition-records') {
    throw 'Fallout 2 Temple transition output has an unexpected manifest identity.'
}
$sourcePath = [System.IO.Path]::GetFullPath((Require-String $cacheSource.file 'cache sourceManifest file'))
$profilePath = [System.IO.Path]::GetFullPath((Require-String $cacheProfile.file 'cache sourceProfile file'))
if ([System.IO.Path]::GetFullPath((Require-String $transition.sourceManifest.file 'transition sourceManifest file')) -ne $sourcePath -or
    (Require-Hash $transition.sourceManifest.sha256 'transition sourceManifest sha256') -ne $sourceHash -or
    [System.IO.Path]::GetFullPath((Require-String $transition.sourceProfile.file 'transition sourceProfile file')) -ne $profilePath -or
    (Require-Hash $transition.sourceProfile.sha256 'transition sourceProfile sha256') -ne $profileHash -or
    (Require-String $transition.sourceProfile.sourceProfileId 'transition sourceProfileId') -ne $profileId) {
    throw 'Fallout 2 Temple transition output does not bind the cache source/profile.'
}

Write-Output $transitionPath
