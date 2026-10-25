[CmdletBinding()]
param(
    [string]$DataRoot = "D:\SteamLibrary\steamapps\common\Fallout New Vegas",
    [string]$SavePath = "local\nick-play-20261025\profiles\newvegas\courier-v1.json",
    [ValidateSet("continue", "new")][string]$Entry = "continue",
    [ValidateSet("Release", "Debug")][string]$Configuration = "Debug",
    [string]$Godot = "",
    [switch]$Headless,
    [ValidateRange(10, 86400)][int]$DurationSeconds = 600,
    [string]$HarnessDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'GodotToolchain.ps1')
. (Join-Path $PSScriptRoot 'ManagedToolchain.ps1')
Initialize-OpenNVManagedToolchain

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtime = Join-Path $repository "runtime"

if ([string]::IsNullOrWhiteSpace($Godot)) {
    $Godot = 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
}
if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) {
    throw "Godot executable is missing: $Godot"
}

$DataRoot = [IO.Path]::GetFullPath($DataRoot)
if (-not (Test-Path -LiteralPath $DataRoot)) {
    throw "Data root does not exist: $DataRoot"
}

$resolvedSave = Join-Path $repository $SavePath
if (-not (Test-Path -LiteralPath $resolvedSave)) {
    if ($Entry -eq "continue") {
        Write-Warning "Save file not found at $resolvedSave; falling back to entry='new'"
        $Entry = "new"
    }
}

if ([string]::IsNullOrWhiteSpace($HarnessDirectory)) {
    $HarnessDirectory = Join-Path $repository "tmp\campaign-bot-live"
}
$HarnessDirectory = [IO.Path]::GetFullPath($HarnessDirectory)
[IO.Directory]::CreateDirectory($HarnessDirectory) | Out-Null

# Clean up stale commands and receipts in harness directory
Get-ChildItem -LiteralPath $HarnessDirectory -Filter "*.command" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $HarnessDirectory -Filter "*.receipt.json" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $HarnessDirectory -Filter "*.tmp" -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path -LiteralPath (Join-Path $HarnessDirectory "live-state.json")) {
    Remove-Item -LiteralPath (Join-Path $HarnessDirectory "live-state.json") -Force
}

$env:DOTNET_ROOT = 'D:\code\gd\dotnet'
$env:DOTNET_ROOT_X64 = 'D:\code\gd\dotnet'
$env:PATH = "D:\code\gd\dotnet;$env:PATH"

$solution = Join-Path $runtime "OpenNV.sln"
Write-Host "Building OpenNV solution ($Configuration)..."
& dotnet build $solution --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV solution build failed." }

Write-Host "=========================================="
Write-Host " OpenNV Autonomous Campaign Bot"
Write-Host " Configuration: $Configuration"
Write-Host " Entry:         $Entry"
Write-Host " Save:          $resolvedSave"
Write-Host " Data root:     $DataRoot"
Write-Host " Harness:       $HarnessDirectory"
Write-Host " Duration:      $DurationSeconds s"
Write-Host " Headless:      $Headless"
Write-Host "=========================================="

$godotArgs = @('--path', $runtime)
if ($Headless) {
    $godotArgs += '--headless'
}
$godotArgs += @(
    '--',
    '--data-root', $DataRoot,
    '--campaign', 'fallout-new-vegas',
    '--save-path', $resolvedSave,
    '--opening-menu',
    '--live-harness', $HarnessDirectory
)
if ($Headless) {
    $godotArgs += '--headless-live'
}

$stdout = Join-Path $HarnessDirectory "stdout.log"
$stderr = Join-Path $HarnessDirectory "stderr.log"
if (Test-Path -LiteralPath $stdout) { Remove-Item -LiteralPath $stdout -Force }
if (Test-Path -LiteralPath $stderr) { Remove-Item -LiteralPath $stderr -Force }

$quoted = $godotArgs | ForEach-Object { '"' + $_ + '"' }
$windowStyle = if ($Headless) { [Diagnostics.ProcessWindowStyle]::Hidden } else { [Diagnostics.ProcessWindowStyle]::Normal }

Write-Host "Starting OpenNV process..."
$game = Start-Process -FilePath $Godot -ArgumentList $quoted -WindowStyle $windowStyle -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$null = $game.Handle

$script:sequence = 0
$script:lastPrintedLine = 0

function Read-State {
    $path = Join-Path $HarnessDirectory "live-state.json"
    if (!(Test-Path -LiteralPath $path)) { return $null }
    $stream = [IO.File]::Open($path, "Open", "Read", ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { $reader = [IO.StreamReader]::new($stream); return $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $stream.Dispose() }
}

function Read-RunningOutput([string]$path = $stdout) {
    if (!(Test-Path -LiteralPath $path)) { return "" }
    $stream = [IO.File]::Open($path, "Open", "Read", ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { $reader = [IO.StreamReader]::new($stream); return $reader.ReadToEnd() }
    finally { $stream.Dispose() }
}

function Print-NewOutput {
    $out = Read-RunningOutput $stdout
    if ($out) {
        $lines = $out -split "`r?`n"
        while ($script:lastPrintedLine -lt $lines.Count) {
            $line = $lines[$script:lastPrintedLine]
            $script:lastPrintedLine++
            if ($line -match '^OPENNV_LOAD_STATUS|^OPENNV_LOAD_PHASE|^OPENNV_LOAD_COMPLETE|^OPENNV_NATIVE_MENU_LOAD|^OPENNV_CAMPAIGN_BOT|^OPENNV_NATIVE_STACK_READY') {
                Write-Host "  [Engine] $line" -ForegroundColor Cyan
            }
        }
    }
}

function Wait-State([scriptblock]$predicate, [int]$Timeout = 90) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Timeout)
    while ([DateTime]::UtcNow -lt $deadline) {
        Print-NewOutput
        $game.Refresh()
        if ($game.HasExited) {
            $err = Read-RunningOutput $stderr
            $out = Read-RunningOutput $stdout
            throw "OpenNV exited early with $($game.ExitCode).`nSTDOUT:`n$out`nSTDERR:`n$err"
        }
        try { $state = Read-State }
        catch [IO.IOException] { $state = $null }
        if ($null -ne $state -and (& $predicate $state)) { return $state }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for state condition."
}

function Send-Input([hashtable]$command) {
    $script:sequence++
    $path = Join-Path $HarnessDirectory ("{0:D10}.command" -f $script:sequence)
    $tmp = $path + ".tmp"
    $json = $command | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tmp -Destination $path -Force
    Write-Host "  [Command] Sent request $($script:sequence): $json" -ForegroundColor Green
}

try {
    Write-Host "Waiting for OpenNV runtime initialization..."
    $initialState = Wait-State { param($value) $null -ne $value.process } 120
    Write-Host "OpenNV live-state connected (Process PID: $($game.Id))" -ForegroundColor Green

    # Wait for the StartMenu to be fully presented with controls
    Write-Host "Waiting for start menu controls..."
    $menuState = Wait-State { param($value) @($value.controls).Count -gt 0 } 120

    Write-Host "Dispatching campaign start (entry=$Entry)..."
    Send-Input @{ op = "campaign"; action = "start"; entry = $Entry }

    Write-Host "Campaign bot started! Monitoring progression..." -ForegroundColor Green
    $runClock = [Diagnostics.Stopwatch]::StartNew()
    $lastReport = ""
    $lastPhase = ""

    while ($runClock.Elapsed.TotalSeconds -lt $DurationSeconds) {
        $game.Refresh()
        if ($game.HasExited) {
            Write-Host "OpenNV process exited with code $($game.ExitCode)."
            break
        }

        try { $state = Read-State } catch { $state = $null }

        if ($state -and $state.campaignBot) {
            $bot = $state.campaignBot
            $report = "Phase: $($bot.phase) | Progress: $($bot.observedProgress) | Quests/Goals: $($bot.goalAttempts) | Choices: $($bot.choiceAttempts) | Checkpoints: $($bot.writtenCheckpoints)"
            if ($bot.goal) {
                $report += " | Goal: $($bot.goal.quest):$($bot.goal.objective) -> $($bot.goal.reference) ($($bot.goal.mode))"
            }
            if ($report -ne $lastReport) {
                Write-Host "  [Bot] $report" -ForegroundColor Green
                $lastReport = $report
            }
            if ($bot.phase -ne $lastPhase) {
                $lastPhase = $bot.phase
                if ($bot.phase -eq "blocked") {
                    Write-Warning "Campaign bot blocked: $($bot.error) (kind=$($bot.failureKind))"
                    break
                }
            }
        }

        Start-Sleep -Seconds 1
    }

    Write-Host "Campaign run completed after $([int]$runClock.Elapsed.TotalSeconds) seconds."
} finally {
    $game.Refresh()
    if (-not $game.HasExited) {
        Write-Host "Stopping campaign bot..."
        try { Send-Input @{ op = "campaign"; action = "stop" } } catch {}
        Start-Sleep -Seconds 1
        $game.Refresh()
        if (-not $game.HasExited) {
            Write-Host "Terminating Godot process $($game.Id)..."
            $game.Kill()
            $game.WaitForExit(5000)
        }
    }
}

Write-Host "Done. Output logs saved to: $HarnessDirectory"
