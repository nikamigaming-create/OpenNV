[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PreviewSet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$PreviewSetSchemaV3 = 'opennv-owned-player-facegen-preview-set/v3'
$PreviewSetSchemaV4 = 'opennv-owned-player-facegen-preview-set/v4'
$PreviewSetStatusV3 =
    'compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-all-native-geometry-controls-runtime-bound'
$PreviewSetStatusV4 =
    'compiled-default-custom-and-six-classic-premade-full-body-analogs-runtime-bound'
$ActorSidecarSchema = 'opennv-actor-gltf/v4'
$ActorSidecarStatus = 'skinned-animated'
$RequiredSexes = @('male', 'female')
$RequiredBodyRoles = @('body', 'left-hand', 'right-hand')
$RequiredAnalogBodyRoles = @('outfit-0', 'left-hand', 'right-hand')
$RequiredAnalogKeys = @(
    'fallout1:max-stone',
    'fallout1:natalia',
    'fallout1:albert',
    'fallout2:combat',
    'fallout2:stealth',
    'fallout2:diplomat')
$Sha256Pattern = '^[0-9a-fA-F]{64}$'
$FormIdPattern = '^[0-9a-fA-F]{8}$'

function Require-String([object]$Value, [string]$Label) {
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Classic humanoid donor field is empty: $Label"
    }
    return $text
}

function Require-Hash([object]$Value, [string]$Label) {
    $hash = Require-String $Value $Label
    if ($hash -notmatch $Sha256Pattern) {
        throw "Classic humanoid donor hash is invalid: $Label"
    }
    return $hash.ToLowerInvariant()
}

function Assert-HashBoundFile([string]$Path, [string]$ExpectedHash, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Classic humanoid donor $Label is missing: $Path"
    }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedHash) {
        throw "Classic humanoid donor $Label hash mismatch: $Path"
    }
}

$resolvedPreviewSet = [IO.Path]::GetFullPath($PreviewSet)
if (-not (Test-Path -LiteralPath $resolvedPreviewSet -PathType Leaf)) {
    throw "Classic humanoid donor preview set is missing: $resolvedPreviewSet"
}
$manifest = Get-Content -LiteralPath $resolvedPreviewSet -Raw | ConvertFrom-Json -Depth 32
$expectedStatus = switch ([string]$manifest.schema) {
    $PreviewSetSchemaV3 { $PreviewSetStatusV3; break }
    $PreviewSetSchemaV4 { $PreviewSetStatusV4; break }
    default { '' }
}
if ([string]::IsNullOrWhiteSpace($expectedStatus) -or
    $manifest.status -ne $expectedStatus -or
    -not [bool]$manifest.fullBody -or
    (@($manifest.bodyComponentRoles) -join ',') -ne ($RequiredBodyRoles -join ',') -or
    (Require-String $manifest.presentationOutfitFormId 'presentationOutfitFormId') -notmatch $FormIdPattern -or
    [string]::IsNullOrWhiteSpace([string]$manifest.playerFormId)) {
    throw "Unexpected classic humanoid donor preview set: $resolvedPreviewSet"
}

$bodySourcesBySex = $manifest.bodyComponentSourcesBySex
foreach ($sex in $RequiredSexes) {
    $bodySourceProperty = $bodySourcesBySex.PSObject.Properties[$sex]
    if ($null -eq $bodySourceProperty) {
        throw 'Classic humanoid donor sex variants are incomplete or duplicated.'
    }
    $modules = @($bodySourceProperty.Value)
    if ($modules.Count -ne $RequiredBodyRoles.Count -or
        (@($modules | ForEach-Object { $_.role }) -join ',') -ne ($RequiredBodyRoles -join ',')) {
        throw "Classic humanoid donor body/outfit join is incomplete for $sex"
    }
    foreach ($module in $modules) {
        foreach ($property in @('modelSha256', 'diffuseSha256', 'normalSha256')) {
            [void](Require-Hash $module.$property "$sex/$($module.role)/$property")
        }
        if ([int]$module.retainedSurfaceCount -lt 1) {
            throw "Classic humanoid donor retained surface count is invalid for $sex/$($module.role)"
        }
    }

    $rows = @($manifest.previews | Where-Object { $_.sex -eq $sex })
    if ($rows.Count -ne 1) {
        throw "Classic humanoid donor has no unique $sex preview variant"
    }
    $outputs = $rows[0].outputs
    $modelPath = [IO.Path]::GetFullPath((Require-String $outputs.gltf "$sex/gltf"))
    $sidecarPath = [IO.Path]::GetFullPath((Require-String $outputs.sidecar "$sex/sidecar"))
    Assert-HashBoundFile $modelPath (Require-Hash $outputs.gltfSha256 "$sex/gltfSha256") "$sex model"
    Assert-HashBoundFile $sidecarPath (Require-Hash $outputs.sidecarSha256 "$sex/sidecarSha256") "$sex sidecar"
    $sidecar = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json -Depth 32
    if ($sidecar.schema -ne $ActorSidecarSchema -or
        $sidecar.status -ne $ActorSidecarStatus -or
        [string]::IsNullOrWhiteSpace([string]$sidecar.skeleton.rigidAttachmentNode)) {
        throw "Classic humanoid donor socket contract is incomplete for $sex"
    }
    foreach ($module in $modules) {
        $matchingSurfaces = @($sidecar.surfaces | Where-Object { $_.role -eq $module.role })
        if ($matchingSurfaces.Count -ne [int]$module.retainedSurfaceCount) {
            throw "Classic humanoid donor body surface contract differs for $sex/$($module.role)"
        }
    }
}

if (@($manifest.previews).Count -ne $RequiredSexes.Count -or
    @($bodySourcesBySex.PSObject.Properties).Count -ne $RequiredSexes.Count) {
    throw 'Classic humanoid donor sex variants are incomplete or duplicated.'
}

if ($manifest.schema -eq $PreviewSetSchemaV4) {
    $analogRows = @($manifest.premadeAnalogs)
    $analogKeys = @($analogRows | ForEach-Object {
            '{0}:{1}' -f ([string]$_.campaign).ToLowerInvariant(), ([string]$_.characterId).ToLowerInvariant()
        } | Sort-Object)
    if ($analogRows.Count -ne $RequiredAnalogKeys.Count -or
        ($analogKeys -join ',') -ne (($RequiredAnalogKeys | Sort-Object) -join ',')) {
        throw 'Classic humanoid donor premade analog roster is incomplete or duplicated.'
    }
    foreach ($analog in $analogRows) {
        $key = '{0}:{1}' -f $analog.campaign, $analog.characterId
        if ((@($analog.bodyRoles) -join ',') -ne ($RequiredAnalogBodyRoles -join ',') -or
            (Require-String $analog.sourceActorFormId "$key/sourceActorFormId") -notmatch $FormIdPattern -or
            (Require-String $analog.outfitFormId "$key/outfitFormId") -notmatch $FormIdPattern -or
            [string]$analog.sex -notin @('male', 'female')) {
            throw "Classic humanoid donor premade analog identity is invalid: $key"
        }
        $outputs = $analog.outputs
        $modelPath = [IO.Path]::GetFullPath((Require-String $outputs.gltf "$key/gltf"))
        $sidecarPath = [IO.Path]::GetFullPath((Require-String $outputs.sidecar "$key/sidecar"))
        Assert-HashBoundFile $modelPath (Require-Hash $outputs.gltfSha256 "$key/gltfSha256") "$key model"
        Assert-HashBoundFile $sidecarPath (Require-Hash $outputs.sidecarSha256 "$key/sidecarSha256") "$key sidecar"
        $sidecar = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json -Depth 32
        if ($sidecar.schema -ne $ActorSidecarSchema -or
            $sidecar.status -ne $ActorSidecarStatus -or
            [string]$sidecar.actorFormId -ne [string]$analog.sourceActorFormId -or
            [int]$analog.coverage.surfaces -ne @($sidecar.surfaces).Count -or
            [int]$analog.coverage.textures -ne @($sidecar.textures).Count -or
            [int]$analog.coverage.animations -ne @($sidecar.animations).Count -or
            -not (@($sidecar.animations.logicalPath) -match 'idle') -or
            -not (@($sidecar.animations.logicalPath) -match 'forward') -or
            [string]$sidecar.skeleton.rigidAttachmentNode -ne [string]$analog.rigidAttachmentNode -or
            [string]$analog.equipmentSocketNode -ne 'Bip01 R Hand') {
            throw "Classic humanoid donor premade analog coverage is invalid: $key"
        }
        foreach ($role in $RequiredAnalogBodyRoles) {
            if (@($sidecar.surfaces | Where-Object { $_.role -eq $role }).Count -lt 1) {
                throw "Classic humanoid donor premade analog has no $role surface: $key"
            }
        }
        foreach ($field in @('height', 'chest', 'shoulders', 'waist', 'arms', 'thighs', 'calves')) {
            $number = [double]$analog.bodyProfile.$field
            if (-not [double]::IsFinite($number) -or $number -lt 0.70 -or $number -gt 1.35) {
                throw "Classic humanoid donor premade analog body field is invalid: $key/$field"
            }
        }
    }
}

Write-Output "OPENNV_CLASSIC_HUMANOID_DONOR_PRECHECK_PASS manifest=$resolvedPreviewSet"
