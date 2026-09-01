[CmdletBinding()]
param(
    [string]$Godot = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
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

$runtimeProject = Join-Path $runtimeRoot "OpenNV.csproj"
if (-not (Test-Path -LiteralPath $runtimeProject -PathType Leaf)) {
    throw "OpenNV runtime project is missing: $runtimeProject"
}
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $dotnet) {
    throw "dotnet was not found. Install the .NET SDK required by the OpenNV runtime."
}
& $dotnet.Source build $runtimeProject --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "OpenNV runtime $Configuration build failed."
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

$env:OPENNV_NEWVEGAS_PREFLIGHT_ERROR = $null
if ($env:OS -eq "Windows_NT" -and -not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
    $registrationPath = Join-Path `
        $env:APPDATA `
        "@open-nevada\launcher\newvegas-cache-registration.json"
    if (Test-Path -LiteralPath $registrationPath -PathType Leaf) {
        try {
            $registration = Get-Content -LiteralPath $registrationPath -Raw |
                ConvertFrom-Json
            $cacheRoot = [IO.Path]::GetFullPath([string]$registration.cacheRoot)
            $installManifestPath = Join-Path $cacheRoot "install-manifest.json"
            $runtimeConfigurationPath = Join-Path `
                $runtimeRoot `
                "config\open-nv-runtime-v1.json"
            $runtimeConfiguration = Get-Content -LiteralPath $runtimeConfigurationPath -Raw |
                ConvertFrom-Json
            $cellRecipe = [string]$runtimeConfiguration.legalAssets.defaultCellRecipe
            $python = Get-Command python -CommandType Application -ErrorAction Stop |
                Select-Object -First 1
            $identityOutput = & $python.Source `
                (Join-Path $repoRoot "content\tools\prepare_legal_assets.py") `
                --compiler-identity `
                --cell-recipe $cellRecipe
            if ($LASTEXITCODE -ne 0) {
                throw "The active New Vegas compiler identity could not be read."
            }
            $identityPrefix = "OPENNV_CONTENT_COMPILER_IDENTITY "
            $identityLine = $identityOutput |
                Where-Object { $_.StartsWith($identityPrefix, [StringComparison]::Ordinal) } |
                Select-Object -Last 1
            if ([string]::IsNullOrWhiteSpace($identityLine)) {
                throw "The active New Vegas compiler identity was not emitted."
            }
            $activeIdentity = $identityLine.Substring($identityPrefix.Length) |
                ConvertFrom-Json
            $installManifest = Get-Content -LiteralPath $installManifestPath -Raw |
                ConvertFrom-Json
            foreach ($family in $activeIdentity.families.PSObject.Properties.Name) {
                $expected = [string]$activeIdentity.families.$family.sha256
                $actual = [string]$installManifest.compilerFamilies.$family.sha256
                if (-not $actual.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
                    $env:OPENNV_NEWVEGAS_PREFLIGHT_ERROR =
                        "The registered New Vegas cache is stale. Refresh it before Play."
                    break
                }
            }
        }
        catch {
            $env:OPENNV_NEWVEGAS_PREFLIGHT_ERROR =
                "The registered New Vegas cache could not be validated. Refresh it before Play."
        }
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
    Write-Host "OpenNV developer launch ready: Godot $version; runtime $runtimeRoot; configuration $Configuration"
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
