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
    [Parameter(ParameterSetName = 'Launch')]
    [string]$StartingCell = '',
    [ValidateRange(1, 60)]
    [int]$ObserveSeconds = 4,
    [ValidateRange(1, 20)]
    [int]$SampleCount = 3,
    [ValidateRange(0, [int]::MaxValue)]
    [int]$AwaitReferenceLoadSeconds = 0,
    [switch]$CaptureStage10Contract,
    [string]$ContractOutputPath = '',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedGameSha256 = 'c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e'
$expectedGameVersion = '1.7.0.4'
$expectedGhidrustSha256 = '10070829e620ae2e1e26d338a38bc4dcb21d8c855f1fa3d846e03f71b812cc41'
$expectedToolSurface = 8
$regionMaximumCount = 4096
$ReferenceScanMaximumHits = 64
$PointerScanMaximumHits = 256
$referenceLoadPollMilliseconds =
    [int]([TimeSpan]::TicksPerSecond / [TimeSpan]::TicksPerMillisecond)
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$privateEvidenceRoot = 'D:\Dev\Tools\Ghidrust\workspace\evidence\fallout3_1_7_0_4\cg00'
$gameExe = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Fallout3.exe'))
$ghidrustExe = [IO.Path]::GetFullPath($GhidrustPath)

# Exact Fallout3.exe 1.7.0.4 RVAs and layouts. These values are target-wide
# binary contracts, not Vault 101 staging values. The NiCamera and
# NiControllerSequence layouts were recovered from their exact constructors;
# the BSSceneGraph active-camera field was recovered from its exact constructor.
$bsSceneGraphSingletonRva = [uint64]0x00F18E64
$bsSceneGraphVtableRva = [uint64]0x00C048C0
$bsSceneGraphActiveCameraOffset = 0xAC
$niCameraVtableRva = [uint64]0x00C20C4C
$niControllerSequenceVtableRva = [uint64]0x00C27A88
$camera1stFixedStringGlobalRva = [uint64]0x00E21B7C
$tesObjectRefrTypeId = 0x3A
$tesObjectRefrReferenceIdOffset = 0x0C
$tesObjectRefrBaseFormOffset = 0x1C
$tesObjectRefrRotationOffset = 0x20
$tesObjectRefrPositionOffset = 0x2C
$tesObjectRefrScaleOffset = 0x38
$tesObjectRefrParentCellOffset = 0x3C
# FOSE 1.7 structurally corroborates this field. Promotion additionally
# requires its live value to resolve to a valid target-build NiAV object.
$tesObjectRefrRenderStateOffset = 0x5C
$referenceRenderStateNodeOffset = 0x14
$niAvNameOffset = 0x08
$niAvParentOffset = 0x18
$niAvControllerOffset = 0x1C
$niAvFlagsOffset = 0x30
$niAvLocalTransformOffset = 0x34
$niAvWorldTransformOffset = 0x68
$niAvSize = 0x9C
$niAvAppCulledFlag = [uint32]0x00000001
$niControllerSequenceSize = 0x68
$niControllerSequenceNameOffset = 0x08
$niControllerSequenceCycleOffset = 0x24
$niControllerSequenceFrequencyOffset = 0x28
$niControllerSequenceBeginOffset = 0x2C
$niControllerSequenceEndOffset = 0x30
$niControllerSequenceLastOffset = 0x34
$niControllerSequenceLastScaledOffset = 0x3C
$niControllerSequenceStateOffset = 0x44
$niControllerSequenceAccumulationRootOffset = 0x60
$niCameraFrustumOffset = 0xDC
$niCameraNearOffset = 0xEC
$niCameraFarOffset = 0xF0
$niCameraOrthographicOffset = 0xF4
$niCameraViewportOffset = 0x100
$niCameraSize = 0x114
$niCameraForwardAxis = 0
$MaximumNiAvAncestryDepth = 64
$singlePrecisionValueSize = 4
$matrixOrder = 3
$MinimumNonSingularScale = [single]1.0e-6
$RadiansToDegrees = 180.0 / [Math]::PI
$Cg00Stage10 = 10
$HexadecimalRadix = 16
$MaximumRemoteStringBytes = 128
$TesObjectRefrFlagsOffset = 8
$ProjectionMatrixValueCount = 16
$ContractJsonDepth = 30

$stage10Actors = [ordered]@{
    player = [ordered]@{
        reference_form_id = [uint32]0x00000014
        package_form_id = [uint32]0x00038EA3
        idle_form_id = [uint32]0x00069EF9
        activation_stage = $null
        sequence_name = 'SpecialIdle_CG00PlayerSection01'
    }
    father = [ordered]@{
        reference_form_id = [uint32]0x000290A7
        package_form_id = [uint32]0x0006B245
        idle_form_id = [uint32]0x00068AB0
        activation_stage = $Cg00Stage10
        sequence_name = 'SpecialIdle_CG00DadSection01'
    }
    doctor = [ordered]@{
        reference_form_id = [uint32]0x000290A5
        package_form_id = [uint32]0x0006A813
        idle_form_id = [uint32]0x00068AB1
        activation_stage = $Cg00Stage10
        sequence_name = 'SpecialIdle_CG00DrLiSection01'
    }
    mother = [ordered]@{
        reference_form_id = [uint32]0x0005EDE0
        package_form_id = [uint32]0x0006B244
        idle_form_id = [uint32]0x00069EF4
        activation_stage = $Cg00Stage10
        sequence_name = 'SpecialIdle_CG00MomSection01'
    }
}

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

function ConvertTo-AsciiAob([string]$Value) {
    $bytes = [Text.Encoding]::ASCII.GetBytes($Value + [char]0)
    return (($bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
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

function Get-UInt32([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Get-Single([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToSingle($Bytes, $Offset)
}

function Get-Vector3([byte[]]$Bytes, [int]$Offset) {
    return @(
        Get-Single $Bytes $Offset
        Get-Single $Bytes ($Offset + $singlePrecisionValueSize)
        Get-Single $Bytes ($Offset + 2 * $singlePrecisionValueSize)
    )
}

function Test-FiniteSingle([single]$Value) {
    return -not [single]::IsNaN($Value) -and -not [single]::IsInfinity($Value)
}

function Test-FiniteVector([object[]]$Values) {
    return $Values.Count -eq $matrixOrder -and
        @($Values | Where-Object { -not (Test-FiniteSingle ([single]$_)) }).Count -eq 0
}

function Get-NiTransform([byte[]]$Bytes, [int]$Offset) {
    $rotation = for ($index = 0; $index -lt ($matrixOrder * $matrixOrder); $index++) {
        Get-Single $Bytes ($Offset + $index * $singlePrecisionValueSize)
    }
    $translation = Get-Vector3 $Bytes ($Offset + 0x24)
    $scale = Get-Single $Bytes ($Offset + 0x30)
    if (@($rotation | Where-Object { -not (Test-FiniteSingle ([single]$_)) }).Count -gt 0 -or
        -not (Test-FiniteVector $translation) -or
        -not (Test-FiniteSingle $scale) -or
        [Math]::Abs($scale) -lt $minimumNonSingularScale) {
        throw 'NiTransform contains a non-finite or singular source value.'
    }
    return [ordered]@{
        rotation_row_major = @($rotation)
        translation_game_units = @($translation)
        scale = $scale
    }
}

function Read-RemoteUInt32(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address
) {
    $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $Address -Size 4
    return Get-UInt32 $bytes 0
}

function Read-RemoteCString(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address,
    [int]$MaximumBytes = $MaximumRemoteStringBytes
) {
    if ($Address -eq 0) { return $null }
    $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $Address -Size $MaximumBytes
    $terminator = [Array]::IndexOf($bytes, [byte]0)
    if ($terminator -lt 0) {
        throw ('Remote string at 0x{0:X} is not terminated within {1} bytes.' -f
            $Address, $MaximumBytes)
    }
    return [Text.Encoding]::ASCII.GetString($bytes, 0, $terminator)
}

function Get-NiAvSnapshot(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize
) {
    if ($Address -eq 0) { throw 'NiAV address is null.' }
    $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $Address -Size $niAvSize
    $vtable = [uint64](Get-UInt32 $bytes 0)
    if ($vtable -lt $ModuleBase -or $vtable -ge ($ModuleBase + $ModuleSize)) {
        throw ('NiAV candidate 0x{0:X} has a vtable outside Fallout3.exe: 0x{1:X}.' -f
            $Address, $vtable)
    }
    $nameAddress = [uint64](Get-UInt32 $bytes $niAvNameOffset)
    $flags = Get-UInt32 $bytes $niAvFlagsOffset
    return [ordered]@{
        address = $Address
        vtable = $vtable
        name_address = $nameAddress
        name = if ($nameAddress -eq 0) { $null } else {
            Read-RemoteCString -Process $Process -NextId $NextId `
                -SessionId $SessionId -Address $nameAddress
        }
        parent = [uint64](Get-UInt32 $bytes $niAvParentOffset)
        controller = [uint64](Get-UInt32 $bytes $niAvControllerOffset)
        flags = $flags
        app_culled = ($flags -band $niAvAppCulledFlag) -ne 0
        visible = ($flags -band $niAvAppCulledFlag) -eq 0
        local_transform = Get-NiTransform $bytes $niAvLocalTransformOffset
        world_transform = Get-NiTransform $bytes $niAvWorldTransformOffset
    }
}

function Get-NiAvAncestry(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$StartAddress,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize
) {
    $ancestry = [Collections.Generic.List[uint64]]::new()
    $seen = [Collections.Generic.HashSet[uint64]]::new()
    $current = $StartAddress
    while ($current -ne 0 -and $ancestry.Count -lt $maximumNiAvAncestryDepth) {
        if (-not $seen.Add($current)) {
            throw ('NiAV ancestry cycle detected at 0x{0:X}.' -f $current)
        }
        $ancestry.Add($current)
        $node = Get-NiAvSnapshot -Process $Process -NextId $NextId `
            -SessionId $SessionId -Address $current `
            -ModuleBase $ModuleBase -ModuleSize $ModuleSize
        $current = [uint64]$node.parent
    }
    if ($current -ne 0) {
        throw "NiAV ancestry exceeded $maximumNiAvAncestryDepth nodes."
    }
    return @($ancestry)
}

function Resolve-LiveReferenceFromSequence(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint32]$ReferenceFormId,
    [uint64]$SequenceRoot,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize
) {
    $ancestry = Get-NiAvAncestry -Process $Process -NextId $NextId `
        -SessionId $SessionId -StartAddress $SequenceRoot `
        -ModuleBase $ModuleBase -ModuleSize $ModuleSize
    $candidates = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[uint64]]::new()
    foreach ($nodeAddress in $ancestry) {
        $nodePointerScan = Invoke-McpTool -Process $Process -NextId $NextId `
            -Name 'process_scan' -Arguments @{
                session_id = $SessionId
                aob = ConvertTo-UInt32Aob ([uint32]$nodeAddress)
                max_hits = $pointerScanMaximumHits
            }
        foreach ($nodePointerHit in @($nodePointerScan.hits)) {
            $nodePointerAddress = [uint64]$nodePointerHit.va
            if ($nodePointerAddress -lt $referenceRenderStateNodeOffset) { continue }
            $renderState = $nodePointerAddress - $referenceRenderStateNodeOffset
            try {
                $renderStateBytes = Read-RemoteBytes -Process $Process -NextId $NextId `
                    -SessionId $SessionId -Address $renderState `
                    -Size ($referenceRenderStateNodeOffset + 4)
                if ([uint64](Get-UInt32 $renderStateBytes $referenceRenderStateNodeOffset) -ne
                    $nodeAddress) { continue }
                $renderStatePointerScan = Invoke-McpTool `
                    -Process $Process -NextId $NextId -Name 'process_scan' -Arguments @{
                        session_id = $SessionId
                        aob = ConvertTo-UInt32Aob ([uint32]$renderState)
                        max_hits = $pointerScanMaximumHits
                    }
                foreach ($renderStatePointerHit in @($renderStatePointerScan.hits)) {
                    $pointerAddress = [uint64]$renderStatePointerHit.va
                    if ($pointerAddress -lt $tesObjectRefrRenderStateOffset) { continue }
                    $address = $pointerAddress - $tesObjectRefrRenderStateOffset
                    if (-not $seen.Add($address)) { continue }
                    try {
                        $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
                            -SessionId $SessionId -Address $address `
                            -Size ($tesObjectRefrRenderStateOffset + 4)
                        if ($bytes[4] -ne $tesObjectRefrTypeId -or
                            (Get-UInt32 $bytes $tesObjectRefrReferenceIdOffset) -ne
                                $ReferenceFormId -or
                            [uint64](Get-UInt32 $bytes $tesObjectRefrRenderStateOffset) -ne
                                $renderState) {
                            continue
                        }
                        $baseForm = [uint64](Get-UInt32 $bytes $tesObjectRefrBaseFormOffset)
                        $parentCell = [uint64](Get-UInt32 $bytes $tesObjectRefrParentCellOffset)
                        $scale = Get-Single $bytes $tesObjectRefrScaleOffset
                        $rotation = Get-Vector3 $bytes $tesObjectRefrRotationOffset
                        $position = Get-Vector3 $bytes $tesObjectRefrPositionOffset
                        if ($baseForm -eq 0 -or $parentCell -eq 0 -or
                            -not (Test-FiniteSingle $scale) -or $scale -le 0.0 -or
                            -not (Test-FiniteVector $rotation) -or
                            -not (Test-FiniteVector $position)) {
                            continue
                        }
                        $node = Get-NiAvSnapshot -Process $Process -NextId $NextId `
                            -SessionId $SessionId -Address $nodeAddress `
                            -ModuleBase $ModuleBase -ModuleSize $ModuleSize
                        $candidates.Add([ordered]@{
                            address = $address
                            reference_form_id = ('{0:x8}' -f $ReferenceFormId)
                            type_id = $bytes[4]
                            flags = Get-UInt32 $bytes $TesObjectRefrFlagsOffset
                            base_form_address = $baseForm
                            parent_cell_address = $parentCell
                            authored_rotation_radians = @($rotation)
                            authored_position_game_units = @($position)
                            authored_scale = $scale
                            render_state_address = $renderState
                            rendered_node = $node
                            resolution_path =
                                'active-Section01-sequence -> accumulation-root ancestry -> render-state node -> owning TESObjectREFR'
                        })
                    }
                    catch {}
                }
            }
            catch {}
        }
    }
    return [ordered]@{
        reference_form_id = ('{0:x8}' -f $ReferenceFormId)
        sequence_root = $SequenceRoot
        ancestry_node_count = $ancestry.Count
        candidates = @($candidates)
        unique = $candidates.Count -eq 1
        discovery =
            'semantic reverse join from active source-named sequence; FormID validates ownership and is never scanned to discover the object'
    }
}

function Resolve-ControllerSequence(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [string]$SequenceName,
    [uint64]$ExpectedActorNode,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize
) {
    $nameScan = Invoke-McpTool -Process $Process -NextId $NextId `
        -Name 'process_scan' -Arguments @{
            session_id = $SessionId
            aob = ConvertTo-AsciiAob $SequenceName
            max_hits = $referenceScanMaximumHits
        }
    $expectedVtable = $ModuleBase + $niControllerSequenceVtableRva
    $candidates = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[uint64]]::new()
    foreach ($nameHit in @($nameScan.hits)) {
        $nameAddress = [uint64]$nameHit.va
        $pointerScan = Invoke-McpTool -Process $Process -NextId $NextId `
            -Name 'process_scan' -Arguments @{
                session_id = $SessionId
                aob = ConvertTo-UInt32Aob ([uint32]$nameAddress)
                max_hits = $pointerScanMaximumHits
            }
        foreach ($pointerHit in @($pointerScan.hits)) {
            $pointerAddress = [uint64]$pointerHit.va
            if ($pointerAddress -lt $niControllerSequenceNameOffset) { continue }
            $address = $pointerAddress - $niControllerSequenceNameOffset
            if (-not $seen.Add($address)) { continue }
            try {
                $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
                    -SessionId $SessionId -Address $address -Size $niControllerSequenceSize
                if ([uint64](Get-UInt32 $bytes 0) -ne $expectedVtable -or
                    [uint64](Get-UInt32 $bytes $niControllerSequenceNameOffset) -ne $nameAddress) {
                    continue
                }
                $cycle = Get-UInt32 $bytes $niControllerSequenceCycleOffset
                $frequency = Get-Single $bytes $niControllerSequenceFrequencyOffset
                $begin = Get-Single $bytes $niControllerSequenceBeginOffset
                $end = Get-Single $bytes $niControllerSequenceEndOffset
                $last = Get-Single $bytes $niControllerSequenceLastOffset
                $lastScaled = Get-Single $bytes $niControllerSequenceLastScaledOffset
                $state = Get-UInt32 $bytes $niControllerSequenceStateOffset
                $root = [uint64](Get-UInt32 $bytes $niControllerSequenceAccumulationRootOffset)
                if ($cycle -gt 2 -or -not (Test-FiniteSingle $frequency) -or
                    $frequency -le 0.0 -or -not (Test-FiniteSingle $begin) -or
                    -not (Test-FiniteSingle $end) -or $end -lt $begin -or
                    -not (Test-FiniteSingle $last) -or
                    -not (Test-FiniteSingle $lastScaled) -or $state -eq 0 -or $root -eq 0) {
                    continue
                }
                $ancestry = Get-NiAvAncestry -Process $Process -NextId $NextId `
                    -SessionId $SessionId -StartAddress $root `
                    -ModuleBase $ModuleBase -ModuleSize $ModuleSize
                if ($ExpectedActorNode -ne 0 -and
                    $ancestry -notcontains $ExpectedActorNode) { continue }
                $candidates.Add([ordered]@{
                    address = $address
                    name = $SequenceName
                    name_address = $nameAddress
                    cycle_type = $cycle
                    frequency = $frequency
                    begin_time_seconds = $begin
                    end_time_seconds = $end
                    last_time_seconds = $last
                    last_scaled_time_seconds = $lastScaled
                    state = $state
                    accumulation_root = $root
                    actor_node_ancestry_join = if ($ExpectedActorNode -eq 0) {
                        $null
                    } else { $ExpectedActorNode }
                })
            }
            catch {}
        }
    }
    return [ordered]@{
        name = $SequenceName
        string_hit_count = @($nameScan.hits).Count
        candidates = @($candidates)
        unique_active_actor_join = $candidates.Count -eq 1
    }
}

function Resolve-Camera1stNode(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$PlayerSequenceRoot,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize
) {
    $nameAddress = [uint64](Read-RemoteUInt32 -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address ($ModuleBase + $camera1stFixedStringGlobalRva))
    if ($nameAddress -eq 0) { throw 'Camera1st fixed-string global is null.' }
    $name = Read-RemoteCString -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $nameAddress
    if ($name -ne 'Camera1st') {
        throw "Camera1st fixed-string global resolved to '$name'."
    }
    $pointerScan = Invoke-McpTool -Process $Process -NextId $NextId `
        -Name 'process_scan' -Arguments @{
            session_id = $SessionId
            aob = ConvertTo-UInt32Aob ([uint32]$nameAddress)
            max_hits = $pointerScanMaximumHits
        }
    $candidates = [Collections.Generic.List[object]]::new()
    foreach ($hit in @($pointerScan.hits)) {
        $pointerAddress = [uint64]$hit.va
        if ($pointerAddress -lt $niAvNameOffset) { continue }
        $address = $pointerAddress - $niAvNameOffset
        try {
            $node = Get-NiAvSnapshot -Process $Process -NextId $NextId `
                -SessionId $SessionId -Address $address `
                -ModuleBase $ModuleBase -ModuleSize $ModuleSize
            if ([uint64]$node.name_address -ne $nameAddress) { continue }
            $ancestry = Get-NiAvAncestry -Process $Process -NextId $NextId `
                -SessionId $SessionId -StartAddress $address `
                -ModuleBase $ModuleBase -ModuleSize $ModuleSize
            if ($ancestry -notcontains $PlayerSequenceRoot) { continue }
            $candidates.Add($node)
        }
        catch {}
    }
    return [ordered]@{
        fixed_string_global_address = $ModuleBase + $camera1stFixedStringGlobalRva
        fixed_string_address = $nameAddress
        candidates = @($candidates)
        unique_player_sequence_join = $candidates.Count -eq 1
    }
}

function Resolve-ActiveCamera(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize
) {
    $sceneGraphAddress = $ModuleBase + $bsSceneGraphSingletonRva
    $sceneGraphBytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $sceneGraphAddress `
        -Size ($bsSceneGraphActiveCameraOffset + 4)
    $expectedSceneGraphVtable = $ModuleBase + $bsSceneGraphVtableRva
    $actualSceneGraphVtable = [uint64](Get-UInt32 $sceneGraphBytes 0)
    if ($actualSceneGraphVtable -ne $expectedSceneGraphVtable) {
        throw ('BSSceneGraph singleton vtable differs: expected=0x{0:X} actual=0x{1:X}' -f
            $expectedSceneGraphVtable, $actualSceneGraphVtable)
    }
    $cameraAddress = [uint64](Get-UInt32 $sceneGraphBytes $bsSceneGraphActiveCameraOffset)
    $cameraBytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $cameraAddress -Size $niCameraSize
    $expectedCameraVtable = $ModuleBase + $niCameraVtableRva
    $actualCameraVtable = [uint64](Get-UInt32 $cameraBytes 0)
    if ($actualCameraVtable -ne $expectedCameraVtable) {
        throw ('Active BSSceneGraph camera vtable differs: expected=0x{0:X} actual=0x{1:X}' -f
            $expectedCameraVtable, $actualCameraVtable)
    }
    $near = Get-Single $cameraBytes $niCameraNearOffset
    $far = Get-Single $cameraBytes $niCameraFarOffset
    $frustum = for ($index = 0; $index -lt 4; $index++) {
        Get-Single $cameraBytes ($niCameraFrustumOffset + $index * 4)
    }
    $viewport = for ($index = 0; $index -lt 4; $index++) {
        Get-Single $cameraBytes ($niCameraViewportOffset + $index * 4)
    }
    $derivedWorldToClip = for ($index = 0; $index -lt $ProjectionMatrixValueCount; $index++) {
        Get-Single $cameraBytes (0x9C + $index * 4)
    }
    if (-not (Test-FiniteSingle $near) -or -not (Test-FiniteSingle $far) -or
        $near -le 0.0 -or $far -le $near -or
        @($frustum | Where-Object { -not (Test-FiniteSingle ([single]$_)) }).Count -gt 0 -or
        @($viewport | Where-Object { -not (Test-FiniteSingle ([single]$_)) }).Count -gt 0 -or
        @($derivedWorldToClip | Where-Object { -not (Test-FiniteSingle ([single]$_)) }).Count -gt 0) {
        throw 'Active NiCamera frustum, viewport, or derived projection is invalid.'
    }
    return [ordered]@{
        bs_scene_graph_address = $sceneGraphAddress
        camera_address = $cameraAddress
        vtable = $actualCameraVtable
        world_transform = Get-NiTransform $cameraBytes $niAvWorldTransformOffset
        frustum = [ordered]@{
            left = $frustum[0]
            right = $frustum[1]
            top = $frustum[2]
            bottom = $frustum[3]
            near_game_units = $near
            far_game_units = $far
            orthographic = $cameraBytes[$niCameraOrthographicOffset] -ne 0
            horizontal_fov_degrees = if ($cameraBytes[$niCameraOrthographicOffset] -ne 0) {
                $null
            } else {
                ([Math]::Atan($frustum[1] / $near) -
                    [Math]::Atan($frustum[0] / $near)) * $radiansToDegrees
            }
            vertical_fov_degrees = if ($cameraBytes[$niCameraOrthographicOffset] -ne 0) {
                $null
            } else {
                ([Math]::Atan($frustum[2] / $near) -
                    [Math]::Atan($frustum[3] / $near)) * $radiansToDegrees
            }
        }
        viewport_normalized = @($viewport)
        derived_world_to_clip_row_major = @($derivedWorldToClip)
    }
}

function ConvertTo-CameraSpace(
    [object]$CameraTransform,
    [object[]]$WorldPoint
) {
    $translation = @($CameraTransform.translation_game_units)
    $rotation = @($CameraTransform.rotation_row_major)
    $scale = [single]$CameraTransform.scale
    $delta = for ($axis = 0; $axis -lt $matrixOrder; $axis++) {
        [single]$WorldPoint[$axis] - [single]$translation[$axis]
    }
    # NiTransform publishes world = scale * R * local + translation, so the
    # exact inverse is transpose(R) * (world - translation) / scale.
    $cameraSpace = for ($column = 0; $column -lt $matrixOrder; $column++) {
        $value = 0.0
        for ($row = 0; $row -lt $matrixOrder; $row++) {
            $value += [single]$rotation[$row * $matrixOrder + $column] * $delta[$row]
        }
        $value / $scale
    }
    return @($cameraSpace)
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

function Get-HitContext(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [object]$Hit,
    [object[]]$Regions
) {
    $hitAddress = [uint64]$Hit.va
    $region = @($Regions | Where-Object {
        $base = [uint64]$_.base
        $hitAddress -ge $base -and $hitAddress -lt ($base + [uint64]$_.size)
    }) | Select-Object -First 1
    if ($null -eq $region) {
        $refreshedRegions = @(Invoke-McpTool -Process $Process -NextId $NextId `
            -Name 'process_regions' -Arguments @{
                session_id = $SessionId
                max = $regionMaximumCount
            })
        $region = @($refreshedRegions | Where-Object {
            $base = [uint64]$_.base
            $hitAddress -ge $base -and $hitAddress -lt ($base + [uint64]$_.size)
        }) | Select-Object -First 1
    }
    if ($null -eq $region) {
        return [ordered]@{
            hit_va = $hitAddress
            hit_preview_hex = $Hit.preview_hex
            module = if ($Hit.PSObject.Properties.Name -contains 'module') {
                $Hit.module
            } else { $null }
            rva = if ($Hit.PSObject.Properties.Name -contains 'rva') {
                $Hit.rva
            } else { $null }
            region = $null
            context_start_va = $null
            context_bytes_read = 0
            context_hex = $null
            context_unavailable = 'scan-hit-outside-refreshed-region-snapshot'
        }
    }
    $regionBase = [uint64]$region.base
    $regionEnd = $regionBase + [uint64]$region.size
    $contextStart = if ($hitAddress -gt ($regionBase + 256)) {
        $hitAddress - 256
    } else {
        $regionBase
    }
    $contextEnd = [Math]::Min([double]$regionEnd, [double]($hitAddress + 272))
    $contextSize = [int]($contextEnd - $contextStart)
    $read = Invoke-McpTool -Process $Process -NextId $NextId `
        -Name 'process_read' -Arguments @{
            session_id = $SessionId
            addr = ('0x{0:X}' -f $contextStart)
            size = $contextSize
        }
    return [ordered]@{
        hit_va = $hitAddress
        hit_preview_hex = $Hit.preview_hex
        module = if ($Hit.PSObject.Properties.Name -contains 'module') {
            $Hit.module
        } else { $null }
        rva = if ($Hit.PSObject.Properties.Name -contains 'rva') {
            $Hit.rva
        } else { $null }
        region = [ordered]@{
            base = $regionBase
            size = [uint64]$region.size
            protect = [string]$region.protect
            state = [string]$region.state
            type = [string]$region.typ
        }
        context_start_va = [uint64]$read.va
        context_bytes_read = [int]$read.bytes_read
        context_hex = [string]$read.hex
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
if (-not [string]::IsNullOrWhiteSpace($StartingCell) -and -not $Launch) {
    throw 'StartingCell is valid only with -Launch.'
}
if (-not [string]::IsNullOrWhiteSpace($StartingCell) -and
    $StartingCell -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') {
    throw "StartingCell is not a canonical editor ID: $StartingCell"
}
if ($Launch -and $AwaitReferenceLoadSeconds -gt 0) {
    throw 'AwaitReferenceLoadSeconds requires an already running user-started retail process.'
}
if ($CaptureStage10Contract -and $Launch) {
    throw 'CaptureStage10Contract requires an already running user-started retail process.'
}
if (-not $CaptureStage10Contract -and
    -not [string]::IsNullOrWhiteSpace($ContractOutputPath)) {
    throw 'ContractOutputPath requires CaptureStage10Contract.'
}
if ($ValidateOnly) {
    $identity.observer['stage10_contract_layout'] = [ordered]@{
        target = 'Fallout3.exe-1.7.0.4'
        bs_scene_graph_singleton_rva = ('0x{0:X8}' -f $bsSceneGraphSingletonRva)
        bs_scene_graph_active_camera_offset = ('0x{0:X}' -f $bsSceneGraphActiveCameraOffset)
        ni_camera_vtable_rva = ('0x{0:X8}' -f $niCameraVtableRva)
        ni_controller_sequence_vtable_rva = ('0x{0:X8}' -f $niControllerSequenceVtableRva)
        camera1st_fixed_string_global_rva = ('0x{0:X8}' -f $camera1stFixedStringGlobalRva)
        camera_forward_axis = 'NiCamera-local-positive-X'
        live_reference_render_join = 'TESObjectREFR+0x5c -> render-state+0x14 -> NiAV'
    }
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
$contractOutput = $null
if ($CaptureStage10Contract) {
    if ([string]::IsNullOrWhiteSpace($ContractOutputPath)) {
        $contractOutput = [IO.Path]::ChangeExtension($output, $null) + '-stage10-contract.json'
    }
    else {
        $contractOutput = [IO.Path]::GetFullPath($ContractOutputPath)
    }
    if (-not (Test-PathWithin -Path $contractOutput -Root $privateEvidenceRoot) -or
        (Test-PathWithin -Path $contractOutput -Root $repoRoot)) {
        throw "Stage10 contract output must remain private below ${privateEvidenceRoot}: $contractOutput"
    }
    if (Test-Path -LiteralPath $contractOutput) {
        throw "Refusing to overwrite retail stage10 contract evidence: $contractOutput"
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $contractOutput) | Out-Null
}

$mcp = $null
$launchedProcess = $null
$shadowDirectory = $null
$falloutIni = $null
$falloutIniBackup = $null
$falloutIniBackupVerified = $false
$originalIniSha256 = $null
$modifiedIniSha256 = $null
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
        if (-not [string]::IsNullOrWhiteSpace($StartingCell)) {
            $falloutIni = Join-Path ([Environment]::GetFolderPath('MyDocuments')) `
                'My Games\Fallout3\FALLOUT.INI'
            if (-not (Test-Path -LiteralPath $falloutIni -PathType Leaf)) {
                throw "Missing Fallout 3 user INI required for StartingCell staging: $falloutIni"
            }
            $falloutIniBackup = Join-Path $privateEvidenceRoot `
                ('.Fallout.ini-backup-' + [Guid]::NewGuid().ToString('N'))
            if (Test-Path -LiteralPath $falloutIniBackup) {
                throw "Refusing to overwrite Fallout 3 INI backup: $falloutIniBackup"
            }
            $originalIniSha256 = Get-Sha256 $falloutIni
            Copy-Item -LiteralPath $falloutIni -Destination $falloutIniBackup
            if ((Get-Sha256 $falloutIniBackup) -ne $originalIniSha256) {
                throw 'Fallout 3 INI backup hash mismatch.'
            }
            $falloutIniBackupVerified = $true
            $iniEncoding = [Text.Encoding]::Default
            $iniText = [IO.File]::ReadAllText($falloutIni, $iniEncoding)
            $startingCellMatches = [regex]::Matches(
                $iniText, '(?im)^SStartingCell\s*=.*$')
            if ($startingCellMatches.Count -ne 1) {
                throw "Expected exactly one SStartingCell entry, found $($startingCellMatches.Count)."
            }
            $stagedIniText = [regex]::Replace(
                $iniText,
                '(?im)^SStartingCell\s*=.*$',
                "SStartingCell=$StartingCell")
            [IO.File]::WriteAllText($falloutIni, $stagedIniText, $iniEncoding)
            $modifiedIniSha256 = Get-Sha256 $falloutIni
            if ($modifiedIniSha256 -eq $originalIniSha256) {
                throw 'Fallout 3 StartingCell staging did not change the user INI.'
            }
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
        if (-not $mcp.Start()) {
            throw 'Failed to start the private Win32 Ghidrust MCP.'
        }
        # Windows PowerShell 5.1 lacks ProcessStartInfo.StandardInputEncoding,
        # so materialize the redirected writer while Console.InputEncoding is
        # temporarily configured as BOM-free UTF-8.
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
    $regions = @(Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_regions' -Arguments @{
            session_id = $sessionId
            max = $regionMaximumCount
        })
    $mainModule = @($modules | Where-Object { $_.name -ieq 'Fallout3.exe' })
    if ($mainModule.Count -ne 1) {
        throw "Expected one Fallout3.exe module, found $($mainModule.Count)."
    }
    $moduleBase = [uint64]$mainModule[0].base
    $moduleSize = [uint64]$mainModule[0].size

    $patterns = [ordered]@{
        doctor_li_reference_form_id = ConvertTo-UInt32Aob 0x000290A5
        doctor_li_authored_position = ConvertTo-Aob @(-5138.02392578125, -7313.3408203125, 7542.5361328125)
        doctor_li_stage0_marker_position = ConvertTo-Aob @(-5286.771484375, -7202.22998046875, 7542.5361328125)
    }
    $referenceLoadTransition = [ordered]@{
        requested = $AwaitReferenceLoadSeconds -gt 0
        armed_before_reference_load = $false
        first_seen_elapsed_milliseconds = $null
        poll_count = 0
        candidate_hit_count = 0
        classification = if ($AwaitReferenceLoadSeconds -gt 0) {
            'pending-read-only-reference-load-transition'
        }
        else {
            'not-requested'
        }
        limitation =
            'A reference-load transition proves that the record became resident. It does not by itself prove New Game, quest stage, package state, or camera state.'
    }
    if ($AwaitReferenceLoadSeconds -gt 0) {
        $initialScan = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
            -Name 'process_scan' -Arguments @{
                session_id = $sessionId
                aob = $patterns.doctor_li_reference_form_id
                max_hits = $referenceScanMaximumHits
            }
        $initialHits = @($initialScan.hits)
        $referenceLoadTransition.poll_count++
        if ($initialHits.Count -ne 0) {
            throw 'Doctor Li reference identity was already resident; arm the observer before starting a genuine retail New Game.'
        }
        $referenceLoadTransition.armed_before_reference_load = $true
        $awaitStartedAt = [DateTime]::UtcNow
        while ($true) {
            if (([DateTime]::UtcNow - $awaitStartedAt).TotalSeconds -ge
                $AwaitReferenceLoadSeconds) {
                throw "Doctor Li reference did not become resident within $AwaitReferenceLoadSeconds seconds."
            }
            Start-Sleep -Milliseconds $referenceLoadPollMilliseconds
            $transitionScan = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
                -Name 'process_scan' -Arguments @{
                    session_id = $sessionId
                    aob = $patterns.doctor_li_reference_form_id
                    max_hits = $referenceScanMaximumHits
                }
            $transitionHits = @($transitionScan.hits)
            $referenceLoadTransition.poll_count++
            if ($transitionHits.Count -gt 0) {
                $referenceLoadTransition.first_seen_elapsed_milliseconds =
                    [int]([DateTime]::UtcNow - $awaitStartedAt).TotalMilliseconds
                $referenceLoadTransition.candidate_hit_count = $transitionHits.Count
                $referenceLoadTransition.classification =
                    'read-only-reference-load-transition-observed-not-stage-proof'
                $regions = @(Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
                    -Name 'process_regions' -Arguments @{
                        session_id = $sessionId
                        max = $regionMaximumCount
                    })
                break
            }
        }
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
                    max_hits = $referenceScanMaximumHits
                }
            $contexts = @($scan.hits | ForEach-Object {
                Get-HitContext -Process $mcp -NextId ([ref]$nextId) `
                    -SessionId $sessionId -Hit $_ -Regions $regions
            })
            $matches[$pattern.Key] = [ordered]@{
                scan = $scan
                contexts = $contexts
            }
        }
        $samples.Add([ordered]@{
            index = $sampleIndex
            elapsed_milliseconds = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds
            matches = $matches
        })
    }

    $stage10Resolution = [ordered]@{
        requested = [bool]$CaptureStage10Contract
        classification = if ($CaptureStage10Contract) {
            'pending-live-stage10-resolution'
        } else { 'not-requested' }
        actors = [ordered]@{}
        sequences = [ordered]@{}
        camera1st = $null
        active_camera = $null
        camera_space = [ordered]@{}
        promotion_failures = @()
    }
    $stage10Contract = $null
    if ($CaptureStage10Contract) {
        $promotionFailures = [Collections.Generic.List[string]]::new()
        foreach ($actor in $stage10Actors.GetEnumerator()) {
            $sequence = Resolve-ControllerSequence -Process $mcp -NextId ([ref]$nextId) `
                -SessionId $sessionId -SequenceName ([string]$actor.Value.sequence_name) `
                -ExpectedActorNode ([uint64]0) `
                -ModuleBase $moduleBase -ModuleSize $moduleSize
            $stage10Resolution.sequences[$actor.Key] = $sequence
            if (-not $sequence.unique_active_actor_join) {
                $promotionFailures.Add(
                    "$($actor.Key)-section01-sequence-not-unique:$(@($sequence.candidates).Count)")
            }
        }
        foreach ($actor in $stage10Actors.GetEnumerator()) {
            $sequence = $stage10Resolution.sequences[$actor.Key]
            if (-not $sequence.unique_active_actor_join) { continue }
            $reference = Resolve-LiveReferenceFromSequence `
                -Process $mcp -NextId ([ref]$nextId) -SessionId $sessionId `
                -ReferenceFormId ([uint32]$actor.Value.reference_form_id) `
                -SequenceRoot ([uint64]$sequence.candidates[0].accumulation_root) `
                -ModuleBase $moduleBase -ModuleSize $moduleSize
            $stage10Resolution.actors[$actor.Key] = $reference
            if (-not $reference.unique) {
                $promotionFailures.Add(
                    "$($actor.Key)-sequence-to-live-reference-not-unique:" +
                    @($reference.candidates).Count)
            }
            elseif (-not $reference.candidates[0].rendered_node.visible) {
                $promotionFailures.Add("$($actor.Key)-rendered-node-app-culled")
            }
        }
        try {
            $stage10Resolution.active_camera = Resolve-ActiveCamera `
                -Process $mcp -NextId ([ref]$nextId) -SessionId $sessionId `
                -ModuleBase $moduleBase -ModuleSize $moduleSize
        }
        catch {
            $promotionFailures.Add("active-camera-unresolved:$($_.Exception.Message)")
        }
        if ($stage10Resolution.sequences.Contains('player') -and
            $stage10Resolution.sequences.player.unique_active_actor_join) {
            $playerRoot = [uint64]$stage10Resolution.sequences.player.candidates[0].accumulation_root
            try {
                $stage10Resolution.camera1st = Resolve-Camera1stNode `
                    -Process $mcp -NextId ([ref]$nextId) -SessionId $sessionId `
                    -PlayerSequenceRoot $playerRoot `
                    -ModuleBase $moduleBase -ModuleSize $moduleSize
                if (-not $stage10Resolution.camera1st.unique_player_sequence_join) {
                    $promotionFailures.Add(
                        "camera1st-player-sequence-join-not-unique:" +
                        @($stage10Resolution.camera1st.candidates).Count)
                }
            }
            catch {
                $promotionFailures.Add("camera1st-unresolved:$($_.Exception.Message)")
            }
        }
        else {
            $promotionFailures.Add('camera1st-blocked-by-player-section01-sequence')
        }
        if ($null -ne $stage10Resolution.active_camera) {
            $cameraTransform = $stage10Resolution.active_camera.world_transform
            $near = [single]$stage10Resolution.active_camera.frustum.near_game_units
            foreach ($actor in $stage10Actors.GetEnumerator()) {
                $reference = $stage10Resolution.actors[$actor.Key]
                if ($null -eq $reference -or -not $reference.unique) { continue }
                $node = $reference.candidates[0].rendered_node
                $cameraSpace = @(ConvertTo-CameraSpace `
                    -CameraTransform $cameraTransform `
                    -WorldPoint @($node.world_transform.translation_game_units))
                $stage10Resolution.camera_space[$actor.Key] = [ordered]@{
                    rendered_root_game_units = @($node.world_transform.translation_game_units)
                    camera_local_game_units = $cameraSpace
                    forward_axis = 'NiCamera-local-positive-X'
                    rendered_root_depth_game_units = $cameraSpace[$niCameraForwardAxis]
                    rendered_root_near_plane_separation_game_units =
                        $cameraSpace[$niCameraForwardAxis] - $near
                    limitation =
                        'Root separation is exact for the rendered root; posed mesh bounds remain an owned-model/animation join in the OpenNV telemetry gate.'
                }
            }
        }
        $stage10Resolution.promotion_failures = @($promotionFailures)
        if ($promotionFailures.Count -eq 0) {
            $stage10Resolution.classification =
                'exact-live-stage10-camera-participant-contract-ready'
            $participants = [ordered]@{}
            foreach ($actor in $stage10Actors.GetEnumerator()) {
                $reference = $stage10Resolution.actors[$actor.Key].candidates[0]
                $sequence = $stage10Resolution.sequences[$actor.Key].candidates[0]
                $participants[$actor.Key] = [ordered]@{
                    reference_form_id = $reference.reference_form_id
                    live_reference_address = $reference.address
                    rendered_node_address = $reference.rendered_node.address
                    visible = $reference.rendered_node.visible
                    app_culled = $reference.rendered_node.app_culled
                    rendered_world_transform = $reference.rendered_node.world_transform
                    section01_sequence = $sequence
                    camera_space = $stage10Resolution.camera_space[$actor.Key]
                }
            }
            $stage10Contract = [ordered]@{
                schema = 'opennv.fo3-retail-cg00-stage10-camera-contract/v1'
                classification =
                    'private-exact-live-stage10-camera-and-participant-contract-not-pixel-parity'
                captured_utc = [DateTime]::UtcNow.ToString('o')
                target = $identity.game
                observer = $identity.observer
                stage_identity = [ordered]@{
                    quest = 'CG00'
                    stage = $Cg00Stage10
                    proof =
                        'The exact Dad/Doctor/Mom Section01 sequences are active and joined to their rendered references; their owned PACK conditions each require GetStage CG00 == 10. The player Section01 sequence is independently joined through Camera1st.'
                    owned_package_idle_joins = [ordered]@{}
                }
                active_camera = $stage10Resolution.active_camera
                camera1st = $stage10Resolution.camera1st.candidates[0]
                participants = $participants
                coordinate_contract = [ordered]@{
                    source_units = 'Gamebryo game units'
                    matrix_storage = 'row-major-3x3'
                    world_to_local = 'transpose(worldRotation)*(worldPoint-worldTranslation)/worldScale'
                    camera_forward_axis = 'local-positive-X'
                    evidence =
                        'Exact NiCamera 1.7.0.4 derived world-to-clip constructor stores the local-X row as homogeneous projection depth.'
                }
                unimplemented_boundary =
                    'Posed actor mesh near-plane separation and matched pixels require joining these live roots/phases to the hash-bound owned actor meshes in OpenNV; this private contract does not claim parity.'
            }
            foreach ($actor in $stage10Actors.GetEnumerator()) {
                $stage10Contract.stage_identity.owned_package_idle_joins[$actor.Key] =
                    [ordered]@{
                        package_form_id = ('{0:x8}' -f [uint32]$actor.Value.package_form_id)
                        idle_form_id = ('{0:x8}' -f [uint32]$actor.Value.idle_form_id)
                        sequence_name = [string]$actor.Value.sequence_name
                        activation_stage = $actor.Value.activation_stage
                    }
            }
        }
        else {
            $stage10Resolution.classification =
                'live-stage10-contract-rejected-fail-closed'
        }
    }

    $unresolved = [Collections.Generic.List[string]]::new()
    if ($null -eq $stage10Contract) {
        $unresolved.Add(
            'Exact live stage10 actor/camera contract was not requested or did not resolve')
        foreach ($failure in @($stage10Resolution.promotion_failures)) {
            $unresolved.Add([string]$failure)
        }
    }
    else {
        $unresolved.Add(
            'Posed owned-mesh camera-space bounds and near-plane separation are an OpenNV telemetry join after this live root/phase contract')
    }
    $unresolved.Add('Dialogue topic/INFO timing is outside this camera staging contract')
    $evidence = [ordered]@{
        schema = 'opennv.fo3-retail-raw-observation.v4'
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
        startup_configuration = [ordered]@{
            starting_cell = $StartingCell
            temporary_ini_staging = -not [string]::IsNullOrWhiteSpace($StartingCell)
            original_ini_sha256 = $originalIniSha256
            staged_ini_sha256 = $modifiedIniSha256
            restoration_required = -not [string]::IsNullOrWhiteSpace($StartingCell)
            restoration_verification = 'required-before-successful-observer-exit'
        }
        reference_load_transition = $referenceLoadTransition
        module_inventory = $modules
        region_count = $regions.Count
        record_derived_candidate_patterns = $patterns
        samples = $samples
        stage10_resolution = $stage10Resolution
        unresolved = @($unresolved)
        prohibitions = @(
            'No process-memory writes',
            'No injected input or UI automation',
            'No Godot behavior contract may be emitted from raw candidates'
        )
    }
    $evidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $output -Encoding utf8NoBOM
    if ($null -ne $stage10Contract) {
        $stage10Contract['raw_observation'] = [ordered]@{
            path = $output
            sha256 = Get-Sha256 $output
        }
        $stage10Contract | ConvertTo-Json -Depth $ContractJsonDepth |
            Set-Content -LiteralPath $contractOutput -Encoding utf8NoBOM
    }
    $evidence | ConvertTo-Json -Depth 8
    Write-Host "Private FO3 observation written to $output"
    if ($CaptureStage10Contract -and $null -eq $stage10Contract) {
        throw ('FO3 live stage10 contract rejected: ' +
            ($stage10Resolution.promotion_failures -join '; '))
    }
    if ($null -ne $stage10Contract) {
        Write-Host "Private FO3 stage10 contract written to $contractOutput"
    }
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
    if ($null -ne $falloutIniBackup -and
        (Test-Path -LiteralPath $falloutIniBackup -PathType Leaf)) {
        if ($falloutIniBackupVerified) {
            Copy-Item -LiteralPath $falloutIniBackup -Destination $falloutIni -Force
            $restoredIniSha256 = Get-Sha256 $falloutIni
            if ($restoredIniSha256 -ne $originalIniSha256) {
                throw "Fallout 3 INI restoration hash mismatch: $restoredIniSha256"
            }
        }
        Remove-Item -LiteralPath $falloutIniBackup -Force
    }
    if ($null -ne $shadowDirectory -and (Test-Path -LiteralPath $shadowDirectory)) {
        if (-not (Test-PathWithin -Path $shadowDirectory -Root $privateEvidenceRoot)) {
            throw "Refusing to remove shadow outside the private evidence root: $shadowDirectory"
        }
        Remove-Item -LiteralPath $shadowDirectory -Recurse -Force
    }
}
