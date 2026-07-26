param(
    [Parameter(Mandatory=$true)][string]$EngineInstallRoot,
    [Parameter(Mandatory=$true)][string]$LauncherRoot,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [string]$Version = "nightly",
    [string]$EngineRevision = "",
    [string]$LauncherRevision = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Directory([string]$Path, [string]$Description) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        throw "Missing ${Description}: $full"
    }
    return $full
}

function Copy-ReleaseItem([string]$SourceRoot, [string]$RelativePath, [string]$DestinationRoot) {
    $source = Join-Path $SourceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Release input is missing: $source"
    }
    $destination = Join-Path $DestinationRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}

$productRoot = Split-Path -Parent $PSScriptRoot
$EngineInstallRoot = Require-Directory $EngineInstallRoot "engine install root"
$LauncherRoot = Require-Directory $LauncherRoot "launcher source root"
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$stage = Join-Path $OutputRoot "OpenNV-$Version"
$archive = Join-Path $OutputRoot "OpenNV-$Version-windows-x64.zip"
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $archive)) {
    throw "Refusing to overwrite an existing release output. Choose a new OutputRoot or Version."
}

$binary = @(Get-ChildItem -LiteralPath $EngineInstallRoot -Recurse -Filter "openmw.exe" -File -ErrorAction SilentlyContinue)
if ($binary.Count -ne 1) {
    throw "Expected exactly one openmw.exe below $EngineInstallRoot; found $($binary.Count)."
}
$resource = @(Get-ChildItem -LiteralPath $EngineInstallRoot -Recurse -Directory -Filter "resources" -ErrorAction SilentlyContinue |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "vfs") -PathType Container })
if ($resource.Count -ne 1) {
    throw "Expected exactly one runtime resources directory below $EngineInstallRoot; found $($resource.Count)."
}
if (-not (Test-Path -LiteralPath (Join-Path $EngineInstallRoot "LICENSE.txt") -PathType Leaf)) {
    throw "The staged runtime is missing LICENSE.txt. A distributable OpenNV build must retain its engine license."
}

$runtime = Join-Path $stage "local/openmw-ttw-compat"
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
Copy-Item -Path (Join-Path $binary[0].Directory.FullName "*") -Destination $runtime -Recurse -Force
if ((Resolve-Path -LiteralPath $resource[0].FullName).Path -ne (Join-Path $runtime "resources")) {
    Copy-Item -LiteralPath $resource[0].FullName -Destination (Join-Path $runtime "resources") -Recurse -Force
}

$launcherItems = @(
    "catalog/open-nv-modules.json",
    "config/paths.example.json",
    "docs/open-nv-mod-manager.md",
    "docs/open-nv-styles.md",
    "docs/opennv-authentic-start-telemetry.md",
    "docs/ttw-compatibility-layer.md",
    "scripts/Configure-OpenNV.ps1",
    "scripts/Get-OpenNVLauncherState.ps1",
    "scripts/Initialize-OpenFO3BaseProfile.ps1",
    "scripts/Initialize-OpenNVBaseProfile.ps1",
    "scripts/Initialize-TTWCompatibilityProfile.ps1",
    "scripts/Invoke-OpenNVStartupTelemetry.ps1",
    "scripts/Manage-OpenNVMods.ps1",
    "scripts/OpenNVModManager.ps1",
    "scripts/Start-OpenNV.ps1",
    "scripts/Start-TTWCompatibilityExisting.ps1",
    "scripts/WorldViewerPaths.ps1",
    "templates/open-nv/settings.cfg"
)
foreach ($item in $launcherItems) {
    Copy-ReleaseItem -SourceRoot $LauncherRoot -RelativePath $item -DestinationRoot $stage
}
Copy-Item -LiteralPath (Join-Path $productRoot "README.md") -Destination (Join-Path $stage "README.md") -Force
Copy-Item -LiteralPath (Join-Path $productRoot "NOTICE.md") -Destination (Join-Path $stage "NOTICE.md") -Force
Get-ChildItem -LiteralPath (Join-Path $productRoot "docs") -Force | Copy-Item -Destination (Join-Path $stage "docs") -Recurse -Force

$buildInfo = [ordered]@{
    schema = "opennv-build-info/v1"
    productVersion = $Version
    builtAtUtc = [DateTime]::UtcNow.ToString("o")
    engineRevision = $EngineRevision
    launcherRevision = $LauncherRevision
    assetPolicy = "No Bethesda assets, third-party mods, saves, or profiles are included."
}
[IO.File]::WriteAllText((Join-Path $stage "BUILD-INFO.json"), (($buildInfo | ConvertTo-Json -Depth 4) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

& (Join-Path $stage "scripts/Start-OpenNV.ps1") -ShowChoices
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
[pscustomobject]@{ stage = $stage; archive = $archive } | ConvertTo-Json -Compress
