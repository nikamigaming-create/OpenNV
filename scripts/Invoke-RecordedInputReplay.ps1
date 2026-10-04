[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CommandDirectory,
    [Parameter(Mandatory = $true)][string]$InputTape,
    [Parameter(Mandatory = $true)][guid]$CheckpointId,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 300,
    [ValidateRange(0, 10000000)][long]$MaximumLatenessMicroseconds = 250000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskInputDirectory = [IO.Path]::GetFullPath($CommandDirectory)
$taskTape = [IO.Path]::GetFullPath($InputTape)
if (-not (Test-Path -LiteralPath $taskTape -PathType Leaf)) { throw 'Input tape is missing.' }
$taskClock = [Diagnostics.Stopwatch]::StartNew()
$taskProcessId = 0

function Read-InputSnapshot {
    param([string]$Path)
    $taskStream = $null
    try {
        $taskStream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $taskReader = [IO.StreamReader]::new($taskStream)
        try { return $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
    } catch [IO.IOException] { return $null } finally { if ($taskStream) { $taskStream.Dispose() } }
}

function Read-InputState {
    param([switch]$WaitForFresh)
    $taskState = Read-InputSnapshot (Join-Path $taskInputDirectory 'live-state.json')
    if (-not $taskState) { return $null }
    $taskAge = ([Diagnostics.Stopwatch]::GetTimestamp() * (1000.0 / [Diagnostics.Stopwatch]::Frequency)) -
        ($taskState.sampledNanoseconds / 1000000.0)
    if (-not (Get-Process -Id $taskState.process -ErrorAction SilentlyContinue)) {
        throw 'Native input process is gone.'
    }
    if ($taskAge -lt 0 -or $taskAge -gt 5000) {
        if ($WaitForFresh) { return $null }
        throw 'Native input state is stale.'
    }
    if ($taskProcessId -ne 0 -and $taskState.process -ne $taskProcessId) { throw 'The native process changed during replay.' }
    return $taskState
}

function Test-InputDeadline {
    if ($taskClock.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Recorded-input replay timed out.' }
}

function Send-RecordedInputCommand {
    param([object]$Command)
    Test-InputDeadline
    $taskState = Read-InputState
    if (-not $taskState) { throw 'Native input state is unavailable.' }
    $taskNumbers = @(Get-ChildItem -LiteralPath $taskInputDirectory -Filter '*.command' |
        ForEach-Object { [long]$_.BaseName })
    $taskNumber = [Math]::Max([long]$taskState.nextCommandRequest,
        1 + [long](($taskNumbers | Measure-Object -Maximum).Maximum))
    $taskPath = Join-Path $taskInputDirectory ('{0:D10}.command' -f $taskNumber)
    $taskTemporary = $taskPath + '.' + [guid]::NewGuid().ToString('N') + '.pending'
    try {
        [IO.File]::WriteAllText($taskTemporary, ($Command | ConvertTo-Json -Compress -Depth 8), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($taskTemporary, $taskPath)
    } finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary } }
    $taskReceiptPath = Join-Path $taskInputDirectory ('{0:D10}.receipt.json' -f $taskNumber)
    do {
        Test-InputDeadline
        $taskReceipt = Read-InputSnapshot $taskReceiptPath
        if (-not $taskReceipt) { Start-Sleep -Milliseconds 100 }
    } until ($taskReceipt)
    if (-not $taskReceipt.delivered) { throw ('Native command refused: ' + $taskReceipt.message) }
    return $taskReceipt
}

try {
    $taskInitial = Read-InputState
    if (-not $taskInitial) { throw 'Start a native OpenNV session before replay.' }
    $taskProcessId = $taskInitial.process
    $null = Send-RecordedInputCommand @{ op = 'checkpoint.load'; id = $CheckpointId.ToString('N'); pauseAfterLoad = $false }
    do {
        Test-InputDeadline
        $taskState = Read-InputState -WaitForFresh
        $taskReady = $taskState -and -not $taskState.checkpointTransitioning -and
            $taskState.checkpointRestored -and $taskState.checkpointRestored.id -eq $CheckpointId.ToString('N')
        if (-not $taskReady) { Start-Sleep -Milliseconds 100 }
    } until ($taskReady)
    $taskReplayReceipt = Send-RecordedInputCommand @{ op = 'input.replay.start'; path = $taskTape; maximumLatenessMicroseconds = $MaximumLatenessMicroseconds }
    do {
        Test-InputDeadline
        $taskState = Read-InputState -WaitForFresh
        $taskPlayback = if ($taskState -and $taskState.recordedInput.replayRequest -eq $taskReplayReceipt.request) {
            $taskState.recordedInput.playback
        } else { $null }
        if (-not $taskPlayback -or $taskPlayback.active) { Start-Sleep -Milliseconds 100 }
    } until ($taskPlayback -and -not $taskPlayback.active)
    if (-not $taskPlayback.complete) { throw ('Playback stopped: ' + $taskPlayback.error) }
    [ordered]@{ schema = 'opennv-input-replay-run/v1'; process = $taskProcessId; checkpoint = $CheckpointId.ToString('N');
        path = $taskTape; playback = $taskPlayback; gameplayParity = 'unverified'; elapsedSeconds = $taskClock.Elapsed.TotalSeconds } |
        ConvertTo-Json -Depth 12
} catch {
    # Release input through the existing owner even after a rejected command or timeout.
    if (Test-Path -LiteralPath $taskInputDirectory -PathType Container) {
        [IO.File]::WriteAllText((Join-Path $taskInputDirectory 'stop.request'), [DateTime]::UtcNow.ToString('O'), [Text.UTF8Encoding]::new($false))
    }
    throw
}
