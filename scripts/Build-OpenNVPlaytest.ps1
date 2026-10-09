[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Godot,
    [ValidatePattern('^$|^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$Version = '',
    [string]$OutputRoot = '',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9 ._-]*$')][string]$LaunchName = 'OpenNV Test'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskCommit = (& git -C $taskRepository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
$taskTree = (& git -C $taskRepository rev-parse 'HEAD^{tree}').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source tree.' }
if (!$Version) { $Version = 'dev-' + $taskCommit.Substring(0, 12) }
if (!$OutputRoot) { $OutputRoot = Join-Path $taskRepository 'local/releases' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$taskPackage = Join-Path $OutputRoot "OpenNV-$Version-windows-x64"
if ((Test-Path -LiteralPath $taskPackage) -or (Test-Path -LiteralPath ($taskPackage + '.zip'))) {
    throw "Pinned package already exists: $taskPackage"
}
& git -C $taskRepository diff --quiet
if ($LASTEXITCODE -ne 0) { throw 'Commit the implementation before pinning its test build.' }
& git -C $taskRepository diff --cached --quiet
if ($LASTEXITCODE -ne 0 -or @(& git -C $taskRepository ls-files --others --exclude-standard).Count -ne 0) {
    throw 'Staged or untracked implementation files are not pinned to the source commit.'
}

& (Join-Path $PSScriptRoot 'Start-OpenNV.ps1') -Godot $Godot -Configuration Release -ValidateOnly
& (Join-Path $PSScriptRoot 'Build-NativePluginDomain.ps1') -Configuration Release
$taskGodotPath = [IO.Path]::GetFullPath($Godot)
$taskGodotVersion = (& $taskGodotPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the exporter version.' }
$taskEditorPath = if ($taskGodotPath.EndsWith('_console.exe', [StringComparison]::OrdinalIgnoreCase)) {
    $taskGodotPath.Substring(0, $taskGodotPath.Length - '_console.exe'.Length) + '.exe'
} else { $taskGodotPath }
if (!(Test-Path -LiteralPath $taskEditorPath -PathType Leaf)) { throw 'The exporter editor executable is absent.' }
$taskSdkVersion = (& dotnet --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the .NET SDK version.' }
if ((& git -C $taskRepository rev-parse HEAD).Trim() -ne $taskCommit -or
    (& git -C $taskRepository rev-parse 'HEAD^{tree}').Trim() -ne $taskTree) {
    throw 'Source commit changed during the release build.'
}
$taskToolchain = [ordered]@{
    schema = 'opennv-build-toolchain/v1'
    sourceCommit = $taskCommit
    sourceTree = $taskTree
    godotVersion = $taskGodotVersion
    godotExecutableSha256 = (Get-FileHash -LiteralPath $taskGodotPath -Algorithm SHA256).Hash.ToLowerInvariant()
    godotEditorExecutableSha256 = (Get-FileHash -LiteralPath $taskEditorPath -Algorithm SHA256).Hash.ToLowerInvariant()
    dotnetSdkVersion = $taskSdkVersion
    configuration = 'ExportRelease'
}
$taskExport = Join-Path $taskRepository 'tmp/development-runtime/windows'
$taskToolchain | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $taskExport 'build-toolchain.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Package-OpenNV.ps1') -Version $Version -ExportDirectory $taskExport -OutputRoot $OutputRoot
$taskManifest = Get-Content -LiteralPath (Join-Path $taskPackage 'release-manifest.json') -Raw | ConvertFrom-Json
if ($taskManifest.commit -ne $taskCommit -or $taskManifest.dirty -or $taskManifest.ownedDataIncluded) {
    throw 'The package is not an immutable clean-source playtest build.'
}
foreach ($taskFile in $taskManifest.files) {
    $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskPackage $taskFile.path))
    if (!$taskTarget.StartsWith($taskPackage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskFile.sha256) {
        throw "Pinned package file differs from its manifest: $($taskFile.path)"
    }
}

$taskSmokeLog = Join-Path $OutputRoot ($LaunchName + '-startup.log')
# Windows GUI exports do not inherit the PowerShell console's output handles.
# Use the runtime's own file logger and wait for the actual exported process.
$taskStartup = Start-Process -FilePath (Join-Path $taskPackage 'OpenNV.exe') -ArgumentList (
    '--headless --xr-mode off --quit-after 3 --log-file "' + $taskSmokeLog + '" -- --launcher') -WindowStyle Hidden -PassThru
if (!$taskStartup.WaitForExit(45000)) {
    $taskStartup.Kill()
    throw "Pinned launcher startup did not finish: $taskSmokeLog"
}
$taskStartupExit = $taskStartup.ExitCode
if (!(Test-Path -LiteralPath $taskSmokeLog -PathType Leaf)) {
    throw "The exported launcher did not write its startup log: $taskSmokeLog"
}
$taskStartupText = [IO.File]::ReadAllText($taskSmokeLog)
if ($taskStartupExit -ne 0 -or $taskStartupText -match '(?im)^(?:ERROR:|SCRIPT ERROR:|WARNING:.*(?:leaked at exit|ObjectDB.*leak|RIDs? .*not freed|resources still in use))') {
    throw "Pinned launcher startup failed; inspect $taskSmokeLog"
}
$taskReadyRows = @([regex]::Matches($taskStartupText, '(?m)^OPENNV_LAUNCHER_READY (\{[^\r\n]+\})\r?$'))
if ($taskReadyRows.Count -ne 1) { throw "The exported native launcher did not report its constructed state: $taskSmokeLog" }
$taskReady = $taskReadyRows[0].Groups[1].Value | ConvertFrom-Json
$taskRuntimeManifestHash = (Get-FileHash -LiteralPath (Join-Path $taskRepository 'runtime/runtime-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($taskReady.phase -ne 'launcher-construction' -or !$taskReady.refreshed -or !$taskReady.artworkLoaded -or
    $taskReady.buildVersion -ne $Version -or $taskReady.sourceCommit -ne $taskCommit -or
    $taskReady.sourceModified -ne $false -or $taskReady.runtimeManifestSha256 -ne $taskRuntimeManifestHash) {
    throw "Exported launcher identity differs from the pinned source/package: $taskSmokeLog"
}

# The pointer can advance. Each package and checksum retains its original path.
$taskRelative = [IO.Path]::GetRelativePath($OutputRoot, $taskPackage)
$taskCommand = @('@echo off', 'cd /d "%~dp0"',
    ('start "OpenNV" "%~dp0' + $taskRelative + '\OpenNV.exe" --xr-mode off -- --launcher %*'))
$taskCommandPath = Join-Path $OutputRoot ($LaunchName + '.cmd')
$taskPending = $taskCommandPath + '.pending'
$taskCommand | Set-Content -LiteralPath $taskPending -Encoding ascii
Move-Item -LiteralPath $taskPending -Destination $taskCommandPath -Force
[ordered]@{ schema = 'opennv-test-build/v1'; commit = $taskCommit; version = $Version;
    package = $taskPackage; launcher = $taskCommandPath;
    archiveSha256 = (Get-FileHash -LiteralPath ($taskPackage + '.zip') -Algorithm SHA256).Hash.ToLowerInvariant() } |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputRoot ($LaunchName + '.json')) -Encoding utf8
Write-Output "OPENNV_PLAYTEST_PINNED commit=$taskCommit package=$taskPackage launcher=$taskCommandPath"
