[CmdletBinding()]
param(
    [string]$Godot = "",
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$desktopRoot = Join-Path $repoRoot "desktop"

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
    $bundledToolRoot = Join-Path $workspaceRoot "gd\Godot_v4.7.2-stable_mono_win64"
    $candidates += @(
        (Join-Path $bundledToolRoot "Godot_v4.7.2-stable_mono_win64_console.exe"),
        (Join-Path $bundledToolRoot "Godot_v4.7.2-stable_mono_win64.exe")
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

    throw "Godot 4.7.2 Mono was not found. Pass its executable with -Godot."
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
$versionOutput = & $versionCheckPath --version 2>&1
$versionExitCode = if (Test-Path Variable:\LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
if ($versionExitCode -ne 0) {
    throw "Godot version check failed for: $godotPath"
}
$version = ($versionOutput | Out-String).Trim()
if ($version -notmatch '(?i)^4\.7\.2(?:[.-]|$).*mono') {
    throw "OpenNV requires Godot 4.7.2 Mono; '$godotPath' reports '$version'."
}

foreach ($requiredFile in @(
    (Join-Path $runtimeRoot "project.godot"),
    (Join-Path $runtimeRoot "runtime-manifest.json"),
    (Join-Path $desktopRoot "package.json")
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "OpenNV developer launch input is missing: $requiredFile"
    }
}

$npm = Get-Command npm.cmd -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $npm) {
    $npm = Get-Command npm -ErrorAction SilentlyContinue | Select-Object -First 1
}
if ($null -eq $npm) {
    throw "npm was not found. Install a current Node.js LTS release first."
}

$env:OPENNV_RUNTIME_ROOT = $runtimeRoot
$env:OPENNV_GODOT = $godotPath

if ($ValidateOnly) {
    Write-Host "OpenNV developer launch ready: Godot $version; runtime $runtimeRoot"
    return
}

if ($env:OS -eq "Windows_NT") {
    $electronExecutable = [IO.Path]::GetFullPath(
        (Join-Path $desktopRoot "node_modules\electron\dist\electron.exe"))
    Get-Process -Name "electron" -ErrorAction SilentlyContinue |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.Path) -and
            [string]::Equals(
                [IO.Path]::GetFullPath($_.Path),
                $electronExecutable,
                [StringComparison]::OrdinalIgnoreCase)
        } |
        Stop-Process -Force
}

Push-Location $desktopRoot
try {
    & $npm.Source run dev
    if ($LASTEXITCODE -ne 0) {
        throw "OpenNV launcher exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
