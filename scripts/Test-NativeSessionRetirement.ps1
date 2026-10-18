[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DataRoot,
    [Parameter(Mandatory = $true)][string]$Checkpoint,
    [string]$Executable = "",
    [string]$OpenXrSimulatorRuntime = "",
    [switch]$Launcher,
    [ValidateSet("d3d12", "vulkan")][string]$RenderingDriver = "d3d12",
    [ValidateSet("continue-reload-quit", "continue-window-close", "fresh-title-quit", "window-close", "loading-window-close", "index-window-close", "activation-index-window-close", "radio-index-window-close", "navigation-index-window-close")]
    [string[]]$Cases = @("continue-reload-quit", "fresh-title-quit", "window-close"),
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (!$Executable) { $Executable = Join-Path $repository "tmp/development-runtime/windows/OpenNV.exe" }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$DataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$Checkpoint = (Resolve-Path -LiteralPath $Checkpoint).Path
$originalHash = (Get-FileHash -LiteralPath $Checkpoint -Algorithm SHA256).Hash
$xrSimulator = ![string]::IsNullOrWhiteSpace($OpenXrSimulatorRuntime)
if ($xrSimulator) {
    $OpenXrSimulatorRuntime = (Resolve-Path -LiteralPath $OpenXrSimulatorRuntime).Path
    if ($RenderingDriver -ne "d3d12" -or @($Cases | Where-Object { $_ -notin @("continue-window-close", "window-close") }).Count -ne 0) {
        throw "The XR simulator draw-retirement check requires D3D12 and explicit continue-window-close/window-close cases."
    }
}
if ($Launcher -and @($Cases | Where-Object { $_ -ne "continue-window-close" }).Count -ne 0) {
    throw "The launcher retirement check requires the explicit continue-window-close case."
}
$diagnostics = Join-Path $repository "tmp/native-session-retirement"
[IO.Directory]::CreateDirectory($diagnostics) | Out-Null
$results = @()
$auditRun = [Guid]::NewGuid().ToString("N")
[IO.File]::WriteAllText((Join-Path $diagnostics "report.json"),
    (@{ schema = "opennv-native-session-retirement/1"; run = $auditRun; status = "running";
        scenarios = @(); frameRecording = $false; overallXrAccepted = $false } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

if ($null -eq ("OpenNV.Diagnostics.SessionWindow" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
namespace OpenNV.Diagnostics {
    public static class SessionWindow {
        private delegate bool Callback(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        public static IntPtr Find(uint processId) {
            var found = IntPtr.Zero;
            EnumWindows((window, data) => {
                GetWindowThreadProcessId(window, out var owner);
                if (owner != processId) return true;
                var name = new StringBuilder(256); var title = new StringBuilder(256);
                GetClassName(window, name, 256); GetWindowText(window, title, 256);
                if (name.ToString() == "Engine" && title.ToString() == "OpenNV") found = window;
                return true;
            }, IntPtr.Zero);
            return found;
        }
        public static bool RequestClose(IntPtr window) => window != IntPtr.Zero &&
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }
}
'@
}

foreach ($scenario in $Cases) {
    $privateRun = [IO.Path]::GetFullPath((Join-Path $diagnostics ([Guid]::NewGuid().ToString("N"))))
    $inputRoot = Join-Path $privateRun "input"
    [IO.Directory]::CreateDirectory($inputRoot) | Out-Null
    $save = Join-Path $privateRun "save.json"
    if ($scenario -in @("continue-reload-quit", "continue-window-close", "loading-window-close", "index-window-close", "activation-index-window-close", "radio-index-window-close", "navigation-index-window-close")) { Copy-Item -LiteralPath $Checkpoint -Destination $save }
    $stdout = Join-Path $diagnostics "$scenario.stdout.log"
    $stderr = Join-Path $diagnostics "$scenario.stderr.log"
    $game = $null
    $sequence = 0L
    $beforeExitDrawErrors = 0
    $xrEnvironment = @{}
    try {
        if ($xrSimulator) {
            foreach ($name in @("XR_RUNTIME_JSON", "OPENXR_SIMULATOR_DATA_DIR", "OPENXR_SIMULATOR_HEADLESS", "OPENXR_SIMULATOR_LOG_PATH", "OPENXR_SIMULATOR_DESKTOP_PREVIEW")) {
                $xrEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
            }
            [Environment]::SetEnvironmentVariable("XR_RUNTIME_JSON", $OpenXrSimulatorRuntime, "Process")
            [Environment]::SetEnvironmentVariable("OPENXR_SIMULATOR_DATA_DIR", (Join-Path $privateRun "simulator"), "Process")
            [Environment]::SetEnvironmentVariable("OPENXR_SIMULATOR_HEADLESS", "1", "Process")
            [Environment]::SetEnvironmentVariable("OPENXR_SIMULATOR_LOG_PATH", (Join-Path $diagnostics "$scenario.simulator.log"), "Process")
            [Environment]::SetEnvironmentVariable("OPENXR_SIMULATOR_DESKTOP_PREVIEW", $null, "Process")
        }
        $xrMode = if ($xrSimulator) { "on" } else { "off" }
        $arguments = @("--rendering-method", "forward_plus", "--rendering-driver", $RenderingDriver,
            "--xr-mode", $xrMode, "--resolution", "1280x720", "--", "--data-root", $DataRoot,
            "--campaign", "fallout-new-vegas", "--save-path", $save, "--live-harness", $inputRoot)
        if ($xrSimulator) { $arguments += @("--presentation", "openxr") }
        if ($Launcher) {
            $profileRoot = Join-Path $privateRun "profile"
            [IO.Directory]::CreateDirectory((Join-Path $profileRoot "launcher")) | Out-Null
            $installation = if ((Split-Path $DataRoot -Leaf) -ieq "Data") { Split-Path $DataRoot -Parent } else { $DataRoot }
            @{ schema = "opennv-godot-launcher-profiles/v1"; selectedGame = "newvegas"; profiles = @{ newvegas = @{
                campaignId = "newvegas"; installRoot = $installation; savePath = $save; enabledMods = @(); automaticModOrder = $true
            } } } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $profileRoot "launcher/profiles-v1.json")
            $presentation = if ($xrSimulator) { "openxr" } else { "first-person" }
            $arguments = @("--rendering-method", "forward_plus", "--rendering-driver", $RenderingDriver,
                "--xr-mode", $xrMode, "--resolution", "1280x720", "--", "--launcher", "--launcher-game", "newvegas",
                "--presentation", $presentation, "--profile-root", $profileRoot, "--live-harness", $inputRoot,
                "--native-plugins", "none", "--content-selection", "vanilla")
        }
        $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
        $game = Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $null = $game.Handle

        function Read-State {
            $path = Join-Path $inputRoot "live-state.json"
            if (!(Test-Path -LiteralPath $path)) { return $null }
            $stream = [IO.File]::Open($path, "Open", "Read", ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try { $reader = [IO.StreamReader]::new($stream); return $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $stream.Dispose() }
        }
        function Read-RunningOutput([string]$path = $stdout) {
            $stream = [IO.File]::Open($path, "Open", "Read", ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try { $reader = [IO.StreamReader]::new($stream); return $reader.ReadToEnd() }
            finally { $stream.Dispose() }
        }
        function Wait-State([scriptblock]$predicate) {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            while ([DateTime]::UtcNow -lt $deadline) {
                $game.Refresh()
                if ($game.HasExited) { throw "$scenario exited early with $($game.ExitCode)." }
                try { $state = Read-State }
                catch [IO.IOException] { $state = $null }
                if ($null -ne $state -and (& $predicate $state)) { return $state }
                Start-Sleep -Milliseconds 100
            }
            throw "$scenario timed out waiting for ordinary session state."
        }
        function Send-Input([hashtable]$command) {
            $script:sequence++
            $path = Join-Path $inputRoot ("{0:D10}.command" -f $script:sequence)
            [IO.File]::WriteAllText($path + ".tmp", ($command | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath ($path + ".tmp") -Destination $path
        }
        function Press-Button([string]$text) {
            $state = Wait-State { param($value) @($value.controls | Where-Object { $_.text -eq $text }).Count -eq 1 }
            $control = @($state.controls | Where-Object { $_.text -eq $text })[0]
            Send-Input @{ op = "button"; path = $control.path }
        }
        function Wait-WorldReady {
            # Player construction precedes the final LOD/loading publication.
            # Input delivered in that interval is correctly blocked by loading.
            $ready = Wait-State { param($value) $null -ne $value.gameplay.player -and
                $null -eq $value.gameplay.loading -and $null -eq $value.gameplay.loadingFeedback }
            $readyDraw = $ready.drawCount
            $null = Wait-State { param($value) $value.drawCount -ge $readyDraw + 2 -and
                (!$xrSimulator -or $value.gameplay.xr.calibrated) }
        }
        if ($scenario -eq "continue-reload-quit") {
            Press-Button "Continue"
            Wait-WorldReady
            Send-Input @{ op = "key"; key = "Escape"; pressed = $true; leaseMilliseconds = 50 }
            Press-Button "MAIN MENU"
            Press-Button "YES"
            Press-Button "Continue"
            Wait-WorldReady
            Send-Input @{ op = "key"; key = "Escape"; pressed = $true; leaseMilliseconds = 50 }
            Press-Button "QUIT GAME"
            Press-Button "YES"
        } elseif ($scenario -eq "continue-window-close") {
            Press-Button "Continue"
            Wait-WorldReady
            $beforeExitDrawErrors = ([regex]::Matches((Read-RunningOutput $stderr),
                'rb->get_view_count\(\) != p_camera_data->view_count|expected: 2D, actual: 2DArray')).Count
            if (![OpenNV.Diagnostics.SessionWindow]::RequestClose([OpenNV.Diagnostics.SessionWindow]::Find($game.Id))) {
                throw "The loaded native window rejected its close request."
            }
        } elseif ($scenario -in @("index-window-close", "activation-index-window-close", "radio-index-window-close", "navigation-index-window-close")) {
            $indexMarker = switch ($scenario) {
                "activation-index-window-close" { "OPENNV_NATIVE_SOURCE_INDEX_PREPARING stage=activation-parents" }
                "radio-index-window-close" { "OPENNV_NATIVE_SOURCE_INDEX_PREPARING stage=radio-stations" }
                "navigation-index-window-close" { "OPENNV_NATIVE_SOURCE_INDEX_PREPARING stage=saved-navigation" }
                default { "OPENNV_NATIVE_SCRIPT_INDEX_PREPARING" }
            }
            $null = Wait-State { param($value) (Read-RunningOutput).Contains($indexMarker) }
            if (![OpenNV.Diagnostics.SessionWindow]::RequestClose([OpenNV.Diagnostics.SessionWindow]::Find($game.Id))) {
                throw "The indexing window rejected its close request."
            }
        } elseif ($scenario -eq "loading-window-close") {
            Press-Button "Continue"
            $null = Wait-State { param($value) (Read-RunningOutput).Contains("OPENNV_NATIVE_NPC_PREPARING") }
            if (![OpenNV.Diagnostics.SessionWindow]::RequestClose([OpenNV.Diagnostics.SessionWindow]::Find($game.Id))) {
                throw "The loading window rejected its close request."
            }
        } elseif ($scenario -eq "fresh-title-quit") {
            # No save exists: the ordinary title path prewarms its initial cell.
            $null = Wait-State { param($value) @($value.controls | Where-Object { $_.text -eq "New" }).Count -eq 1 }
            Press-Button "Quit"
        } else {
            # Process.MainWindowHandle omits hidden test windows. Send the
            # ordinary WM_CLOSE to the observed Godot window owned by this PID.
            $opened = Wait-State { param($value) $null -ne $value.process -and
                [OpenNV.Diagnostics.SessionWindow]::Find($game.Id) -ne [IntPtr]::Zero }
            if ($xrSimulator) {
                $openedDraw = $opened.drawCount
                $null = Wait-State { param($value) $value.drawCount -ge $openedDraw + 2 }
                $beforeExitDrawErrors = ([regex]::Matches((Read-RunningOutput $stderr),
                    'rb->get_view_count\(\) != p_camera_data->view_count|expected: 2D, actual: 2DArray')).Count
            }
            if (![OpenNV.Diagnostics.SessionWindow]::RequestClose([OpenNV.Diagnostics.SessionWindow]::Find($game.Id))) {
                throw "The native window rejected its close request."
            }
        }
        if (!$game.WaitForExit($TimeoutSeconds * 1000)) { throw "$scenario did not finish native shutdown." }
        $output = [IO.File]::ReadAllText($stdout)
        $errors = [IO.File]::ReadAllText($stderr)
        if ($game.ExitCode -ne 0 -or $output -notmatch "OPENNV_NATIVE_SESSION_QUIT.*sourceReaders=drained" -or
            $errors -match 'RID[s]? of type "(?:Compute|Shader|Sampler)" (?:was|were) leaked|OPENNV_NATIVE_SESSION_QUIT_FAILURE') {
            throw "$scenario failed retirement: exit=$($game.ExitCode); inspect $stdout and $stderr."
        }
        if ($scenario -eq "loading-window-close" -and
            ($output -notmatch "OPENNV_NATIVE_MENU_LOAD_CANCELLED" -or
                $errors -match "OPENNV_NATIVE_LOAD_REJECTED|ObjectDisposedException")) {
            throw "Cancelled loading became a load failure or outlived its source handles; inspect $stdout and $stderr."
        }
        $completedIndex = switch ($scenario) {
            "activation-index-window-close" { "OPENNV_NATIVE_SOURCE_INDEX_READY stage=activation-parents" }
            "radio-index-window-close" { "OPENNV_NATIVE_SOURCE_INDEX_READY stage=radio-stations" }
            "navigation-index-window-close" { "OPENNV_NATIVE_SOURCE_INDEX_READY stage=saved-navigation" }
            default { "OPENNV_NATIVE_SCRIPT_INDEX_READY" }
        }
        if ($scenario -in @("index-window-close", "activation-index-window-close", "radio-index-window-close", "navigation-index-window-close") -and
            ($output -notmatch "OPENNV_NATIVE_MENU_INDEX_CANCELLED" -or
                $output.Contains($completedIndex) -or
                $errors -match "OPENNV_NATIVE_LOAD_REJECTED|ObjectDisposedException")) {
            throw "Cancelled indexing published an incomplete result or outlived its source handles; inspect $stdout and $stderr."
        }
        $results += [ordered]@{ scenario = $scenario; exitCode = $game.ExitCode; sourceReadersDrained = $true }
        if ($xrSimulator) {
            $drawErrors = ([regex]::Matches($errors,
                'rb->get_view_count\(\) != p_camera_data->view_count|expected: 2D, actual: 2DArray')).Count
            $profileLeaks = ([regex]::Matches($output + $errors, "InteractionProfileE' were leaked at exit")).Count
            $results[-1]["startupDrawErrors"] = $beforeExitDrawErrors
            $results[-1]["exitDrawErrors"] = $drawErrors - $beforeExitDrawErrors
            $results[-1]["interactionProfileLeaks"] = $profileLeaks
            $results[-1]["overallXrAccepted"] = $false
            Write-Output "OPENNV_NATIVE_XR_DRAW_RETIREMENT_RESULT scenario=$scenario startupDrawErrors=$beforeExitDrawErrors exitDrawErrors=$($drawErrors - $beforeExitDrawErrors) interactionProfileLeaks=$profileLeaks overallXrAccepted=false"
            if ($drawErrors -ne $beforeExitDrawErrors) {
                throw "XR retirement added draw errors; inspect $stdout and $stderr."
            }
        }
        Write-Output "OPENNV_NATIVE_SESSION_CASE_PASS scenario=$scenario exit=0 sourceReaders=drained"
    } finally {
        foreach ($name in $xrEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $xrEnvironment[$name], "Process") }
        if ($null -ne $game -and !$game.HasExited) {
            # Only failed diagnostics use forced cleanup; it never counts as a pass.
            Stop-Process -Id $game.Id -Force
            $game.WaitForExit()
        }
        $resolvedRun = [IO.Path]::GetFullPath($privateRun)
        $allowedRoot = [IO.Path]::GetFullPath($diagnostics) + [IO.Path]::DirectorySeparatorChar
        if (!$resolvedRun.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing cleanup outside the private session audit directory."
        }
        Remove-Item -LiteralPath $resolvedRun -Recurse -Force
        if ((Get-FileHash -LiteralPath $Checkpoint -Algorithm SHA256).Hash -ne $originalHash) {
            throw "The read-only checkpoint changed during the session audit."
        }
    }
}
[IO.File]::WriteAllText((Join-Path $diagnostics "report.json"),
    (@{ schema = "opennv-native-session-retirement/1"; run = $auditRun; status = "complete"; scenarios = $results; checkpointUnchanged = $true;
        frameRecording = $false; xrSimulator = $xrSimulator; overallXrAccepted = $false; parity = "unverified" } | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
if ($xrSimulator) {
    Write-Output "OPENNV_NATIVE_XR_DRAW_RETIREMENT_OWNED_PASS ordinaryClose=true checkpointUnchanged=true recording=false overallXrAccepted=false"
} else {
    Write-Output "OPENNV_NATIVE_SESSION_RETIREMENT_OWNED_PASS ordinaryInput=true checkpointUnchanged=true recording=false parity=unverified"
}
