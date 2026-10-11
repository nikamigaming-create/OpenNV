[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DataRoot,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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
    param([string]$Name, [string]$Project, [string[]]$ProbeArguments, [string]$Evidence)
    & dotnet run --project $Project --configuration $Configuration --no-build -- @ProbeArguments
    $offlineChecks.Add([pscustomobject]@{ name = $Name; exitCode = $LASTEXITCODE; evidence = $Evidence })
}

# Independent checks continue after a failure, so one broken production child
# does not conceal unrelated format, persistence or cold-binding failures.
Invoke-Fallout3OfflineCheck 'source-images-and-cold-bindings' $offlineProject @('--test-source-main-images') 'authored contracts'
Invoke-Fallout3OfflineCheck 'script-save-serialization' $offlineProject @('--script-save-contracts') 'authored contracts'
Invoke-Fallout3OfflineCheck 'ordered-save-requests' $offlineProject @('--unified-save-order-contracts') 'authored contracts'
Invoke-Fallout3OfflineCheck 'manual-save-preparation' $offlineProject @('--manual-save-preparation-contracts') 'authored contracts'
Invoke-Fallout3OfflineCheck 'player-cell-and-cold-state' $offlineProject @('--test-source-main-player-cell') 'authored contracts'
Invoke-Fallout3OfflineCheck 'failed-production-children-refuse-saves' $offlineProject @('--test-source-main-product-refusals') 'production refusal contracts'
Invoke-Fallout3OfflineCheck 'save-slot-activation-and-rollback' $offlineSlotsProject @() 'filesystem and serialization contracts'
Invoke-Fallout3OfflineCheck 'winning-image-profiles' $offlineProject @('--audit-source-main-images', $offlineData, 'fallout-3') 'owned-data format audit'
Invoke-Fallout3OfflineCheck 'all-winning-compiled-script-layouts' $offlineProject @('--audit-owned-compiled-framing-offline', $offlineData, 'fallout-3') 'owned-data layout audit; semantic execution unexecuted'
Invoke-Fallout3OfflineCheck 'production-main-clock-tail' $offlineProject @('--audit-owned-production-main-tail', $offlineData) 'production Main callbacks; native and Player fixture boundaries remain explicit'

$offlineFailures = @($offlineChecks | Where-Object { $_.exitCode -ne 0 })
Write-Output ('OPENNV_FO3_OFFLINE_REPORT ' + ([pscustomobject]@{
    schema = 'opennv-fo3-offline-checks/v1'
    status = $(if ($offlineFailures.Count -eq 0) { 'components-passed' } else { 'blocked' })
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
