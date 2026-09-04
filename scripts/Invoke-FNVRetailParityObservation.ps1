[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$TargetProcessId,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$ParityChannel,
    [string]$PrivateLayoutPath =
        'D:\Dev\Tools\Ghidrust\workspace\evidence\falloutnv_1_4_0_525\camera\racesex-preview-live-layout.json',
    [ValidateRange(2, 64)]
    [int]$SampleCount = 3,
    [ValidateRange(10, 1000)]
    [int]$SampleIntervalMilliseconds = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ExpectedLayoutSchema = 'nikami-private-fnv-racesex-preview-live-layout/v1'
$AllowedCapabilities = @(
    'read', 'modules', 'regions', 'resolve', 'scan', 'watch_read', 'watch',
    'stack_sample'
)

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
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

if (-not (Test-Path -LiteralPath $PrivateLayoutPath -PathType Leaf)) {
    throw "Missing private observer layout: $PrivateLayoutPath"
}
$layout = Get-Content -Raw -LiteralPath $PrivateLayoutPath | ConvertFrom-Json
if ([string]$layout.schema -cne $ExpectedLayoutSchema) {
    throw 'Private observer layout schema differs.'
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
        $nanoseconds = [Diagnostics.Stopwatch]::GetTimestamp() * 1000000000L /
            [Diagnostics.Stopwatch]::Frequency
        $snapshot = [ordered]@{
            schema = 'opennv-retail-parity-snapshot/v1'
            simulationTick = '0'
            monotonicNanoseconds = [string]$nanoseconds
            eventOrdinal = '0'
            stateKey = 'observer:retail-module-identity'
            fields = @(
                [ordered]@{ category = 'Execution'; name = 'runtime.target.sha256'; kind = 'Utf8'; value = [string]$layout.target.sha256 }
                [ordered]@{ category = 'Execution'; name = 'runtime.target.version'; kind = 'Utf8'; value = [string]$layout.target.version }
                [ordered]@{ category = 'Execution'; name = 'runtime.module.name'; kind = 'Utf8'; value = [string]$layout.target.moduleName }
                [ordered]@{ category = 'Execution'; name = 'runtime.module.pe-signature'; kind = 'Bytes'; value = 'TVo=' }
            )
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
        "observe-only=1 packet-v1=1 state=module-identity-only channel=$ParityChannel")
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
