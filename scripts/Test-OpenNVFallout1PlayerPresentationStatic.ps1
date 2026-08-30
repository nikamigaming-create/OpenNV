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
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid install-manifest resolution failed.' }
& $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid donor preflight failed.' }
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

$flow = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1NewGameFlow.cs') -Raw
$session = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1TacticalSession.cs') -Raw
$preview = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1PremadePlayerPreview.cs') -Raw
$profile = Get-Content -LiteralPath (
    Join-Path $repository 'runtime\src\Campaigns\Fallout1\Fo1CharacterProfile.cs') -Raw
foreach ($required in @(
    'ApplyCharacter(profile, creator.SelectedPremade)',
    'owned-premade-gcd-frm',
    'WeaponAttachmentsBound',
    'playerPresentationIdentity = _playerPresentationBinding?.Identity.SaveState()',
    'Fo1PlayerPresentationIdentity.Load',
    'RestoreSavedPlayerPresentationIfReady',
    'CharacterId == "max-stone" && CharacterName != "Max Stone"',
    'CharacterId == "natalia" && CharacterName != "Natalia"',
    'CharacterId == "albert" && CharacterName != "Albert"',
    'identity.CharacterId is "max-stone" or "albert"',
    'actorSupportsWeapons && !_meleeWeaponEquipped',
    'actorSupportsWeapons && _meleeWeaponEquipped',
    'WeaponVisualsSuppressed',
    'ApplyAppearance(profile.Appearance)',
    'appearance_mode',
    'Equals(first.Appearance, second.Appearance)',
    '"opennv-fo1-character/v2"',
    'appearance = Appearance?.Report()',
    'Natalia identity is exact owned GCD/portrait FRM',
    'requires male and female owned donors',
    'no substitute humanoid geometry'
)) {
    if (-not (($flow + $session + $preview + $profile).Contains($required))) {
        throw "Fallout 1 player presentation wiring marker is missing: $required"
    }
}

Write-Output (
    'OPENNV_FO1_PLAYER_PRESENTATION_STATIC_PASS donorPreviewSet={0} premades={1} policy={2} coldIdentity=v1 customAppearance=character-v2' -f
        ([IO.Path]::GetFullPath($classicHumanoidDonorPreviewSet)),
        ($premades.id -join ','),
        ($policy -join ','))
