[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Godot,
    [Parameter(Mandatory = $true)]
    [string]$CellScene,
    [Parameter(Mandatory = $true)]
    [string]$ActorScenes,
    [Parameter(Mandatory = $true)]
    [string]$InstallManifest,
    [Parameter(Mandatory = $true)]
    [string]$SimulatorRuntimeManifest,
    [Parameter(Mandatory = $true)]
    [string]$CapturePreflightScript,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$Configuration = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$JsonDepth = 8

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtime = Join-Path $repository "runtime"
$reportValidator = Join-Path $repository "content\tools\validate_runtime_report.py"
if ([string]::IsNullOrWhiteSpace($Configuration)) {
    $Configuration = Join-Path $runtime "config\open-nv-xr-simulator-driver-v1.json"
}

foreach ($path in @(
    $Godot,
    $CellScene,
    $ActorScenes,
    $InstallManifest,
    $SimulatorRuntimeManifest,
    $CapturePreflightScript,
    $Configuration,
    $reportValidator,
    (Join-Path $runtime "project.godot")
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing OpenXR simulator acceptance input: $path"
    }
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Refusing to overwrite an existing OpenXR proof directory: $OutputDirectory"
}

$contract = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json -Depth $JsonDepth
if ($contract.schema -ne "opennv-openxr-simulator-driver/v1") {
    throw "Unsupported OpenXR simulator driver schema: $($contract.schema)"
}

$cell = Get-Content -LiteralPath $CellScene -Raw | ConvertFrom-Json -Depth $JsonDepth
$proofDoorId = [string]$cell.proof.doorReferenceFormId
$proofDoor = @($cell.references | Where-Object { [string]$_.formId -eq $proofDoorId })
if ($proofDoor.Count -ne 1) {
    throw "Cell scene must contain exactly one proof door: $proofDoorId"
}
$unitScale = [double]$cell.coordinates.unitsToMeters
$doorPosition = @($proofDoor[0].positionGodotUnits)
$doorAimPosition = @($contract.poses.doorAim.positionMeters)
$targetDeltaX = ([double]$doorPosition[0] * $unitScale) - [double]$doorAimPosition[0]
$targetDeltaZ = ([double]$doorPosition[2] * $unitScale) - [double]$doorAimPosition[2]
$dataDerivedDoorYaw = [Math]::Atan2(-$targetDeltaX, -$targetDeltaZ)

& $CapturePreflightScript `
    -Target ([string]$contract.capturePreflight.target) `
    -Scenario ([string]$contract.capturePreflight.scenario) | Out-Host

$simulatorData = Join-Path $OutputDirectory "simulator-data"
New-Item -ItemType Directory -Path $simulatorData | Out-Null
$stdoutPath = Join-Path $OutputDirectory "stdout.log"
$stderrPath = Join-Path $OutputDirectory "stderr.log"
$engineReportPath = Join-Path $OutputDirectory "acceptance.json"
$driverReportPath = Join-Path $OutputDirectory "driver-report.json"
$savePath = Join-Path $OutputDirectory "save.json"
$pidPath = Join-Path $OutputDirectory "pid"

$previousRuntimeManifest = $env:XR_RUNTIME_JSON
$previousSimulatorData = $env:OPENXR_SIMULATOR_DATA_DIR
$gameProcess = $null

function Wait-ForMarker([string]$Path, [string]$Marker, [DateTime]$Deadline) {
    do {
        if ($null -ne $script:gameProcess -and $script:gameProcess.HasExited) {
            throw "Godot exited before marker '$Marker': $(Get-Content -LiteralPath $script:stderrPath -Raw)"
        }
        if ((Test-Path -LiteralPath $Path) -and
            (Select-String -LiteralPath $Path -SimpleMatch $Marker -Quiet)) {
            return
        }
        Start-Sleep -Milliseconds ([int]$contract.ipc.pollMilliseconds)
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "Timed out waiting for OpenXR marker: $Marker"
}

function Send-ControllerState(
    [int]$Hand,
    [object]$Pose,
    [double]$Yaw,
    [double]$Pitch,
    [double]$Trigger,
    [double]$Grip,
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
        yaw = $Yaw
        pitch = $Pitch
        roll = [double]$Pose.rollRadians
        trigger = $Trigger
        grip = $Grip
        buttonA = $ButtonA
        buttonB = $ButtonB
        menu = 0
        thumbstickClick = 0
        thumbstickX = $StickX
        thumbstickY = $StickY
    }
    $commandPath = Join-Path $simulatorData "controller_pose_command.json"
    $temporaryPath = "$commandPath.tmp"
    $acknowledgementPath = Join-Path $simulatorData "command_ack.json"
    if (Test-Path -LiteralPath $acknowledgementPath) {
        Remove-Item -LiteralPath $acknowledgementPath -Force
    }
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($command | ConvertTo-Json -Compress -Depth $JsonDepth),
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $commandPath -Force
    $deadline = [DateTime]::UtcNow.AddSeconds([double]$contract.ipc.commandTimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $acknowledgementPath) {
            return
        }
        if ($gameProcess.HasExited) {
            throw "Godot exited while publishing simulator controller state."
        }
        Start-Sleep -Milliseconds ([int]$contract.ipc.pollMilliseconds)
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "The OpenXR simulator did not acknowledge a controller state."
}

function Send-Pose([int]$Hand, [object]$Pose) {
    Send-ControllerState $Hand $Pose `
        ([double]$Pose.yawRadians) ([double]$Pose.pitchRadians) 0 0 0 0 0 0
}

function Send-Action(
    [int]$Hand,
    [object]$Pose,
    [double]$Trigger,
    [double]$Grip,
    [int]$ButtonA,
    [int]$ButtonB,
    [double]$StickX,
    [double]$StickY
) {
    Send-ControllerState $Hand $Pose `
        ([double]$Pose.yawRadians) ([double]$Pose.pitchRadians) `
        $Trigger $Grip $ButtonA $ButtonB $StickX $StickY
}

try {
    $env:XR_RUNTIME_JSON = [IO.Path]::GetFullPath($SimulatorRuntimeManifest)
    $env:OPENXR_SIMULATOR_DATA_DIR = $simulatorData
    $arguments = @(
        "--path", $runtime,
        "--editor-pid", "0",
        "--xr-mode", "on",
        "--",
        "--cell-scene", ([IO.Path]::GetFullPath($CellScene)),
        "--actor-scenes", ([IO.Path]::GetFullPath($ActorScenes)),
        "--vr",
        "--xr-simulator-proof",
        "--report", $engineReportPath,
        "--save-path", $savePath
    )
    $gameProcess = Start-Process -FilePath $Godot -ArgumentList $arguments `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath `
        -WindowStyle Hidden -PassThru
    [IO.File]::WriteAllText($pidPath, [string]$gameProcess.Id)

    Wait-ForMarker $stdoutPath ([string]$contract.ipc.readyMarker) `
        ([DateTime]::UtcNow.AddSeconds([double]$contract.ipc.readyTimeoutSeconds))

    Send-Pose 0 $contract.poses.leftNeutral
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.neutralSettleMilliseconds)
    Send-Pose 0 $contract.poses.leftTravel
    Send-Pose 1 $contract.poses.rightTravel
    Start-Sleep -Milliseconds ([int]$contract.timing.travelSettleMilliseconds)
    Send-Pose 0 $contract.poses.leftNeutral
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.stickReleaseMilliseconds)

    $doorAccepted = $false
    foreach ($yawOffset in @($contract.doorAim.yawOffsetsRadians)) {
        foreach ($pitch in @($contract.doorAim.pitchCandidatesRadians)) {
            $doorPose = [pscustomobject]@{
                positionMeters = $contract.poses.doorAim.positionMeters
                yawRadians = $dataDerivedDoorYaw + [double]$yawOffset
                pitchRadians = [double]$pitch
                rollRadians = [double]$contract.poses.doorAim.rollRadians
            }
            Send-Action 1 $doorPose 0 ([double]$contract.doorAim.releaseValue) 0 0 0 0
            Start-Sleep -Milliseconds ([int]$contract.timing.doorPoseSettleMilliseconds)
            Send-Action 1 $doorPose 0 ([double]$contract.doorAim.pressValue) 0 0 0 0
            Start-Sleep -Milliseconds ([int]$contract.timing.doorPressMilliseconds)
            Send-Action 1 $doorPose 0 ([double]$contract.doorAim.releaseValue) 0 0 0 0
            Start-Sleep -Milliseconds ([int]$contract.timing.doorReleaseMilliseconds)
            if (Select-String -LiteralPath $stdoutPath `
                -SimpleMatch ([string]$contract.ipc.acceptedActivationMarker) -Quiet) {
                $doorAccepted = $true
                break
            }
        }
        if ($doorAccepted) { break }
    }
    if (-not $doorAccepted) {
        throw "No authored door accepted the simulator squeeze action."
    }

    Send-Pose 1 $contract.poses.rightNeutral
    $move = @($contract.locomotion.leftStick)
    Send-Action 0 $contract.poses.leftNeutral 0 0 0 0 ([double]$move[0]) ([double]$move[1])
    Start-Sleep -Milliseconds ([int]$contract.timing.locomotionMilliseconds)
    Send-Pose 0 $contract.poses.leftNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.stickReleaseMilliseconds)

    foreach ($turn in @($contract.turn.rightStickSteps)) {
        Send-Action 1 $contract.poses.rightNeutral 0 0 0 0 ([double]$turn) 0
        Start-Sleep -Milliseconds ([int]$contract.timing.turnPressMilliseconds)
        Send-Pose 1 $contract.poses.rightNeutral
        Start-Sleep -Milliseconds ([int]$contract.timing.stickReleaseMilliseconds)
    }

    Send-Action 1 $contract.poses.rightNeutral ([double]$contract.actions.pressValue) 0 0 0 0 0
    Start-Sleep -Milliseconds ([int]$contract.timing.actionPressMilliseconds)
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.actionReleaseMilliseconds)

    Send-Action 1 $contract.poses.rightNeutral 0 0 0 1 0 0
    Start-Sleep -Milliseconds ([int]$contract.timing.actionPressMilliseconds)
    Send-Pose 1 $contract.poses.rightNeutral
    Start-Sleep -Milliseconds ([int]$contract.timing.actionReleaseMilliseconds)

    $screenshotRequest = [ordered]@{
        eye = [string]$contract.screenshot.eye
        layer = [string]$contract.screenshot.layer
    }
    [IO.File]::WriteAllText(
        (Join-Path $simulatorData "screenshot_request.json"),
        ($screenshotRequest | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
    $screenshotPath = Join-Path $simulatorData "screenshot.bmp"
    $screenshotDeadline = [DateTime]::UtcNow.AddSeconds([double]$contract.ipc.screenshotTimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $screenshotPath) { break }
        Start-Sleep -Milliseconds ([int]$contract.ipc.pollMilliseconds)
    } while ([DateTime]::UtcNow -lt $screenshotDeadline)
    if (-not (Test-Path -LiteralPath $screenshotPath)) {
        throw "The OpenXR simulator did not produce a native projection screenshot."
    }

    Send-Action 0 $contract.poses.leftNeutral 0 0 1 0 0 0
    if (-not $gameProcess.WaitForExit([int]$contract.ipc.exitTimeoutMilliseconds)) {
        throw "Godot did not finish after every simulator acceptance transition passed."
    }
    if ($gameProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $engineReportPath)) {
        throw "OpenXR simulator acceptance failed: $(Get-Content -LiteralPath $stderrPath -Raw)"
    }
    $engineReport = Get-Content -LiteralPath $engineReportPath -Raw | ConvertFrom-Json -Depth $JsonDepth
    if ($engineReport.schema -ne "opennv-openxr-simulator-acceptance/v1" -or
        $engineReport.status -ne "pass" -or [bool]$engineReport.hardwareHeadsetValidated) {
        throw "The OpenXR simulator acceptance report is not a simulator-only pass."
    }
    & python $reportValidator --mode xr-simulator --report $engineReportPath `
        --install-manifest ([IO.Path]::GetFullPath($InstallManifest))
    if ($LASTEXITCODE -ne 0) {
        throw "The OpenXR simulator acceptance report failed owned-data validation."
    }
    $errorLines = @(
        Get-Content -LiteralPath @($stdoutPath, $stderrPath) |
            Where-Object { $_.StartsWith("ERROR:", [StringComparison]::Ordinal) }
    )
    $unexpectedErrors = @($errorLines | Where-Object {
        $line = $_
        -not @($contract.allowedGodotShutdownDiagnostics).Where({
            $line.StartsWith([string]$_.prefix, [StringComparison]::Ordinal)
        })
    })
    if ($unexpectedErrors.Count -gt 0) {
        throw "Unexpected Godot error diagnostics:`n$($unexpectedErrors -join [Environment]::NewLine)"
    }

    $runtimeManifest = Get-Content -LiteralPath $SimulatorRuntimeManifest -Raw | ConvertFrom-Json -Depth $JsonDepth
    $runtimeDll = [IO.Path]::GetFullPath([string]$runtimeManifest.runtime.library_path)
    $driverReport = [ordered]@{
        schema = "opennv-openxr-simulator-driver/v1"
        status = "pass"
        evidenceLevel = "simulator"
        hardwareValidated = $false
        windowsAppControl = $false
        foregroundInput = $false
        inputTransport = "repo-local-openxr-runtime-file-ipc"
        processId = $gameProcess.Id
        proofDoorReferenceFormId = $proofDoorId
        proofDoorYawRadians = $dataDerivedDoorYaw
        cellSceneSha256 = (Get-FileHash -LiteralPath $CellScene -Algorithm SHA256).Hash.ToLowerInvariant()
        actorScenesSha256 = (Get-FileHash -LiteralPath $ActorScenes -Algorithm SHA256).Hash.ToLowerInvariant()
        installManifestSha256 = (Get-FileHash -LiteralPath $InstallManifest -Algorithm SHA256).Hash.ToLowerInvariant()
        simulatorRuntimeSha256 = (Get-FileHash -LiteralPath $runtimeDll -Algorithm SHA256).Hash.ToLowerInvariant()
        screenshot = $screenshotPath
        screenshotSha256 = (Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
        engineReport = $engineReportPath
        engineReportSha256 = (Get-FileHash -LiteralPath $engineReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        allowedGodotShutdownDiagnostics = @($contract.allowedGodotShutdownDiagnostics)
    }
    [IO.File]::WriteAllText(
        $driverReportPath,
        ($driverReport | ConvertTo-Json -Depth $JsonDepth),
        [Text.UTF8Encoding]::new($false))
    $driverReport
}
finally {
    if ($null -ne $gameProcess -and -not $gameProcess.HasExited) {
        Stop-Process -Id $gameProcess.Id -ErrorAction SilentlyContinue
    }
    $env:XR_RUNTIME_JSON = $previousRuntimeManifest
    $env:OPENXR_SIMULATOR_DATA_DIR = $previousSimulatorData
}
