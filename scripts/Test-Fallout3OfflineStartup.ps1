[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DataRoot,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$Godot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ManagedToolchain.ps1')
Initialize-OpenNVManagedToolchain
if ($Godot) {
    . (Join-Path $PSScriptRoot 'GodotToolchain.ps1')
    $null = Assert-OpenNVGodotVersion -Executable $Godot
}
$offlineRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$offlineProject = Join-Path $offlineRepository 'contract-tests/ReferenceScriptContractProbe/ReferenceScriptContractProbe.csproj'
$offlineSlotsProject = Join-Path $offlineRepository 'contract-tests/RuntimeSaveSlotContractProbe/RuntimeSaveSlotContractProbe.csproj'
$offlineData = [IO.Path]::GetFullPath($DataRoot)
if (-not (Test-Path -LiteralPath $offlineData -PathType Container)) { throw 'Fallout 3 Data folder does not exist.' }

& dotnet build $offlineProject --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Offline startup probe build failed.' }
& dotnet build $offlineSlotsProject --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Offline save-slot probe build failed.' }

$offlineChecks = [Collections.Generic.List[object]]::new()
function Invoke-Fallout3OfflineCheck {
    param([string]$Name, [string]$Project, [string[]]$ProbeArguments, [string]$Evidence,
        [switch]$AllowHeadlessInputBoundary)
    $offlineOutput = @(& dotnet run --project $Project --configuration $Configuration --no-build -- @ProbeArguments)
    $offlineCode = $LASTEXITCODE
    $offlineOutput | ForEach-Object { Write-Output $_ }
    $offlineStatus = if ($offlineCode -eq 0) { 'passed' } else { 'failed' }
    $offlineBlocker = $null
    $offlineReports = @($offlineOutput | Where-Object { $_ -cmatch '^OPENNV_OWNED_PLAYER_SCENE_ENTRY_(PASS|BLOCKED) ' })
    if ($offlineReports.Count -eq 1) {
        $offlineEntry = ($offlineReports[0] -replace '^OPENNV_OWNED_PLAYER_SCENE_ENTRY_(PASS|BLOCKED) ', '') | ConvertFrom-Json
        $offlineBlocker = $offlineEntry.blocker
        # This exception is exact and diagnostic only. A failed native getter,
        # missing owned file, fixture failure or later Player child stays failed.
        if ($AllowHeadlessInputBoundary -and $offlineCode -eq 1 -and
            $offlineEntry.schema -ceq 'opennv-offline-player-scene-entry/v10' -and
            $offlineEntry.liveInputBoundary -eq $true -and
            $offlineEntry.blocker -ceq 'source-Player-scene-current-input-manager-product-factory-unbound' -and
            $offlineEntry.failedAt -ceq 'InputManagerQuery' -and $offlineEntry.phase -ceq 'Failed' -and
            $offlineEntry.gameLaunched -eq $false -and $offlineEntry.framesRecorded -eq $false) {
            $offlineStatus = 'expected-live-input-boundary'
        }
    }
    $offlineChecks.Add([pscustomobject]@{ name = $Name; status = $offlineStatus;
        exitCode = $offlineCode; blocker = $offlineBlocker; evidence = $Evidence })
}

# Independent checks continue after a failure, so one broken production child
# does not conceal unrelated format, persistence or cold-binding failures.
Invoke-Fallout3OfflineCheck 'source-images-and-cold-bindings' $offlineProject @('--test-source-main-images') 'authored contracts'
Invoke-Fallout3OfflineCheck 'source-menu-image-capture-and-restore' $offlineProject @('--source-image-menu-contracts') 'shared C# manager factory/list and two unadvanced source clock updates, source receiver fields, form-wide removal, failed prefix and cold state; native world capture and final pixels unexecuted'
Invoke-Fallout3OfflineCheck 'headless-shared-input' $offlineProject @('--headless-input-contracts') 'explicit windowless provider, shared control/script/native input, committed callback failures and retirement; campaign progression unexecuted'
Invoke-Fallout3OfflineCheck 'headless-hdr-and-renderer-handoff' $offlineProject @('--test-source-sky-transfer') 'headless declares no native renderer/resources; shader draws refuse; cold reconstruction rebinds the selected native renderer'
Invoke-Fallout3OfflineCheck 'renderer-clock-and-cold-state' $offlineProject @('--renderer-clock-contracts') 'all source update modes, retained remainder, signed rates, failure and serialized cold state'
Invoke-Fallout3OfflineCheck 'player-reference-hud-and-cold-state' $offlineProject @('--player-reference-hud-contracts') 'actual source template instances, equality arm, byte stores, partial failure and cold receipt; input-manager byte fixture does not certify HWND/device construction'
Invoke-Fallout3OfflineCheck 'player-current-thread-context' $offlineProject @('--player-thread-context-contracts') 'actual calling thread, separate loaded/current kind, empty selected initializer range, prior-kind restoration and repeated invocations; blocked bodies do not issue an exit'
Invoke-Fallout3OfflineCheck 'player-current-process-queries' $offlineProject @('--player-process-query-contracts') 'actual common process fields, class/epoch/lease, independent nullable getters and cold history; nonnull equipment/animation writers remain unowned'
Invoke-Fallout3OfflineCheck 'compiled-script-structure-and-state' $offlineProject @('--compiled-structure-contracts') 'qualified assignment, result ranges, source scene flag and cold shared state'
Invoke-Fallout3OfflineCheck 'compiled-trigger-callbacks-and-cold-state' $offlineProject @('--compiled-trigger-contracts') 'original binary enter/leave/contact blocks, fixed and live reference filters, reserved player identity, shared action context and retained failed prefixes; native contact parity remains unverified'
Invoke-Fallout3OfflineCheck 'trigger-membership-and-cold-state' $offlineProject @('--trigger-membership-contracts') 'shared ordered reference membership, atomic cold restoration without callback replay and committed failure prefixes; source physical contact production remains unowned'
Invoke-Fallout3OfflineCheck 'compiled-event-membership-and-instruction-index' $offlineProject @('--compiled-event-index-contracts') 'reference-identity event ownership and original ordered instruction slices; linear construction and constant-time lookup, foreign-event refusal and cold reconstruction'
Invoke-Fallout3OfflineCheck 'compiled-headless-screen-blood' $offlineProject @('--compiled-headless-screen-blood-contracts') 'original compiled effect, validated CPU DDS input, shared source count/random/geometry and menu-paused lifetime, failed-resource prefix and cold no-replay; rendered pixels unexecuted'
Invoke-Fallout3OfflineCheck 'selected-sound-data-namespace' $offlineProject @('--sound-search-namespace-contracts') 'winning read-only layered DATA files, independent installation auxiliary input, actual archive byte selection and cold source-drift refusal; layered loose native enumeration remains explicitly unowned'
Invoke-Fallout3OfflineCheck 'script-save-serialization' $offlineProject @('--script-save-contracts') 'authored contracts'
Invoke-Fallout3OfflineCheck 'ordered-save-requests' $offlineProject @('--unified-save-order-contracts') 'authored contracts'
Invoke-Fallout3OfflineCheck 'manual-save-preparation' $offlineProject @('--manual-save-preparation-contracts') 'authored contracts'
Invoke-Fallout3OfflineCheck 'player-cell-and-cold-state' $offlineProject @('--test-source-main-player-cell') 'authored contracts'
Invoke-Fallout3OfflineCheck 'failed-production-children-refuse-saves' $offlineProject @('--test-source-main-product-refusals') 'production refusal contracts'
Invoke-Fallout3OfflineCheck 'save-slot-activation-and-rollback' $offlineSlotsProject @() 'filesystem and serialization contracts'
Invoke-Fallout3OfflineCheck 'winning-image-profiles' $offlineProject @('--audit-source-main-images', $offlineData, 'fallout-3') 'owned-data format audit'
Invoke-Fallout3OfflineCheck 'owned-source-sound-startup' $offlineProject @('--audit-owned-sound-startup', $offlineData, 'fallout-3') 'actual selected installation settings, auxiliary/configured source archive registration and cold startup without replaying menu callers; native audio voices and gameplay unexecuted'
Invoke-Fallout3OfflineCheck 'winning-idle-package-fields' $offlineProject @('--audit-owned-idle-package-fields', $offlineData) 'owned package selection/constructor/getter declaration and all winning PACK kind/idle-collection fields; actual native package factory requires a live campaign'
Invoke-Fallout3OfflineCheck 'player-status-values-and-cold-state' $offlineProject @('--audit-owned-player-status-values', $offlineData) 'actual owned descriptor/default/getter associations, separate source-effect and mutable pools, Float32 current value and cold reconstruction; ordinary gameplay remains unexecuted'
Invoke-Fallout3OfflineCheck 'vault-and-megaton-route-preflight' $offlineProject @('--audit-owned-fo3-playable-route', $offlineData) 'owned exit/entrance records and shared door cold state; ordinary travel and complete saved campaign remain unexecuted'
Invoke-Fallout3OfflineCheck 'all-winning-compiled-script-layouts' $offlineProject @('--audit-owned-compiled-framing-offline', $offlineData, 'fallout-3') 'owned-data layout audit; semantic execution unexecuted'
Invoke-Fallout3OfflineCheck 'all-winning-compiled-trigger-headers' $offlineProject @('--audit-owned-compiled-trigger-headers', $offlineData, 'fallout-3') 'original winning SCDA/SCRO/SCRV header bindings; count and layout evidence cannot certify quest execution'
Invoke-Fallout3OfflineCheck 'compiled-package-callbacks' $offlineProject @('--compiled-package-contracts') 'required winning SCRO PACK filters, ordered actor event membership, null action/caller and failed-prefix/cold state; SCRV cached-reference lifetime remains unowned'
Invoke-Fallout3OfflineCheck 'all-winning-compiled-package-headers' $offlineProject @('--audit-owned-compiled-package-headers', $offlineData, 'fallout-3') 'original winning SCDA/SCRO PACK bindings; authored header framing does not execute package lifecycles'
Invoke-Fallout3OfflineCheck 'owned-headless-screen-blood' $offlineProject @('--audit-owned-headless-screen-blood', $offlineData, 'fallout-3') 'selected executable command declaration, installation enable setting, winning original DDS inputs and shared effect state without GPU resources; whole campaign and final pixels unverified'
Invoke-Fallout3OfflineCheck 'production-main-clock-tail' $offlineProject @('--audit-owned-production-main-tail', $offlineData) 'production Main callbacks; native and Player fixture boundaries remain explicit'
Invoke-Fallout3OfflineCheck 'player-scene-virtual-entry' $offlineProject @('--audit-owned-player-scene-entry', $offlineData) 'production C# Main/Player CELL and Player virtual; actual clock delivery/shared child release; live input requires the product window' -AllowHeadlessInputBoundary
Invoke-Fallout3OfflineCheck 'player-scene-entry-with-authored-device-bytes' $offlineProject @('--audit-owned-player-scene-entry-with-input-fixture', $offlineData) 'same production callers and actual owned-data actor/common process; device bytes are an explicit fixture, expose the following failed Player child and cannot certify physical input'

if ($Godot) {
    if ($Configuration -ne 'Debug' -or -not (Test-Path -LiteralPath $Godot -PathType Leaf)) {
        throw 'Native HUD projection check requires the Debug project and an existing Godot executable.'
    }
    & $Godot --headless --path (Join-Path $offlineRepository 'runtime') 'res://tools/NativePlayerReferenceHudAudit/NativePlayerReferenceHudAudit.tscn' -- $offlineData
    $offlineChecks.Add([pscustomobject]@{ name = 'native-headless-player-hud-projection';
        status = $(if ($LASTEXITCODE -eq 0) { 'passed' } else { 'failed' }); exitCode = $LASTEXITCODE; blocker = $null;
        evidence = 'actual native HUD, source art/fonts and shared C# traits; vitals input and color writer are explicit fixture boundaries' })
    & $Godot --headless --path (Join-Path $offlineRepository 'runtime') 'res://tools/NativePlayerIdleModelAudit/NativePlayerIdleModelAudit.tscn' -- $offlineData
    $offlineChecks.Add([pscustomobject]@{ name = 'native-headless-player-idle-model';
        status = $(if ($LASTEXITCODE -eq 0) { 'passed' } else { 'failed' }); exitCode = $LASTEXITCODE; blocker = $null;
        evidence = 'actual source first-person assembly, independent animation factory, current posed skin spheres and retired/fresh lifetimes; campaign/input/cold Continue unexecuted' })
}

$offlineFailures = @($offlineChecks | Where-Object { $_.status -eq 'failed' })
$offlineLiveBoundaries = @($offlineChecks | Where-Object { $_.status -eq 'expected-live-input-boundary' })
Write-Output ('OPENNV_FO3_OFFLINE_REPORT ' + ([pscustomobject]@{
    schema = 'opennv-fo3-offline-checks/v2'
    status = $(if ($offlineFailures.Count -ne 0) { 'blocked' } elseif ($offlineLiveBoundaries.Count -ne 0) { 'offline-components-passed-live-input-required' } else { 'components-passed' })
    failedChecks = $offlineFailures.Count
    expectedLiveBoundaries = $offlineLiveBoundaries.Count
    checks = $offlineChecks.ToArray()
    gameLaunched = $false
    framesRecorded = $false
    wholeCampaignStartup = 'UNEXECUTED'
    saveQuitColdContinue = 'UNEXECUTED'
    physicalInputAndRendering = 'UNEXECUTED'
} | ConvertTo-Json -Depth 6 -Compress))
if ($offlineFailures.Count -ne 0) {
    Write-Host "OPENNV_FO3_OFFLINE_STARTUP_BLOCKED failed=$($offlineFailures.Count); do not launch a gameplay acceptance run."
    exit 1
}
Write-Host 'OPENNV_FO3_OFFLINE_STARTUP_COMPONENTS_PASS whole-campaign/save/reload/input/rendering remain unexecuted.'
