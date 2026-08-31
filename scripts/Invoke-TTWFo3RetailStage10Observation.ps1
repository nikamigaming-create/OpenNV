[CmdletBinding()]
param(
    [string]$TtwRoot = 'D:\TTW\Installed',
    [string]$TtwProfilePath = '',
    [string]$EffectiveNamespacePath = '',
    [string]$OpeningProfilePath = '',
    [string]$ObserverRecipePath = '',
    [string]$FalloutNvPath = '',
    [string]$GhidrustPath =
        'D:\Dev\Tools\Ghidrust\builds\wow64-i686-codex-nogpu\i686-pc-windows-msvc\release\ghidrust.exe',
    [int]$TargetProcessId = 0,
    [string]$OutputPath = '',
    [string]$ContractOutputPath = '',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$ProfileRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OpenNV\profiles'
if ([string]::IsNullOrWhiteSpace($TtwProfilePath)) {
    $TtwProfilePath = Join-Path $ProfileRoot 'ttw-profile.json'
}
if ([string]::IsNullOrWhiteSpace($EffectiveNamespacePath)) {
    $EffectiveNamespacePath = Join-Path $ProfileRoot 'ttw-effective-source.json'
}
if ([string]::IsNullOrWhiteSpace($OpeningProfilePath)) {
    $OpeningProfilePath = Join-Path $ProfileRoot 'ttw-fo3-opening-profile.json'
}
if ([string]::IsNullOrWhiteSpace($ObserverRecipePath)) {
    $recipeCandidates = @(Get-ChildItem `
        (Join-Path $RepoRoot 'content\recipes') `
        -Filter 'ttw-fo3-retail-stage10-observer-*.json' -File)
    if ($recipeCandidates.Count -ne 1) {
        throw 'Expected one TTW FO3 retail stage10 observer recipe.'
    }
    $ObserverRecipePath = $recipeCandidates[0].FullName
}

$ExpectedObserverRecipeSchema = 'opennv-ttw-fo3-retail-stage10-observer/v1'
$ExpectedRawObservationSchema =
    'opennv.ttw-fo3-retail-cg00-stage10-observation/v1'
$ExpectedContractSchema =
    'opennv.ttw-fo3-retail-cg00-stage10-camera-contract/v1'
$ExpectedContractClassification =
    'private-exact-live-ttw-stage10-camera-and-participant-contract-not-pixel-parity'
$ExpectedValidationSchema =
    'opennv.ttw-fo3-retail-cg00-stage10-observer-validation/v1'
$ExpectedMcpServerName = 'ghidrust'
$UInt32Size = 4
$SingleSize = 4
$MatrixOrder = 3
$ProjectionOrder = 4
$FormIdCharacters = 8
$RotationRowOneColumnTwoIndex = 5
$RotationRowTwoColumnZeroIndex = 6
$RotationRowTwoColumnOneIndex = 7
$RotationRowTwoColumnTwoIndex = 8
$NiTransformTranslationOffset = 0x24
$NiTransformScaleOffset = 0x30
$RadiansToDegrees = 180.0 / [Math]::PI
$HexadecimalRadix = 16

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing required JSON file: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-Property([object]$Source, [string]$Name, [string]$Label) {
    if ($null -eq $Source -or $Source.PSObject.Properties.Name -notcontains $Name) {
        throw "$Label is missing required property '$Name'."
    }
    return $Source.$Name
}

function Assert-ExactText(
    [object]$Source,
    [string]$Name,
    [string]$Expected,
    [string]$Label
) {
    $actual = [string](Get-Property $Source $Name $Label)
    if ($actual -cne $Expected) {
        throw "$Label.$Name differs: expected='$Expected' actual='$actual'."
    }
}

function Test-PathWithin([string]$Path, [string]$Root) {
    $absolutePath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $absoluteRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    return $absolutePath.StartsWith(
        $absoluteRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function ConvertFrom-HexUInt32([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{1,8}$') {
        throw "$Label is not a bounded hexadecimal UInt32."
    }
    return [Convert]::ToUInt32($Value, $HexadecimalRadix)
}

function Get-StableLocalFormId([string]$FormKey, [string]$Label) {
    if ($FormKey -notmatch '^[^:]+:(?<local>[0-9a-fA-F]{6})$') {
        throw "$Label is not a stable origin/local FormKey: $FormKey"
    }
    return $Matches.local.PadLeft($FormIdCharacters, '0').ToLowerInvariant()
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
    } | ConvertTo-Json -Compress -Depth 4
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

function Test-FiniteSingle([single]$Value) {
    return -not [single]::IsNaN($Value) -and -not [single]::IsInfinity($Value)
}

function Get-Vector3([byte[]]$Bytes, [int]$Offset) {
    return @(
        Get-Single $Bytes $Offset
        Get-Single $Bytes ($Offset + $SingleSize)
        Get-Single $Bytes ($Offset + 2 * $SingleSize)
    )
}

function Test-FiniteVector([object[]]$Values) {
    return $Values.Count -eq $MatrixOrder -and
        @($Values | Where-Object {
            -not (Test-FiniteSingle ([single]$_))
        }).Count -eq 0
}

function Get-NiTransform(
    [byte[]]$Bytes,
    [int]$Offset,
    [single]$MinimumNonSingularScale
) {
    $rotation = for ($index = 0; $index -lt ($MatrixOrder * $MatrixOrder); $index++) {
        Get-Single $Bytes ($Offset + $index * $SingleSize)
    }
    $translation = Get-Vector3 $Bytes ($Offset + $NiTransformTranslationOffset)
    $scale = Get-Single $Bytes ($Offset + $NiTransformScaleOffset)
    if (@($rotation | Where-Object {
            -not (Test-FiniteSingle ([single]$_))
        }).Count -gt 0 -or
        -not (Test-FiniteVector $translation) -or
        -not (Test-FiniteSingle $scale) -or
        [Math]::Abs($scale) -lt $MinimumNonSingularScale) {
        throw 'NiTransform contains a non-finite or singular live value.'
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
        -SessionId $SessionId -Address $Address -Size $UInt32Size
    return Get-UInt32 $bytes 0
}

function Read-RemoteCString(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$Address,
    [int]$MaximumBytes
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
    [uint64]$ModuleSize,
    [object]$Abi,
    [single]$MinimumNonSingularScale,
    [int]$MaximumRemoteStringBytes
) {
    if ($Address -eq 0) { throw 'NiAV address is null.' }
    $layout = $Abi.niAv
    $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $Address -Size ([int]$layout.size)
    $vtable = [uint64](Get-UInt32 $bytes 0)
    if ($vtable -lt $ModuleBase -or $vtable -ge ($ModuleBase + $ModuleSize)) {
        throw ('NiAV candidate 0x{0:X} has a vtable outside FalloutNV.exe.' -f
            $Address)
    }
    $nameAddress = [uint64](Get-UInt32 $bytes ([int]$layout.nameOffset))
    $flags = Get-UInt32 $bytes ([int]$layout.flagsOffset)
    $appCulled = ($flags -band [uint32]$layout.appCulledFlag) -ne 0
    return [ordered]@{
        address = $Address
        vtable = $vtable
        name_address = $nameAddress
        name = if ($nameAddress -eq 0) { $null } else {
            Read-RemoteCString -Process $Process -NextId $NextId `
                -SessionId $SessionId -Address $nameAddress `
                -MaximumBytes $MaximumRemoteStringBytes
        }
        parent = [uint64](Get-UInt32 $bytes ([int]$layout.parentOffset))
        controller = [uint64](Get-UInt32 $bytes ([int]$layout.controllerOffset))
        flags = $flags
        app_culled = $appCulled
        visible = -not $appCulled
        local_transform = Get-NiTransform $bytes ([int]$layout.localTransformOffset) `
            -MinimumNonSingularScale $MinimumNonSingularScale
        world_transform = Get-NiTransform $bytes ([int]$layout.worldTransformOffset) `
            -MinimumNonSingularScale $MinimumNonSingularScale
    }
}

function Get-NiAvAncestry(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$StartAddress,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize,
    [object]$Abi,
    [single]$MinimumNonSingularScale,
    [int]$MaximumRemoteStringBytes,
    [int]$MaximumDepth
) {
    $ancestry = [Collections.Generic.List[uint64]]::new()
    $seen = [Collections.Generic.HashSet[uint64]]::new()
    $current = $StartAddress
    while ($current -ne 0 -and $ancestry.Count -lt $MaximumDepth) {
        if (-not $seen.Add($current)) {
            throw ('NiAV ancestry cycle detected at 0x{0:X}.' -f $current)
        }
        $ancestry.Add($current)
        $node = Get-NiAvSnapshot -Process $Process -NextId $NextId `
            -SessionId $SessionId -Address $current -ModuleBase $ModuleBase `
            -ModuleSize $ModuleSize -Abi $Abi `
            -MinimumNonSingularScale $MinimumNonSingularScale `
            -MaximumRemoteStringBytes $MaximumRemoteStringBytes
        $current = [uint64]$node.parent
    }
    if ($current -ne 0) {
        throw "NiAV ancestry exceeded $MaximumDepth nodes."
    }
    return @($ancestry)
}

function Resolve-ControllerSequences(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [string]$SequenceName,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize,
    [object]$Abi,
    [int]$MaximumScanHits,
    [int]$MaximumRemoteStringBytes,
    [int]$MaximumDepth,
    [single]$MinimumNonSingularScale
) {
    $layout = $Abi.controllerSequence
    $nameScan = Invoke-McpTool -Process $Process -NextId $NextId `
        -Name 'process_scan' -Arguments @{
            session_id = $SessionId
            aob = ConvertTo-AsciiAob $SequenceName
            max_hits = $MaximumScanHits
        }
    $candidates = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[uint64]]::new()
    foreach ($nameHit in @($nameScan.hits)) {
        $nameAddress = [uint64]$nameHit.va
        $pointerScan = Invoke-McpTool -Process $Process -NextId $NextId `
            -Name 'process_scan' -Arguments @{
                session_id = $SessionId
                aob = ConvertTo-UInt32Aob ([uint32]$nameAddress)
                max_hits = $MaximumScanHits
            }
        foreach ($pointerHit in @($pointerScan.hits)) {
            $pointerAddress = [uint64]$pointerHit.va
            if ($pointerAddress -lt [uint64]$layout.nameOffset) { continue }
            $address = $pointerAddress - [uint64]$layout.nameOffset
            if (-not $seen.Add($address)) { continue }
            try {
                $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
                    -SessionId $SessionId -Address $address -Size ([int]$layout.size)
                $vtable = [uint64](Get-UInt32 $bytes 0)
                if ($vtable -lt $ModuleBase -or $vtable -ge ($ModuleBase + $ModuleSize) -or
                    [uint64](Get-UInt32 $bytes ([int]$layout.nameOffset)) -ne $nameAddress) {
                    continue
                }
                $cycle = Get-UInt32 $bytes ([int]$layout.cycleOffset)
                $frequency = Get-Single $bytes ([int]$layout.frequencyOffset)
                $begin = Get-Single $bytes ([int]$layout.beginOffset)
                $end = Get-Single $bytes ([int]$layout.endOffset)
                $last = Get-Single $bytes ([int]$layout.lastOffset)
                $lastScaled = Get-Single $bytes ([int]$layout.lastScaledOffset)
                $state = Get-UInt32 $bytes ([int]$layout.stateOffset)
                $root = [uint64](Get-UInt32 $bytes ([int]$layout.accumulationRootOffset))
                if ($cycle -gt 2 -or -not (Test-FiniteSingle $frequency) -or
                    $frequency -le 0.0 -or -not (Test-FiniteSingle $begin) -or
                    -not (Test-FiniteSingle $end) -or $end -le $begin -or
                    -not (Test-FiniteSingle $last) -or $last -lt $begin -or $last -gt $end -or
                    -not (Test-FiniteSingle $lastScaled) -or $state -eq 0 -or $root -eq 0) {
                    continue
                }
                $rootNode = Get-NiAvSnapshot -Process $Process -NextId $NextId `
                    -SessionId $SessionId -Address $root -ModuleBase $ModuleBase `
                    -ModuleSize $ModuleSize -Abi $Abi `
                    -MinimumNonSingularScale $MinimumNonSingularScale `
                    -MaximumRemoteStringBytes $MaximumRemoteStringBytes
                $ancestry = Get-NiAvAncestry -Process $Process -NextId $NextId `
                    -SessionId $SessionId -StartAddress $root -ModuleBase $ModuleBase `
                    -ModuleSize $ModuleSize -Abi $Abi `
                    -MinimumNonSingularScale $MinimumNonSingularScale `
                    -MaximumRemoteStringBytes $MaximumRemoteStringBytes `
                    -MaximumDepth $MaximumDepth
                $candidates.Add([ordered]@{
                    address = $address
                    vtable = $vtable
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
                    accumulation_root_name = $rootNode.name
                    ancestry = @($ancestry)
                })
            }
            catch {}
        }
    }
    return @($candidates)
}

function Resolve-LiveReferenceFromSequence(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint32]$ReferenceFormId,
    [object]$Sequence,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize,
    [object]$Abi,
    [int]$MaximumScanHits,
    [int]$MaximumRemoteStringBytes,
    [single]$MinimumNonSingularScale
) {
    $referenceLayout = $Abi.reference
    $candidates = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[uint64]]::new()
    foreach ($nodeAddress in @($Sequence.ancestry)) {
        $nodePointerScan = Invoke-McpTool -Process $Process -NextId $NextId `
            -Name 'process_scan' -Arguments @{
                session_id = $SessionId
                aob = ConvertTo-UInt32Aob ([uint32]$nodeAddress)
                max_hits = $MaximumScanHits
            }
        foreach ($nodePointerHit in @($nodePointerScan.hits)) {
            $nodePointerAddress = [uint64]$nodePointerHit.va
            if ($nodePointerAddress -lt [uint64]$referenceLayout.renderStateRootNodeOffset) {
                continue
            }
            $renderState =
                $nodePointerAddress - [uint64]$referenceLayout.renderStateRootNodeOffset
            try {
                $renderStateBytes = Read-RemoteBytes -Process $Process -NextId $NextId `
                    -SessionId $SessionId -Address $renderState `
                    -Size ([int]$referenceLayout.renderStateRootNodeOffset + $UInt32Size)
                if ([uint64](Get-UInt32 $renderStateBytes `
                        ([int]$referenceLayout.renderStateRootNodeOffset)) -ne $nodeAddress) {
                    continue
                }
                $renderStatePointerScan = Invoke-McpTool -Process $Process `
                    -NextId $NextId -Name 'process_scan' -Arguments @{
                        session_id = $SessionId
                        aob = ConvertTo-UInt32Aob ([uint32]$renderState)
                        max_hits = $MaximumScanHits
                    }
                foreach ($renderStatePointerHit in @($renderStatePointerScan.hits)) {
                    $pointerAddress = [uint64]$renderStatePointerHit.va
                    if ($pointerAddress -lt [uint64]$referenceLayout.renderStateOffset) {
                        continue
                    }
                    $address = $pointerAddress - [uint64]$referenceLayout.renderStateOffset
                    if (-not $seen.Add($address)) { continue }
                    try {
                        $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
                            -SessionId $SessionId -Address $address `
                            -Size ([int]$referenceLayout.minimumSize)
                        if ($bytes[[int]$referenceLayout.formTypeOffset] -ne
                                [byte]$referenceLayout.formTypeId -or
                            (Get-UInt32 $bytes ([int]$referenceLayout.referenceFormIdOffset)) -ne
                                $ReferenceFormId -or
                            [uint64](Get-UInt32 $bytes ([int]$referenceLayout.renderStateOffset)) -ne
                                $renderState) {
                            continue
                        }
                        $baseForm = [uint64](Get-UInt32 $bytes ([int]$referenceLayout.baseFormOffset))
                        $parentCell = [uint64](Get-UInt32 $bytes ([int]$referenceLayout.parentCellOffset))
                        $scale = Get-Single $bytes ([int]$referenceLayout.scaleOffset)
                        $rotation = Get-Vector3 $bytes ([int]$referenceLayout.rotationOffset)
                        $position = Get-Vector3 $bytes ([int]$referenceLayout.positionOffset)
                        $baseProcess = [uint64](Get-UInt32 $bytes ([int]$referenceLayout.baseProcessOffset))
                        if ($baseForm -eq 0 -or $parentCell -eq 0 -or $baseProcess -eq 0 -or
                            -not (Test-FiniteSingle $scale) -or $scale -le 0.0 -or
                            -not (Test-FiniteVector $rotation) -or
                            -not (Test-FiniteVector $position)) {
                            continue
                        }
                        $node = Get-NiAvSnapshot -Process $Process -NextId $NextId `
                            -SessionId $SessionId -Address $nodeAddress `
                            -ModuleBase $ModuleBase -ModuleSize $ModuleSize -Abi $Abi `
                            -MinimumNonSingularScale $MinimumNonSingularScale `
                            -MaximumRemoteStringBytes $MaximumRemoteStringBytes
                        $candidates.Add([ordered]@{
                            address = $address
                            reference_form_id = ('{0:x8}' -f $ReferenceFormId)
                            type_id = $bytes[[int]$referenceLayout.formTypeOffset]
                            flags = Get-UInt32 $bytes ([int]$referenceLayout.flagsOffset)
                            base_form_address = $baseForm
                            parent_cell_address = $parentCell
                            authored_rotation_radians = @($rotation)
                            authored_position_game_units = @($position)
                            authored_scale = $scale
                            base_process_address = $baseProcess
                            render_state_address = $renderState
                            rendered_node = $node
                        })
                    }
                    catch {}
                }
            }
            catch {}
        }
    }
    return @($candidates)
}

function Get-LivePackageIdleJoin(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$BaseProcessAddress,
    [uint32]$ExpectedPackageFormId,
    [uint32]$ExpectedIdleFormId,
    [object]$Abi
) {
    $layout = $Abi.baseProcess
    $bytes = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $BaseProcessAddress `
        -Size ([int]$layout.minimumSize)
    $processLevel = Get-UInt32 $bytes ([int]$layout.processLevelOffset)
    if ($processLevel -gt [uint32]$layout.maximumMiddleHighProcessLevel) {
        throw "Actor process level $processLevel cannot expose the exact live IDLE join."
    }
    $packageAddress = [uint64](Get-UInt32 $bytes ([int]$layout.currentPackageOffset))
    $idleAddress = [uint64](Get-UInt32 $bytes ([int]$layout.middleHighIdleFormOffset))
    if ($packageAddress -eq 0 -or $idleAddress -eq 0) {
        throw 'Actor current PACK/IDLE pointer is null.'
    }
    $packageFormId = Read-RemoteUInt32 -Process $Process -NextId $NextId `
        -SessionId $SessionId `
        -Address ($packageAddress + [uint64]$Abi.form.formIdOffset)
    $idleFormId = Read-RemoteUInt32 -Process $Process -NextId $NextId `
        -SessionId $SessionId `
        -Address ($idleAddress + [uint64]$Abi.form.formIdOffset)
    if ($packageFormId -ne $ExpectedPackageFormId -or
        $idleFormId -ne $ExpectedIdleFormId) {
        throw ('Live PACK/IDLE differs: expected={0:x8}/{1:x8} actual={2:x8}/{3:x8}' -f
            $ExpectedPackageFormId, $ExpectedIdleFormId, $packageFormId, $idleFormId)
    }
    return [ordered]@{
        base_process_address = $BaseProcessAddress
        process_level = $processLevel
        package_address = $packageAddress
        package_runtime_form_id = ('{0:x8}' -f $packageFormId)
        idle_address = $idleAddress
        idle_runtime_form_id = ('{0:x8}' -f $idleFormId)
    }
}

function Resolve-Participant(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [object]$Contract,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize,
    [object]$Abi,
    [object]$Observation
) {
    $sequenceCandidates = @(Resolve-ControllerSequences -Process $Process `
        -NextId $NextId -SessionId $SessionId `
        -SequenceName ([string]$Contract.sequence_name) `
        -ModuleBase $ModuleBase -ModuleSize $ModuleSize -Abi $Abi `
        -MaximumScanHits ([int]$Observation.maximumScanHits) `
        -MaximumRemoteStringBytes ([int]$Observation.maximumRemoteStringBytes) `
        -MaximumDepth ([int]$Observation.maximumNiAvAncestryDepth) `
        -MinimumNonSingularScale ([single]$Observation.minimumNonSingularScale))
    $live = [Collections.Generic.List[object]]::new()
    foreach ($sequence in $sequenceCandidates) {
        $references = @(Resolve-LiveReferenceFromSequence -Process $Process `
            -NextId $NextId -SessionId $SessionId `
            -ReferenceFormId ([uint32]$Contract.runtime_reference_form_id_numeric) `
            -Sequence $sequence -ModuleBase $ModuleBase -ModuleSize $ModuleSize `
            -Abi $Abi -MaximumScanHits ([int]$Observation.maximumScanHits) `
            -MaximumRemoteStringBytes ([int]$Observation.maximumRemoteStringBytes) `
            -MinimumNonSingularScale ([single]$Observation.minimumNonSingularScale))
        foreach ($reference in $references) {
            try {
                $packageIdle = Get-LivePackageIdleJoin -Process $Process -NextId $NextId `
                    -SessionId $SessionId `
                    -BaseProcessAddress ([uint64]$reference.base_process_address) `
                    -ExpectedPackageFormId ([uint32]$Contract.package_runtime_form_id_numeric) `
                    -ExpectedIdleFormId ([uint32]$Contract.idle_runtime_form_id_numeric) `
                    -Abi $Abi
                if (-not $reference.rendered_node.visible) { continue }
                $live.Add([ordered]@{
                    sequence = $sequence
                    reference = $reference
                    package_idle = $packageIdle
                })
            }
            catch {}
        }
    }
    return [ordered]@{
        role = $Contract.role
        sequence_candidate_count = $sequenceCandidates.Count
        joined_candidate_count = $live.Count
        candidates = @($live)
        unique = $live.Count -eq 1
    }
}

function Get-MatrixProduct([object[]]$Left, [object[]]$Right) {
    $result = [Collections.Generic.List[double]]::new()
    for ($row = 0; $row -lt $ProjectionOrder; $row++) {
        for ($column = 0; $column -lt $ProjectionOrder; $column++) {
            $value = 0.0
            for ($inner = 0; $inner -lt $ProjectionOrder; $inner++) {
                $value += [double]$Left[$row * $ProjectionOrder + $inner] *
                    [double]$Right[$inner * $ProjectionOrder + $column]
            }
            $result.Add($value)
        }
    }
    return @($result)
}

function Get-WorldToClip(
    [object]$WorldTransform,
    [single]$Left,
    [single]$Right,
    [single]$Top,
    [single]$Bottom,
    [single]$Near,
    [single]$Far
) {
    $rotation = @($WorldTransform.rotation_row_major)
    $translation = @($WorldTransform.translation_game_units)
    $scale = [double]$WorldTransform.scale
    $view = @(
        $rotation[0] / $scale, $rotation[3] / $scale,
            $rotation[$RotationRowTwoColumnZeroIndex] / $scale,
            -($rotation[0] * $translation[0] + $rotation[3] * $translation[1] +
                $rotation[$RotationRowTwoColumnZeroIndex] * $translation[2]) / $scale,
        $rotation[1] / $scale, $rotation[4] / $scale,
            $rotation[$RotationRowTwoColumnOneIndex] / $scale,
            -($rotation[1] * $translation[0] + $rotation[4] * $translation[1] +
                $rotation[$RotationRowTwoColumnOneIndex] * $translation[2]) / $scale,
        $rotation[2] / $scale, $rotation[$RotationRowOneColumnTwoIndex] / $scale,
            $rotation[$RotationRowTwoColumnTwoIndex] / $scale,
            -($rotation[2] * $translation[0] +
                $rotation[$RotationRowOneColumnTwoIndex] * $translation[1] +
                $rotation[$RotationRowTwoColumnTwoIndex] * $translation[2]) / $scale,
        0.0, 0.0, 0.0, 1.0
    )
    $width = [double]$Right - [double]$Left
    $height = [double]$Top - [double]$Bottom
    $depth = [double]$Far - [double]$Near
    $projection = @(
        -([double]$Right + [double]$Left) / $width,
            2.0 * [double]$Near / $width, 0.0, 0.0,
        -([double]$Top + [double]$Bottom) / $height,
            0.0, 2.0 * [double]$Near / $height, 0.0,
        [double]$Far / $depth, 0.0, 0.0,
            -([double]$Far * [double]$Near) / $depth,
        1.0, 0.0, 0.0, 0.0
    )
    return Get-MatrixProduct $projection $view
}

function Resolve-ActiveCamera(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize,
    [object]$Abi,
    [single]$MinimumNonSingularScale
) {
    $sceneGraphGlobal = $ModuleBase +
        (ConvertFrom-HexUInt32 ([string]$Abi.sceneGraphSingletonPointerRva) `
            'sceneGraphSingletonPointerRva')
    $sceneGraphAddress = [uint64](Read-RemoteUInt32 -Process $Process `
        -NextId $NextId -SessionId $SessionId -Address $sceneGraphGlobal)
    if ($sceneGraphAddress -eq 0) { throw 'World SceneGraph singleton is null.' }
    $sceneGraph = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $sceneGraphAddress `
        -Size ([int]$Abi.sceneGraph.size)
    $sceneGraphVtable = [uint64](Get-UInt32 $sceneGraph 0)
    if ($sceneGraphVtable -lt $ModuleBase -or
        $sceneGraphVtable -ge ($ModuleBase + $ModuleSize)) {
        throw 'World SceneGraph vtable is outside FalloutNV.exe.'
    }
    $cameraAddress = [uint64](Get-UInt32 $sceneGraph `
        ([int]$Abi.sceneGraph.activeCameraOffset))
    $cullingAddress = [uint64](Get-UInt32 $sceneGraph `
        ([int]$Abi.sceneGraph.cullingProcessOffset))
    $sceneGraphFov = Get-Single $sceneGraph ([int]$Abi.sceneGraph.fovOffset)
    if ($cameraAddress -eq 0 -or $cullingAddress -eq 0 -or
        -not (Test-FiniteSingle $sceneGraphFov)) {
        throw 'World SceneGraph has no valid live camera/culling/FOV join.'
    }
    $camera = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $cameraAddress -Size ([int]$Abi.camera.size)
    $cameraVtable = [uint64](Get-UInt32 $camera 0)
    if ($cameraVtable -lt $ModuleBase -or $cameraVtable -ge ($ModuleBase + $ModuleSize)) {
        throw 'Active NiCamera vtable is outside FalloutNV.exe.'
    }
    $culling = Read-RemoteBytes -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $cullingAddress `
        -Size ([int]$Abi.cullingProcess.minimumSize)
    $cullingVtable = [uint64](Get-UInt32 $culling 0)
    $cullingCamera = [uint64](Get-UInt32 $culling `
        ([int]$Abi.cullingProcess.cameraOffset))
    if ($cullingVtable -lt $ModuleBase -or $cullingVtable -ge ($ModuleBase + $ModuleSize) -or
        $cullingCamera -ne $cameraAddress) {
        throw 'Active NiCamera is not the live culling-process camera.'
    }
    $frustum = for ($index = 0; $index -lt 4; $index++) {
        Get-Single $camera ([int]$Abi.camera.frustumOffset + $index * $SingleSize)
    }
    $near = Get-Single $camera ([int]$Abi.camera.nearOffset)
    $far = Get-Single $camera ([int]$Abi.camera.farOffset)
    $viewport = for ($index = 0; $index -lt 4; $index++) {
        Get-Single $camera ([int]$Abi.camera.viewportOffset + $index * $SingleSize)
    }
    $orthographic = $camera[[int]$Abi.camera.orthographicOffset] -ne 0
    if ($orthographic -or $near -le 0.0 -or $far -le $near -or
        $frustum[0] -ge $frustum[1] -or $frustum[3] -ge $frustum[2] -or
        @($frustum + $viewport | Where-Object {
            -not (Test-FiniteSingle ([single]$_))
        }).Count -gt 0) {
        throw 'Active NiCamera perspective/frustum/viewport is invalid.'
    }
    for ($index = 0; $index -lt 4; $index++) {
        $cameraFrustumValue = Get-Single $camera `
            ([int]$Abi.camera.frustumOffset + $index * $SingleSize)
        $cullingFrustumValue = Get-Single $culling `
            ([int]$Abi.cullingProcess.frustumOffset + $index * $SingleSize)
        if ([Math]::Abs($cameraFrustumValue - $cullingFrustumValue) -gt
            [single]$MinimumNonSingularScale) {
            throw 'Active NiCamera frustum differs from the culling-process frustum.'
        }
    }
    $worldTransform = Get-NiTransform $camera ([int]$Abi.niAv.worldTransformOffset) `
        -MinimumNonSingularScale $MinimumNonSingularScale
    return [ordered]@{
        bs_scene_graph_address = $sceneGraphAddress
        scene_graph_global_address = $sceneGraphGlobal
        scene_graph_vtable = $sceneGraphVtable
        scene_graph_fov = $sceneGraphFov
        culling_process_address = $cullingAddress
        culling_process_vtable = $cullingVtable
        camera_address = $cameraAddress
        vtable = $cameraVtable
        world_transform = $worldTransform
        frustum = [ordered]@{
            left = $frustum[0]
            right = $frustum[1]
            top = $frustum[2]
            bottom = $frustum[3]
            near_game_units = $near
            far_game_units = $far
            orthographic = $false
            horizontal_fov_degrees =
                ([Math]::Atan($frustum[1] / $near) -
                    [Math]::Atan($frustum[0] / $near)) * $RadiansToDegrees
            vertical_fov_degrees =
                ([Math]::Atan($frustum[2] / $near) -
                    [Math]::Atan($frustum[3] / $near)) * $RadiansToDegrees
        }
        viewport_normalized = @($viewport)
        derived_world_to_clip_row_major = @(Get-WorldToClip `
            -WorldTransform $worldTransform -Left $frustum[0] -Right $frustum[1] `
            -Top $frustum[2] -Bottom $frustum[3] -Near $near -Far $far)
    }
}

function Resolve-Camera1st(
    [Diagnostics.Process]$Process,
    [ref]$NextId,
    [string]$SessionId,
    [object]$PlayerSequence,
    [uint64]$ModuleBase,
    [uint64]$ModuleSize,
    [object]$Abi,
    [object]$Observation
) {
    $global = $ModuleBase +
        (ConvertFrom-HexUInt32 ([string]$Abi.camera1stPointerRva) `
            'camera1stPointerRva')
    $address = [uint64](Read-RemoteUInt32 -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $global)
    $node = Get-NiAvSnapshot -Process $Process -NextId $NextId `
        -SessionId $SessionId -Address $address -ModuleBase $ModuleBase `
        -ModuleSize $ModuleSize -Abi $Abi `
        -MinimumNonSingularScale ([single]$Observation.minimumNonSingularScale) `
        -MaximumRemoteStringBytes ([int]$Observation.maximumRemoteStringBytes)
    if ($node.name -cne 'Camera1st' -or -not $node.visible -or $node.parent -eq 0) {
        throw 'Camera1st global does not resolve to the live visible Camera1st node.'
    }
    $ancestry = Get-NiAvAncestry -Process $Process -NextId $NextId `
        -SessionId $SessionId -StartAddress $address -ModuleBase $ModuleBase `
        -ModuleSize $ModuleSize -Abi $Abi `
        -MinimumNonSingularScale ([single]$Observation.minimumNonSingularScale) `
        -MaximumRemoteStringBytes ([int]$Observation.maximumRemoteStringBytes) `
        -MaximumDepth ([int]$Observation.maximumNiAvAncestryDepth)
    if ($ancestry -notcontains [uint64]$PlayerSequence.accumulation_root) {
        throw 'Camera1st is not joined to the active player Section01 accumulation root.'
    }
    return [ordered]@{
        global_address = $global
        node = $node
    }
}

function ConvertTo-CameraSpace([object]$CameraTransform, [object[]]$WorldPoint) {
    $translation = @($CameraTransform.translation_game_units)
    $rotation = @($CameraTransform.rotation_row_major)
    $scale = [double]$CameraTransform.scale
    $delta = for ($axis = 0; $axis -lt $MatrixOrder; $axis++) {
        [double]$WorldPoint[$axis] - [double]$translation[$axis]
    }
    $result = for ($column = 0; $column -lt $MatrixOrder; $column++) {
        $value = 0.0
        for ($row = 0; $row -lt $MatrixOrder; $row++) {
            $value += [double]$rotation[$row * $MatrixOrder + $column] * $delta[$row]
        }
        $value / $scale
    }
    return @($result)
}

function Get-ValidatedIdentity {
    $resolvedTtwRoot = [IO.Path]::GetFullPath($TtwRoot).TrimEnd('\')
    $profilePath = [IO.Path]::GetFullPath($TtwProfilePath)
    $namespacePath = [IO.Path]::GetFullPath($EffectiveNamespacePath)
    $openingPath = [IO.Path]::GetFullPath($OpeningProfilePath)
    $recipePath = [IO.Path]::GetFullPath($ObserverRecipePath)
    $ghidrustExe = [IO.Path]::GetFullPath($GhidrustPath)
    foreach ($required in @(
            $resolvedTtwRoot, $profilePath, $namespacePath, $openingPath,
            $recipePath, $ghidrustExe)) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Missing required TTW observer input: $required"
        }
    }
    $recipe = Get-Json $recipePath
    Assert-ExactText $recipe 'schema' $ExpectedObserverRecipeSchema 'observer recipe'
    $profile = Get-Json $profilePath
    $namespace = Get-Json $namespacePath
    $opening = Get-Json $openingPath
    Assert-ExactText $profile 'schema' `
        ([string]$recipe.sourceIdentity.sourceProfileSchema) 'TTW profile'
    Assert-ExactText $profile 'status' `
        ([string]$recipe.sourceIdentity.sourceProfileStatus) 'TTW profile'
    Assert-ExactText $namespace 'schema' `
        ([string]$recipe.sourceIdentity.effectiveNamespaceSchema) 'TTW namespace'
    Assert-ExactText $namespace 'status' `
        ([string]$recipe.sourceIdentity.effectiveNamespaceStatus) 'TTW namespace'
    Assert-ExactText $opening 'schema' `
        ([string]$recipe.sourceIdentity.openingProfileSchema) 'TTW opening profile'
    Assert-ExactText $opening 'status' `
        ([string]$recipe.sourceIdentity.openingProfileStatus) 'TTW opening profile'

    $profileSha = Get-Sha256 $profilePath
    $namespaceSha = Get-Sha256 $namespacePath
    $openingSha = Get-Sha256 $openingPath
    $recipeSha = Get-Sha256 $recipePath
    $pluginStackId = [string]$profile.pluginStackId
    $saveCompatibilityId = [string]$profile.saveCompatibilityId
    $primaryMaster = ([string]$recipe.participants.player.packageFormKey).Split(':')[0]
    if ($saveCompatibilityId -cne "ttw:$pluginStackId") {
        throw 'TTW save namespace is not derived from the exact plugin stack.'
    }
    $sourceRoots = @($profile.sourceRoots | ForEach-Object {
        [IO.Path]::GetFullPath([string]$_).TrimEnd('\')
    })
    if (@($sourceRoots | Where-Object {
            $_.Equals($resolvedTtwRoot, [StringComparison]::OrdinalIgnoreCase)
        }).Count -ne 1) {
        throw 'TTW source profile does not bind the exact requested TTW root.'
    }
    if ([string]$namespace.sourceProfile.sha256 -cne $profileSha -or
        [string]$namespace.sourceProfile.pluginStackId -cne $pluginStackId -or
        [string]$namespace.sourceProfile.saveCompatibilityId -cne $saveCompatibilityId -or
        [string]$namespace.resolutionPolicy -cne
            [string]$recipe.sourceIdentity.namespaceResolutionPolicy) {
        throw 'TTW effective-source namespace does not bind the source profile.'
    }
    if ([string]$opening.sourceProfile.sha256 -cne $profileSha -or
        [string]$opening.sourceProfile.pluginStackId -cne $pluginStackId -or
        [string]$opening.sourceProfile.saveCompatibilityId -cne $saveCompatibilityId -or
        [string]$opening.sourceNamespace.sha256 -cne $namespaceSha) {
        throw 'TTW opening profile does not bind the effective-source namespace.'
    }

    $newVegasDataRoots = @($sourceRoots | Where-Object {
        Test-Path -LiteralPath (Join-Path $_ $primaryMaster) -PathType Leaf
    })
    if ($newVegasDataRoots.Count -lt 1) {
        throw 'TTW profile has no owned primary-game master source root.'
    }
    if ([string]::IsNullOrWhiteSpace($FalloutNvPath)) {
        $targetExe = [IO.Path]::GetFullPath(
            (Join-Path (Split-Path -Parent $newVegasDataRoots[0]) `
                ([string]$recipe.target.executable)))
    }
    else {
        $targetExe = [IO.Path]::GetFullPath($FalloutNvPath)
    }
    if (-not (Test-Path -LiteralPath $targetExe -PathType Leaf)) {
        throw "Missing FalloutNV.exe target: $targetExe"
    }
    $targetSha = Get-Sha256 $targetExe
    $targetVersion = (Get-Item -LiteralPath $targetExe).VersionInfo.FileVersion
    if ($targetSha -cne [string]$recipe.target.sha256 -or
        $targetVersion -cne [string]$recipe.target.version) {
        throw "Unsupported FalloutNV.exe identity: version=$targetVersion sha256=$targetSha"
    }
    $observerSha = Get-Sha256 $ghidrustExe
    if ($observerSha -cne [string]$recipe.observer.sha256) {
        throw "Unreviewed Ghidrust observer identity: sha256=$observerSha"
    }

    $participants = [ordered]@{}
    foreach ($roleProperty in $recipe.participants.PSObject.Properties) {
        $role = [string]$roleProperty.Name
        $source = $roleProperty.Value
        $referenceFormKey = [string]$source.referenceFormKey
        $stableReference = Get-StableLocalFormId $referenceFormKey "$role reference"
        if ($source.PSObject.Properties.Name -contains 'openingOperand') {
            $operandName = [string]$source.openingOperand
            $operand = Get-Property $opening.operands $operandName 'TTW opening operands'
            if ([string]$operand.formKey -cne $referenceFormKey) {
                throw "$role reference differs from its hash-bound opening operand."
            }
            $runtimeReference = [string]$operand.runtimeFormId
        }
        else {
            $runtimeReference = $stableReference
        }
        $packageFormKey = [string]$source.packageFormKey
        $idleFormKey = [string]$source.idleFormKey
        $packageStable = Get-StableLocalFormId $packageFormKey "$role PACK"
        $idleStable = Get-StableLocalFormId $idleFormKey "$role IDLE"
        if (($packageFormKey.Split(':')[0] -cne $primaryMaster) -or
            ($idleFormKey.Split(':')[0] -cne $primaryMaster)) {
            throw "$role Section01 PACK/IDLE is not in the zero-index FalloutNV origin."
        }
        $participants[$role] = [ordered]@{
            role = $role
            reference_form_key = $referenceFormKey
            stable_local_form_id = $stableReference
            runtime_form_id = $runtimeReference.ToLowerInvariant()
            runtime_reference_form_id_numeric =
                [Convert]::ToUInt32($runtimeReference, $HexadecimalRadix)
            package_form_key = $packageFormKey
            package_stable_local_form_id = $packageStable
            package_runtime_form_id = $packageStable
            package_runtime_form_id_numeric =
                [Convert]::ToUInt32($packageStable, $HexadecimalRadix)
            idle_form_key = $idleFormKey
            idle_stable_local_form_id = $idleStable
            idle_runtime_form_id = $idleStable
            idle_runtime_form_id_numeric =
                [Convert]::ToUInt32($idleStable, $HexadecimalRadix)
            sequence_name = [string]$source.sequenceName
            activation_stage = $source.activationStage
        }
    }
    if (($participants.Keys -join ',') -cne 'player,father,doctor,mother') {
        throw 'TTW observer recipe participant order/closure differs.'
    }
    return [ordered]@{
        recipe = $recipe
        recipe_path = $recipePath
        recipe_sha256 = $recipeSha
        source_root = $resolvedTtwRoot
        source_profile = [ordered]@{
            path = $profilePath
            sha256 = $profileSha
            schema = [string]$profile.schema
            status = [string]$profile.status
        }
        effective_source_namespace = [ordered]@{
            path = $namespacePath
            sha256 = $namespaceSha
            schema = [string]$namespace.schema
            status = [string]$namespace.status
        }
        opening_profile = [ordered]@{
            path = $openingPath
            sha256 = $openingSha
            schema = [string]$opening.schema
            status = [string]$opening.status
        }
        plugin_stack_id = $pluginStackId
        save_compatibility_id = $saveCompatibilityId
        record_resolution_policy = [string]$recipe.sourceIdentity.recordResolutionPolicy
        target = [ordered]@{
            path = $targetExe
            version = $targetVersion
            sha256 = $targetSha
            edition = 'TTW'
        }
        observer = [ordered]@{
            path = $ghidrustExe
            sha256 = $observerSha
            required_tool_surface = [int]$recipe.observer.toolSurface
            required_mode = [string]$recipe.observer.mode
        }
        participants = $participants
    }
}

$identity = Get-ValidatedIdentity
$recipe = $identity.recipe
$validation = [ordered]@{
    schema = $ExpectedValidationSchema
    validated_utc = [DateTime]::UtcNow.ToString('o')
    classification = 'preflight-only-no-process-attached'
    target = $identity.target
    observer = $identity.observer
    observer_recipe = [ordered]@{
        path = $identity.recipe_path
        sha256 = $identity.recipe_sha256
        schema = [string]$recipe.schema
    }
    ttw_identity = [ordered]@{
        source_root = $identity.source_root
        source_profile = $identity.source_profile
        effective_source_namespace = $identity.effective_source_namespace
        opening_profile = $identity.opening_profile
        plugin_stack_id = $identity.plugin_stack_id
        save_compatibility_id = $identity.save_compatibility_id
        record_resolution_policy = $identity.record_resolution_policy
    }
    participant_count = $identity.participants.Count
    participants = $identity.participants
    process_attached = $false
    production_contract_emitted = $false
    live_blocker =
        'A user-started exact FalloutNV.exe at TTW CG00 stage10 must be supplied by explicit PID.'
}
if ($ValidateOnly) {
    $validation | ConvertTo-Json -Depth ([int]$recipe.observation.contractJsonDepth)
    return
}

if ($TargetProcessId -le 0) {
    throw 'TargetProcessId is required; this observer never launches or selects FalloutNV.exe.'
}
$targetProcess = Get-Process -Id $TargetProcessId -ErrorAction Stop
$targetProcessPath = [IO.Path]::GetFullPath([string]$targetProcess.Path)
if (-not $targetProcessPath.Equals(
        [string]$identity.target.path,
        [StringComparison]::OrdinalIgnoreCase) -or
    (Get-Sha256 $targetProcessPath) -cne [string]$identity.target.sha256) {
    throw 'Target PID is not the exact FalloutNV.exe bound by the TTW profile.'
}

$privateEvidenceRoot = [IO.Path]::GetFullPath(
    [string]$recipe.observation.privateEvidenceRoot).TrimEnd('\')
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $privateEvidenceRoot `
        "ttw-fo3-cg00-stage10-observation-$stamp.json"
}
if ([string]::IsNullOrWhiteSpace($ContractOutputPath)) {
    $ContractOutputPath = Join-Path $privateEvidenceRoot `
        "ttw-fo3-cg00-stage10-camera-contract-$stamp.json"
}
$output = [IO.Path]::GetFullPath($OutputPath)
$contractOutput = [IO.Path]::GetFullPath($ContractOutputPath)
foreach ($candidate in @($output, $contractOutput)) {
    if (-not (Test-PathWithin $candidate $privateEvidenceRoot) -or
        (Test-PathWithin $candidate $RepoRoot)) {
        throw "Private TTW retail evidence output is outside its evidence root: $candidate"
    }
    if (Test-Path -LiteralPath $candidate) {
        throw "Refusing to overwrite private TTW retail evidence: $candidate"
    }
}
New-Item -ItemType Directory -Force -Path $privateEvidenceRoot | Out-Null

$mcp = $null
$sessionId = ''
$nextId = 1
try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [string]$identity.observer.path
    $startInfo.Arguments = 'mcp'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    if ($null -ne $startInfo.PSObject.Properties['StandardInputEncoding']) {
        $startInfo.StandardInputEncoding = $utf8NoBom
    }
    $mcp = [Diagnostics.Process]::new()
    $mcp.StartInfo = $startInfo
    if (-not $mcp.Start()) {
        throw 'Failed to start the private Win32 Ghidrust MCP observer.'
    }
    $null = $mcp.StandardInput
    $init = Send-McpRequest -Process $mcp -Id $nextId -Method 'initialize' -Params @{
        protocolVersion = [string]$recipe.observation.mcpProtocolVersion
        capabilities = @{}
        clientInfo = @{ name = 'opennv-ttw-fo3-retail-observer'; version = '1' }
    }
    $nextId++
    if ([string]$init.serverInfo.name -cne $ExpectedMcpServerName -or
        [int]$init.serverInfo.toolSurface -ne [int]$identity.observer.required_tool_surface) {
        throw 'Unexpected Ghidrust MCP server identity/tool surface.'
    }
    $mcp.StandardInput.WriteLine((@{
        jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{}
    } | ConvertTo-Json -Compress))
    $mcp.StandardInput.Flush()

    $session = Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_attach' -Arguments @{
            pid = $TargetProcessId
            mode = 'observe'
        }
    $sessionId = [string]$session.session_id
    if ([string]$session.mode -cne 'observe' -or
        @($session.capabilities) -contains 'write' -or
        @($session.capabilities) -contains 'break') {
        throw 'Ghidrust did not establish a strict read-only observe session.'
    }
    $modules = @(Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_modules' -Arguments @{ session_id = $sessionId })
    $mainModule = @($modules | Where-Object { $_.name -ieq 'FalloutNV.exe' })
    if ($mainModule.Count -ne 1) {
        throw "Expected one FalloutNV.exe module, found $($mainModule.Count)."
    }
    $moduleBase = [uint64]$mainModule[0].base
    $moduleSize = [uint64]$mainModule[0].size
    $regions = @(Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
        -Name 'process_regions' -Arguments @{
            session_id = $sessionId
            max = [int]$recipe.observation.maximumRegions
        })

    $resolution = [ordered]@{
        captured_utc = [DateTime]::UtcNow.ToString('o')
        participants = [ordered]@{}
        active_camera = $null
        camera1st = $null
        promotion_failures = [Collections.Generic.List[string]]::new()
    }
    foreach ($role in $identity.participants.Keys) {
        $participantResolution = Resolve-Participant -Process $mcp `
            -NextId ([ref]$nextId) -SessionId $sessionId `
            -Contract $identity.participants[$role] -ModuleBase $moduleBase `
            -ModuleSize $moduleSize -Abi $recipe.abi `
            -Observation $recipe.observation
        $resolution.participants[$role] = $participantResolution
        if (-not $participantResolution.unique) {
            $resolution.promotion_failures.Add(
                "$role-live-sequence-reference-pack-idle-join-not-unique:" +
                $participantResolution.joined_candidate_count)
        }
    }
    try {
        $resolution.active_camera = Resolve-ActiveCamera -Process $mcp `
            -NextId ([ref]$nextId) -SessionId $sessionId `
            -ModuleBase $moduleBase -ModuleSize $moduleSize -Abi $recipe.abi `
            -MinimumNonSingularScale `
                ([single]$recipe.observation.minimumNonSingularScale)
    }
    catch {
        $resolution.promotion_failures.Add("active-camera:$($_.Exception.Message)")
    }
    if ($resolution.participants.player.unique) {
        try {
            $playerSequence = $resolution.participants.player.candidates[0].sequence
            $resolution.camera1st = Resolve-Camera1st -Process $mcp `
                -NextId ([ref]$nextId) -SessionId $sessionId `
                -PlayerSequence $playerSequence -ModuleBase $moduleBase `
                -ModuleSize $moduleSize -Abi $recipe.abi `
                -Observation $recipe.observation
        }
        catch {
            $resolution.promotion_failures.Add("camera1st:$($_.Exception.Message)")
        }
    }
    else {
        $resolution.promotion_failures.Add('camera1st:player-live-join-unresolved')
    }

    $participants = [ordered]@{}
    if ($null -ne $resolution.active_camera) {
        $cameraTransform = $resolution.active_camera.world_transform
        $near = [double]$resolution.active_camera.frustum.near_game_units
        foreach ($role in $identity.participants.Keys) {
            $resolved = $resolution.participants[$role]
            if (-not $resolved.unique) { continue }
            $join = $resolved.candidates[0]
            $reference = $join.reference
            $sequence = $join.sequence
            $contract = $identity.participants[$role]
            $cameraLocal = @(ConvertTo-CameraSpace $cameraTransform `
                @($reference.rendered_node.world_transform.translation_game_units))
            $participants[$role] = [ordered]@{
                reference_form_key = $contract.reference_form_key
                stable_local_form_id = $contract.stable_local_form_id
                runtime_form_id = $contract.runtime_form_id
                live_reference_address = $reference.address
                rendered_node_address = $reference.rendered_node.address
                visible = $reference.rendered_node.visible
                app_culled = $reference.rendered_node.app_culled
                rendered_world_transform = $reference.rendered_node.world_transform
                section01_sequence = [ordered]@{
                    address = $sequence.address
                    name = $sequence.name
                    name_address = $sequence.name_address
                    cycle_type = $sequence.cycle_type
                    frequency = $sequence.frequency
                    begin_time_seconds = $sequence.begin_time_seconds
                    end_time_seconds = $sequence.end_time_seconds
                    last_time_seconds = $sequence.last_time_seconds
                    last_scaled_time_seconds = $sequence.last_scaled_time_seconds
                    state = $sequence.state
                    accumulation_root = $sequence.accumulation_root
                    actor_node_ancestry_join = $sequence.accumulation_root_name
                }
                camera_local_game_units = $cameraLocal
                rendered_root_depth_game_units = $cameraLocal[0]
                rendered_root_near_plane_separation_game_units = $cameraLocal[0] - $near
            }
        }
    }
    if ($participants.Count -ne $identity.participants.Count) {
        $resolution.promotion_failures.Add(
            "camera-space-participant-closure:$($participants.Count)/$($identity.participants.Count)")
    }
    if ($null -eq $resolution.camera1st) {
        $resolution.promotion_failures.Add('camera1st:unresolved')
    }

    $raw = [ordered]@{
        schema = $ExpectedRawObservationSchema
        captured_utc = $resolution.captured_utc
        classification = if ($resolution.promotion_failures.Count -eq 0) {
            'private-exact-live-ttw-stage10-observation-ready-for-contract-emission'
        } else { 'private-live-ttw-stage10-observation-rejected-fail-closed' }
        observer_recipe = [ordered]@{
            path = $identity.recipe_path
            sha256 = $identity.recipe_sha256
            schema = [string]$recipe.schema
        }
        ttw_identity = $validation.ttw_identity
        process = [ordered]@{
            pid = $TargetProcessId
            executable_path = $targetProcessPath
            launched_by_observer = $false
            session_mode = [string]$session.mode
            capabilities = @($session.capabilities)
        }
        module_inventory = $modules
        region_count = $regions.Count
        resolution = $resolution
        prohibitions = @($recipe.prohibitions)
    }
    $raw | ConvertTo-Json -Depth ([int]$recipe.observation.contractJsonDepth) |
        Set-Content -LiteralPath $output -Encoding utf8NoBOM
    if ($resolution.promotion_failures.Count -ne 0) {
        throw ('TTW FO3 live stage10 contract rejected: ' +
            ($resolution.promotion_failures -join '; '))
    }

    $cameraContract = [ordered]@{
        bs_scene_graph_address = $resolution.active_camera.bs_scene_graph_address
        camera_address = $resolution.active_camera.camera_address
        vtable = $resolution.active_camera.vtable
        world_transform = $resolution.active_camera.world_transform
        frustum = $resolution.active_camera.frustum
        viewport_normalized = $resolution.active_camera.viewport_normalized
        derived_world_to_clip_row_major =
            $resolution.active_camera.derived_world_to_clip_row_major
    }
    $camera1stContract = $resolution.camera1st.node
    $stageJoins = [ordered]@{}
    foreach ($role in $identity.participants.Keys) {
        $source = $identity.participants[$role]
        $stageJoins[$role] = [ordered]@{
            package_form_key = $source.package_form_key
            package_stable_local_form_id = $source.package_stable_local_form_id
            package_runtime_form_id = $source.package_runtime_form_id
            idle_form_key = $source.idle_form_key
            idle_stable_local_form_id = $source.idle_stable_local_form_id
            idle_runtime_form_id = $source.idle_runtime_form_id
            sequence_name = $source.sequence_name
            activation_stage = $source.activation_stage
        }
    }
    $contract = [ordered]@{
        schema = $ExpectedContractSchema
        classification = $ExpectedContractClassification
        captured_utc = $resolution.captured_utc
        target = $identity.target
        observer = $identity.observer
        ttw_identity = $validation.ttw_identity
        stage_identity = [ordered]@{
            quest = [string]$recipe.observation.questEditorId
            stage = [int]$recipe.observation.stage
            proof =
                'Each exact Section01 controller is live and uniquely joined through its rendered root to the expected runtime reference, current PACK, and current IDLE in the hash-bound TTW effective namespace.'
            owned_package_idle_joins = $stageJoins
        }
        active_camera = $cameraContract
        camera1st = $camera1stContract
        participants = $participants
        coordinate_contract = [ordered]@{
            source_units = [string]$recipe.observation.sourceUnits
            matrix_storage = [string]$recipe.observation.matrixStorage
            world_to_local = [string]$recipe.observation.worldToLocal
            camera_forward_axis = [string]$recipe.observation.cameraForwardAxis
            evidence =
                'FalloutNV.exe NiCamera, SceneGraph, NiAV, TESObjectREFR, BaseProcess, PACK/IDLE, and NiControllerSequence layouts are exact target-wide clean-room contracts; no stage transform is authored here.'
        }
        unimplemented_boundary =
            'This private TTW contract proves the exact live stage10 camera/root/package/idle/controller snapshot only; it does not claim matched posed-mesh pixels, dialogue timing, xNVSE/JAM execution, or an interactive OpenNV Vault runtime.'
        raw_observation = [ordered]@{
            path = $output
            sha256 = Get-Sha256 $output
        }
    }
    $contract | ConvertTo-Json -Depth ([int]$recipe.observation.contractJsonDepth) |
        Set-Content -LiteralPath $contractOutput -Encoding utf8NoBOM
    $contract | ConvertTo-Json -Depth 4
    Write-Host "Private TTW FO3 stage10 observation written to $output"
    Write-Host "Private TTW FO3 stage10 contract written to $contractOutput"
}
finally {
    if ($null -ne $mcp -and -not $mcp.HasExited) {
        if (-not [string]::IsNullOrWhiteSpace($sessionId)) {
            try {
                Invoke-McpTool -Process $mcp -NextId ([ref]$nextId) `
                    -Name 'process_detach' -Arguments @{ session_id = $sessionId } |
                    Out-Null
            }
            catch {}
        }
        try { $mcp.StandardInput.Close() } catch {}
        if (-not $mcp.WaitForExit([int]$recipe.observation.maximumRemoteStringBytes)) {
            $mcp.Kill()
            $mcp.WaitForExit()
        }
        $mcp.Dispose()
    }
}
