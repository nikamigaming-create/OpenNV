using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record Fallout1NativePrototypeDetail(
    int Pid,
    int ObjectType,
    int? MessageNumber,
    uint? Fid,
    int? Subtype,
    string? LogicalPath);

internal sealed record Fallout1NativeMapObject(
    int Serial,
    int SourceOffset,
    string BaseLayout,
    int ObjectId,
    int Tile,
    int PixelX,
    int PixelY,
    int Frame,
    int Rotation,
    uint Fid,
    uint Flags,
    int Elevation,
    int Pid,
    uint ScriptId,
    int InventoryLength,
    uint InstanceFlags,
    IReadOnlyList<int> InstanceValues,
    int Depth,
    Fallout1NativePrototypeDetail Prototype,
    IReadOnlyList<Fallout1NativeMapObject> Inventory);

internal sealed record Fallout1NativeObjectGraph(
    int TotalTopLevelObjects,
    IReadOnlyList<Fallout1NativeMapObject> TopLevelObjects,
    int NestedObjects,
    int EndOffset);

internal static class Fallout1NativeObjectGraphReader
{
    private const int CompactObjectWords = 17;
    private const int FullObjectWords = 21;
    private const int MaximumDepth = 16;
    private const int MaximumObjects = 100000;
    private const int MaximumInventoryEntries = 10000;
    private const int MapTileCount = 40000;
    private const int MaximumRotation = 5;
    private const int MaximumElevation = 2;
    private const int Fallout1MapVersion = 19;
    private const int ObjectTypeShift = 24;
    private const uint ObjectIndexMask = 0x00ffffffU;
    private const int CritterObjectType = 1;
    private const int ItemObjectType = 0;
    private const int SceneryObjectType = 2;
    private const int MiscObjectType = 5;
    private const int PrototypeHeaderBytes = 12;
    private const int PrototypeSubtypeOffset = 0x20;
    private const int PrototypeTypedBytes = 0x24;
    private const int BuiltinPlayerPid = 0x01000000;
    private const int FullObjectIdIndex = 0;
    private const int FullTileIndex = 1;
    private const int FullPixelXIndex = 2;
    private const int FullPixelYIndex = 3;
    private const int FullFrameIndex = 6;
    private const int FullRotationIndex = 7;
    private const int FullFidIndex = 8;
    private const int FullFlagsIndex = 9;
    private const int FullElevationIndex = 10;
    private const int FullPidIndex = 11;
    private const int FullScriptIdIndex = 16;
    private const int FullInventoryLengthIndex = 18;
    private const int CompactObjectIdIndex = 0;
    private const int CompactTileIndex = 1;
    private const int CompactPixelXIndex = 2;
    private const int CompactPixelYIndex = 3;
    private const int CompactFidIndex = 4;
    private const int CompactFlagsIndex = 5;
    private const int CompactElevationIndex = 6;
    private const int CompactPidIndex = 7;
    private const int CompactScriptIdIndex = 12;
    private const int CompactInventoryLengthIndex = 14;
    private const int CritterInstanceWords = 11;
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
    private const int ExitGridFirstMessageLowerBound = 1600;
    private const int ExitGridFirstMessageUpperBound = 2400;
    private const int ExitGridSecondMessageLowerBound = 3100;
    private const int ExitGridSecondMessageUpperBound = 4700;
    private const int ExitGridMessageStride = 100;

    internal static Fallout1NativeObjectGraph Read(
        byte[] data,
        Fallout1NativeMap map,
        Fallout1OwnedContentSource source)
    {
        var offset = map.ObjectSectionOffset;
        var total = ReadInt32(data, ref offset, "total object count");
        if (total != map.TotalObjects || total is < 0 or > MaximumObjects)
            throw new InvalidDataException("Fallout 1 MAP object count differs from its validated header walk.");
        var topLevel = new List<Fallout1NativeMapObject>(total);
        var serial = 0;
        var nested = 0;
        for (var elevation = 0; elevation <= MaximumElevation; ++elevation)
        {
            var count = ReadInt32(data, ref offset, $"elevation {elevation} object count");
            if (count is < 0 or > MaximumObjects || topLevel.Count + count > total)
                throw new InvalidDataException($"Fallout 1 MAP elevation {elevation} object count is invalid.");
            for (var index = 0; index < count; ++index)
            {
                var placed = ReadObject(
                    data, ref offset, source, map.Version, elevation, 0, ref serial, ref nested);
                if (placed.Elevation != elevation)
                    throw new InvalidDataException(
                        $"Fallout 1 top-level object elevation differs at 0x{placed.SourceOffset:x}.");
                topLevel.Add(placed);
            }
        }
        if (topLevel.Count != total || offset != data.Length)
            throw new InvalidDataException(
                $"Fallout 1 MAP object graph ended at 0x{offset:x}; expected 0x{data.Length:x}.");
        return new Fallout1NativeObjectGraph(total, topLevel, nested, offset);
    }

    private static Fallout1NativeMapObject ReadObject(
        byte[] data,
        ref int offset,
        Fallout1OwnedContentSource source,
        int mapVersion,
        int containingElevation,
        int depth,
        ref int serial,
        ref int nestedCount)
    {
        if (depth > MaximumDepth)
            throw new InvalidDataException("Fallout 1 MAP inventory nesting exceeds its admitted depth.");
        var sourceOffset = offset;
        Require(data, offset, CompactObjectWords * sizeof(int), "object base");
        int[]? full = null;
        if (data.Length - offset >= FullObjectWords * sizeof(int))
            full = ReadWords(data, offset, FullObjectWords);
        int objectId;
        int tile;
        int pixelX;
        int pixelY;
        int frame;
        int rotation;
        int fidSigned;
        int flagsSigned;
        int elevation;
        int pid;
        int scriptSigned;
        int inventoryLength;
        string layout;
        if (full is not null && Structural(
                full[FullTileIndex], full[FullRotationIndex], full[FullFidIndex],
                full[FullElevationIndex], full[FullPidIndex], full[FullInventoryLengthIndex]))
        {
            objectId = full[FullObjectIdIndex];
            tile = full[FullTileIndex];
            pixelX = full[FullPixelXIndex];
            pixelY = full[FullPixelYIndex];
            frame = full[FullFrameIndex];
            rotation = full[FullRotationIndex];
            fidSigned = full[FullFidIndex];
            flagsSigned = full[FullFlagsIndex];
            elevation = full[FullElevationIndex];
            pid = full[FullPidIndex];
            scriptSigned = full[FullScriptIdIndex];
            inventoryLength = full[FullInventoryLengthIndex];
            layout = "full-21";
            offset += FullObjectWords * sizeof(int);
        }
        else
        {
            var compact = ReadWords(data, offset, CompactObjectWords);
            if (!Structural(
                    compact[CompactTileIndex], 0, compact[CompactFidIndex],
                    compact[CompactElevationIndex], compact[CompactPidIndex],
                    compact[CompactInventoryLengthIndex]))
                throw new NotSupportedException(
                    $"Fallout 1 MAP object at 0x{sourceOffset:x} matches no admitted base layout.");
            objectId = compact[CompactObjectIdIndex];
            tile = compact[CompactTileIndex];
            pixelX = compact[CompactPixelXIndex];
            pixelY = compact[CompactPixelYIndex];
            frame = 0;
            rotation = 0;
            fidSigned = compact[CompactFidIndex];
            flagsSigned = compact[CompactFlagsIndex];
            elevation = compact[CompactElevationIndex];
            pid = compact[CompactPidIndex];
            scriptSigned = compact[CompactScriptIdIndex];
            inventoryLength = compact[CompactInventoryLengthIndex];
            layout = "compact-17";
            offset += CompactObjectWords * sizeof(int);
        }
        if (depth == 0 && elevation != containingElevation)
            throw new InvalidDataException($"Fallout 1 MAP object at 0x{sourceOffset:x} has wrong elevation.");
        var prototype = ResolvePrototype(source, pid);
        var extraWords = InstanceExtraWords(mapVersion, prototype);
        uint instanceFlags;
        int[] instanceValues;
        if (prototype.ObjectType == CritterObjectType)
        {
            Require(data, offset, extraWords * sizeof(int), "critter instance");
            instanceValues = ReadWords(data, offset, extraWords);
            instanceFlags = unchecked((uint)instanceValues[0]);
            offset += extraWords * sizeof(int);
        }
        else
        {
            instanceFlags = unchecked((uint)ReadInt32(data, ref offset, "instance flags"));
            Require(data, offset, extraWords * sizeof(int), "typed instance");
            instanceValues = ReadWords(data, offset, extraWords);
            offset += extraWords * sizeof(int);
        }

        var inventory = new List<Fallout1NativeMapObject>(inventoryLength);
        for (var index = 0; index < inventoryLength; ++index)
        {
            _ = ReadInt32(data, ref offset, "inventory quantity");
            inventory.Add(ReadObject(
                data, ref offset, source, mapVersion, containingElevation, depth + 1,
                ref serial, ref nestedCount));
            nestedCount++;
        }
        serial++;
        return new Fallout1NativeMapObject(
            serial, sourceOffset, layout, objectId, tile, pixelX, pixelY, frame, rotation,
            unchecked((uint)fidSigned), unchecked((uint)flagsSigned), elevation, pid,
            unchecked((uint)scriptSigned), inventoryLength, instanceFlags, instanceValues,
            depth, prototype, inventory);
    }

    private static Fallout1NativePrototypeDetail ResolvePrototype(
        Fallout1OwnedContentSource source,
        int pid)
    {
        var unsigned = unchecked((uint)pid);
        var objectType = (int)(unsigned >> ObjectTypeShift);
        var listIndex = (int)(unsigned & ObjectIndexMask);
        if (pid == BuiltinPlayerPid)
            return new Fallout1NativePrototypeDetail(pid, CritterObjectType, null, null, null, null);
        var directories = new[] { "items", "critters", "scenery", "walls", "tiles", "misc" };
        if (objectType < 0 || objectType >= directories.Length || listIndex <= 0)
            throw new NotSupportedException($"Fallout 1 PID 0x{unsigned:x8} has an unsupported object type.");
        var directory = directories[objectType];
        var names = Fallout1NativeLists.Read(source.Read($"proto\\{directory}\\{directory}.lst").Bytes);
        if (listIndex > names.Count)
            throw new InvalidDataException($"Fallout 1 PID 0x{unsigned:x8} exceeds its prototype list.");
        var logicalPath = $"proto\\{directory}\\{names[listIndex - 1]}";
        var bytes = source.Read(logicalPath).Bytes;
        if (bytes.Length < PrototypeHeaderBytes || BinaryPrimitives.ReadUInt32BigEndian(bytes) != unsigned)
            throw new InvalidDataException($"Fallout 1 PRO identity differs: {logicalPath}");
        var message = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(sizeof(uint)));
        var fid = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizeof(uint) * 2));
        int? subtype = null;
        if (objectType is ItemObjectType or SceneryObjectType)
        {
            if (bytes.Length < PrototypeTypedBytes)
                throw new InvalidDataException($"Fallout 1 typed PRO is truncated: {logicalPath}");
            subtype = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(PrototypeSubtypeOffset));
        }
        return new Fallout1NativePrototypeDetail(pid, objectType, message, fid, subtype, logicalPath);
    }

    private static int InstanceExtraWords(int mapVersion, Fallout1NativePrototypeDetail prototype)
    {
        if (prototype.ObjectType == CritterObjectType) return CritterInstanceWords;
        if (prototype.ObjectType == ItemObjectType)
            return prototype.Subtype switch
            {
                0 or 1 or 2 => 0,
                ItemWeaponSubtype => 2,
                ItemAmmoSubtype or ItemMiscSubtype or ItemKeySubtype => 1,
                _ => throw new NotSupportedException(
                    $"Fallout 1 item PRO subtype {prototype.Subtype} is unsupported."),
            };
        if (prototype.ObjectType == SceneryObjectType)
            return prototype.Subtype switch
            {
                SceneryDoorSubtype => 1,
                SceneryStairsSubtype or SceneryElevatorSubtype => 2,
                SceneryLadderBottomSubtype or SceneryLadderTopSubtype =>
                    mapVersion == Fallout1MapVersion ? 1 : 2,
                SceneryGenericSubtype => 0,
                _ => throw new NotSupportedException(
                    $"Fallout 1 scenery PRO subtype {prototype.Subtype} is unsupported."),
            };
        if (IsExitGrid(prototype)) return 4;
        return 0;
    }

    internal static bool IsExitGrid(Fallout1NativePrototypeDetail prototype) =>
        prototype.ObjectType == MiscObjectType && IsExitGridMessage(prototype.MessageNumber);

    private static bool IsExitGridMessage(int? message) => message is { } value &&
        (value >= ExitGridFirstMessageLowerBound && value < ExitGridFirstMessageUpperBound &&
         value % ExitGridMessageStride == 0 ||
         value >= ExitGridSecondMessageLowerBound && value < ExitGridSecondMessageUpperBound &&
         value % ExitGridMessageStride == 0);

    private static bool Structural(int tile, int rotation, int fid, int elevation, int pid, int inventory) =>
        (tile == -1 || tile is >= 0 and < MapTileCount) && rotation is >= 0 and <= MaximumRotation &&
        elevation is >= 0 and <= MaximumElevation && inventory is >= 0 and <= MaximumInventoryEntries &&
        ((unchecked((uint)fid) >> ObjectTypeShift) & 0x0f) <= MiscObjectType &&
        (unchecked((uint)pid) >> ObjectTypeShift) <= MiscObjectType &&
        (unchecked((uint)pid) == BuiltinPlayerPid || (unchecked((uint)pid) & ObjectIndexMask) > 0);

    private static int[] ReadWords(byte[] data, int offset, int count)
    {
        Require(data, offset, count * sizeof(int), "object words");
        return Enumerable.Range(0, count)
            .Select(index => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + index * sizeof(int))))
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
            throw new InvalidDataException($"Fallout 1 MAP is truncated at {label}.");
    }
}
