[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Godot,
    [Parameter(Mandatory = $true)][string]$Scene,
    [Parameter(Mandatory = $true)][string]$SimulatorRuntimeManifest,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$VideoPath,
    [Parameter(Mandatory = $true)][string]$StereoImagePath,
    [Parameter(Mandatory = $true)][string]$SingleEyeImagePath,
    [ValidateRange(24, 160)][int]$FrameCount = 64,
    [ValidateRange(4, 16)][int]$CaptureFps = 8
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$JsonDepth = 10

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtime = Join-Path $repository "runtime"
$paths = @(
    $Godot,
    $Scene,
    $SimulatorRuntimeManifest,
    (Join-Path $runtime "project.godot")
)
foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Fallout OpenXR preview input: $path"
    }
}
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw "ffmpeg is required to assemble the phone-ready proof."
}
if (-not (Get-Command ffprobe -ErrorAction SilentlyContinue)) {
    throw "ffprobe is required to verify the proof output."
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$VideoPath = [IO.Path]::GetFullPath($VideoPath)
$StereoImagePath = [IO.Path]::GetFullPath($StereoImagePath)
$SingleEyeImagePath = [IO.Path]::GetFullPath($SingleEyeImagePath)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Refusing to overwrite the OpenXR preview directory: $OutputDirectory"
}
foreach ($deliveryPath in @($VideoPath, $StereoImagePath, $SingleEyeImagePath)) {
    if (Test-Path -LiteralPath $deliveryPath) {
        throw "Refusing to overwrite an existing delivery artifact: $deliveryPath"
    }
    $deliveryParent = Split-Path -Parent $deliveryPath
    if (-not (Test-Path -LiteralPath $deliveryParent -PathType Container)) {
        New-Item -ItemType Directory -Path $deliveryParent | Out-Null
    }
}

$simulatorData = Join-Path $OutputDirectory "simulator-data"
$frames = Join-Path $OutputDirectory "native-sbs-frames"
New-Item -ItemType Directory -Path $simulatorData -Force | Out-Null
New-Item -ItemType Directory -Path $frames -Force | Out-Null
$stdoutPath = Join-Path $OutputDirectory "stdout.log"
$stderrPath = Join-Path $OutputDirectory "stderr.log"
$simulatorLogPath = Join-Path $OutputDirectory "openxr-simulator.log"
$engineReportPath = Join-Path $OutputDirectory "engine-report.json"
$driverReportPath = Join-Path $OutputDirectory "driver-report.json"
$pidPath = Join-Path $OutputDirectory "pid"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$gameProcess = $null

$previousRuntimeManifest = $env:XR_RUNTIME_JSON
$previousHeadless = $env:OPENXR_SIMULATOR_HEADLESS
$previousSimulatorData = $env:OPENXR_SIMULATOR_DATA_DIR
$previousSimulatorLog = $env:OPENXR_SIMULATOR_LOG_PATH
$previousDesktopPreview = $env:OPENXR_SIMULATOR_DESKTOP_PREVIEW

function Write-AtomicJson([string]$Path, [object]$Value, [int]$Ordinal) {
    $temporaryPath = "$Path.$PID.$Ordinal.tmp"
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($Value | ConvertTo-Json -Compress -Depth $JsonDepth),
        $utf8NoBom)
    [IO.File]::Move($temporaryPath, $Path)
}

function Wait-ForMarker([string]$Marker, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ($null -ne $script:gameProcess -and $script:gameProcess.HasExited) {
            $errors = if (Test-Path -LiteralPath $script:stderrPath) {
                Get-Content -LiteralPath $script:stderrPath -Raw
            } else { "" }
            throw "Godot exited before '$Marker'. $errors"
        }
        if ((Test-Path -LiteralPath $script:stdoutPath) -and
            (Select-String -LiteralPath $script:stdoutPath -SimpleMatch $Marker -Quiet)) {
            return
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for OpenXR preview marker: $Marker"
}

function Publish-HeadPose(
    [double]$X,
    [double]$Y,
    [double]$Z,
    [double]$Yaw,
    [double]$Pitch,
    [double]$Roll,
    [int]$Ordinal
) {
    $commandPath = Join-Path $simulatorData "head_pose_command.json"
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while (Test-Path -LiteralPath $commandPath -PathType Leaf) {
        if ($gameProcess.HasExited) {
            throw "Godot exited while waiting for a head-pose command acknowledgement."
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "OpenXR simulator did not consume a head-pose command."
        }
        Start-Sleep -Milliseconds 5
    }
    Write-AtomicJson $commandPath ([ordered]@{
        x = $X; y = $Y; z = $Z
        yaw = $Yaw; pitch = $Pitch; roll = $Roll
    }) $Ordinal
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while (Test-Path -LiteralPath $commandPath -PathType Leaf) {
        if ($gameProcess.HasExited) {
            throw "Godot exited while publishing a head pose."
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "OpenXR simulator did not acknowledge head pose $Ordinal."
        }
        Start-Sleep -Milliseconds 5
    }
}

function Capture-NativeProjection([int]$Ordinal) {
    $screenshotPath = Join-Path $simulatorData "screenshot.bmp"
    if (Test-Path -LiteralPath $screenshotPath) {
        Remove-Item -LiteralPath $screenshotPath -Force
    }
    $requestPath = Join-Path $simulatorData "screenshot_request.json"
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while (Test-Path -LiteralPath $requestPath -PathType Leaf) {
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "OpenXR simulator did not consume its screenshot request."
        }
        Start-Sleep -Milliseconds 5
    }
    Write-AtomicJson $requestPath ([ordered]@{
        eye = "both"
        layer = "projection"
    }) $Ordinal
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        if (Test-Path -LiteralPath $screenshotPath -PathType Leaf) { break }
        if ($gameProcess.HasExited) {
            throw "Godot exited before producing native projection frame $Ordinal."
        }
        Start-Sleep -Milliseconds 10
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not (Test-Path -LiteralPath $screenshotPath -PathType Leaf)) {
        throw "OpenXR simulator did not produce native projection frame $Ordinal."
    }
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(4)
    do {
        try {
            $stream = [IO.File]::Open(
                $screenshotPath,
                [IO.FileMode]::Open,
                [IO.FileAccess]::Read,
                [IO.FileShare]::None)
            $ready = $stream.Length -gt 54
            $stream.Dispose()
        } catch {
            $ready = $false
        }
        if (-not $ready) { Start-Sleep -Milliseconds 10 }
    } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
    if (-not $ready) {
        throw "Native projection frame $Ordinal never became readable."
    }
    $target = Join-Path $frames ("frame-{0:D4}.bmp" -f $Ordinal)
    Move-Item -LiteralPath $screenshotPath -Destination $target
    return $target
}

function Ease-InOut([double]$Value) {
    $t = [Math]::Max(0.0, [Math]::Min(1.0, $Value))
    return $t * $t * (3.0 - 2.0 * $t)
}

function Pose-ForFrame([int]$Index) {
    $progress = $Index / [double]([Math]::Max(1, $FrameCount - 1))
    $x = 0.045 * [Math]::Sin($progress * 2.0 * [Math]::PI)
    $y = 1.70 + 0.018 * [Math]::Sin($progress * 4.0 * [Math]::PI)
    $z = 0.035 * [Math]::Sin($progress * 2.0 * [Math]::PI + 0.8)
    $yaw = 0.0
    $pitch = -0.03
    $segment = "cave"
    if ($progress -lt 0.12) {
        $yaw = -0.12 + 0.24 * ($progress / 0.12)
        $pitch = -0.04
    } elseif ($progress -lt 0.36) {
        $t = Ease-InOut (($progress - 0.12) / 0.24)
        $yaw = 3.02 * $t
        $pitch = -0.04 + 0.03 * $t
        $segment = "turn-to-vault"
    } elseif ($progress -lt 0.54) {
        $t = ($progress - 0.36) / 0.18
        $yaw = 3.02 + 0.12 * [Math]::Sin($t * 2.0 * [Math]::PI)
        $pitch = -0.04 + 0.10 * [Math]::Sin($t * [Math]::PI)
        $segment = "open-vault-13"
    } elseif ($progress -lt 0.78) {
        $t = Ease-InOut (($progress - 0.54) / 0.24)
        $yaw = 3.02 * (1.0 - $t)
        $pitch = -0.01 - 0.08 * $t
        $segment = "turn-to-cave"
    } else {
        $t = ($progress - 0.78) / 0.22
        $walk = Ease-InOut ([Math]::Min(1.0, $t / 0.72))
        # Walk in the canonical cave-forward direction toward the near rat at
        # tile 19888, then crouch slightly and hold the final view.  This is
        # simulated HMD translation, not a claim of gameplay locomotion.
        $x = 0.54 * $walk
        $y = 1.70 - 0.18 * $walk
        $z = -7.25 * $walk
        $yaw = 0.07 * [Math]::Sin($t * 2.0 * [Math]::PI)
        $pitch = -0.08 - 0.26 * $walk
        $segment = "walk-to-near-rat"
    }
    return [pscustomobject][ordered]@{
        x = $x
        y = $y
        z = $z
        yaw = $yaw
        pitch = $pitch
        roll = 0.012 * [Math]::Sin($progress * 2.0 * [Math]::PI + 1.7)
        segment = $segment
    }
}

try {
    $env:XR_RUNTIME_JSON = [IO.Path]::GetFullPath($SimulatorRuntimeManifest)
    $env:OPENXR_SIMULATOR_HEADLESS = "1"
    $env:OPENXR_SIMULATOR_DATA_DIR = $simulatorData
    $env:OPENXR_SIMULATOR_LOG_PATH = $simulatorLogPath
    Remove-Item Env:OPENXR_SIMULATOR_DESKTOP_PREVIEW -ErrorAction SilentlyContinue

    $arguments = @(
        "--path", $runtime,
        "--editor-pid", "0",
        "--rendering-method", "forward_plus",
        "--rendering-driver", "d3d12",
        "--xr-mode", "on",
        "--",
        "--fo1-hex-scene", ([IO.Path]::GetFullPath($Scene)),
        "--vr",
        "--fo1-xr-simulator-preview",
        "--report", $engineReportPath
    )
    $gameProcess = Start-Process -FilePath $Godot -ArgumentList $arguments `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath `
        -WindowStyle Hidden -PassThru
    [IO.File]::WriteAllText($pidPath, [string]$gameProcess.Id, $utf8NoBom)
    Wait-ForMarker "OPENNV_FO1_XR_SIMULATOR_PREVIEW_READY" 90

    $poses = New-Object 'System.Collections.Generic.List[object]'
    $nativeFrames = New-Object 'System.Collections.Generic.List[string]'
    for ($index = 0; $index -lt $FrameCount; $index++) {
        $pose = Pose-ForFrame $index
        Publish-HeadPose `
            -X $pose.x -Y $pose.y -Z $pose.z `
            -Yaw $pose.yaw -Pitch $pose.pitch -Roll $pose.roll `
            -Ordinal $index
        Start-Sleep -Milliseconds ([Math]::Max(20, [int](500 / $CaptureFps)))
        [void]$nativeFrames.Add((Capture-NativeProjection $index))
        [void]$poses.Add([ordered]@{
            ordinal = $index
            segment = $pose.segment
            x = $pose.x; y = $pose.y; z = $pose.z
            yaw = $pose.yaw; pitch = $pose.pitch; roll = $pose.roll
        })
    }

    $lastFrame = $nativeFrames[$nativeFrames.Count - 1]
    & ffmpeg -hide_banner -loglevel error -y -i $lastFrame `
        -vf "scale=960:-2" -frames:v 1 $StereoImagePath
    if ($LASTEXITCODE -ne 0) { throw "Could not export the final stereo PNG." }
    & ffmpeg -hide_banner -loglevel error -y -i $lastFrame `
        -vf "crop=iw/2:ih:0:0,scale=720:720" -frames:v 1 $SingleEyeImagePath
    if ($LASTEXITCODE -ne 0) { throw "Could not export the final single-eye PNG." }

    $framePattern = Join-Path $frames "frame-%04d.bmp"
    $videoFilter = @(
        "[0:v]scale=960:480:force_original_aspect_ratio=decrease,"
        "pad=960:540:(ow-iw)/2:(oh-ih)/2:color=0x070605,"
        "fps=24,setsar=1,setpts=PTS-STARTPTS[sbs];"
        "[1:v]scale=540:540:force_original_aspect_ratio=decrease,"
        "pad=960:540:(ow-iw)/2:(oh-ih)/2:color=0x070605,"
        "fps=24,setsar=1,setpts=PTS-STARTPTS[eye];"
        "[sbs][eye]concat=n=2:v=1:a=0[outv]"
    ) -join ""
    & ffmpeg -hide_banner -loglevel error -y `
        -framerate $CaptureFps -i $framePattern `
        -loop 1 -t 3.0 -i $SingleEyeImagePath `
        -filter_complex $videoFilter -map "[outv]" `
        -c:v libx264 -profile:v main -level 3.1 -preset medium -crf 23 `
        -pix_fmt yuv420p -color_range tv -movflags +faststart -an $VideoPath
    if ($LASTEXITCODE -ne 0) { throw "Could not assemble the mobile OpenXR preview video." }

    Write-AtomicJson (Join-Path $simulatorData "preview_stop.json") `
        ([ordered]@{ reason = "native-projection-capture-complete" }) ($FrameCount + 1)
    if (-not $gameProcess.WaitForExit(15000)) {
        throw "Godot did not exit after the bounded simulator preview completed."
    }
    if ($gameProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $engineReportPath)) {
        throw "Fallout OpenXR preview failed. $(Get-Content -LiteralPath $stderrPath -Raw)"
    }
    $engineReport = Get-Content -LiteralPath $engineReportPath -Raw | ConvertFrom-Json -Depth $JsonDepth
    if ($engineReport.schema -ne "opennv-fo1-xr-simulator-preview/v1" -or
        $engineReport.status -ne "pass" -or
        [bool]$engineReport.hardwareHeadsetValidated) {
        throw "The engine report is not a bounded simulator-only pass."
    }

    $probe = & ffprobe -v error -select_streams v:0 `
        -show_entries stream=codec_name,profile,width,height,pix_fmt,color_range,r_frame_rate,duration `
        -show_entries format=size,duration -of json $VideoPath
    if ($LASTEXITCODE -ne 0) { throw "The delivered MP4 failed ffprobe verification." }
    $probeObject = $probe | ConvertFrom-Json -Depth $JsonDepth
    $runtimeManifest = Get-Content -LiteralPath $SimulatorRuntimeManifest -Raw | ConvertFrom-Json -Depth $JsonDepth
    $runtimeDll = [IO.Path]::GetFullPath([string]$runtimeManifest.runtime.library_path)
    $driverReport = [ordered]@{
        schema = "opennv-fo1-xr-simulator-capture/v1"
        status = "pass"
        evidenceLevel = "simulator"
        hardwareHeadsetValidated = $false
        windowsAppControlUsed = $false
        foregroundInputInjected = $false
        inputTransport = "repo-local-openxr-runtime-file-ipc"
        projectionLayer = "native-both-eye"
        frameCount = $FrameCount
        captureFps = $CaptureFps
        singleEyeEndingSeconds = 3.0
        sceneSha256 = (Get-FileHash -LiteralPath $Scene -Algorithm SHA256).Hash.ToLowerInvariant()
        simulatorRuntimeSha256 = (Get-FileHash -LiteralPath $runtimeDll -Algorithm SHA256).Hash.ToLowerInvariant()
        video = $VideoPath
        videoSha256 = (Get-FileHash -LiteralPath $VideoPath -Algorithm SHA256).Hash.ToLowerInvariant()
        stereoImage = $StereoImagePath
        stereoImageSha256 = (Get-FileHash -LiteralPath $StereoImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        singleEyeImage = $SingleEyeImagePath
        singleEyeImageSha256 = (Get-FileHash -LiteralPath $SingleEyeImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        engineReport = $engineReportPath
        engineReportSha256 = (Get-FileHash -LiteralPath $engineReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        videoProbe = $probeObject
        headPoses = @($poses.ToArray())
    }
    [IO.File]::WriteAllText(
        $driverReportPath,
        ($driverReport | ConvertTo-Json -Depth $JsonDepth),
        $utf8NoBom)
    $driverReport
}
finally {
    if ($null -ne $gameProcess -and -not $gameProcess.HasExited) {
        Stop-Process -Id $gameProcess.Id -ErrorAction SilentlyContinue
    }
    $env:XR_RUNTIME_JSON = $previousRuntimeManifest
    $env:OPENXR_SIMULATOR_HEADLESS = $previousHeadless
    $env:OPENXR_SIMULATOR_DATA_DIR = $previousSimulatorData
    $env:OPENXR_SIMULATOR_LOG_PATH = $previousSimulatorLog
    $env:OPENXR_SIMULATOR_DESKTOP_PREVIEW = $previousDesktopPreview
}
