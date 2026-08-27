[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Godot,
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
$reportValidator = Join-Path $repoRoot "content\tools\validate_runtime_report.py"
$RuntimeManifestJsonDepth = 8
$RuntimeConfigurationJsonDepth = 100
$runtimeConfigurationPath = Join-Path $runtimeRoot "config\open-nv-runtime-v1.json"
$runtimeConfiguration = Get-Content -Raw -LiteralPath $runtimeConfigurationPath |
    ConvertFrom-Json -Depth $RuntimeConfigurationJsonDepth
$ownedDataConfiguration = $runtimeConfiguration.legalAssets.ownedData
$releaseConfigurationPath = Join-Path $repoRoot "release\open-nv-release-manifest.json"
$releaseConfiguration = Get-Content -Raw -LiteralPath $releaseConfigurationPath |
    ConvertFrom-Json -Depth $RuntimeConfigurationJsonDepth
$enginePackage = $releaseConfiguration.runtime.enginePackage
$ExportTimeoutSeconds = 60
$MillisecondsPerSecond = 1000
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
$pack = [IO.Path]::ChangeExtension($binary, ".pck")
$exportTimedOutAfterArtifacts = $false
$exportProcess = Start-Process -FilePath $Godot `
    -ArgumentList @(
        "--headless",
        "--path", ('"' + $runtimeRoot + '"'),
        "--export-release", '"Windows Experimental"',
        ('"' + $binary + '"')
    ) `
    -PassThru -WindowStyle Hidden
if (-not $exportProcess.WaitForExit($ExportTimeoutSeconds * $MillisecondsPerSecond)) {
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf) -or
        -not (Test-Path -LiteralPath $pack -PathType Leaf)) {
        Stop-Process -Id $exportProcess.Id -Force
        throw "Godot Windows export timed out before producing the EXE/PCK artifacts."
    }
    Stop-Process -Id $exportProcess.Id -Force
    $exportProcess.WaitForExit()
    $exportTimedOutAfterArtifacts = $true
    Write-Host "Godot export process exceeded its bounded exit wait after writing EXE/PCK; continuing to executable smoke validation."
}
elseif ($exportProcess.ExitCode -ne 0 -or
    -not (Test-Path -LiteralPath $binary -PathType Leaf) -or
    -not (Test-Path -LiteralPath $pack -PathType Leaf)) {
    throw "Godot Windows export failed with exit code $($exportProcess.ExitCode)."
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
    -ArgumentList @("--headless", "--xr-mode", "off", "--", "--report", $smokeReport) `
    -PassThru -Wait -WindowStyle Hidden
$smokeExitCode = $smokeProcess.ExitCode
if ($smokeExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeReport -PathType Leaf)) {
    throw "Exported OpenNV runtime did not complete its headless startup smoke."
}
$smoke = Get-Content -Raw -LiteralPath $smokeReport | ConvertFrom-Json
if ($smoke.schema -ne "opennv-godot-startup/v1" -or
    $smoke.status -ne "experimental" -or
    [bool]$smoke.playable -or
    -not [bool]$smoke.playableSandbox -or
    -not [bool]$smoke.openXrLaunchable -or
    [bool]$smoke.openXrHardwareValidated) {
    throw "Exported OpenNV startup report is invalid."
}
Remove-Item -LiteralPath $smokeReport

$xrReport = Join-Path $stage "openxr-rig-smoke.json"
$xrSave = Join-Path $stage "openxr-rig-save.json"
$xrProcess = Start-Process -FilePath $binary `
    -ArgumentList @(
        "--headless", "--xr-mode", "off", "--",
        "--xr-rig-proof",
        "--save-path", ('"' + $xrSave + '"'),
        "--report", ('"' + $xrReport + '"')
    ) `
    -PassThru -Wait -WindowStyle Hidden
if ($xrProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $xrReport -PathType Leaf)) {
    throw "Exported OpenNV runtime did not complete its OpenXR rig smoke."
}
& python $reportValidator --mode xr --report $xrReport
if ($LASTEXITCODE -ne 0) { throw "Exported OpenNV OpenXR rig report is invalid." }
foreach ($temporaryPath in @($xrReport, $xrSave)) {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
    $ownedSelectionRoot = [IO.Path]::GetFullPath($FalloutNewVegasData)
    if ((Split-Path -Leaf $ownedSelectionRoot).Equals(
            [string]$ownedDataConfiguration.dataDirectoryName,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath (
            Join-Path $ownedSelectionRoot ([string]$ownedDataConfiguration.masterFile)) -PathType Leaf)) {
        $ownedSelectionRoot = Split-Path -Parent $ownedSelectionRoot
    }
    $ownedCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $ownedReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-report-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $reuseReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-reuse-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $portalSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-portal-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $gameplaySave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-gameplay-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $gameplayReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-gameplay-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $gameplayReloadReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-gameplay-reload-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $vrLayoutSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-vr-layout-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $vrLayoutReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-vr-layout-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $flatControlsSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-flat-controls-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $flatControlsReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-flat-controls-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $poolFlatSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-pool-flat-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $poolFlatReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-pool-flat-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $poolXrSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-pool-xr-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $poolXrReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-pool-xr-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $ownedProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--data-root", ('"' + $ownedSelectionRoot + '"'),
                "--cache-root", ('"' + $ownedCache + '"'),
                "--report", ('"' + $ownedReport + '"'),
                "--save-path", ('"' + $portalSave + '"'),
                "--portal-proof",
                "--quit-after-load"
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($ownedProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ownedReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its owned-data end-to-end gate."
        }
        $ownedInstallManifest = Join-Path $ownedCache "install-manifest.json"
        & python $reportValidator --mode cell --report $ownedReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) { throw "Packaged OpenNV owned-data report is invalid." }
        $owned = Get-Content -Raw -LiteralPath $ownedReport | ConvertFrom-Json

        $reuseProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--report", ('"' + $reuseReport + '"'),
                "--save-path", ('"' + $portalSave + '"'),
                "--portal-proof",
                "--quit-after-load"
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($reuseProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reuseReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its persistent-cache gate."
        }
        & python $reportValidator --mode cell --report $reuseReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) { throw "Packaged OpenNV persistent-cache report is invalid." }
        $reused = Get-Content -Raw -LiteralPath $reuseReport | ConvertFrom-Json
        if ($reused.cellFormId -ne $owned.cellFormId) {
            throw "Packaged OpenNV persistent-cache loaded another cell."
        }

        $vrLayoutProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--save-path", ('"' + $vrLayoutSave + '"'),
                "--vr-layout-proof",
                "--report", ('"' + $vrLayoutReport + '"'),
                "--quit-after-load"
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($vrLayoutProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $vrLayoutReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its VR presentation-layout gate."
        }
        & python $reportValidator --mode vr-layout --report $vrLayoutReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged OpenNV VR presentation-layout report is invalid."
        }

        $flatControlsProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--save-path", ('"' + $flatControlsSave + '"'),
                "--flat-controls-proof",
                "--report", ('"' + $flatControlsReport + '"')
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($flatControlsProcess.ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath $flatControlsReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its flat control-input gate."
        }
        & python $reportValidator --mode flat-controls --report $flatControlsReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged OpenNV flat control-input report is invalid."
        }

        $poolFlatProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--save-path", ('"' + $poolFlatSave + '"'),
                "--pool-proof",
                "--report", ('"' + $poolFlatReport + '"')
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($poolFlatProcess.ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath $poolFlatReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its flat pool-contact gate."
        }
        & python $reportValidator --mode pool-flat --report $poolFlatReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged OpenNV flat pool-contact report is invalid."
        }

        $poolXrProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--save-path", ('"' + $poolXrSave + '"'),
                "--vr-layout-proof",
                "--pool-proof",
                "--report", ('"' + $poolXrReport + '"')
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($poolXrProcess.ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath $poolXrReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its OpenXR-layout pool-contact gate."
        }
        & python $reportValidator --mode pool-xr-layout --report $poolXrReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged OpenNV OpenXR-layout pool-contact report is invalid."
        }

        $gameplayProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--save-path", ('"' + $gameplaySave + '"'),
                "--gameplay-proof",
                "--report", ('"' + $gameplayReport + '"')
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($gameplayProcess.ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath $gameplayReport -PathType Leaf) -or
            -not (Test-Path -LiteralPath $gameplaySave -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its playable-route gate."
        }
        & python $reportValidator --mode gameplay --report $gameplayReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) { throw "Packaged OpenNV playable-route report is invalid." }

        $gameplayReloadProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--reuse-cache",
                "--cache-root", ('"' + $ownedCache + '"'),
                "--save-path", ('"' + $gameplaySave + '"'),
                "--gameplay-reload-proof",
                "--report", ('"' + $gameplayReloadReport + '"')
            ) `
            -PassThru -Wait -WindowStyle Hidden
        if ($gameplayReloadProcess.ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath $gameplayReloadReport -PathType Leaf)) {
            throw "Packaged OpenNV runtime failed its gameplay cold-reload gate."
        }
        & python $reportValidator --mode gameplay-reload --report $gameplayReloadReport `
            --install-manifest $ownedInstallManifest
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged OpenNV gameplay cold-reload report is invalid."
        }
    }
    finally {
        foreach ($temporaryPath in @(
            $ownedCache,
            $ownedReport,
            $reuseReport,
            $portalSave,
            $gameplaySave,
            $gameplayReport,
            $gameplayReloadReport,
            $vrLayoutSave,
            $vrLayoutReport,
            $flatControlsSave,
            $flatControlsReport,
            $poolFlatSave,
            $poolFlatReport,
            $poolXrSave,
            $poolXrReport
        )) {
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
$manifest | ConvertTo-Json -Depth $RuntimeManifestJsonDepth | Set-Content -LiteralPath (Join-Path $stage "runtime-manifest.json") -Encoding utf8NoBOM
$revision = (git -C $repoRoot rev-parse HEAD).Trim()
$buildInfo = [ordered]@{
    schema = "opennv-godot-build/v1"
    status = "experimental"
    version = $Version
    revision = $revision
    sourceTreeDirty = $dirty
    godotVersion = [string]$enginePackage.versionLabel
    godotWindowsArchiveSha256 = [string]$enginePackage.windowsArchiveSha256
    exportTimedOutAfterArtifacts = $exportTimedOutAfterArtifacts
    contentToolSha256 = (Get-FileHash -LiteralPath $contentBinary -Algorithm SHA256).Hash.ToLowerInvariant()
    playable = $false
    playableSandbox = $true
    openXrLaunchable = $true
    openXrHardwareValidated = $false
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
$archiveReader = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entryNames = @($archiveReader.Entries | ForEach-Object FullName)
    $unsafeEntries = @($entryNames | Where-Object {
        $normalized = $_.Replace("\\", "/")
        $normalized.StartsWith("/", [StringComparison]::Ordinal) -or
        $normalized.Split("/", [StringSplitOptions]::RemoveEmptyEntries) -contains ".."
    })
    if ($unsafeEntries.Count -gt 0) {
        throw "OpenNV archive contains unsafe entry paths:`n$($unsafeEntries -join [Environment]::NewLine)"
    }
    $forbiddenArchiveEntries = @($entryNames | Where-Object {
        [IO.Path]::GetExtension($_) -in @(".bsa", ".dds", ".esm", ".esp", ".fos", ".nif")
    })
    if ($forbiddenArchiveEntries.Count -gt 0) {
        throw "OpenNV archive contains retail-derived assets:`n$($forbiddenArchiveEntries -join [Environment]::NewLine)"
    }
    foreach ($requiredName in @(
        "OpenNV.exe",
        "OpenNV.pck",
        "OpenNV.Content.exe",
        "runtime-manifest.json",
        "BUILD-INFO.json"
    )) {
        if (-not ($entryNames | Where-Object { $_.Replace("\\", "/").EndsWith("/$requiredName", [StringComparison]::OrdinalIgnoreCase) })) {
            throw "OpenNV archive is missing required payload: $requiredName"
        }
    }
}
finally {
    $archiveReader.Dispose()
}
[pscustomobject][ordered]@{
    schema = "opennv-godot-package/v1"
    status = "pass"
    stage = $stage
    archive = $archive
    archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
}
