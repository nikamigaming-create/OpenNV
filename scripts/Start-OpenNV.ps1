[CmdletBinding()]
param(
    [string]$Godot = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ValidateOnly,
    [switch]$Vr,
    [ValidateSet('none')][string]$NativePlugins = 'none',
    [ValidateSet('vanilla', 'installed')][string]$ContentSelection = 'vanilla',
    [ValidateSet('newvegas', 'fallout3', 'fallout1', 'fallout2')][string]$Game = 'newvegas',
    [string]$ProfileRoot = '',
    [switch]$Direct,
    [switch]$NewGame,
    [string]$DataRoot = '',
    [string]$SavePath = ''
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

function Assert-ExportChildPath {
    param([string]$Path, [string]$Parent)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (![StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetDirectoryName($fullPath), $Parent)) {
        throw "Export operation escaped its workspace: $fullPath"
    }
    if ((Test-Path -LiteralPath $fullPath) -and
        ((Get-Item -LiteralPath $fullPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Export operation cannot replace a linked directory: $fullPath"
    }
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

if ([string]::IsNullOrWhiteSpace($ProfileRoot)) {
    $ProfileRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Godot/app_userdata/OpenNV/vanilla'
}
$ProfileRoot = [IO.Path]::GetFullPath($ProfileRoot)

if ($Direct) {
    if ([string]::IsNullOrWhiteSpace($DataRoot)) {
        $profilesJsonPath = Join-Path $ProfileRoot 'launcher/profiles-v1.json'
        if (-not (Test-Path -LiteralPath $profilesJsonPath -PathType Leaf)) {
            $profilesJsonPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Godot/app_userdata/OpenNV/launcher/profiles-v1.json'
        }
        if (Test-Path -LiteralPath $profilesJsonPath -PathType Leaf) {
            try {
                $profilesData = Get-Content -LiteralPath $profilesJsonPath -Raw | ConvertFrom-Json
                if ($profilesData.profiles.$Game.installRoot) {
                    $DataRoot = $profilesData.profiles.$Game.installRoot
                }
            } catch {}
        }
    }
    if ([string]::IsNullOrWhiteSpace($DataRoot)) {
        if ($Game -eq 'newvegas') {
            $regKey = Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Bethesda Softworks\FalloutNV' -ErrorAction SilentlyContinue
            if ($regKey -and $regKey.'installed path') { $DataRoot = $regKey.'installed path'.TrimEnd('\', '/') }
            if (-not $DataRoot) {
                $regKey2 = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Bethesda Softworks\FalloutNV' -ErrorAction SilentlyContinue
                if ($regKey2 -and $regKey2.'installed path') { $DataRoot = $regKey2.'installed path'.TrimEnd('\', '/') }
            }
        } elseif ($Game -eq 'fallout3') {
            $regKey = Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout3' -ErrorAction SilentlyContinue
            if ($regKey -and $regKey.'installed path') { $DataRoot = $regKey.'installed path'.TrimEnd('\', '/') }
        }
    }
    if ([string]::IsNullOrWhiteSpace($DataRoot)) {
        $candidates = @()
        if ($Game -eq 'newvegas') {
            $candidates = @(
                "D:\SteamLibrary\steamapps\common\Fallout New Vegas",
                "C:\Program Files (x86)\Steam\steamapps\common\Fallout New Vegas",
                "C:\Steam\steamapps\common\Fallout New Vegas",
                "C:\GOG Games\Fallout New Vegas",
                "D:\GOG Games\Fallout New Vegas"
            )
        } elseif ($Game -eq 'fallout3') {
            $candidates = @(
                "D:\SteamLibrary\steamapps\common\Fallout 3 goty",
                "D:\SteamLibrary\steamapps\common\Fallout 3",
                "C:\Program Files (x86)\Steam\steamapps\common\Fallout 3 goty",
                "C:\Steam\steamapps\common\Fallout 3 goty"
            )
        } elseif ($Game -eq 'fallout1') {
            $candidates = @(
                "D:\SteamLibrary\steamapps\common\Fallout",
                "C:\Program Files (x86)\Steam\steamapps\common\Fallout"
            )
        } elseif ($Game -eq 'fallout2') {
            $candidates = @(
                "D:\SteamLibrary\steamapps\common\Fallout 2",
                "C:\Program Files (x86)\Steam\steamapps\common\Fallout 2"
            )
        }
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $DataRoot = $candidate
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($DataRoot)) {
        throw "Could not automatically resolve installation for $Game. Pass -DataRoot explicitly or run without -Direct to use the launcher."
    }

    $campaignName = switch ($Game) {
        'newvegas' { 'fallout-new-vegas' }
        'fallout3' { 'fallout-3' }
        'fallout1' { 'fallout-1' }
        'fallout2' { 'fallout-2' }
    }

    if ([string]::IsNullOrWhiteSpace($SavePath)) {
        $saveFileName = switch ($Game) {
            'newvegas' { 'courier-v1.json' }
            'fallout3' { 'capital-wasteland-v1.json' }
            'fallout1' { 'vault-dweller-v1.json' }
            'fallout2' { 'chosen-v1.json' }
        }
        $SavePath = Join-Path $ProfileRoot "profiles/$Game/$saveFileName"
    }

    $launchArguments = @('--xr-mode', $(if ($Vr) { 'on' } else { 'off' }), '--',
        '--data-root', $DataRoot, '--campaign', $campaignName, '--save-path', $SavePath,
        '--presentation', $(if ($Vr) { 'openxr' } else { 'first-person' }),
        '--native-plugins', $NativePlugins, '--content-selection', $ContentSelection, '--profile-root', $ProfileRoot)
    if ($NewGame) {
        $launchArguments += '--new-game'
    }
} else {
    $launchArguments = @('--xr-mode', $(if ($Vr) { 'on' } else { 'off' }), '--', '--launcher',
        '--launcher-game', $Game, '--presentation', $(if ($Vr) { 'openxr' } else { 'first-person' }),
        '--native-plugins', $NativePlugins, '--content-selection', $ContentSelection, '--profile-root', $ProfileRoot)
}

if ($Configuration -eq "Release") {
    # The editor's --path runner always loads Debug OpenNV and GodotSharp.
    # ExportRelease compiles both against the optimized runtime and produces
    # the single Godot product executable with the same campaigns.
    $exportParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'tmp/development-runtime'))
    [IO.Directory]::CreateDirectory($exportParent) | Out-Null
    $exportParent = (Resolve-Path -LiteralPath $exportParent).Path
    $exportRoot = Join-Path $exportParent 'windows'
    $exportStaging = Join-Path $exportParent ('windows.pending-' + [Guid]::NewGuid().ToString('N'))
    $exportPrevious = Join-Path $exportParent ('windows.previous-' + [Guid]::NewGuid().ToString('N'))
    foreach ($path in @($exportRoot, $exportStaging, $exportPrevious)) {
        Assert-ExportChildPath -Path $path -Parent $exportParent
    }
    $exportLog = Join-Path $exportParent 'windows-export.log'
    [IO.Directory]::CreateDirectory($exportStaging) | Out-Null
    $stagedExecutable = Join-Path $exportStaging 'OpenNV.exe'
    $exportPromoted = $false
    # The Windows console wrapper waits for inherited output handles. A compiler
    # server spawned by the export can retain them after the editor exits.
    $exportSharedCompilation = [Environment]::GetEnvironmentVariable('UseSharedCompilation', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('UseSharedCompilation', 'false', 'Process')
        & $versionCheckPath --headless --path $runtimeRoot --xr-mode off --export-release "Windows Experimental" $stagedExecutable 2>&1 |
            Tee-Object -LiteralPath $exportLog | ForEach-Object { Write-Host $_ }
        $exportExitCode = $LASTEXITCODE
        $exportText = [IO.File]::ReadAllText($exportLog) -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
        # Godot can report managed export failure while returning exit code zero.
        if ($exportExitCode -ne 0 -or $exportText -match '(?im)^\s*(?:ERROR:|SCRIPT ERROR:|Build FAILED\.)|Export \.NET Project: Failed|\berror (?:MSB|NETSDK|NU|CS)\d+\s*:') {
            throw "OpenNV release export failed (exit $exportExitCode). Inspect $exportLog"
        }
        foreach ($relative in @('OpenNV.exe', 'OpenNV.pck', 'opennv_audio.dll',
            'data_OpenNV_windows_x86_64/OpenNV.dll', 'data_OpenNV_windows_x86_64/GodotSharp.dll',
            'data_OpenNV_windows_x86_64/OpenNV.deps.json')) {
            $file = Join-Path $exportStaging $relative
            if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
                throw "OpenNV release export is incomplete: $relative. Inspect $exportLog"
            }
        }
        & (Join-Path $PSScriptRoot 'Stage-NativePluginDomain.ps1') `
            -SourceDirectory (Join-Path $repoRoot 'runtime/generated/native-plugins/Release') `
            -DestinationDirectory (Join-Path $exportStaging 'generated/native-plugins/Release')
        $exportManifest = Get-Content -LiteralPath (Join-Path $runtimeRoot 'runtime-manifest.json') -Raw | ConvertFrom-Json
        $exportManifest.runtime | Add-Member -NotePropertyName executables -NotePropertyValue @{ win32 = 'OpenNV.exe' } -Force
        [IO.File]::WriteAllText((Join-Path $exportStaging 'runtime-manifest.json'),
            ($exportManifest | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $exportRoot) {
            Assert-ExportChildPath -Path $exportRoot -Parent $exportParent
            Assert-ExportChildPath -Path $exportPrevious -Parent $exportParent
            Move-Item -LiteralPath $exportRoot -Destination $exportPrevious
        }
        try {
            Assert-ExportChildPath -Path $exportStaging -Parent $exportParent
            Assert-ExportChildPath -Path $exportRoot -Parent $exportParent
            Move-Item -LiteralPath $exportStaging -Destination $exportRoot
            $exportPromoted = $true
        } catch {
            if ((Test-Path -LiteralPath $exportPrevious) -and !(Test-Path -LiteralPath $exportRoot)) {
                Assert-ExportChildPath -Path $exportPrevious -Parent $exportParent
                Assert-ExportChildPath -Path $exportRoot -Parent $exportParent
                Move-Item -LiteralPath $exportPrevious -Destination $exportRoot
            }
            throw
        }
    } finally {
        [Environment]::SetEnvironmentVariable('UseSharedCompilation', $exportSharedCompilation, 'Process')
        if (Test-Path -LiteralPath $exportStaging) {
            Assert-ExportChildPath -Path $exportStaging -Parent $exportParent
            Remove-Item -LiteralPath $exportStaging -Recurse -Force
        }
        if ($exportPromoted -and (Test-Path -LiteralPath $exportPrevious)) {
            Assert-ExportChildPath -Path $exportPrevious -Parent $exportParent
            Remove-Item -LiteralPath $exportPrevious -Recurse -Force
        }
    }
    $exportExecutable = Join-Path $exportRoot 'OpenNV.exe'
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
    & $exportExecutable @launchArguments
} else {
    & $godotPath --path $runtimeRoot @launchArguments
}
if ($LASTEXITCODE -ne 0) {
    throw "OpenNV Godot launcher exited with code $LASTEXITCODE."
}
