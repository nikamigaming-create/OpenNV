[CmdletBinding()]
param(
    [string]$Godot = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ValidateOnly,
    [switch]$Vr,
    [ValidateSet('installed', 'none')][string]$NativePlugins = 'installed'
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'GodotToolchain.ps1')
. (Join-Path $PSScriptRoot 'ManagedToolchain.ps1')
Initialize-OpenNVManagedToolchain
$toolchain = Get-OpenNVGodotToolchain

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"

function Resolve-GodotExecutable {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "Godot executable not found: $ExplicitPath"
        }
        return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ExplicitPath).Path)
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:OPENNV_GODOT)) {
        $candidates += $env:OPENNV_GODOT
    }
    $workspaceRoot = Split-Path $repoRoot -Parent
    $bundledToolRoot = Join-Path (Join-Path $workspaceRoot 'gd') $toolchain.windowsDirectory
    $candidates += @(
        (Join-Path $bundledToolRoot $toolchain.windowsConsole),
        (Join-Path $bundledToolRoot $toolchain.windowsConsole.Replace('_console.exe', '.exe'))
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $candidate).Path)
        }
    }

    foreach ($commandName in @("godot-mono", "godot4-mono", "godot")) {
        $command = Get-Command $commandName -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $command) {
            return [IO.Path]::GetFullPath($command.Source)
        }
    }

    throw "Godot $($toolchain.release) Mono was not found. Run scripts/Install-GodotToolchain.ps1 or pass -Godot."
}

$godotPath = Resolve-GodotExecutable -ExplicitPath $Godot
$versionCheckPath = $godotPath
if (-not $versionCheckPath.EndsWith("_console.exe", [StringComparison]::OrdinalIgnoreCase)) {
    $consoleCandidate = Join-Path `
        ([IO.Path]::GetDirectoryName($versionCheckPath)) `
        ([IO.Path]::GetFileNameWithoutExtension($versionCheckPath) + "_console.exe")
    if (Test-Path -LiteralPath $consoleCandidate -PathType Leaf) {
        $versionCheckPath = $consoleCandidate
    }
}
$version = Assert-OpenNVGodotVersion -Executable $versionCheckPath

$runtimeProject = Join-Path $runtimeRoot "OpenNV.csproj"
if (-not (Test-Path -LiteralPath $runtimeProject -PathType Leaf)) {
    throw "OpenNV runtime project is missing: $runtimeProject"
}
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $dotnet) {
    throw "dotnet was not found. Install the .NET SDK required by the OpenNV runtime."
}
foreach ($requiredFile in @(
    (Join-Path $runtimeRoot "project.godot"),
    (Join-Path $runtimeRoot "runtime-manifest.json")
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "OpenNV developer launch input is missing: $requiredFile"
    }
}

& (Join-Path $PSScriptRoot 'Build-NativeAudio.ps1') -Godot $versionCheckPath
& (Join-Path $PSScriptRoot 'Build-NativePluginDomain.ps1') -Configuration Release

if ($Configuration -eq "Release") {
    # The editor's --path runner always loads Debug OpenNV and GodotSharp.
    # ExportRelease compiles both against the optimized runtime and produces
    # the single Godot product executable with the same campaigns.
    $exportRoot = Join-Path $repoRoot "tmp\development-runtime\windows"
    [IO.Directory]::CreateDirectory($exportRoot) | Out-Null
    $exportExecutable = Join-Path $exportRoot "OpenNV.exe"
    # The Windows console wrapper waits for inherited output handles. A compiler
    # server spawned by the export can retain them after the editor exits.
    $exportSharedCompilation = [Environment]::GetEnvironmentVariable('UseSharedCompilation', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('UseSharedCompilation', 'false', 'Process')
        & $versionCheckPath --headless --path $runtimeRoot --xr-mode off --export-release "Windows Experimental" $exportExecutable
    } finally {
        [Environment]::SetEnvironmentVariable('UseSharedCompilation', $exportSharedCompilation, 'Process')
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exportExecutable -PathType Leaf)) {
        throw "OpenNV release export failed. Install the matching Godot $($toolchain.release) Mono export templates."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $exportRoot 'opennv_audio.dll') -PathType Leaf)) {
        throw 'Godot did not export the native audio extension.'
    }
    & (Join-Path $PSScriptRoot 'Stage-NativePluginDomain.ps1') `
        -SourceDirectory (Join-Path $repoRoot 'runtime/generated/native-plugins/Release') `
        -DestinationDirectory (Join-Path $exportRoot 'generated/native-plugins/Release')
    $exportManifest = Get-Content -LiteralPath (Join-Path $runtimeRoot "runtime-manifest.json") -Raw | ConvertFrom-Json
    $exportManifest.runtime | Add-Member -NotePropertyName executables -NotePropertyValue @{ win32 = "OpenNV.exe" } -Force
    [IO.File]::WriteAllText((Join-Path $exportRoot "runtime-manifest.json"),
        ($exportManifest | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    $runtimeRoot = $exportRoot
} else {
    & $dotnet.Source build $runtimeProject --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw "OpenNV runtime Debug build failed." }
}

$env:OPENNV_RUNTIME_ROOT = $runtimeRoot
$env:OPENNV_GODOT = $godotPath

if ($ValidateOnly) {
    Write-Host "OpenNV developer launch ready: Godot $version; runtime $runtimeRoot; configuration $Configuration"
    return
}

if ($Configuration -eq "Release") {
    # The exported Godot runtime is now the product launcher and game. The
    # selected world is entered in this same process.
    & $exportExecutable --xr-mode $(if ($Vr) { 'on' } else { 'off' }) -- --launcher --presentation $(if ($Vr) { 'openxr' } else { 'first-person' }) --native-plugins $NativePlugins
} else {
    & $godotPath --path $runtimeRoot --xr-mode $(if ($Vr) { 'on' } else { 'off' }) -- --launcher --presentation $(if ($Vr) { 'openxr' } else { 'first-person' }) --native-plugins $NativePlugins
}
if ($LASTEXITCODE -ne 0) {
    throw "OpenNV Godot launcher exited with code $LASTEXITCODE."
}
