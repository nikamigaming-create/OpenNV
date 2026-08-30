[CmdletBinding()]
param(
    [int]$TargetProcessId = 0,
    [string]$PrivateLayoutPath =
        'D:\Dev\Tools\Ghidrust\workspace\evidence\falloutnv_1_4_0_525\camera\racesex-preview-live-layout.json',
    [string]$PublicContractOutputPath = '',
    [ValidateRange(2, 8)]
    [int]$SampleCount = 3,
    [ValidateRange(10, 1000)]
    [int]$SampleIntervalMilliseconds = 100,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ContractSchema = 'opennv-fnv-racesex-preview-camera/v1'
$LayoutSchema = 'nikami-private-fnv-racesex-preview-live-layout/v1'
$ValidationSchema = 'opennv-fnv-racesex-preview-camera-observer-validation/v1'
$ExpectedFaceGrab = @(150.0, 50.0, 680.0, 620.0)
$RequiredObjects = @('raceSexMenu', 'renderedRaceSex', 'faceGrab', 'camera', 'targetAnchor')
$RequiredProbes = @(
    'faceGrabId', 'faceGrabRect', 'projectionKindCode', 'projectionMatrix',
    'fovDegrees', 'fovAxisCode', 'targetWorld', 'targetAnchorWorld',
    'cameraWorld', 'distance', 'near', 'far', 'aspect', 'fullIn', 'fullOut',
    'startingZoomPercent'
)
$AllowedObserveCapabilities = @(
    'read', 'modules', 'regions', 'resolve', 'scan', 'watch_read', 'watch',
    'stack_sample'
)
$FloatTolerance = 1.0e-5
$HexadecimalRadix = 16
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-Offset([object]$Value, [string]$Label) {
    if ($Value -is [int] -or $Value -is [long]) {
        if ([long]$Value -lt 0) { throw "$Label cannot be negative." }
        return [uint64]$Value
    }
    $text = [string]$Value
    if ($text -notmatch '^0x[0-9A-Fa-f]+$') {
        throw "$Label is not a hexadecimal offset."
    }
    return [Convert]::ToUInt64($text.Substring(2), $HexadecimalRadix)
}

function ConvertFrom-ProcessHex([string]$Hex) {
    $compact = $Hex -replace '[^0-9A-Fa-f]', ''
    if (($compact.Length % 2) -ne 0) {
        throw 'Ghidrust process_read returned an odd-length hex payload.'
    }
    $bytes = [byte[]]::new($compact.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte(
            $compact.Substring($index * 2, 2),
            $HexadecimalRadix)
    }
    return $bytes
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
    if ($Name -notin @('process_attach', 'process_modules', 'process_regions', 'process_read', 'process_detach')) {
        throw "RaceSex observer forbids MCP tool $Name."
    }
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

function Read-RemoteBytes(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address,
    [int]$Size
) {
    $read = Invoke-McpTool -Process $Process -NextId $NextId `
        -Name 'process_read' -Arguments @{
            session_id = $SessionId
            addr = ('0x{0:X}' -f $Address)
            size = $Size
        }
    if ([int]$read.bytes_read -ne $Size) {
        throw ('Short process read at 0x{0:X}: expected={1} actual={2}' -f
            $Address, $Size, [int]$read.bytes_read)
    }
    return @(ConvertFrom-ProcessHex ([string]$read.hex))
}

function Read-UInt32(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address
) {
    return [BitConverter]::ToUInt32(
        [byte[]](Read-RemoteBytes $Process $NextId $SessionId $Address 4),
        0)
}

function Test-Finite([double]$Value) {
    return -not [double]::IsNaN($Value) -and -not [double]::IsInfinity($Value)
}

function Test-NearlyEqual([double]$Left, [double]$Right, [double]$Tolerance) {
    return [Math]::Abs($Left - $Right) -le $Tolerance
}

function Read-ProbeValue(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address,
    [string]$Type
) {
    $count = switch ($Type) {
        'int32' { 1 }
        'uint32' { 1 }
        'float32' { 1 }
        'vector3f' { 3 }
        'vector4f' { 4 }
        'matrix4x4f' { 16 }
        default { throw "Unsupported private probe type: $Type" }
    }
    $bytes = [byte[]](Read-RemoteBytes $Process $NextId $SessionId $Address ($count * 4))
    if ($Type -eq 'int32') { return [BitConverter]::ToInt32($bytes, 0) }
    if ($Type -eq 'uint32') { return [BitConverter]::ToUInt32($bytes, 0) }
    if ($Type -eq 'float32') {
        $value = [double][BitConverter]::ToSingle($bytes, 0)
        if (-not (Test-Finite $value)) { throw 'Live scalar probe is non-finite.' }
        return $value
    }
    $values = for ($index = 0; $index -lt $count; $index++) {
        [double][BitConverter]::ToSingle($bytes, $index * 4)
    }
    if (@($values | Where-Object { -not (Test-Finite $_) }).Count -ne 0) {
        throw "Live $Type probe contains a non-finite value."
    }
    return @($values)
}

function Test-PrivateLayoutReady([pscustomobject]$Layout) {
    if ([string]$Layout.schema -cne $LayoutSchema -or
        [string]$Layout.status -cne 'ready-reviewed-observe-layout') {
        return $false
    }
    foreach ($name in $RequiredObjects) {
        if ($Layout.objects.PSObject.Properties.Name -notcontains $name) { return $false }
    }
    foreach ($name in $RequiredProbes) {
        if ($Layout.probes.PSObject.Properties.Name -notcontains $name) { return $false }
    }
    foreach ($name in @(
        'projectionKinds', 'fovAxes', 'minimumFovDegrees', 'maximumFovDegrees',
        'minimumStartingZoomPercent', 'maximumStartingZoomPercent',
        'aspectPolicy', 'floatTolerance')) {
        if ($Layout.constraints.PSObject.Properties.Name -notcontains $name) { return $false }
    }
    return $true
}

function Resolve-LiveObjects(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$ModuleBase,
    [pscustomobject]$Layout
) {
    $resolved = [ordered]@{}
    foreach ($property in $Layout.objects.PSObject.Properties) {
        $definition = $property.Value
        if ($definition.PSObject.Properties.Name -contains 'rootRva') {
            $address = Read-UInt32 $Process $NextId $SessionId `
                ($ModuleBase + (ConvertTo-Offset $definition.rootRva "$($property.Name).rootRva"))
        }
        elseif ($definition.PSObject.Properties.Name -contains 'parent') {
            $parent = [string]$definition.parent
            if (-not $resolved.Contains($parent)) {
                throw "Private object $($property.Name) has unresolved parent $parent."
            }
            $address = [uint64]$resolved[$parent]
        }
        else {
            throw "Private object $($property.Name) has no root or parent."
        }
        foreach ($offset in @($definition.pointerOffsets)) {
            $address = Read-UInt32 $Process $NextId $SessionId `
                ($address + (ConvertTo-Offset $offset "$($property.Name).pointerOffset"))
        }
        if ($address -eq 0) { throw "Private object $($property.Name) resolved null." }
        if ($definition.PSObject.Properties.Name -contains 'expectedVtableRva') {
            $vtable = Read-UInt32 $Process $NextId $SessionId $address
            $expected = $ModuleBase +
                (ConvertTo-Offset $definition.expectedVtableRva "$($property.Name).expectedVtableRva")
            if ([uint64]$vtable -ne $expected) {
                throw "Private object $($property.Name) vtable differs from the reviewed layout."
            }
        }
        $resolved[$property.Name] = [uint64]$address
    }
    $unique = @($RequiredObjects | ForEach-Object { [uint64]$resolved[$_] } | Select-Object -Unique)
    if ($unique.Count -ne $RequiredObjects.Count) {
        throw 'RaceSex live object join aliases two required object identities.'
    }
    return $resolved
}

function Read-LiveSnapshot(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [System.Collections.IDictionary]$Objects,
    [pscustomobject]$Layout
) {
    $result = [ordered]@{}
    foreach ($property in $Layout.probes.PSObject.Properties) {
        $probe = $property.Value
        $owner = [string]$probe.object
        if (-not $Objects.Contains($owner)) { throw "Probe $($property.Name) has no object $owner." }
        $address = [uint64]$Objects[$owner] +
            (ConvertTo-Offset $probe.offset "$($property.Name).offset")
        $result[$property.Name] = Read-ProbeValue `
            $Process $NextId $SessionId $address ([string]$probe.type)
    }
    return $result
}

function Assert-LiveSnapshot([System.Collections.IDictionary]$Snapshot, [pscustomobject]$Layout) {
    if ([int]$Snapshot.faceGrabId -ne 1 -or @($Snapshot.faceGrabRect).Count -ne 4) {
        throw 'Live FaceGrab identity is not the owned id/rect contract.'
    }
    for ($index = 0; $index -lt 4; $index++) {
        if (-not (Test-NearlyEqual $Snapshot.faceGrabRect[$index] $ExpectedFaceGrab[$index] $FloatTolerance)) {
            throw 'Live FaceGrab rect differs from the owned XML contract.'
        }
    }
    $kindKey = [string][int]$Snapshot.projectionKindCode
    $axisKey = [string][int]$Snapshot.fovAxisCode
    if ($Layout.constraints.projectionKinds.PSObject.Properties.Name -notcontains $kindKey -or
        $Layout.constraints.fovAxes.PSObject.Properties.Name -notcontains $axisKey) {
        throw 'Live projection kind or FOV axis is absent from the reviewed layout.'
    }
    $matrix = @($Snapshot.projectionMatrix)
    if ($matrix.Count -ne 16 -or
        @($matrix | Where-Object { [Math]::Abs([double]$_) -gt $FloatTolerance }).Count -lt 4) {
        throw 'Live projection matrix is empty or degenerate.'
    }
    $fov = [double]$Snapshot.fovDegrees
    if ($fov -lt [double]$Layout.constraints.minimumFovDegrees -or
        $fov -gt [double]$Layout.constraints.maximumFovDegrees) {
        throw 'Live RaceSex FOV falls outside the reviewed contract.'
    }
    foreach ($name in @('targetWorld', 'targetAnchorWorld', 'cameraWorld', 'fullIn', 'fullOut')) {
        if (@($Snapshot[$name]).Count -ne 3) { throw "Live $name is not a vector3." }
    }
    $tolerance = [double]$Layout.constraints.floatTolerance
    for ($index = 0; $index -lt 3; $index++) {
        if (-not (Test-NearlyEqual $Snapshot.targetWorld[$index] $Snapshot.targetAnchorWorld[$index] $tolerance)) {
            throw 'Live RaceSex camera target does not match its reviewed anchor join.'
        }
    }
    $derivedDistance = [Math]::Sqrt((0..2 | ForEach-Object {
        $delta = [double]$Snapshot.cameraWorld[$_] - [double]$Snapshot.targetWorld[$_]
        $delta * $delta
    } | Measure-Object -Sum).Sum)
    if (-not (Test-NearlyEqual $derivedDistance ([double]$Snapshot.distance) $tolerance) -or
        [double]$Snapshot.distance -le 0.0) {
        throw 'Live RaceSex distance does not join camera position to target.'
    }
    if ([double]$Snapshot.near -le 0.0 -or
        [double]$Snapshot.far -le [double]$Snapshot.near -or
        [double]$Snapshot.aspect -le 0.0) {
        throw 'Live RaceSex frustum or aspect is invalid.'
    }
    if ([string]$Layout.constraints.aspectPolicy -eq 'face-grab-rect' -and
        -not (Test-NearlyEqual ([double]$Snapshot.aspect) (34.0 / 31.0) $tolerance)) {
        throw 'Live projection aspect does not match the reviewed FaceGrab policy.'
    }
    if ((0..2 | Where-Object {
        -not (Test-NearlyEqual $Snapshot.fullIn[$_] $Snapshot.fullOut[$_] $tolerance)
    }).Count -eq 0) {
        throw 'Live Full-in and Full-out vectors are indistinguishable.'
    }
    $zoom = [double]$Snapshot.startingZoomPercent
    if ($zoom -lt [double]$Layout.constraints.minimumStartingZoomPercent -or
        $zoom -gt [double]$Layout.constraints.maximumStartingZoomPercent) {
        throw 'Live starting zoom lies outside the reviewed percent contract.'
    }
}

function Assert-StableSnapshots([object[]]$Snapshots, [double]$Tolerance) {
    if ($Snapshots.Count -lt 2) { throw 'RaceSex observation requires repeated samples.' }
    $baseline = $Snapshots[0]
    foreach ($sample in $Snapshots | Select-Object -Skip 1) {
        foreach ($name in $RequiredProbes) {
            $left = @($baseline[$name])
            $right = @($sample[$name])
            if ($left.Count -ne $right.Count) { throw "Live probe $name changed shape." }
            for ($index = 0; $index -lt $left.Count; $index++) {
                if ($left[$index] -is [string] -or $right[$index] -is [string]) {
                    if ([string]$left[$index] -cne [string]$right[$index]) {
                        throw "Live probe $name changed between observe-only samples."
                    }
                }
                elseif (-not (Test-NearlyEqual ([double]$left[$index]) ([double]$right[$index]) $Tolerance)) {
                    throw "Live probe $name changed between observe-only samples."
                }
            }
        }
    }
}

foreach ($path in @($PrivateLayoutPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing private layout: $path" }
}
$layoutPath = [IO.Path]::GetFullPath($PrivateLayoutPath)
$layout = Get-Content -Raw -LiteralPath $layoutPath | ConvertFrom-Json
if ([string]$layout.schema -cne $LayoutSchema) { throw 'Private RaceSex layout schema differs.' }
foreach ($required in @([string]$layout.target.path, [string]$layout.observer.path)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing reviewed observer input: $required" }
}
$targetHash = Get-Sha256 ([string]$layout.target.path)
$observerHash = Get-Sha256 ([string]$layout.observer.path)
$targetVersion = (Get-Item -LiteralPath ([string]$layout.target.path)).VersionInfo.FileVersion
if ($targetHash -cne [string]$layout.target.sha256 -or
    $targetVersion -cne [string]$layout.target.version) {
    throw 'Private RaceSex target identity differs from its reviewed layout.'
}
if ($observerHash -cne [string]$layout.observer.sha256 -or
    [string]$layout.observer.mode -cne 'observe') {
    throw 'Private RaceSex observer identity or mode differs.'
}
$layoutReady = Test-PrivateLayoutReady $layout
$validation = [ordered]@{
    schema = $ValidationSchema
    status = if ($layoutReady) { 'ready' } else { 'blocked-private-layout-incomplete' }
    target = [ordered]@{ version = $targetVersion; sha256 = $targetHash }
    observer = [ordered]@{
        sha256 = $observerHash
        required_mode = 'observe'
        required_tool_surface = [int]$layout.observer.toolSurface
    }
    private_layout = [ordered]@{
        path = $layoutPath
        sha256 = Get-Sha256 $layoutPath
        status = [string]$layout.status
        ready = $layoutReady
    }
    process_attached = $false
    public_contract_emitted = $false
}
if ($ValidateOnly) {
    $validation | ConvertTo-Json -Depth 10
    return
}
if ($TargetProcessId -le 0) {
    throw 'TargetProcessId is required; this observer never launches or selects FalloutNV.exe.'
}
if (-not $layoutReady) {
    throw 'Private RaceSex live layout is incomplete; refusing process attachment.'
}
if ([string]::IsNullOrWhiteSpace($PublicContractOutputPath)) {
    throw 'PublicContractOutputPath is required for a live RaceSex observation.'
}
$publicOutput = [IO.Path]::GetFullPath($PublicContractOutputPath)
if (Test-Path -LiteralPath $publicOutput) { throw "Refusing to overwrite public contract: $publicOutput" }
$targetProcess = Get-Process -Id $TargetProcessId -ErrorAction Stop
if ((Get-Sha256 $targetProcess.Path) -cne $targetHash) {
    throw 'Explicit target PID does not own the reviewed FalloutNV.exe build.'
}

$mcp = $null
$sessionId = $null
$nextId = 1
try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [string]$layout.observer.path
    $startInfo.Arguments = 'mcp'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $originalConsoleInputEncoding = $null
    if ($null -ne $startInfo.PSObject.Properties['StandardInputEncoding']) {
        $startInfo.StandardInputEncoding = $utf8NoBom
    }
    else {
        $originalConsoleInputEncoding = [Console]::InputEncoding
        [Console]::InputEncoding = $utf8NoBom
    }
    $mcp = [Diagnostics.Process]::new()
    $mcp.StartInfo = $startInfo
    try {
        if (-not $mcp.Start()) { throw 'Failed to start private Win32 Ghidrust MCP.' }
        $null = $mcp.StandardInput
    }
    finally {
        if ($null -ne $originalConsoleInputEncoding) {
            [Console]::InputEncoding = $originalConsoleInputEncoding
        }
    }
    $init = Send-McpRequest -Process $mcp -Id $nextId -Method 'initialize' -Params @{
        protocolVersion = '2024-11-05'
        capabilities = @{}
        clientInfo = @{ name = 'opennv-fnv-racesex-camera-observer'; version = '1' }
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
    $session = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_attach' -Arguments @{ pid = $TargetProcessId; mode = 'observe' }
    $sessionId = [string]$session.session_id
    $unexpectedCapabilities = @($session.capabilities | Where-Object {
        [string]$_ -notin $AllowedObserveCapabilities
    })
    if ([string]$session.mode -cne 'observe' -or $unexpectedCapabilities.Count -ne 0) {
        throw 'Ghidrust did not establish the strict reviewed observe-only capability set.'
    }
    $modules = @(Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_modules' -Arguments @{ session_id = $sessionId })
    $main = @($modules | Where-Object { [string]$_.name -ieq [string]$layout.target.moduleName })
    if ($main.Count -ne 1) { throw 'Explicit PID has no unique reviewed FalloutNV.exe module.' }
    $moduleBase = [uint64]$main[0].base
    $null = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_regions' -Arguments @{ session_id = $sessionId; max = 4096 }
    $objects = Resolve-LiveObjects $mcp ([ref]$nextId) $sessionId $moduleBase $layout
    $snapshots = @()
    for ($sampleIndex = 0; $sampleIndex -lt $SampleCount; $sampleIndex++) {
        $snapshot = Read-LiveSnapshot $mcp ([ref]$nextId) $sessionId $objects $layout
        Assert-LiveSnapshot $snapshot $layout
        $snapshots += $snapshot
        if ($sampleIndex + 1 -lt $SampleCount) {
            Start-Sleep -Milliseconds $SampleIntervalMilliseconds
        }
    }
    Assert-StableSnapshots $snapshots ([double]$layout.constraints.floatTolerance)
    $observed = $snapshots[0]
    $kindKey = [string][int]$observed.projectionKindCode
    $axisKey = [string][int]$observed.fovAxisCode
    $contract = [ordered]@{
        '$schema' = 'fnv-racesex-preview-camera-v1.schema.json'
        schema = $ContractSchema
        engineBuild = '1.4.0.525'
        status = 'observed-live-unique'
        parityReady = $false
        cameraContractReady = $true
        ownedUi = [ordered]@{
            logicalPath = 'menus\chargen\race_sex_menu.xml'
            memberSha256 = '1c5e9daa5aa5eb9ae11044718874d0d27cb3665ec994487b2cc77a828805af98'
            menuClass = 'RaceSexMenu'
            renderedMenuClass = 'FORenderedMenuRaceSex'
            faceGrab = [ordered]@{
                tile = 'RSM_Face_Grab'; id = 1; x = 150; y = 50
                width = 680; height = 620; tileDepth = 100
                aspect = [ordered]@{ numerator = 34; denominator = 31 }
            }
            onMenuOpenTraits = [ordered]@{
                fullIn = @('user10', 'user11', 'user12')
                fullOut = @('user13', 'user14', 'user15')
                startingZoomPercent = 'user16'
                runtimeValuesStatus = 'observed'
            }
        }
        camera = [ordered]@{
            projection = [ordered]@{
                status = 'observed'
                value = [ordered]@{
                    kind = [string]$layout.constraints.projectionKinds.$kindKey
                    matrixRowMajor = @($observed.projectionMatrix)
                    fovDegrees = [double]$observed.fovDegrees
                    fovAxis = [string]$layout.constraints.fovAxes.$axisKey
                }
                blocker = ''
            }
            target = [ordered]@{
                status = 'observed'; value = @($observed.targetWorld); blocker = ''
            }
            distance = [ordered]@{
                status = 'observed'
                value = [ordered]@{
                    cameraWorld = @($observed.cameraWorld)
                    distance = [double]$observed.distance
                    fullIn = @($observed.fullIn)
                    fullOut = @($observed.fullOut)
                    startingZoomPercent = [double]$observed.startingZoomPercent
                }
                blocker = ''
            }
            frustum = [ordered]@{
                status = 'observed'
                value = [ordered]@{
                    near = [double]$observed.near
                    far = [double]$observed.far
                }
                blocker = ''
            }
            aspectBehavior = [ordered]@{
                status = 'observed'
                value = [ordered]@{
                    aspect = [double]$observed.aspect
                    policy = [string]$layout.constraints.aspectPolicy
                }
                blocker = ''
            }
        }
        evidence = [ordered]@{
            classification = 'private-clean-room-observe-only-analysis'
            retailProcessLaunched = $false
            liveAttachUsed = $true
            staticClassRoute = 'confirmed'
            staticCodeBody = 'unavailable-encrypted-at-rest'
        }
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publicOutput) | Out-Null
    [IO.File]::WriteAllText(
        $publicOutput,
        ($contract | ConvertTo-Json -Depth 20) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    [ordered]@{
        schema = $ValidationSchema
        status = 'passed'
        process_attached = $true
        public_contract_emitted = $true
        public_contract_path = $publicOutput
        public_contract_sha256 = Get-Sha256 $publicOutput
        sample_count = $SampleCount
        unique_object_count = $objects.Count
    } | ConvertTo-Json -Depth 10
}
finally {
    if ($null -ne $mcp -and -not [string]::IsNullOrWhiteSpace($sessionId)) {
        try {
            $null = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
                -Name 'process_detach' -Arguments @{ session_id = $sessionId }
        }
        catch { Write-Warning "Observe-only detach failed: $($_.Exception.Message)" }
    }
    if ($null -ne $mcp -and -not $mcp.HasExited) {
        try { $mcp.StandardInput.Close() } catch {}
        if (-not $mcp.WaitForExit(2000)) { $mcp.Kill() }
    }
    if ($null -ne $mcp) { $mcp.Dispose() }
}
