[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$TargetProcessId,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$ParityChannel,
    [string]$PrivateLayoutPath =
        'D:\Dev\Tools\Ghidrust\workspace\evidence\falloutnv_1_4_0_525\parity_telemetry\gameplay-live-layout.json',
    [ValidateRange(2, 64)]
    [int]$SampleCount = 3,
    [ValidateRange(10, 1000)]
    [int]$SampleIntervalMilliseconds = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ExpectedLayoutSchema = 'nikami-private-fnv-parity-gameplay-live-layout/v1'
$AllowedCapabilities = @(
    'read', 'modules', 'regions', 'resolve', 'scan', 'watch_read', 'watch',
    'stack_sample'
)

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Convert-HexUInt64([string]$Value, [string]$Label) {
    if ($Value -notmatch '^0x[0-9A-Fa-f]{1,16}$') {
        throw "$Label is not a canonical hexadecimal value."
    }
    return [Convert]::ToUInt64($Value.Substring(2), 16)
}

function Assert-ByteRange([string]$Label, [int]$Offset, [int]$Width, [int]$Length) {
    if ($Offset -lt 0 -or $Width -le 0 -or $Length -le 0 -or
        $Offset -gt $Length - $Width) {
        throw "$Label is outside its reviewed read extent."
    }
}

function Get-UInt32([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Get-Float32([byte[]]$Bytes, [int]$Offset) {
    $value = [BitConverter]::ToSingle($Bytes, $Offset)
    if (-not [single]::IsFinite($value)) {
        throw "Observed retail float at offset $Offset is not finite."
    }
    return $value
}

function Format-Float64([double]$Value) {
    if (-not [double]::IsFinite($Value)) {
        throw 'Retail parity Float64 value is not finite.'
    }
    return $Value.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
}

function Resolve-FormKey([uint32]$RuntimeFormId, [string[]]$PluginLoadOrder) {
    $index = [int]($RuntimeFormId -shr 24)
    $objectId = [uint32]($RuntimeFormId -band [uint32]0x00ffffff)
    if ($index -ge $PluginLoadOrder.Count) {
        throw ('Observed runtime FormID 0x{0:x8} has load index {1}, but the reviewed ' +
            'private load order contains {2} plugins.' -f
            $RuntimeFormId, $index, $PluginLoadOrder.Count)
    }
    return '{0}:{1:x6}' -f $PluginLoadOrder[$index], $objectId
}

function Send-McpRequest(
    [Diagnostics.Process]$Process,
    [int]$Id,
    [string]$Method,
    [hashtable]$Params
) {
    $request = [ordered]@{
        jsonrpc = '2.0'; id = $Id; method = $Method; params = $Params
    } | ConvertTo-Json -Compress -Depth 10
    $Process.StandardInput.WriteLine($request)
    $Process.StandardInput.Flush()
    $line = $Process.StandardOutput.ReadLine()
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "Ghidrust MCP closed before responding to $Method."
    }
    $response = $line | ConvertFrom-Json
    if ($response.PSObject.Properties.Name -contains 'error') {
        throw "Ghidrust MCP $Method failed: $($response.error.message)"
    }
    return $response.result
}

function Invoke-McpTool(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$Name,
    [hashtable]$Arguments
) {
    if ($Name -notin @('process_attach', 'process_modules', 'process_read', 'process_detach')) {
        throw "Retail parity observer forbids MCP tool $Name."
    }
    $id = $NextId.Value
    $NextId.Value++
    $result = Send-McpRequest $Process $id 'tools/call' @{
        name = $Name; arguments = $Arguments
    }
    if ($result.isError) {
        throw "Ghidrust tool $Name failed: $($result.content[0].text)"
    }
    return ($result.content[0].text | ConvertFrom-Json)
}

function Read-ObservedBytes(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address,
    [int]$Size
) {
    if ($Address -eq 0 -or $Size -le 0 -or $Size -gt 4096) {
        throw 'Retail parity read request is outside the reviewed bounds.'
    }
    $read = Invoke-McpTool $Process $NextId 'process_read' @{
        session_id = $SessionId
        addr = ('0x{0:X}' -f $Address)
        size = $Size
    }
    $hex = [string]$read.hex -replace '[^0-9A-Fa-f]', ''
    if ([int]$read.bytes_read -ne $Size -or $hex.Length -ne $Size * 2) {
        throw ('Retail parity read at 0x{0:X} returned {1} of {2} bytes.' -f
            $Address, [int]$read.bytes_read, $Size)
    }
    $bytes = [byte[]]::new($Size)
    for ($index = 0; $index -lt $Size; $index++) {
        $bytes[$index] = [Convert]::ToByte($hex.Substring($index * 2, 2), 16)
    }
    return $bytes
}

if (-not (Test-Path -LiteralPath $PrivateLayoutPath -PathType Leaf)) {
    throw "Missing private observer layout: $PrivateLayoutPath"
}
$layout = Get-Content -Raw -LiteralPath $PrivateLayoutPath | ConvertFrom-Json
if ([string]$layout.schema -cne $ExpectedLayoutSchema) {
    throw 'Private observer layout schema differs.'
}
if ([string]$layout.target.architecture -cne 'x86' -or
    [int]$layout.runtime.pointerBytes -ne 4) {
    throw 'Private observer layout is not the reviewed FalloutNV x86 pointer contract.'
}
$pluginLoadOrder = @($layout.target.pluginLoadOrder | ForEach-Object { [string]$_ })
if ($pluginLoadOrder.Count -eq 0 -or
    @($pluginLoadOrder | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -ne 0 -or
    @($pluginLoadOrder | Select-Object -Unique).Count -ne $pluginLoadOrder.Count) {
    throw 'Private observer layout has an invalid or duplicate plugin load order.'
}
$timerRva = Convert-HexUInt64 ([string]$layout.runtime.timer.rva) 'Timer RVA'
$playerSingletonRva = Convert-HexUInt64 (
    [string]$layout.runtime.playerSingleton.rva) 'Player singleton RVA'
$timerReadBytes = [int]$layout.runtime.timer.readBytes
$referenceReadBytes = [int]$layout.runtime.reference.readBytes
$cellReadBytes = [int]$layout.runtime.cell.readBytes
$timerSecondsOffset = [int]$layout.runtime.timer.secondsPassedOffset
$timerMillisecondsOffset = [int]$layout.runtime.timer.millisecondsPassedOffset
$referenceFormIdOffset = [int]$layout.runtime.reference.formIdOffset
$referenceRotationOffset = [int]$layout.runtime.reference.rotationOffset
$referencePositionOffset = [int]$layout.runtime.reference.positionOffset
$referenceCellOffset = [int]$layout.runtime.reference.parentCellPointerOffset
$cellFormIdOffset = [int]$layout.runtime.cell.formIdOffset
$cellAttachStateOffset = [int]$layout.runtime.cell.attachStateOffset
$cellWorldspaceOffset = [int]$layout.runtime.cell.worldspacePointerOffset
Assert-ByteRange 'Timer seconds field' $timerSecondsOffset 4 $timerReadBytes
Assert-ByteRange 'Timer milliseconds field' $timerMillisecondsOffset 4 $timerReadBytes
Assert-ByteRange 'Reference FormID field' $referenceFormIdOffset 4 $referenceReadBytes
Assert-ByteRange 'Reference rotation field' $referenceRotationOffset 12 $referenceReadBytes
Assert-ByteRange 'Reference position field' $referencePositionOffset 12 $referenceReadBytes
Assert-ByteRange 'Reference CELL pointer' $referenceCellOffset 4 $referenceReadBytes
Assert-ByteRange 'CELL FormID field' $cellFormIdOffset 4 $cellReadBytes
Assert-ByteRange 'CELL attach-state field' $cellAttachStateOffset 1 $cellReadBytes
Assert-ByteRange 'CELL worldspace pointer' $cellWorldspaceOffset 4 $cellReadBytes
$gameUnitsToMeters = 0.0
if (-not [double]::TryParse(
        [string]$layout.runtime.gameUnitsToMeters,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$gameUnitsToMeters) -or
    -not [double]::IsFinite($gameUnitsToMeters) -or $gameUnitsToMeters -le 0.0) {
    throw 'Private observer layout has an invalid game-unit scale.'
}
foreach ($path in @([string]$layout.target.path, [string]$layout.observer.path)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing reviewed observer input: $path"
    }
}
if ((Get-Sha256 ([string]$layout.target.path)) -cne [string]$layout.target.sha256 -or
    (Get-Sha256 ([string]$layout.observer.path)) -cne [string]$layout.observer.sha256 -or
    [string]$layout.observer.mode -cne 'observe') {
    throw 'Private target or observer identity differs from the reviewed layout.'
}
$target = Get-Process -Id $TargetProcessId -ErrorAction Stop
if ((Get-Sha256 $target.Path) -cne [string]$layout.target.sha256) {
    throw 'Explicit target PID does not own the reviewed FalloutNV.exe build.'
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publisherProject = Join-Path $repository 'runtime\tools\ParityRetailPublisher\ParityRetailPublisher.csproj'
$mcp = $null
$publisher = $null
$sessionId = $null
$nextId = 1
try {
    $mcpInfo = [Diagnostics.ProcessStartInfo]::new()
    $mcpInfo.FileName = [string]$layout.observer.path
    $mcpInfo.ArgumentList.Add('mcp')
    $mcpInfo.UseShellExecute = $false
    $mcpInfo.CreateNoWindow = $true
    $mcpInfo.RedirectStandardInput = $true
    $mcpInfo.RedirectStandardOutput = $true
    $mcpInfo.RedirectStandardError = $true
    $mcpInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $mcp = [Diagnostics.Process]::Start($mcpInfo)
    if ($null -eq $mcp) { throw 'Failed to start private Win32 Ghidrust MCP.' }

    $init = Send-McpRequest $mcp $nextId 'initialize' @{
        protocolVersion = '2024-11-05'
        capabilities = @{}
        clientInfo = @{ name = 'opennv-retail-parity-observer'; version = '1' }
    }
    $nextId++
    if ([string]$init.serverInfo.name -cne 'ghidrust' -or
        [int]$init.serverInfo.toolSurface -ne [int]$layout.observer.toolSurface) {
        throw 'Unexpected Ghidrust MCP identity or tool surface.'
    }
    $mcp.StandardInput.WriteLine((@{
        jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{}
    } | ConvertTo-Json -Compress))
    $mcp.StandardInput.Flush()

    $session = Invoke-McpTool $mcp ([ref]$nextId) 'process_attach' @{
        pid = $TargetProcessId; mode = 'observe'
    }
    $sessionId = [string]$session.session_id
    $unexpected = @($session.capabilities | Where-Object { [string]$_ -notin $AllowedCapabilities })
    if ([string]$session.mode -cne 'observe' -or $unexpected.Count -ne 0) {
        throw 'Ghidrust did not establish the reviewed observe-only capability set.'
    }
    $modules = @(Invoke-McpTool $mcp ([ref]$nextId) 'process_modules' @{
        session_id = $sessionId
    })
    $main = @($modules | Where-Object { [string]$_.name -ieq [string]$layout.target.moduleName })
    if ($main.Count -ne 1) { throw 'Observed process has no unique reviewed main module.' }
    $moduleBase = [uint64]$main[0].base
    $pe = Invoke-McpTool $mcp ([ref]$nextId) 'process_read' @{
        session_id = $sessionId; addr = ('0x{0:X}' -f $moduleBase); size = 2
    }
    if ([int]$pe.bytes_read -ne 2 -or ([string]$pe.hex -replace '[^0-9A-Fa-f]', '') -cne '4d5a') {
        throw 'Observed main module did not expose the expected PE signature.'
    }

    $publisherInfo = [Diagnostics.ProcessStartInfo]::new()
    $publisherInfo.FileName = 'dotnet'
    foreach ($argument in @(
        'run', '--no-build', '--configuration', 'Release', '--project',
        $publisherProject, '--', '--channel', $ParityChannel
    )) { $publisherInfo.ArgumentList.Add($argument) }
    $publisherInfo.UseShellExecute = $false
    $publisherInfo.CreateNoWindow = $true
    $publisherInfo.RedirectStandardInput = $true
    $publisherInfo.RedirectStandardOutput = $true
    $publisherInfo.RedirectStandardError = $true
    $publisherInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $publisher = [Diagnostics.Process]::Start($publisherInfo)
    if ($null -eq $publisher) { throw 'Failed to start the retail parity publisher.' }

    for ($index = 0; $index -lt $SampleCount; $index++) {
        if ($target.HasExited) { throw 'Retail target exited during observation.' }
        $timer = Read-ObservedBytes $mcp ([ref]$nextId) $sessionId `
            ($moduleBase + $timerRva) $timerReadBytes
        $playerPointerBytes = Read-ObservedBytes $mcp ([ref]$nextId) $sessionId `
            ($moduleBase + $playerSingletonRva) 4
        $playerAddress = [uint64](Get-UInt32 $playerPointerBytes 0)
        if ($playerAddress -eq 0) {
            throw 'Retail player singleton is null; a loaded gameplay state is required.'
        }
        $player = Read-ObservedBytes $mcp ([ref]$nextId) $sessionId `
            $playerAddress $referenceReadBytes
        $cellAddress = [uint64](Get-UInt32 $player $referenceCellOffset)
        if ($cellAddress -eq 0) {
            throw 'Retail player has no parent CELL; a loaded gameplay state is required.'
        }
        $cell = Read-ObservedBytes $mcp ([ref]$nextId) $sessionId `
            $cellAddress $cellReadBytes
        $playerFormKey = Resolve-FormKey (Get-UInt32 $player $referenceFormIdOffset) `
            $pluginLoadOrder
        $cellFormKey = Resolve-FormKey (Get-UInt32 $cell $cellFormIdOffset) `
            $pluginLoadOrder
        $sourceX = [double](Get-Float32 $player $referencePositionOffset)
        $sourceY = [double](Get-Float32 $player ($referencePositionOffset + 4))
        $sourceZ = [double](Get-Float32 $player ($referencePositionOffset + 8))
        $rotationX = [double](Get-Float32 $player $referenceRotationOffset)
        $rotationY = [double](Get-Float32 $player ($referenceRotationOffset + 4))
        $rotationZ = [double](Get-Float32 $player ($referenceRotationOffset + 8))
        $fields = @(
            [ordered]@{ category = 'Execution'; name = 'runtime.target.sha256'; kind = 'Utf8'; value = [string]$layout.target.sha256 }
            [ordered]@{ category = 'Execution'; name = 'runtime.target.version'; kind = 'Utf8'; value = [string]$layout.target.version }
            [ordered]@{ category = 'Execution'; name = 'runtime.module.name'; kind = 'Utf8'; value = [string]$layout.target.moduleName }
            [ordered]@{ category = 'Execution'; name = 'execution.timer.milliseconds'; kind = 'UInt64'; value = [string](Get-UInt32 $timer $timerMillisecondsOffset) }
            [ordered]@{ category = 'Execution'; name = 'execution.timer.delta-seconds'; kind = 'Float64'; value = Format-Float64 ([double](Get-Float32 $timer $timerSecondsOffset)) }
            [ordered]@{ category = 'World'; name = 'world.cell.form-key'; kind = 'Utf8'; value = $cellFormKey }
            [ordered]@{ category = 'World'; name = 'world.cell.attach-state'; kind = 'UInt64'; value = [string]$cell[$cellAttachStateOffset] }
            [ordered]@{ category = 'Actor'; name = 'actor.player.form-key'; kind = 'Utf8'; value = $playerFormKey }
            [ordered]@{ category = 'Actor'; name = 'actor.player.root.position.x'; kind = 'Float64'; value = Format-Float64 ($sourceX * $gameUnitsToMeters) }
            [ordered]@{ category = 'Actor'; name = 'actor.player.root.position.y'; kind = 'Float64'; value = Format-Float64 ($sourceZ * $gameUnitsToMeters) }
            [ordered]@{ category = 'Actor'; name = 'actor.player.root.position.z'; kind = 'Float64'; value = Format-Float64 (-$sourceY * $gameUnitsToMeters) }
            [ordered]@{ category = 'Actor'; name = 'actor.player.source-rotation.x-radians'; kind = 'Float64'; value = Format-Float64 $rotationX }
            [ordered]@{ category = 'Actor'; name = 'actor.player.source-rotation.y-radians'; kind = 'Float64'; value = Format-Float64 $rotationY }
            [ordered]@{ category = 'Actor'; name = 'actor.player.source-rotation.z-radians'; kind = 'Float64'; value = Format-Float64 $rotationZ }
        )
        $worldspaceAddress = [uint64](Get-UInt32 $cell $cellWorldspaceOffset)
        if ($worldspaceAddress -ne 0) {
            $worldspace = Read-ObservedBytes $mcp ([ref]$nextId) $sessionId `
                $worldspaceAddress ($referenceFormIdOffset + 4)
            $fields += [ordered]@{
                category = 'World'
                name = 'world.worldspace.form-key'
                kind = 'Utf8'
                value = Resolve-FormKey (Get-UInt32 $worldspace $referenceFormIdOffset) `
                    $pluginLoadOrder
            }
        }
        $nanoseconds = [Diagnostics.Stopwatch]::GetTimestamp() * 1000000000L /
            [Diagnostics.Stopwatch]::Frequency
        $snapshot = [ordered]@{
            schema = 'opennv-retail-parity-snapshot/v1'
            simulationTick = [string](Get-UInt32 $timer $timerMillisecondsOffset)
            monotonicNanoseconds = [string]$nanoseconds
            eventOrdinal = '0'
            stateKey = "cell:$cellFormKey"
            fields = $fields
        } | ConvertTo-Json -Compress -Depth 6
        $publisher.StandardInput.WriteLine($snapshot)
        $publisher.StandardInput.Flush()
        $receipt = $publisher.StandardOutput.ReadLine()
        $expected = $index + 1
        if ($receipt -cne "OPENNV_RETAIL_PARITY_PUBLISHED producer=$expected ring=$expected") {
            throw "Retail parity publisher returned an unexpected receipt: $receipt"
        }
        if ($expected -lt $SampleCount) { Start-Sleep -Milliseconds $SampleIntervalMilliseconds }
    }
    $publisher.StandardInput.Close()
    $publisher.WaitForExit()
    if ($publisher.ExitCode -ne 0) {
        throw "Retail parity publisher failed: $($publisher.StandardError.ReadToEnd())"
    }
    Write-Output (
        "OPENNV_FNV_RETAIL_PARITY_OBSERVATION_PASS packets=$SampleCount ordered=1 " +
        "observe-only=1 packet-v1=1 state=gameplay-cell-player-timer " +
        "event-ordinal=unrecovered-zero channel=$ParityChannel")
}
finally {
    if ($null -ne $sessionId -and $null -ne $mcp -and -not $mcp.HasExited) {
        try { $null = Invoke-McpTool $mcp ([ref]$nextId) 'process_detach' @{ session_id = $sessionId } }
        catch { Write-Warning "Could not detach Ghidrust observer: $_" }
    }
    foreach ($process in @($publisher, $mcp)) {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill($true) }
            $process.Dispose()
        }
    }
}
