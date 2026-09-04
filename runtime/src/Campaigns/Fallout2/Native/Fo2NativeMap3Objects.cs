using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Campaigns.Fallout2.Native;

internal sealed record Fo2NativePrototypeDetail(
    uint Pid,
    int ObjectType,
    int? MessageNumber,
    uint? Fid,
    int? Subtype,
    string? LogicalPath);

internal sealed record Fo2NativeMapObject(
    int Serial,
    int SourceOffset,
    int ObjectId,
    int Tile,
    int PixelX,
    int PixelY,
    int Frame,
    int Rotation,
    uint Fid,
    uint Flags,
    int Elevation,
    uint Pid,
    uint ScriptId,
    int InventoryLength,
    uint InstanceFlags,
    IReadOnlyList<int> InstanceValues,
    int Depth,
    Fo2NativePrototypeDetail Prototype,
    IReadOnlyList<Fo2NativeMapObject> Inventory);

internal sealed record Fo2NativeMap3ObjectGraph(
    int ScriptSlots,
    int LiveScripts,
    int TotalTopLevelObjects,
    IReadOnlyList<Fo2NativeMapObject> TopLevelObjects,
    int NestedObjects,
    int EndOffset);

internal static class Fo2NativeMap3ObjectGraphReader
{
    private const int ScriptListCount = 5;
    private const int SupportedMapVersion = 20;
    private const int LegacyMapVersion = 19;
    private const int ScriptSlotsPerExtent = 16;
    private const int ScriptOrdinaryRecordBytes = 64;
    private const int ScriptSpatialRecordBytes = 72;
    private const int ScriptTimedRecordBytes = 68;
    private const int ScriptSpatialType = 1;
    private const int ScriptTimedType = 2;
    private const int FullObjectWords = 21;
    private const int ObjectIdWord = 0;
    private const int TileWord = 1;
    private const int PixelXWord = 2;
    private const int PixelYWord = 3;
    private const int FrameWord = 6;
    private const int RotationWord = 7;
    private const int FidWord = 8;
    private const int FlagsWord = 9;
    private const int ElevationWord = 10;
    private const int PidWord = 11;
    private const int ScriptIdWord = 16;
    private const int InventoryLengthWord = 18;
    private const int MaximumObjects = 100000;
    private const int MaximumInventoryEntries = 10000;
    private const int MaximumDepth = 16;
    private const int MaximumTile = 40000;
    private const int MaximumRotation = 5;
    private const int MaximumElevation = 2;
    private const int ObjectTypeShift = 24;
    private const uint ObjectIndexMask = 0x00ffffffU;
    private const uint BuiltinPlayerPid = 0x01000000U;
    private const uint SourceItemSentinelPid = 0x000000ffU;
    private const uint SourceCritterSentinelPid = 0x010001ffU;
    private const int PrototypeHeaderBytes = 12;
    private const int PrototypeSubtypeOffset = 0x20;
    private const int PrototypeTypedBytes = 0x24;
    private const int CritterType = 1;
    private const int ItemType = 0;
    private const int SceneryType = 2;
    private const int MiscType = 5;
    private const int ItemArmorSubtype = 0;
    private const int ItemContainerSubtype = 1;
    private const int ItemDrugSubtype = 2;
    private const int ItemWeaponSubtype = 3;
    private const int ItemAmmoSubtype = 4;
    private const int ItemMiscSubtype = 5;
    private const int ItemKeySubtype = 6;
    private const int SceneryDoorSubtype = 0;
    private const int SceneryStairsSubtype = 1;
    private const int SceneryElevatorSubtype = 2;
    private const int SceneryLadderBottomSubtype = 3;
    private const int SceneryLadderTopSubtype = 4;
    private const int SceneryGenericSubtype = 5;
    private const int CritterInstanceWords = 11;
    private const int ExitGridFirstLower = 1600;
    private const int ExitGridFirstUpper = 2400;
    private const int ExitGridSecondLower = 3100;
    private const int ExitGridSecondUpper = 4700;
    private const int ExitGridStride = 100;
    private static readonly string[] TypeDirectories =
        ["items", "critters", "scenery", "walls", "tiles", "misc"];

    internal static Fo2NativeMap3ObjectGraph Read(
        byte[] data,
        Fo2NativeMap map,
        Fo2NativeOwnedSource source)
    {
        if (map.Version != SupportedMapVersion || map.MapIndex != 3)
            throw new InvalidDataException("The object graph reader accepts exact Fallout 2 Map 3 only.");
        var resolver = new PrototypeResolver(source);
        var offset = map.ContentOffsetAfterTiles;
        var scriptSlots = 0;
        var liveScripts = 0;
        for (var list = 0; list < ScriptListCount; ++list)
        {
            var live = ReadInt32(data, ref offset, $"script list {list} count");
            if (live < 0)
                throw new InvalidDataException($"Map 3 script list {list} has a negative count.");
            liveScripts += live;
            var extents = checked((live + ScriptSlotsPerExtent - 1) / ScriptSlotsPerExtent);
            var extentLive = 0;
            for (var extent = 0; extent < extents; ++extent)
            {
                for (var slot = 0; slot < ScriptSlotsPerExtent; ++slot)
                {
                    Require(data, offset, sizeof(int), "script identity");
                    var sid = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
                    var type = sid < 0 ? -1 : (int)((uint)sid >> ObjectTypeShift);
                    var bytes = type == ScriptSpatialType ? ScriptSpatialRecordBytes :
                        type == ScriptTimedType ? ScriptTimedRecordBytes : ScriptOrdinaryRecordBytes;
                    Require(data, offset, bytes, "script record");
                    offset += bytes;
                    scriptSlots++;
                }
                var length = ReadInt32(data, ref offset, "script extent length");
                _ = ReadInt32(data, ref offset, "script extent next");
                if (length is < 0 or > ScriptSlotsPerExtent)
                    throw new InvalidDataException($"Map 3 script extent length {length} is invalid.");
                extentLive += length;
            }
            if (extentLive != live)
                throw new InvalidDataException($"Map 3 script list {list} differs: {extentLive}/{live}.");
        }

        var total = ReadInt32(data, ref offset, "total object count");
        if (total is < 0 or > MaximumObjects)
            throw new InvalidDataException($"Map 3 total object count {total} is invalid.");
        var topLevel = new List<Fo2NativeMapObject>(total);
        var serial = 0;
        var nested = 0;
        for (var elevation = 0; elevation <= MaximumElevation; ++elevation)
        {
            var count = ReadInt32(data, ref offset, $"elevation {elevation} object count");
            if (count is < 0 or > MaximumObjects || topLevel.Count + count > total)
                throw new InvalidDataException($"Map 3 elevation {elevation} object count {count} is invalid.");
            for (var index = 0; index < count; ++index)
            {
                var placed = ReadObject(
                    data, ref offset, map.Version, elevation, 0, resolver, ref serial, ref nested);
                if (placed.Elevation != elevation)
                    throw new InvalidDataException(
                        $"Map 3 object at 0x{placed.SourceOffset:x} has the wrong elevation.");
                topLevel.Add(placed);
            }
        }
        if (topLevel.Count != total || offset != data.Length)
            throw new InvalidDataException(
                $"Map 3 object graph ended at 0x{offset:x}; expected 0x{data.Length:x}.");
        return new Fo2NativeMap3ObjectGraph(
            scriptSlots, liveScripts, total, topLevel, nested, offset);
    }

    internal static string ResolveArt(Fo2NativeOwnedSource source, uint fid)
    {
        var type = (int)((fid >> ObjectTypeShift) & 0x0f);
        var artIndex = (int)(fid & 0x0fffU);
        if (type < 0 || type >= TypeDirectories.Length || type == CritterType)
            throw new NotSupportedException($"Fallout 2 FID {fid:x8} requires unsupported art semantics.");
        var directory = TypeDirectories[type];
        var names = ReadList(source.Read($"art\\{directory}\\{directory}.lst", out _));
        if (artIndex >= names.Count)
            throw new InvalidDataException($"Fallout 2 FID {fid:x8} exceeds its art list.");
        return $"art\\{directory}\\{names[artIndex]}";
    }

    private static Fo2NativeMapObject ReadObject(
        byte[] data,
        ref int offset,
        int mapVersion,
        int containingElevation,
        int depth,
        PrototypeResolver resolver,
        ref int serial,
        ref int nested)
    {
        if (depth > MaximumDepth)
            throw new InvalidDataException("Map 3 inventory nesting exceeds the admitted depth.");
        var sourceOffset = offset;
        var values = ReadWords(data, offset, FullObjectWords);
        var tile = values[TileWord];
        var rotation = values[RotationWord];
        var fid = unchecked((uint)values[FidWord]);
        var elevation = values[ElevationWord];
        var pid = unchecked((uint)values[PidWord]);
        var inventoryLength = values[InventoryLengthWord];
        if ((tile != -1 && tile is < 0 or >= MaximumTile) ||
            rotation is < 0 or > MaximumRotation || elevation is < 0 or > MaximumElevation ||
            inventoryLength is < 0 or > MaximumInventoryEntries ||
            depth == 0 && elevation != containingElevation)
            throw new InvalidDataException($"Map 3 object fields are invalid at 0x{sourceOffset:x}.");
        offset += FullObjectWords * sizeof(int);
        var prototype = resolver.Resolve(pid);
        var extraWords = InstanceExtraWords(mapVersion, prototype);
        uint instanceFlags;
        int[] instanceValues;
        if (prototype.ObjectType == CritterType)
        {
            instanceValues = ReadWords(data, offset, extraWords);
            instanceFlags = unchecked((uint)instanceValues[0]);
            offset += extraWords * sizeof(int);
        }
        else
        {
            instanceFlags = unchecked((uint)ReadInt32(data, ref offset, "instance flags"));
            instanceValues = ReadWords(data, offset, extraWords);
            offset += extraWords * sizeof(int);
        }
        var inventory = new List<Fo2NativeMapObject>(inventoryLength);
        for (var index = 0; index < inventoryLength; ++index)
        {
            _ = ReadInt32(data, ref offset, "inventory quantity");
            inventory.Add(ReadObject(
                data, ref offset, mapVersion, containingElevation, depth + 1,
                resolver, ref serial, ref nested));
            nested++;
        }
        serial++;
        return new Fo2NativeMapObject(
            serial, sourceOffset, values[ObjectIdWord], tile, values[PixelXWord], values[PixelYWord],
            values[FrameWord], rotation, fid, unchecked((uint)values[FlagsWord]), elevation, pid,
            unchecked((uint)values[ScriptIdWord]),
            inventoryLength, instanceFlags, instanceValues, depth, prototype, inventory);
    }

    private static int InstanceExtraWords(int mapVersion, Fo2NativePrototypeDetail prototype)
    {
        if (prototype.Pid == SourceItemSentinelPid) return 1;
        if (prototype.ObjectType == CritterType) return CritterInstanceWords;
        if (prototype.ObjectType == ItemType)
            return prototype.Subtype switch
            {
                ItemWeaponSubtype => 2,
                ItemAmmoSubtype or ItemMiscSubtype or ItemKeySubtype => 1,
                ItemArmorSubtype or ItemContainerSubtype or ItemDrugSubtype => 0,
                _ => throw new NotSupportedException(
                    $"Fallout 2 item PRO subtype {prototype.Subtype} is unsupported."),
            };
        if (prototype.ObjectType == SceneryType)
            return prototype.Subtype switch
            {
                SceneryDoorSubtype => 1,
                SceneryStairsSubtype or SceneryElevatorSubtype => 2,
                SceneryLadderBottomSubtype or SceneryLadderTopSubtype =>
                    mapVersion == LegacyMapVersion ? 1 : 2,
                SceneryGenericSubtype => 0,
                _ => throw new NotSupportedException(
                    $"Fallout 2 scenery PRO subtype {prototype.Subtype} is unsupported."),
            };
        return IsExitGrid(prototype) ? 4 : 0;
    }

    private static bool IsExitGrid(Fo2NativePrototypeDetail prototype) =>
        prototype.ObjectType == MiscType && prototype.MessageNumber is int message &&
        (message >= ExitGridFirstLower && message < ExitGridFirstUpper && message % ExitGridStride == 0 ||
         message >= ExitGridSecondLower && message < ExitGridSecondUpper && message % ExitGridStride == 0);

    private sealed class PrototypeResolver
    {
        private readonly Fo2NativeOwnedSource _source;
        private readonly Dictionary<uint, Fo2NativePrototypeDetail> _cache = [];
        private readonly Dictionary<string, IReadOnlyList<string>> _lists =
            new(StringComparer.OrdinalIgnoreCase);

        internal PrototypeResolver(Fo2NativeOwnedSource source) => _source = source;

        internal Fo2NativePrototypeDetail Resolve(uint pid)
        {
            if (_cache.TryGetValue(pid, out var cached)) return cached;
            var type = checked((int)(pid >> ObjectTypeShift));
            var index = checked((int)(pid & ObjectIndexMask));
            if (type < 0 || type >= TypeDirectories.Length)
                throw new NotSupportedException($"Fallout 2 PID {pid:x8} has an unsupported type.");
            if (pid is BuiltinPlayerPid or SourceItemSentinelPid or SourceCritterSentinelPid)
                return Add(new Fo2NativePrototypeDetail(pid, type, null, null, null, null));
            if (index <= 0)
                throw new InvalidDataException($"Fallout 2 PID {pid:x8} has no one-based PRO index.");
            var directory = TypeDirectories[type];
            var names = List($"proto\\{directory}\\{directory}.lst");
            if (index > names.Count)
                throw new InvalidDataException($"Fallout 2 PID {pid:x8} exceeds its prototype list.");
            var logicalPath = $"proto\\{directory}\\{names[index - 1]}";
            var bytes = _source.Read(logicalPath, out _);
            if (bytes.Length < PrototypeHeaderBytes || BinaryPrimitives.ReadUInt32BigEndian(bytes) != pid)
                throw new InvalidDataException($"Fallout 2 PRO identity differs: {logicalPath}.");
            var message = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(sizeof(uint)));
            var fid = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizeof(uint) * 2));
            int? subtype = null;
            if (type is ItemType or SceneryType)
            {
                if (bytes.Length < PrototypeTypedBytes)
                    throw new InvalidDataException($"Fallout 2 typed PRO is truncated: {logicalPath}.");
                subtype = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(PrototypeSubtypeOffset));
            }
            return Add(new Fo2NativePrototypeDetail(pid, type, message, fid, subtype, logicalPath));
        }

        private Fo2NativePrototypeDetail Add(Fo2NativePrototypeDetail value)
        {
            _cache.Add(value.Pid, value);
            return value;
        }

        private IReadOnlyList<string> List(string logicalPath)
        {
            if (_lists.TryGetValue(logicalPath, out var cached)) return cached;
            var value = ReadList(_source.Read(logicalPath, out _));
            _lists.Add(logicalPath, value);
            return value;
        }
    }

    private static IReadOnlyList<string> ReadList(byte[] bytes)
    {
        if (bytes.Any(value => value > 0x7f))
            throw new NotSupportedException("Fallout 2 list filenames outside ASCII are unsupported.");
        return Encoding.ASCII.GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => line.Split(' ', 2)[0].Trim()).Where(line => line.Length > 0).ToArray();
    }

    private static int[] ReadWords(byte[] data, int offset, int count)
    {
        Require(data, offset, checked(count * sizeof(int)), "object words");
        return Enumerable.Range(0, count)
            .Select(index => BinaryPrimitives.ReadInt32BigEndian(
                data.AsSpan(offset + index * sizeof(int))))
            .ToArray();
    }

    private static int ReadInt32(byte[] data, ref int offset, string label)
    {
        Require(data, offset, sizeof(int), label);
        var value = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
        offset += sizeof(int);
        return value;
    }

    private static void Require(byte[] data, int offset, int bytes, string label)
    {
        if (offset < 0 || bytes < 0 || offset > data.Length - bytes)
            throw new InvalidDataException($"Fallout 2 Map 3 is truncated at {label}.");
    }
}
