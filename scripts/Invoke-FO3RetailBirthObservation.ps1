[CmdletBinding(DefaultParameterSetName = 'Attach')]
param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\Fallout 3 goty',
    [string]$GhidrustPath = 'D:\Dev\Tools\Ghidrust\builds\wow64-i686-codex-nogpu\i686-pc-windows-msvc\release\ghidrust.exe',
    [string]$OutputPath = '',
    [Parameter(ParameterSetName = 'Attach')]
    [int]$TargetProcessId = 0,
    [Parameter(ParameterSetName = 'Launch', Mandatory = $true)]
    [switch]$Launch,
    [Parameter(ParameterSetName = 'Launch')]
    [string[]]$LaunchArgument = @(),
    [ValidateRange(1, 60)]
    [int]$ObserveSeconds = 4,
    [ValidateRange(1, 20)]
    [int]$SampleCount = 3,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedGameSha256 = 'c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e'
$expectedGameVersion = '1.7.0.4'
$expectedGhidrustSha256 = '10070829e620ae2e1e26d338a38bc4dcb21d8c855f1fa3d846e03f71b812cc41'
$expectedToolSurface = 8
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$privateEvidenceRoot = 'D:\Dev\Tools\Ghidrust\workspace\evidence\fallout3_1_7_0_4\cg00'
$gameExe = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Fallout3.exe'))
$ghidrustExe = [IO.Path]::GetFullPath($GhidrustPath)

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-PathWithin([string]$Path, [string]$Root) {
    $absolutePath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $absoluteRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    return $absolutePath.StartsWith(
        $absoluteRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function ConvertTo-Aob([single[]]$Values) {
    $bytes = foreach ($value in $Values) {
        [BitConverter]::GetBytes($value)
    }
    return (($bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function ConvertTo-UInt32Aob([uint32]$Value) {
    return (([BitConverter]::GetBytes($Value) |
        ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function Send-McpRequest(
    [Diagnostics.Process]$Process,
    [int]$Id,
    [string]$Method,
    [hashtable]$Params
) {
    $request = [ordered]@{
        jsonrpc = '2.0'
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Compress -Depth 20
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
    $id = $NextId.Value
    $NextId.Value++
    $result = Send-McpRequest -Process $Process -Id $id -Method 'tools/call' -Params @{
        name = $Name
        arguments = $Arguments
    }
    if ($result.isError) {
        throw "Ghidrust tool $Name failed: $($result.content[0].text)"
    }
    return ($result.content[0].text | ConvertFrom-Json)
}

function Assert-CleanModuleInventory([object[]]$Modules, [string]$OwnedGameRoot) {
    $blockedName = '(?i)(openvr|openxr|vrclient|reshade)'
    $proxyName = '(?i)^(d3d9|dxgi|dinput8|xinput1_[1-4]|winmm|version|dsound)\.dll$'
    $violations = foreach ($module in $Modules) {
        $name = [string]$module.name
        $path = if ($null -eq $module.path) { '' } else { [string]$module.path }
        $underGameRoot = -not [string]::IsNullOrWhiteSpace($path) -and
            (Test-PathWithin -Path $path -Root $OwnedGameRoot)
        if ($name -match $blockedName -or $path -match $blockedName -or
            ($name -match $proxyName -and $underGameRoot)) {
            [ordered]@{ name = $name; path = $path }
        }
    }
    if (@($violations).Count -gt 0) {
        throw "Rejected contaminated Fallout 3 process modules: $($violations | ConvertTo-Json -Compress)"
    }
}

foreach ($required in @($gameExe, $ghidrustExe)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing required file: $required"
    }
}

$gameSha256 = Get-Sha256 $gameExe
$gameVersion = (Get-Item -LiteralPath $gameExe).VersionInfo.FileVersion
if ($gameSha256 -ne $expectedGameSha256 -or $gameVersion -ne $expectedGameVersion) {
    throw "Unsupported Fallout3.exe identity: version=$gameVersion sha256=$gameSha256"
}
$ghidrustSha256 = Get-Sha256 $ghidrustExe
if ($ghidrustSha256 -ne $expectedGhidrustSha256) {
    throw "Unreviewed Ghidrust identity: sha256=$ghidrustSha256"
}

$rootProxyNames = @(
    'd3d9.dll', 'dxgi.dll', 'dinput8.dll', 'xinput1_1.dll', 'xinput1_2.dll',
    'xinput1_3.dll', 'xinput1_4.dll', 'winmm.dll', 'version.dll', 'dsound.dll',
    'openvr_api.dll', 'openxr_loader.dll', 'ReShade.ini'
)
$rootProxyFiles = @($rootProxyNames | ForEach-Object {
    $candidate = Join-Path $GameRoot $_
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        [IO.Path]::GetFullPath($candidate)
    }
})
if ($rootProxyFiles.Count -gt 0) {
    throw "Fallout 3 root contains a VR/render/input proxy; refusing retail observation: $($rootProxyFiles -join ', ')"
}

$identity = [ordered]@{
    schema = 'opennv.fo3-retail-observation-preflight.v1'
    game = [ordered]@{
        path = $gameExe
        version = $gameVersion
        sha256 = $gameSha256
    }
    observer = [ordered]@{
        path = $ghidrustExe
        sha256 = $ghidrustSha256
        required_tool_surface = $expectedToolSurface
        required_mode = 'observe'
    }
    root_proxy_files = $rootProxyFiles
}
if ($ValidateOnly) {
    $identity | ConvertTo-Json -Depth 10
    return
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $privateEvidenceRoot "fo3-cg00-raw-observation-$stamp.json"
}
$output = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-PathWithin -Path $output -Root $privateEvidenceRoot)) {
    throw "Evidence output must be private and below ${privateEvidenceRoot}: $output"
}
if (Test-PathWithin -Path $output -Root $repoRoot) {
    throw "Retail observation evidence may not be written inside the OpenNV repository: $output"
}
if (Test-Path -LiteralPath $output) {
    throw "Refusing to overwrite retail observation evidence: $output"
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null

$mcp = $null
$launchedProcess = $null
$shadowDirectory = $null
$sessionId = $null
$nextId = 1
try {
    if ($Launch) {
        if (Get-Process -Name Fallout3 -ErrorAction SilentlyContinue) {
            throw 'Fallout 3 is already running; refusing to create a second retail process.'
        }
        $shadowDirectory = Join-Path $privateEvidenceRoot ('.exe-shadow-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $shadowDirectory | Out-Null
        $shadowExe = Join-Path $shadowDirectory 'Fallout3.exe'
        Copy-Item -LiteralPath $gameExe -Destination $shadowExe
        if ((Get-Sha256 $shadowExe) -ne $expectedGameSha256) {
            throw 'Private Fallout3.exe shadow hash mismatch.'
        }
        $start = @{
            FilePath = $shadowExe
            WorkingDirectory = [IO.Path]::GetFullPath($GameRoot)
            WindowStyle = 'Hidden'
            PassThru = $true
        }
        if ($LaunchArgument.Count -gt 0) {
            $start.ArgumentList = $LaunchArgument
        }
        $launchedProcess = Start-Process @start
        Start-Sleep -Milliseconds 1200
        $launchedProcess.Refresh()
        if ($launchedProcess.HasExited) {
            throw "Private Fallout 3 shadow exited before observation (exit $($launchedProcess.ExitCode))."
        }
        $TargetProcessId = $launchedProcess.Id
    }
    elseif ($TargetProcessId -eq 0) {
        $running = @(Get-Process -Name Fallout3 -ErrorAction SilentlyContinue)
        if ($running.Count -ne 1) {
            throw 'Specify TargetProcessId or use -Launch; exactly one Fallout3 process was not found.'
        }
        $TargetProcessId = $running[0].Id
    }

    $target = Get-Process -Id $TargetProcessId -ErrorAction Stop
    $targetPath = $target.Path
    if ((Get-Sha256 $targetPath) -ne $expectedGameSha256) {
        throw "Target process executable does not match the reviewed Fallout 3 build: $targetPath"
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ghidrustExe
    $startInfo.Arguments = 'mcp'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $mcp = [Diagnostics.Process]::new()
    $mcp.StartInfo = $startInfo
    if (-not $mcp.Start()) {
        throw 'Failed to start the private Win32 Ghidrust MCP.'
    }

    $init = Send-McpRequest -Process $mcp -Id $nextId -Method 'initialize' -Params @{
        protocolVersion = '2024-11-05'
        capabilities = @{}
        clientInfo = @{ name = 'opennv-fo3-retail-observer'; version = '1' }
    }
    $nextId++
    if ([int]$init.serverInfo.toolSurface -ne $expectedToolSurface -or
        [string]$init.serverInfo.name -ne 'ghidrust') {
        throw "Unexpected Ghidrust MCP server identity: $($init.serverInfo | ConvertTo-Json -Compress)"
    }
    $mcp.StandardInput.WriteLine((@{
        jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{}
    } | ConvertTo-Json -Compress))
    $mcp.StandardInput.Flush()

    $session = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_attach' -Arguments @{ pid = $TargetProcessId; mode = 'observe' }
    $sessionId = [string]$session.session_id
    if ([string]$session.mode -ne 'observe' -or
        @($session.capabilities) -contains 'write' -or
        @($session.capabilities) -contains 'break') {
        throw "Ghidrust did not establish a read-only observe session: $($session | ConvertTo-Json -Compress)"
    }

    $modules = @(Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_modules' -Arguments @{ session_id = $sessionId })
    Assert-CleanModuleInventory -Modules $modules -OwnedGameRoot ([IO.Path]::GetFullPath($GameRoot))
    $mainModule = @($modules | Where-Object { $_.name -ieq 'Fallout3.exe' })
    if ($mainModule.Count -ne 1) {
        throw "Expected one Fallout3.exe module, found $($mainModule.Count)."
    }

    $patterns = [ordered]@{
        doctor_li_reference_form_id = ConvertTo-UInt32Aob 0x000290A5
        doctor_li_authored_position = ConvertTo-Aob @(-5138.02392578125, -7313.3408203125, 7542.5361328125)
        doctor_li_stage0_marker_position = ConvertTo-Aob @(-5286.771484375, -7202.22998046875, 7542.5361328125)
    }
    $samples = [Collections.Generic.List[object]]::new()
    $startedAt = [DateTime]::UtcNow
    for ($sampleIndex = 0; $sampleIndex -lt $SampleCount; $sampleIndex++) {
        if ($sampleIndex -gt 0) {
            $intervalMs = [int](($ObserveSeconds * 1000) / [Math]::Max(1, $SampleCount - 1))
            Start-Sleep -Milliseconds $intervalMs
        }
        $matches = [ordered]@{}
        foreach ($pattern in $patterns.GetEnumerator()) {
            $scan = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
                -Name 'process_scan' -Arguments @{
                    session_id = $sessionId
                    aob = $pattern.Value
                    max_hits = 64
                }
            $matches[$pattern.Key] = $scan
        }
        $samples.Add([ordered]@{
            index = $sampleIndex
            elapsed_milliseconds = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds
            matches = $matches
        })
    }

    $evidence = [ordered]@{
        schema = 'opennv.fo3-retail-raw-observation.v1'
        captured_utc = [DateTime]::UtcNow.ToString('o')
        classification = 'private-raw-candidate-evidence-not-a-runtime-contract'
        identity = $identity
        process = [ordered]@{
            pid = $TargetProcessId
            executable_path = $targetPath
            launched_by_observer = [bool]$Launch
            session_mode = [string]$session.mode
            capabilities = @($session.capabilities)
        }
        module_inventory = $modules
        record_derived_candidate_patterns = $patterns
        samples = $samples
        unresolved = @(
            'Doctor Li live object identity and transform field offsets',
            'active package identity and procedure',
            'dialogue topic/INFO and timing',
            'camera transform/projection/FOV and timing'
        )
        prohibitions = @(
            'No process-memory writes',
            'No injected input or UI automation',
            'No Godot behavior contract may be emitted from raw candidates'
        )
    }
    $evidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $output -Encoding utf8NoBOM
    $evidence | ConvertTo-Json -Depth 8
    Write-Host "Private FO3 observation written to $output"
}
finally {
    if ($null -ne $mcp -and -not $mcp.HasExited) {
        if (-not [string]::IsNullOrWhiteSpace($sessionId)) {
            try {
                Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
                    -Name 'process_detach' -Arguments @{ session_id = $sessionId } | Out-Null
            }
            catch {}
        }
        try { $mcp.StandardInput.Close() } catch {}
        if (-not $mcp.WaitForExit(3000)) {
            $mcp.Kill()
            $mcp.WaitForExit(3000) | Out-Null
        }
    }
    if ($null -ne $mcp) { $mcp.Dispose() }
    if ($null -ne $launchedProcess) {
        try {
            $launchedProcess.Refresh()
            if (-not $launchedProcess.HasExited) {
                Stop-Process -Id $launchedProcess.Id -Force -ErrorAction SilentlyContinue
                $launchedProcess.WaitForExit(5000) | Out-Null
            }
        }
        finally { $launchedProcess.Dispose() }
    }
    if ($null -ne $shadowDirectory -and (Test-Path -LiteralPath $shadowDirectory)) {
        if (-not (Test-PathWithin -Path $shadowDirectory -Root $privateEvidenceRoot)) {
            throw "Refusing to remove shadow outside the private evidence root: $shadowDirectory"
        }
        Remove-Item -LiteralPath $shadowDirectory -Recurse -Force
    }
}
