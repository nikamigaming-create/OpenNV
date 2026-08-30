[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PreviewSet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$PreviewSetSchema = 'opennv-owned-player-facegen-preview-set/v3'
$PreviewSetStatus =
    'compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-all-native-geometry-controls-runtime-bound'
$ActorSidecarSchema = 'opennv-actor-gltf/v4'
$ActorSidecarStatus = 'skinned-animated'
$RequiredSexes = @('male', 'female')
$RequiredBodyRoles = @('body', 'left-hand', 'right-hand')
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
if ($manifest.schema -ne $PreviewSetSchema -or
    $manifest.status -ne $PreviewSetStatus -or
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

Write-Output "OPENNV_CLASSIC_HUMANOID_DONOR_PRECHECK_PASS manifest=$resolvedPreviewSet"
