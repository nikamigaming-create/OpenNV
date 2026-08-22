[CmdletBinding()]
param(
    [string]$Godot = "D:\code\gd\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
    [Parameter(Mandatory)]
    [string]$OutputRoot,
    [string]$Version = "experimental",
    [string]$FalloutNewVegasData = "",
    [switch]$SkipTests,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$dirty = -not [string]::IsNullOrWhiteSpace((git -C $repoRoot status --porcelain=v1 | Out-String))
if ($dirty -and -not $AllowDirty) {
    throw "Refusing to package a dirty source tree. Commit the build inputs or pass -AllowDirty for a non-promotable local check."
}
$output = [IO.Path]::GetFullPath($OutputRoot)
$stage = Join-Path $output "OpenNV-$Version-windows-x64"
$archive = Join-Path $output "OpenNV-$Version-windows-x64.zip"
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $archive)) {
    throw "Refusing to overwrite an existing runtime output: $stage or $archive"
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

if (-not $SkipTests) {
    $testArguments = @{ Godot = $Godot }
    if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
        $testArguments.FalloutNewVegasData = $FalloutNewVegasData
    }
    & (Join-Path $PSScriptRoot "Test-GodotRuntime.ps1") @testArguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "OpenNV Godot gate failed before export." }
}

$binary = Join-Path $stage "OpenNV.exe"
$exportOutput = & $Godot --headless --path $runtimeRoot --export-release "Windows Experimental" $binary 2>&1
$exportExitCode = $LASTEXITCODE
$exportText = $exportOutput | Out-String
$exportText | Write-Host
if ($exportExitCode -ne 0 -or -not (Test-Path -LiteralPath $binary -PathType Leaf)) {
    throw "Godot Windows export failed:`n$exportText"
}

$contentBuildRoot = Join-Path ([IO.Path]::GetTempPath()) ("opennv-content-package-{0}" -f [guid]::NewGuid().ToString("N"))
try {
    $contentPackage = & (Join-Path $PSScriptRoot "Build-ContentTool.ps1") -OutputRoot $contentBuildRoot
    if ($contentPackage.status -ne "pass") { throw "OpenNV content-tool package gate failed." }
    $contentBinary = Join-Path $stage "OpenNV.Content.exe"
    Copy-Item -LiteralPath $contentPackage.binary -Destination $contentBinary
    Copy-Item -LiteralPath (Join-Path $contentBuildRoot "CONTENT-THIRD-PARTY.md") -Destination $stage
    $licenseStage = Join-Path $stage "licenses"
    New-Item -ItemType Directory -Path $licenseStage | Out-Null
    Copy-Item -Path (Join-Path $contentPackage.licenses "*") -Destination $licenseStage
    Copy-Item -Path (Join-Path $runtimeRoot "licenses\*") -Destination $licenseStage
    Copy-Item -LiteralPath (Join-Path $runtimeRoot "THIRD_PARTY.md") -Destination (Join-Path $stage "RUNTIME-THIRD-PARTY.md")
    Copy-Item -LiteralPath (Join-Path $repoRoot "NOTICE.md") -Destination $stage
}
finally {
    if (Test-Path -LiteralPath $contentBuildRoot) {
        $resolvedContentBuild = [IO.Path]::GetFullPath($contentBuildRoot)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedContentBuild.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove non-temporary content build: $resolvedContentBuild"
        }
        Remove-Item -LiteralPath $resolvedContentBuild -Recurse -Force
    }
}
if (-not (Test-Path -LiteralPath $contentBinary -PathType Leaf)) {
    throw "Packaged OpenNV runtime has no legal-content helper."
}

$smokeReport = Join-Path $stage "startup-smoke.json"
$smokeProcess = Start-Process -FilePath $binary `
    -ArgumentList @("--headless", "--", "--report", $smokeReport) `
    -PassThru -Wait -WindowStyle Hidden
$smokeExitCode = $smokeProcess.ExitCode
if ($smokeExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeReport -PathType Leaf)) {
    throw "Exported OpenNV runtime did not complete its headless startup smoke."
}
$smoke = Get-Content -Raw -LiteralPath $smokeReport | ConvertFrom-Json
if ($smoke.schema -ne "opennv-godot-startup/v1" -or
    $smoke.status -ne "experimental" -or
    [bool]$smoke.playable) {
    throw "Exported OpenNV startup report is invalid."
}
Remove-Item -LiteralPath $smokeReport

if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
    $ownedCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $ownedReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-report-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $ownedProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--",
                "--data-root", ('"' + [IO.Path]::GetFullPath($FalloutNewVegasData) + '"'),
                "--cache-root", ('"' + $ownedCache + '"'),
                "--report", ('"' + $ownedReport + '"'),
                "--quit-after-load"
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($ownedProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ownedReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its owned-data end-to-end gate."
        }
        $owned = Get-Content -Raw -LiteralPath $ownedReport | ConvertFrom-Json
        if ($owned.schema -ne "opennv-godot-static-model/v1" -or
            $owned.status -ne "pass" -or
            [int]$owned.meshes -lt 1 -or
            [int]$owned.surfaces -lt 1 -or
            [int]$owned.vertices -lt 3) {
            throw "Packaged OpenNV owned-data report is invalid."
        }
    }
    finally {
        foreach ($temporaryPath in @($ownedCache, $ownedReport)) {
            if (Test-Path -LiteralPath $temporaryPath) {
                $resolvedPath = [IO.Path]::GetFullPath($temporaryPath)
                $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
                if (-not $resolvedPath.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Refusing to remove non-temporary owned-data output: $resolvedPath"
                }
                Remove-Item -LiteralPath $resolvedPath -Recurse -Force
            }
        }
    }
}

$manifest = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot "runtime-manifest.json") | ConvertFrom-Json
$manifest.runtime.executables | Add-Member -NotePropertyName win32 -NotePropertyValue "OpenNV.exe" -Force
$manifest.runtime.contentExecutables | Add-Member -NotePropertyName win32 -NotePropertyValue "OpenNV.Content.exe" -Force
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage "runtime-manifest.json") -Encoding utf8NoBOM
$revision = (git -C $repoRoot rev-parse HEAD).Trim()
$buildInfo = [ordered]@{
    schema = "opennv-godot-build/v1"
    status = "experimental"
    version = $Version
    revision = $revision
    sourceTreeDirty = $dirty
    godotVersion = "4.7.1-stable-mono"
    godotWindowsArchiveSha256 = "764a089809fb1a6f745686ce9f6d3ca83adce8fb60fb9a4e2324b63baaebaa45"
    contentToolSha256 = (Get-FileHash -LiteralPath $contentBinary -Algorithm SHA256).Hash.ToLowerInvariant()
    playable = $false
    assetsIncluded = $false
}
$buildInfo | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage "BUILD-INFO.json") -Encoding utf8NoBOM

$forbiddenAssets = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object Extension -in @(
    ".bsa", ".dds", ".esm", ".esp", ".fos", ".nif"
))
if ($forbiddenAssets.Count -gt 0) {
    throw "Export unexpectedly contains retail-derived assets:`n$($forbiddenAssets.FullName -join [Environment]::NewLine)"
}

Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
[pscustomobject][ordered]@{
    schema = "opennv-godot-package/v1"
    status = "pass"
    stage = $stage
    archive = $archive
    archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
}
