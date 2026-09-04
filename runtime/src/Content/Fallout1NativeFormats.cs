using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record Fallout1NativeMap(
    int Version,
    string Name,
    int EnteringTile,
    int EnteringElevation,
    int EnteringRotation,
    IReadOnlyDictionary<int, uint[]> Elevations,
    IReadOnlyList<Fallout1NativeMapScriptRecord> LiveScripts,
    int ObjectSectionOffset,
    int TotalObjects,
    Fallout1NativePlacedObject FirstObject)
{
    internal int FirstObjectOffset => FirstObject.SourceOffset;
    internal int FirstObjectPid => FirstObject.Pid;
    internal uint FirstObjectFid => FirstObject.Fid;
}

internal sealed record Fallout1NativeMapScriptRecord(
    int ListType,
    int ExtentIndex,
    int SlotIndex,
    int SourceOffset,
    uint ScriptId,
    int RecordBytes,
    int ScriptProgramIndex,
    int? ObjectId);

internal sealed record Fallout1NativePlacedObject(
    int SourceOffset,
    string BaseLayout,
    int ObjectId,
    int Tile,
    int PixelX,
    int PixelY,
    int Frame,
    int Rotation,
    uint Fid,
    int Elevation,
    int Pid,
    uint ScriptId,
    int InventoryLength);

internal static class Fallout1NativeMapReader
{
    private const int HeaderBytes = 0xec;
    private const int HeaderNameOffset = 0x04;
    private const int HeaderNameBytes = 16;
    private const int HeaderValuesOffset = 0x14;
    private const int HeaderValueCount = 10;
    private const int HeaderFlagsIndex = 5;
    private const int HeaderGlobalVariableCountIndex = 7;
    private const int Fallout1MapVersion = 19;
    private const int MaximumElevation = 2;
    private const int MaximumRotation = 5;
    private const int MapGridDimension = 200;
    private const int TileEntries = 10000;
    private const int TileBytes = TileEntries * sizeof(uint);
    private const int KnownFlagsMask = 0x0f;
    private const int ScriptListCount = 5;
    private const int ScriptExtentSlots = 16;
    private const int CompactObjectBytes = 68;
    private const int FullObjectBytes = 84;
    private const int MaximumInventoryEntries = 10000;
    private const int MaximumTopLevelObjects = 100000;
    private const int ObjectTypeBitShift = 24;
    private const int MaximumSupportedObjectType = 5;
    private const int FullTileIndex = 1;
    private const int FullRotationIndex = 7;
    private const int FullFidIndex = 8;
    private const int FullFrameIndex = 6;
    private const int FullElevationIndex = 10;
    private const int FullPidIndex = 11;
    private const int FullScriptIdIndex = 16;
    private const int FullInventoryLengthIndex = 18;
    private const int CompactTileIndex = 1;
    private const int CompactFidIndex = 4;
    private const int CompactElevationIndex = 6;
    private const int CompactPidIndex = 7;
    private const int CompactScriptIdIndex = 12;
    private const int CompactInventoryLengthIndex = 14;
    private const int SpatialScriptIdType = 1;
    private const int ObjectScriptIdType = 2;
    private const int MapScriptObjectProgramWord = 3;
    private const int MapScriptSpatialProgramWord = 5;
    private const int MapScriptObjectIdWord = 5;
    private const int SpatialScriptRecordBytes = 72;
    private const int ObjectScriptRecordBytes = 68;
    private const int DefaultScriptRecordBytes = 64;

    internal static Fallout1NativeMap Read(byte[] data)
    {
        if (data.Length < HeaderBytes)
            throw new InvalidDataException("Fallout 1 MAP header is truncated.");
        var version = ReadInt32(data, 0);
        if (version != Fallout1MapVersion)
            throw new NotSupportedException($"Fallout 1 MAP version {version} is unsupported.");
        var nameBytes = data.AsSpan(HeaderNameOffset, HeaderNameBytes);
        var terminator = nameBytes.IndexOf((byte)0);
        if (terminator <= 0)
            throw new InvalidDataException("Fallout 1 MAP name is empty or unterminated.");
        var name = Encoding.ASCII.GetString(nameBytes[..terminator]);
        if (!name.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Fallout 1 MAP stored name is invalid: {name}");
        var values = Enumerable.Range(0, HeaderValueCount)
            .Select(index => ReadInt32(data, HeaderValuesOffset + index * sizeof(int))).ToArray();
        var enteringTile = values[0];
        var enteringElevation = values[1];
        var enteringRotation = values[2];
        var localVariables = values[3];
        var flags = values[HeaderFlagsIndex];
        var globalVariables = values[HeaderGlobalVariableCountIndex];
        if (enteringTile is < 0 or >= MapGridDimension * MapGridDimension ||
            enteringElevation is < 0 or > MaximumElevation ||
            enteringRotation is < 0 or > MaximumRotation ||
            localVariables < 0 || globalVariables < 0 || (flags & ~KnownFlagsMask) != 0)
            throw new InvalidDataException("Fallout 1 MAP header values are outside the admitted contract.");
        var offset = checked(HeaderBytes + (globalVariables + localVariables) * sizeof(int));
        var elevations = new Dictionary<int, uint[]>();
        for (var elevation = 0; elevation <= MaximumElevation; ++elevation)
        {
            var absent = elevation switch { 0 => 0x02, 1 => 0x04, _ => 0x08 };
            if ((flags & absent) != 0) continue;
            if (offset > data.Length - TileBytes)
                throw new InvalidDataException($"Fallout 1 MAP elevation {elevation} is truncated.");
            var tiles = new uint[TileEntries];
            for (var index = 0; index < tiles.Length; ++index)
                tiles[index] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + index * sizeof(uint)));
            elevations.Add(elevation, tiles);
            offset += TileBytes;
        }
        var scriptSection = ReadScriptSection(data, offset);
        offset = scriptSection.EndOffset;
        var objectSectionOffset = offset;
        var totalObjects = ReadInt32(data, offset);
        offset += sizeof(int);
        if (totalObjects <= 0 || totalObjects > MaximumTopLevelObjects)
            throw new InvalidDataException($"Fallout 1 MAP object count is unsupported: {totalObjects}");
        var elevationZeroObjects = ReadInt32(data, offset);
        offset += sizeof(int);
        if (elevationZeroObjects <= 0 || elevationZeroObjects > totalObjects)
            throw new NotSupportedException("The bounded Fallout 1 audit requires a first elevation object.");
        var firstObject = ReadObject(data, offset);
        if (firstObject.Elevation != 0)
            throw new InvalidDataException("The first Fallout 1 MAP object is not in elevation zero.");
        return new Fallout1NativeMap(
            version, name, enteringTile, enteringElevation, enteringRotation,
            elevations, scriptSection.LiveScripts, objectSectionOffset, totalObjects, firstObject);
    }

    private static (int EndOffset, IReadOnlyList<Fallout1NativeMapScriptRecord> LiveScripts)
        ReadScriptSection(byte[] data, int offset)
    {
        var liveScripts = new List<Fallout1NativeMapScriptRecord>();
        for (var list = 0; list < ScriptListCount; ++list)
        {
            var live = ReadInt32(data, offset);
            offset += sizeof(int);
            if (live < 0)
                throw new InvalidDataException($"Fallout 1 MAP script list {list} has a negative count.");
            var extentCount = checked((live + ScriptExtentSlots - 1) / ScriptExtentSlots);
            var admitted = 0;
            for (var extent = 0; extent < extentCount; ++extent)
            {
                var extentRecords = new List<Fallout1NativeMapScriptRecord>(ScriptExtentSlots);
                for (var slot = 0; slot < ScriptExtentSlots; ++slot)
                {
                    var recordOffset = offset;
                    var sid = ReadInt32(data, offset);
                    var scriptId = unchecked((uint)sid);
                    var sidType = sid >= 0 ? (int)(scriptId >> ObjectTypeBitShift) : byte.MaxValue;
                    var recordBytes = sidType switch
                    {
                        SpatialScriptIdType => SpatialScriptRecordBytes,
                        ObjectScriptIdType => ObjectScriptRecordBytes,
                        _ => DefaultScriptRecordBytes,
                    };
                    RequireRange(data, offset, recordBytes, "script record");
                    var scriptProgramIndex = ReadInt32(
                        data,
                        offset + (sidType == SpatialScriptIdType
                            ? MapScriptSpatialProgramWord
                            : MapScriptObjectProgramWord) * sizeof(int));
                    int? objectId = sidType == SpatialScriptIdType
                        ? null
                        : ReadInt32(data, offset + MapScriptObjectIdWord * sizeof(int));
                    extentRecords.Add(new Fallout1NativeMapScriptRecord(
                        list, extent, slot, recordOffset, scriptId, recordBytes,
                        scriptProgramIndex, objectId));
                    offset += recordBytes;
                }
                var length = ReadInt32(data, offset);
                offset += sizeof(int);
                _ = ReadInt32(data, offset);
                offset += sizeof(int);
                if (length is < 0 or > ScriptExtentSlots)
                    throw new InvalidDataException("Fallout 1 MAP script extent length is invalid.");
                liveScripts.AddRange(extentRecords.Take(length));
                admitted += length;
            }
            if (admitted != live)
                throw new InvalidDataException("Fallout 1 MAP script live count differs from its extents.");
        }
        return (offset, liveScripts);
    }

    private static Fallout1NativePlacedObject ReadObject(byte[] data, int offset)
    {
        RequireRange(data, offset, CompactObjectBytes, "first object");
        var full = data.Length - offset >= FullObjectBytes
            ? Enumerable.Range(0, FullObjectBytes / sizeof(int)).Select(index =>
                ReadInt32(data, offset + index * sizeof(int))).ToArray()
            : null;
        if (full is not null && Structural(
                full[FullTileIndex], full[FullRotationIndex], full[FullFidIndex],
                full[FullElevationIndex], full[FullPidIndex], full[FullInventoryLengthIndex]))
            return new Fallout1NativePlacedObject(
                offset,
                "full-21",
                full[0],
                full[FullTileIndex],
                full[2],
                full[3],
                full[FullFrameIndex],
                full[FullRotationIndex],
                unchecked((uint)full[FullFidIndex]),
                full[FullElevationIndex],
                full[FullPidIndex],
                unchecked((uint)full[FullScriptIdIndex]),
                full[FullInventoryLengthIndex]);
        var compact = Enumerable.Range(0, CompactObjectBytes / sizeof(int)).Select(index =>
            ReadInt32(data, offset + index * sizeof(int))).ToArray();
        if (!Structural(
                compact[CompactTileIndex], 0, compact[CompactFidIndex],
                compact[CompactElevationIndex], compact[CompactPidIndex],
                compact[CompactInventoryLengthIndex]))
            throw new NotSupportedException("Fallout 1 first MAP object matches neither admitted base layout.");
        return new Fallout1NativePlacedObject(
            offset,
            "compact-17",
            compact[0],
            compact[CompactTileIndex],
            compact[2],
            compact[3],
            0,
            0,
            unchecked((uint)compact[CompactFidIndex]),
            compact[CompactElevationIndex],
            compact[CompactPidIndex],
            unchecked((uint)compact[CompactScriptIdIndex]),
            compact[CompactInventoryLengthIndex]);
    }

    private static bool Structural(int tile, int rotation, int fid, int elevation, int pid, int inventory) =>
        (tile == -1 || tile is >= 0 and < MapGridDimension * MapGridDimension) &&
        rotation is >= 0 and <= MaximumRotation && elevation is >= 0 and <= MaximumElevation &&
        inventory is >= 0 and <= MaximumInventoryEntries &&
        ((unchecked((uint)fid) >> ObjectTypeBitShift) & 0x0f) <= MaximumSupportedObjectType &&
        ((unchecked((uint)pid) >> ObjectTypeBitShift) <= MaximumSupportedObjectType) &&
        (unchecked((uint)pid) & 0x00ffffff) > 0;

    private static int ReadInt32(byte[] data, int offset)
    {
        RequireRange(data, offset, sizeof(int), "integer");
        return BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, sizeof(int)));
    }

    private static void RequireRange(byte[] data, int offset, int bytes, string label)
    {
        if (offset < 0 || bytes < 0 || offset > data.Length - bytes)
            throw new InvalidDataException($"Fallout 1 MAP is truncated at {label}.");
    }
}

internal sealed record Fallout1NativePrototype(int Pid, uint Fid, string LogicalPath);

internal static class Fallout1NativePrototypeReader
{
    private const int ObjectTypeBitShift = 24;
    private const int MinimumPrototypeBytes = 12;
    private const int PrototypeFidOffset = 8;
    private static readonly string[] Directories =
        ["items", "critters", "scenery", "walls", "tiles", "misc"];

    internal static Fallout1NativePrototype Resolve(Fallout1OwnedContentSource source, int pid)
    {
        var unsigned = unchecked((uint)pid);
        var type = (int)(unsigned >> ObjectTypeBitShift);
        var listIndex = (int)(unsigned & 0x00ffffff);
        if (type < 0 || type >= Directories.Length || listIndex <= 0)
            throw new NotSupportedException($"Fallout 1 PID 0x{unsigned:x8} is outside the admitted PRO types.");
        var directory = Directories[type];
        var names = Fallout1NativeLists.Read(source.Read($"proto\\{directory}\\{directory}.lst").Bytes);
        if (listIndex > names.Count)
            throw new InvalidDataException($"Fallout 1 PID 0x{unsigned:x8} exceeds its PRO list.");
        var logical = $"proto\\{directory}\\{names[listIndex - 1]}";
        var bytes = source.Read(logical).Bytes;
        if (bytes.Length < MinimumPrototypeBytes)
            throw new InvalidDataException($"Fallout 1 PRO is truncated: {logical}");
        var storedPid = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        var fid = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(PrototypeFidOffset));
        if (storedPid != unsigned)
            throw new InvalidDataException($"Fallout 1 PRO PID differs: {logical}");
        return new Fallout1NativePrototype(pid, fid, logical);
    }

    internal static string ResolveArt(Fallout1OwnedContentSource source, uint fid)
    {
        var type = (int)((fid >> ObjectTypeBitShift) & 0x0f);
        var artIndex = (int)(fid & 0x0fff);
        if (type < 0 || type >= Directories.Length)
            throw new NotSupportedException($"Fallout 1 FID 0x{fid:x8} has an unsupported art type.");
        var directory = Directories[type];
        var names = Fallout1NativeLists.Read(source.Read($"art\\{directory}\\{directory}.lst").Bytes);
        if (artIndex >= names.Count)
            throw new InvalidDataException($"Fallout 1 FID 0x{fid:x8} exceeds its art list.");
        if (type == 1)
            throw new NotSupportedException(
                $"Fallout 1 critter FID 0x{fid:x8} requires animation-suffix resolution beyond this audit.");
        return $"art\\{directory}\\{names[artIndex]}";
    }
}

internal static class Fallout1NativeLists
{
    internal static IReadOnlyList<string> Read(byte[] bytes)
    {
        if (bytes.Any(value => value > 0x7f))
            throw new NotSupportedException("Fallout 1 list filenames outside ASCII are not admitted.");
        var text = Encoding.ASCII.GetString(bytes);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => line.Split(' ', 2)[0].Trim())
            .Where(line => line.Length > 0).ToArray();
    }
}

internal sealed record Fallout1NativeFrmFrame(
    uint Version, ushort StoredFps, ushort FramesPerDirection,
    int Rotation, short DirectionX, short DirectionY,
    ushort Width, ushort Height, short FrameX, short FrameY,
    byte[] PaletteIndexes);

internal static class Fallout1NativeFrmReader
{
    private const int HeaderBytes = 0x3e;
    private const int DirectionXOffsetsOffset = 0x0a;
    private const int DirectionYOffsetsOffset = 0x16;
    private const int RotationOffsetsOffset = 0x22;
    private const int FrameAreaBytesOffset = 0x3a;
    private const int FrameHeaderBytes = 12;
    private const uint Fallout1FrmVersion = 3U;
    private const uint Fallout1AlternateFrmVersion = 4U;

    private const int DirectionCount = sizeof(long) - sizeof(short);

    internal static Fallout1NativeFrmFrame ReadFirstFrame(byte[] data, int rotation = 0)
    {
        if (data.Length < HeaderBytes || rotation is < 0 or >= DirectionCount)
            throw new InvalidDataException("Fallout 1 FRM header is truncated.");
        var version = BinaryPrimitives.ReadUInt32BigEndian(data);
        var fps = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(sizeof(uint)));
        var frames = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(sizeof(uint) * 2));
        if (version is not (Fallout1FrmVersion or Fallout1AlternateFrmVersion) || frames == 0)
            throw new NotSupportedException($"Fallout 1 FRM {version}/{frames} is unsupported.");
        var directionX = BinaryPrimitives.ReadInt16BigEndian(
            data.AsSpan(DirectionXOffsetsOffset + rotation * sizeof(short)));
        var directionY = BinaryPrimitives.ReadInt16BigEndian(
            data.AsSpan(DirectionYOffsetsOffset + rotation * sizeof(short)));
        var relativeOffset = BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(RotationOffsetsOffset + rotation * sizeof(uint)));
        var frameAreaBytes = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(FrameAreaBytesOffset));
        var frameAreaEnd = frameAreaBytes == 0 ? data.Length :
            checked((int)Math.Min(data.Length, HeaderBytes + (long)frameAreaBytes));
        var cursor = checked(HeaderBytes + (int)relativeOffset);
        if (cursor > frameAreaEnd - FrameHeaderBytes)
            throw new InvalidDataException("Fallout 1 FRM frame header escapes its frame area.");
        var width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor));
        var height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor + sizeof(ushort)));
        var payloadBytes = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cursor + sizeof(uint)));
        var frameX = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(cursor + sizeof(uint) * 2));
        var frameY = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(cursor + sizeof(uint) * 2 + sizeof(short)));
        cursor += FrameHeaderBytes;
        if (width == 0 || height == 0 || payloadBytes != (uint)width * height ||
            payloadBytes > int.MaxValue || cursor > frameAreaEnd - (int)payloadBytes)
            throw new InvalidDataException("Fallout 1 FRM dimensions are invalid.");
        return new Fallout1NativeFrmFrame(
            version, fps, frames, rotation, directionX, directionY,
            width, height, frameX, frameY,
            data.AsSpan(cursor, (int)payloadBytes).ToArray());
    }
}
