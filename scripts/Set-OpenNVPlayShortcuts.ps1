[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$ProfileRoot,
    [ValidateSet('newvegas', 'fallout3')][string[]]$Games = @('newvegas'),
    [string]$ShortcutDirectory = [Environment]::GetFolderPath('Desktop'),
    [string]$NamePrefix = 'OpenNV Candidate'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$profiles = (Resolve-Path -LiteralPath $ProfileRoot).Path
$executable = Join-Path $release 'OpenNV.exe'
foreach ($file in @($executable, (Join-Path $release 'OpenNV.pck'), (Join-Path $release 'build-toolchain.json'), (Join-Path $release 'runtime-manifest.json'))) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing Release package file: $file" }
}
$toolchain = Get-Content -LiteralPath (Join-Path $release 'build-toolchain.json') -Raw | ConvertFrom-Json
if ($toolchain.configuration -ne 'ExportRelease') { throw 'Play shortcuts require an exported Release package.' }
$manifest = Get-Content -LiteralPath (Join-Path $release 'runtime-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schema -ne 'opennv-runtime-manifest/v2') { throw 'The package runtime manifest is unsupported.' }
$availableModes = @{}
$modes = @(@{ label='Flat'; xr='off'; presentation='first-person' }, @{ label='VR'; xr='on'; presentation='openxr' })
$profileFile = Join-Path $profiles 'launcher/profiles-v1.json'
$profileStore = Get-Content -LiteralPath $profileFile -Raw | ConvertFrom-Json
foreach ($game in $Games) {
    $profile = $profileStore.profiles.PSObject.Properties[$game]
    if (!$profile -or @($profile.Value.enabledMods).Count -ne 0) {
        throw "Configure a profile without selected mods for $game before creating these shortcuts."
    }
    $campaigns = @($manifest.campaigns | Where-Object { $_.id -ieq $game })
    if ($campaigns.Count -ne 1 -or !$campaigns[0].launchable) {
        throw "The package does not offer a launchable route for $game."
    }
    $availableModes[$game] = @($modes | Where-Object {
        $route = $campaigns[0].presentations.PSObject.Properties[$_.presentation]
        $route -and $route.Value.launchable
    })
    if ($availableModes[$game].Count -eq 0) {
        throw "The package does not offer a flat or VR presentation for $game."
    }
}
[void][IO.Directory]::CreateDirectory($ShortcutDirectory)
$shortcutRoot = (Resolve-Path -LiteralPath $ShortcutDirectory).Path
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($game in @(@{ id='newvegas'; title='New Vegas' }, @{ id='fallout3'; title='Fallout 3' }) | Where-Object { $_.id -in $Games }) {
        foreach ($mode in $availableModes[$game.id]) {
            $path = Join-Path $shortcutRoot ($NamePrefix + ' ' + $game.title + ' ' + $mode.label + '.lnk')
            $shortcut = $shell.CreateShortcut($path)
            try {
                $shortcut.TargetPath = $executable
                $shortcut.WorkingDirectory = $release
                $shortcut.Arguments = '--xr-mode ' + $mode.xr + ' -- --launcher --launcher-game ' + $game.id +
                    ' --presentation ' + $mode.presentation + ' --native-plugins none --content-selection vanilla --profile-root "' + $profiles + '"'
                $shortcut.Description = 'OpenNV Release: ' + $game.title + ' in ' + $mode.label + ', using your selected game installation.'
                $shortcut.IconLocation = $executable + ',0'
                $shortcut.WindowStyle = 1
                $shortcut.Save()
                [pscustomobject]@{ shortcut=$path; executable=$executable; mode=$mode.label; game=$game.id; configuration=$toolchain.configuration }
            } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) }
        }
    }
} finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
