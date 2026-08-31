[CmdletBinding()]
param(
    [string]$HexScene = '',
    [Parameter(Mandatory = $true)]
    [string]$ClassicHumanoidInstallManifest
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$classicHumanoidPreflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
$classicHumanoidResolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
$classicHumanoidDonorPreviewSet = & $classicHumanoidResolver -InstallManifest $ClassicHumanoidInstallManifest
if (-not $?) { throw 'Classic humanoid install-manifest resolution failed.' }
& $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
if (-not $?) { throw 'Classic humanoid donor preflight failed.' }
if ([string]::IsNullOrWhiteSpace($HexScene)) {
    $cache = Join-Path $env:LOCALAPPDATA 'OpenNV\cache\fallout1'
    $candidates = if (Test-Path -LiteralPath $cache -PathType Container) {
        @(Get-ChildItem -LiteralPath $cache -Filter 'hex-scene.json' -File -Recurse |
            Where-Object { $_.Directory.Name -eq 'scene-current' })
    } else {
        @()
    }
    if ($candidates.Count -ne 1) {
        throw 'Pass -HexScene explicitly unless exactly one owned Fallout 1 scene manifest exists.'
    }
    $HexScene = $candidates[0].FullName
}

$scene = Get-Content -LiteralPath $HexScene -Raw | ConvertFrom-Json
if ($scene.schema -ne 'opennv-fo1-hex-scene/v1' -or
    $scene.status -ne 'interactive-hex-topology-proof') {
    throw 'Owned Fallout 1 player donor contract is incomplete or changed.'
}

$recipePath = Join-Path $repository 'content\recipes\fo1-character-start-v1.json'
$recipe = Get-Content -LiteralPath $recipePath -Raw | ConvertFrom-Json
$premades = @($recipe.source.premadeCharacters)
if (($premades.id -join ',') -ne 'max-stone,natalia,albert' -or
    $premades.Where({ $_.id -eq 'natalia' }).Count -ne 1) {
    throw 'Owned Fallout 1 premade identity order or Natalia identity changed.'
}

$cacheRoot = Split-Path -Parent (Split-Path -Parent $HexScene)
$characterManifestPath = Join-Path $cacheRoot 'character-start\character-start.json'
$characterManifest = Get-Content -LiteralPath $characterManifestPath -Raw |
    ConvertFrom-Json
$decodedPremades = @($characterManifest.characterPicker.premadeCharacters)
$decodedIdentity = $decodedPremades | ForEach-Object {
    '{0}:{1}:{2}' -f $_.id, $_.profile.name, $_.profile.sex
}
if (($decodedIdentity -join ',') -ne
    'max-stone:Max Stone:Male,natalia:Natalia:Female,albert:Albert:Male' -or
    $decodedPremades.Count -ne 3 -or
    $decodedPremades.Where({ $_.gcdSha256.Length -ne 64 }).Count -ne 0 -or
    $decodedPremades.Where({ $_.sourcePortraitFrmSha256.Length -ne 64 }).Count -ne 0) {
    throw 'Decoded owned Fallout 1 premade identity/sex/provenance changed.'
}
$policy = $decodedPremades | ForEach-Object {
    '{0}:owned-donor' -f $_.id
}
if (($policy -join ',') -ne
    'max-stone:owned-donor,natalia:owned-donor,albert:owned-donor') {
    throw 'Fallout 1 premade gameplay presentation policy changed.'
}

function Get-CSharpSourceModule([string] $entrypoint) {
    $entry = Get-Item -LiteralPath $entrypoint
    $sources = Get-ChildItem -LiteralPath $entry.DirectoryName -Filter (
        '{0}*.cs' -f $entry.BaseName) | Sort-Object -Property FullName
    if ($sources.FullName -notcontains $entry.FullName) {
        throw "C# source module entrypoint is missing: $entrypoint"
    }
    return ($sources | Get-Content -Raw) -join "`n"
}

$flow = Get-CSharpSourceModule (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1NewGameFlow.cs')
$session = Get-CSharpSourceModule (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1TacticalSession.cs')
$preview = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1PremadePlayerPreview.cs') -Raw
$profile = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1CharacterProfile.cs') -Raw
$creator = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1CharacterCreator.cs') -Raw
$customEditor = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1CustomAppearanceEditor.cs') -Raw
$customPortrait = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1CustomPortraitPreview.cs') -Raw
foreach ($required in @(
    'ApplyCharacter(profile)',
    'owned-premade-gcd-bio-frm',
    'WeaponAttachmentsBound',
    'identity = Identity.Report()',
    'Fo1CharacterIdentity.ExpectedSchema',
    'RestoreSavedPlayerPresentationIfReady',
    '"max-stone" => (Name: "Max Stone", Sex: "Male", Role: "combat")',
    '"natalia" => (Name: "Natalia", Sex: "Female", Role: "stealth")',
    '"albert" => (Name: "Albert", Sex: "Male", Role: "diplomat")',
    'EditingLocked',
    'InspectPremade',
    'actorSupportsWeapons && !_meleeWeaponEquipped',
    'actorSupportsWeapons && _meleeWeaponEquipped',
    'WeaponVisualsSuppressed',
    'Equals(first.Appearance, second.Appearance)',
    '"opennv-fo1-character/v3"',
    'appearance = Appearance?.Report()',
    'requires male and female owned donors',
    'no substitute humanoid geometry'
)) {
    if (-not (($flow + $session + $preview + $profile + $creator + $customEditor).Contains($required))) {
        throw "Fallout 1 player presentation wiring marker is missing: $required"
    }
}
if ($creator.Contains('ModifyPremade') -or
    $session.Contains('playerPresentationIdentity =')) {
    throw 'Fallout 1 premade editing or split character-save state was reintroduced.'
}
if (-not $customEditor.Contains('internal bool Live3DVisible => false;') -or
    -not $customEditor.Contains('always a 2D cartoon projection') -or
    -not $customPortrait.Contains('owned-data custom donor') -or
    -not $customPortrait.Contains('never replaces authored premade art')) {
    throw 'Fallout 1 custom 2D owned-donor portrait boundary is no longer explicit.'
}

Write-Output (
    'OPENNV_FO1_PLAYER_PRESENTATION_STATIC_PASS donorPreviewSet={0} premades={1} policy={2} coldIdentity=character-embedded-v1 customAppearance=persisted-v3-owned-donor-cartoon-2d' -f
        ([IO.Path]::GetFullPath($classicHumanoidDonorPreviewSet)),
        ($premades.id -join ','),
        ($policy -join ','))
