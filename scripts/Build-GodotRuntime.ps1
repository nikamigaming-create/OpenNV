[CmdletBinding()]
param(
    [string]$Godot = "D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
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
$xr = Get-Content -Raw -LiteralPath $xrReport | ConvertFrom-Json
    if ($xr.schema -ne "opennv-openxr-rig/v2" -or
    $xr.status -ne "pass" -or
        [int]$xr.actions -ne 8 -or
        @($xr.actionNames).Count -ne 8 -or
        @($xr.actionNames) -notcontains "reload" -or
        @($xr.testedInteractionProfiles).Count -ne 2 -or
        @($xr.testedInteractionProfiles) -notcontains "/interaction_profiles/khr/generic_controller" -or
        @($xr.testedInteractionProfiles) -notcontains "/interaction_profiles/oculus/touch_controller" -or
        $xr.controllerRenderModelManagerType -ne "OpenXRRenderModelManager" -or
        $xr.leftTracker -ne "left_hand" -or
        $xr.rightTracker -ne "right_hand" -or
        [double]$xr.worldScale -ne 1.0 -or
        [double]$xr.desiredEyeHeightMeters -ne 1.68 -or
        [int]$xr.physicsTicksPerSecond -ne 90 -or
        -not [bool]$xr.worldSpaceHud -or
        $xr.sharedSaveSchema.equippedWeaponFormId -ne "0000434f" -or
        [int]$xr.sharedSaveSchema.ammoInMagazine -ne 12 -or
        [int]$xr.sharedSaveSchema.reserveAmmo -ne 11 -or
        [int]$xr.sharedSaveSchema.shotsFired -ne 1) {
    throw "Exported OpenNV OpenXR rig report is invalid."
}
foreach ($temporaryPath in @($xrReport, $xrSave)) {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
    $ownedCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $ownedReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-report-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $reuseReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-reuse-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $portalSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-portal-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $gameplaySave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-gameplay-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $gameplayReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-gameplay-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $gameplayReloadReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-gameplay-reload-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $vrLayoutSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-vr-layout-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $vrLayoutReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-packaged-vr-layout-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $ownedProcess = Start-Process -FilePath $binary `
            -ArgumentList @(
                "--headless", "--xr-mode", "off", "--",
                "--data-root", ('"' + [IO.Path]::GetFullPath($FalloutNewVegasData) + '"'),
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
        $owned = Get-Content -Raw -LiteralPath $ownedReport | ConvertFrom-Json
        if ($owned.schema -ne "opennv-godot-cell/v1" -or
            $owned.status -ne "pass" -or
            [int]$owned.assets -lt 154 -or
            [int]$owned.textures -lt 266 -or
            [int]$owned.materialBindings -lt 339 -or
            [int]$owned.references -lt 348 -or
            [int]$owned.doors -lt 1 -or
            [int]$owned.authoredLights -lt 24 -or
            [int]$owned.actors -ne 1 -or
            [int]$owned.collisionMeshes -lt 348 -or
            [int]$owned.surfaces -lt 1 -or
            [int]$owned.vertices -lt 3 -or
            -not [bool]$owned.doorTraversal.floorHit -or
            [Math]::Abs([double]$owned.doorTraversal.floorY) -gt 0.2 -or
            -not [bool]$owned.doorTraversal.closedHitDoor -or
            [bool]$owned.doorTraversal.openHit) {
            throw "Packaged OpenNV owned-data report is invalid."
        }

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
        $reused = Get-Content -Raw -LiteralPath $reuseReport | ConvertFrom-Json
        if ($reused.schema -ne "opennv-godot-cell/v1" -or
            $reused.status -ne "pass" -or
            $reused.cellFormId -ne $owned.cellFormId -or
            -not [bool]$reused.doorTraversal.closedHitDoor -or
            [bool]$reused.doorTraversal.openHit) {
            throw "Packaged OpenNV persistent-cache report is invalid."
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
        $vrLayout = Get-Content -Raw -LiteralPath $vrLayoutReport | ConvertFrom-Json
        if ($vrLayout.schema -ne "opennv-godot-cell/v1" -or
            $vrLayout.status -ne "pass" -or
            [int]$vrLayout.actors -ne 1 -or
            -not [bool]$vrLayout.xrPresentation.heldWeapon -or
            -not [bool]$vrLayout.xrPresentation.muzzleFeedback -or
            -not [bool]$vrLayout.xrPresentation.wristHud -or
            [double]$vrLayout.xrPresentation.wristHudPixelSize -gt 0.00032 -or
            $vrLayout.xrPresentation.startingLoadout.equippedWeaponFormId -ne "0000434f" -or
            [int]$vrLayout.xrPresentation.startingLoadout.weaponDamage -ne 22 -or
            [int]$vrLayout.xrPresentation.startingLoadout.weaponClipSize -ne 12 -or
            [int]$vrLayout.xrPresentation.startingLoadout.ammoInMagazine -ne 12 -or
            [int]$vrLayout.xrPresentation.startingLoadout.reserveAmmo -ne 12) {
            throw "Packaged OpenNV VR presentation-layout report is invalid."
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
        $gameplay = Get-Content -Raw -LiteralPath $gameplayReport | ConvertFrom-Json
        if ($gameplay.schema -ne "opennv-godot-playable-route/v1" -or
            $gameplay.status -ne "pass" -or
            $gameplay.phase -ne "first-run" -or
            -not [bool]$gameplay.session.objectiveComplete -or
            $gameplay.session.equippedWeaponFormId -ne "0008f216" -or
            $gameplay.session.weaponAmmoFormId -ne "001537e3" -or
            [int]$gameplay.session.weaponDamage -ne 26 -or
            [int]$gameplay.session.weaponClipSize -ne 6 -or
            [int]$gameplay.session.ammoInMagazine -ne 5 -or
            [int]$gameplay.session.shotsFired -ne 1 -or
            [int]$gameplay.session.emptiedContainers -ne 1 -or
            [int]$gameplay.session.openDoors -ne 1) {
            throw "Packaged OpenNV playable-route report is invalid."
        }

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
        $gameplayReload = Get-Content -Raw -LiteralPath $gameplayReloadReport | ConvertFrom-Json
        if ($gameplayReload.schema -ne "opennv-godot-playable-route/v1" -or
            $gameplayReload.status -ne "pass" -or
            $gameplayReload.phase -ne "cold-reload" -or
            -not [bool]$gameplayReload.session.objectiveComplete -or
            $gameplayReload.session.weaponAmmoFormId -ne "001537e3" -or
            [int]$gameplayReload.session.weaponClipSize -ne 6 -or
            [int]$gameplayReload.session.ammoInMagazine -ne 5 -or
            [int]$gameplayReload.session.emptiedContainers -ne 1) {
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
            $vrLayoutReport
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
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage "runtime-manifest.json") -Encoding utf8NoBOM
$revision = (git -C $repoRoot rev-parse HEAD).Trim()
$buildInfo = [ordered]@{
    schema = "opennv-godot-build/v1"
    status = "experimental"
    version = $Version
    revision = $revision
    sourceTreeDirty = $dirty
    godotVersion = "4.7.2-stable-mono"
    godotWindowsArchiveSha256 = "a2a48473a7414c5f19fab690518caebb738c09ef9601f6bd2388676a7f53b3c0"
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
