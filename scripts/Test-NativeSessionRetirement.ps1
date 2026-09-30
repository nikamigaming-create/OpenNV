[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DataRoot,
    [Parameter(Mandatory = $true)][string]$Checkpoint,
    [string]$Executable = "",
    [ValidateSet("d3d12", "vulkan")][string]$RenderingDriver = "d3d12",
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
$diagnostics = Join-Path $repository "tmp/native-session-retirement"
[IO.Directory]::CreateDirectory($diagnostics) | Out-Null
$results = @()

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

foreach ($scenario in @("continue-reload-quit", "fresh-title-quit", "window-close")) {
    $privateRun = [IO.Path]::GetFullPath((Join-Path $diagnostics ([Guid]::NewGuid().ToString("N"))))
    $inputRoot = Join-Path $privateRun "input"
    [IO.Directory]::CreateDirectory($inputRoot) | Out-Null
    $save = Join-Path $privateRun "save.json"
    if ($scenario -eq "continue-reload-quit") { Copy-Item -LiteralPath $Checkpoint -Destination $save }
    $stdout = Join-Path $diagnostics "$scenario.stdout.log"
    $stderr = Join-Path $diagnostics "$scenario.stderr.log"
    $game = $null
    $sequence = 0L
    try {
        $arguments = @("--rendering-method", "forward_plus", "--rendering-driver", $RenderingDriver,
            "--xr-mode", "off", "--resolution", "1280x720", "--", "--data-root", $DataRoot,
            "--campaign", "fallout-new-vegas", "--save-path", $save, "--live-harness", $inputRoot)
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
        if ($scenario -eq "continue-reload-quit") {
            Press-Button "Continue"
            $null = Wait-State { param($value) $null -ne $value.gameplay.player }
            Send-Input @{ op = "key"; key = "Escape"; pressed = $true; leaseMilliseconds = 50 }
            Press-Button "MAIN MENU"
            Press-Button "YES"
            Press-Button "Continue"
            $null = Wait-State { param($value) $null -ne $value.gameplay.player }
            Send-Input @{ op = "key"; key = "Escape"; pressed = $true; leaseMilliseconds = 50 }
            Press-Button "QUIT GAME"
            Press-Button "YES"
        } elseif ($scenario -eq "fresh-title-quit") {
            # No save exists: the ordinary title path prewarms its initial cell.
            $null = Wait-State { param($value) @($value.controls | Where-Object { $_.text -eq "New" }).Count -eq 1 }
            Press-Button "Quit"
        } else {
            # Process.MainWindowHandle omits hidden test windows. Send the
            # ordinary WM_CLOSE to the observed Godot window owned by this PID.
            $null = Wait-State { param($value) $null -ne $value.process -and
                [OpenNV.Diagnostics.SessionWindow]::Find($game.Id) -ne [IntPtr]::Zero }
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
        $results += [ordered]@{ scenario = $scenario; exitCode = $game.ExitCode; sourceReadersDrained = $true }
        Write-Output "OPENNV_NATIVE_SESSION_CASE_PASS scenario=$scenario exit=0 sourceReaders=drained"
    } finally {
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
    (@{ schema = "opennv-native-session-retirement/1"; scenarios = $results; checkpointUnchanged = $true;
        frameRecording = $false; parity = "unverified" } | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "OPENNV_NATIVE_SESSION_RETIREMENT_OWNED_PASS ordinaryInput=true checkpointUnchanged=true recording=false parity=unverified"
