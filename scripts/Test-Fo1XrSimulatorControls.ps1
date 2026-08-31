[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Godot,
    [Parameter(Mandatory = $true)]
    [string]$Scene,
    [Parameter(Mandatory = $true)]
    [string]$SimulatorRuntimeManifest,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ClassicHumanoidInstallManifest,
    [string]$Configuration = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$MinimumRuntimeReadySeconds = 180.0
$JsonDepth = 8
$Utf8NoBom = [Text.UTF8Encoding]::new($false)

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtime = Join-Path $repository "runtime"
$classicHumanoidPreflight = Join-Path $PSScriptRoot 'Assert-ClassicHumanoidDonorPreviewSet.ps1'
$classicHumanoidResolver = Join-Path $PSScriptRoot 'Resolve-ClassicHumanoidDonorPreviewSet.ps1'
if ([string]::IsNullOrWhiteSpace($Configuration)) {
    $Configuration = Join-Path $runtime "config\open-nv-xr-simulator-driver-v1.json"
}
$classicHumanoidDonorPreviewSet = & $classicHumanoidResolver -InstallManifest $ClassicHumanoidInstallManifest
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid install-manifest resolution failed.' }
& $classicHumanoidPreflight -PreviewSet $classicHumanoidDonorPreviewSet
if ($LASTEXITCODE -ne 0) { throw 'Classic humanoid donor preflight failed.' }
foreach ($path in @(
    $Godot,
    $Scene,
    $SimulatorRuntimeManifest,
    $Configuration,
    (Join-Path $runtime "project.godot")
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Fallout 1 OpenXR simulator input: $path"
    }
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Refusing to overwrite an existing Fallout 1 OpenXR proof directory: $OutputDirectory"
}
$contract = Get-Content -LiteralPath $Configuration -Raw |
    ConvertFrom-Json -Depth $JsonDepth
if ($contract.schema -ne "opennv-openxr-simulator-driver/v1") {
    throw "Unsupported OpenXR simulator driver schema: $($contract.schema)"
}

$simulatorData = Join-Path $OutputDirectory "simulator-data"
New-Item -ItemType Directory -Path $simulatorData | Out-Null
$stdoutPath = Join-Path $OutputDirectory "stdout.log"
$stderrPath = Join-Path $OutputDirectory "stderr.log"
$reportPath = Join-Path $OutputDirectory "acceptance.json"
$savePath = Join-Path $OutputDirectory "save.json"
$previousRuntimeManifest = $env:XR_RUNTIME_JSON
$previousSimulatorData = $env:OPENXR_SIMULATOR_DATA_DIR
$previousSimulatorHeadless = $env:OPENXR_SIMULATOR_HEADLESS
$previousSimulatorLog = $env:OPENXR_SIMULATOR_LOG_PATH
$gameProcess = $null

function Wait-ForMarker([string]$Marker, [DateTime]$Deadline) {
    do {
        if ($null -ne $script:gameProcess -and $script:gameProcess.HasExited) {
            throw "Godot exited before marker '$Marker': $(Get-Content -LiteralPath $script:stderrPath -Raw)"
        }
        if ((Test-Path -LiteralPath $script:stdoutPath) -and
            (Select-String -LiteralPath $script:stdoutPath -SimpleMatch $Marker -Quiet)) {
            return
        }
        Start-Sleep -Milliseconds ([int]$script:contract.ipc.pollMilliseconds)
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "Timed out waiting for Fallout 1 OpenXR marker: $Marker"
}

function Send-ControllerState(
    [int]$Hand,
    [object]$Pose,
    [double]$Trigger,
    [int]$ButtonA,
    [int]$ButtonB,
    [double]$StickX,
    [double]$StickY
) {
    $command = [ordered]@{
        hand = $Hand
        posX = [double]$Pose.positionMeters[0]
        posY = [double]$Pose.positionMeters[1]
        posZ = [double]$Pose.positionMeters[2]
        yaw = [double]$Pose.yawRadians
        pitch = [double]$Pose.pitchRadians
        roll = [double]$Pose.rollRadians
        trigger = $Trigger
        grip = 0.0
        buttonA = $ButtonA
        buttonB = $ButtonB
        menu = 0
        thumbstickClick = 0
        thumbstickX = $StickX
        thumbstickY = $StickY
    }
    $commandPath = Join-Path $script:simulatorData "controller_pose_command.json"
    $temporaryPath = "$commandPath.tmp"
    $acknowledgementPath = Join-Path $script:simulatorData "command_ack.json"
    if (Test-Path -LiteralPath $acknowledgementPath) {
        Remove-Item -LiteralPath $acknowledgementPath -Force
    }
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($command | ConvertTo-Json -Compress -Depth $JsonDepth),
        $Utf8NoBom)
    Move-Item -LiteralPath $temporaryPath -Destination $commandPath -Force
    $deadline = [DateTime]::UtcNow.AddSeconds([double]$contract.ipc.commandTimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $acknowledgementPath) { return }
        if ($gameProcess.HasExited) {
            throw "Godot exited while publishing Fallout 1 simulator controller state."
        }
        Start-Sleep -Milliseconds ([int]$contract.ipc.pollMilliseconds)
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "The OpenXR simulator did not acknowledge a controller state."
}

function Send-Pose([int]$Hand, [object]$Pose) {
    Send-ControllerState $Hand $Pose 0.0 0 0 0.0 0.0
}

try {
    $env:XR_RUNTIME_JSON = [IO.Path]::GetFullPath($SimulatorRuntimeManifest)
    $env:OPENXR_SIMULATOR_DATA_DIR = $simulatorData
    $env:OPENXR_SIMULATOR_HEADLESS = "1"
    $env:OPENXR_SIMULATOR_LOG_PATH = Join-Path $OutputDirectory "simulator.log"
    Remove-Item Env:OPENXR_SIMULATOR_DESKTOP_PREVIEW -ErrorAction SilentlyContinue

    $arguments = @(
        "--path", $runtime,
        "--editor-pid", "0",
        "--rendering-method", "forward_plus",
        "--rendering-driver", "d3d12",
        "--xr-mode", "on",
        "--",
        "--fo1-hex-scene", ([IO.Path]::GetFullPath($Scene)),
        "--classic-humanoid-donor-preview-set", ([IO.Path]::GetFullPath($classicHumanoidDonorPreviewSet)),
        "--vr",
        "--fo1-xr-simulator-preview",
        "--fo1-xr-controls-proof",
        "--report", $reportPath,
        "--save-path", $savePath
    )
    $gameProcess = Start-Process -FilePath $Godot -ArgumentList $arguments `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath `
        -WindowStyle Hidden -PassThru
    Wait-ForMarker "OPENNV_FO1_XR_SIMULATOR_PREVIEW_READY" `
        ([DateTime]::UtcNow.AddSeconds(
            [Math]::Max($MinimumRuntimeReadySeconds, [double]$contract.ipc.readyTimeoutSeconds)))

    Send-Pose 0 $contract.poses.leftNeutral
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.neutralSettleMilliseconds)
    Send-Pose 0 $contract.poses.leftTravel
    Send-Pose 1 $contract.poses.rightTravel
    Start-Sleep -Milliseconds ([int]$contract.timing.travelSettleMilliseconds)
    Send-Pose 0 $contract.poses.leftNeutral
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.stickReleaseMilliseconds)

    $move = @($contract.locomotion.leftStick)
    Send-ControllerState 0 $contract.poses.leftNeutral 0.0 0 0 `
        ([double]$move[0]) ([double]$move[1])
    Start-Sleep -Milliseconds ([int]$contract.timing.locomotionMilliseconds)
    Send-Pose 0 $contract.poses.leftNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.stickReleaseMilliseconds)

    foreach ($turn in @($contract.turn.rightStickSteps)) {
        Send-ControllerState 1 $contract.poses.rightNeutral 0.0 0 0 ([double]$turn) 0.0
        Start-Sleep -Milliseconds ([int]$contract.timing.turnPressMilliseconds)
        Send-Pose 1 $contract.poses.rightNeutral
        Start-Sleep -Milliseconds ([int]$contract.timing.stickReleaseMilliseconds)
    }

    Send-ControllerState 1 $contract.poses.rightNeutral `
        ([double]$contract.actions.pressValue) 0 0 0.0 0.0
    Start-Sleep -Milliseconds ([int]$contract.timing.actionPressMilliseconds)
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.actionReleaseMilliseconds)

    Send-ControllerState 1 $contract.poses.rightNeutral 0.0 0 1 0.0 0.0
    Start-Sleep -Milliseconds ([int]$contract.timing.actionPressMilliseconds)
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.actionReleaseMilliseconds)

    Send-ControllerState 0 $contract.poses.leftNeutral 0.0 1 0 0.0 0.0
    Start-Sleep -Milliseconds ([int]$contract.timing.actionPressMilliseconds)
    Send-Pose 0 $contract.poses.leftNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.actionReleaseMilliseconds)

    [IO.File]::WriteAllText(
        (Join-Path $simulatorData "preview_stop.json"),
        '{"reason":"fo1-shared-controls-proof-complete"}',
        $Utf8NoBom)
    if (-not $gameProcess.WaitForExit([int]$contract.ipc.exitTimeoutMilliseconds)) {
        throw "Godot did not exit after the Fallout 1 simulator proof completed."
    }
    if ($gameProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) {
        $failureReport = if (Test-Path -LiteralPath $reportPath) {
            Get-Content -LiteralPath $reportPath -Raw
        } else {
            "report missing"
        }
        throw "Fallout 1 OpenXR controls proof failed. Report: $failureReport"
    }

    $report = Get-Content -LiteralPath $reportPath -Raw |
        ConvertFrom-Json -Depth $JsonDepth
    if ($report.schema -ne "opennv-fo1-xr-simulator-preview/v2" -or
        $report.status -ne "pass" -or
        -not [bool]$report.controllerInputValidated -or
        [bool]$report.doorActivationValidated -or
        [bool]$report.handsValidated -or
        [bool]$report.heldWeaponValidated -or
        [bool]$report.wristHudValidated -or
        [bool]$report.physicalHeadsetValidated) {
        throw "Fallout 1 OpenXR report did not preserve the bounded simulator claim."
    }
    $report
}
finally {
    if ($null -ne $gameProcess -and -not $gameProcess.HasExited) {
        Stop-Process -Id $gameProcess.Id -ErrorAction SilentlyContinue
    }
    $env:XR_RUNTIME_JSON = $previousRuntimeManifest
    $env:OPENXR_SIMULATOR_DATA_DIR = $previousSimulatorData
    $env:OPENXR_SIMULATOR_HEADLESS = $previousSimulatorHeadless
    $env:OPENXR_SIMULATOR_LOG_PATH = $previousSimulatorLog
}
