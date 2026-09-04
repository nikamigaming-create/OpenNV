using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Campaigns.Classic.Native;

namespace OpenNV.Runtime.Campaigns.Fallout2.Native;

internal sealed record Fo2NativePopulationMapRow(
    string LogicalPath,
    int MapIndex,
    string Name,
    int PresentElevations,
    int ScriptRecords,
    int LiveScripts,
    int TopLevelObjects,
    int InventoryObjects,
    int FullLayoutObjects,
    int CompactLayoutObjects,
    IReadOnlyDictionary<int, int> TopLevelObjectsByElevation,
    IReadOnlyDictionary<int, int> ObjectsByType,
    int UniquePids,
    int ValidatedPros,
    string? Unsupported);

internal sealed record Fo2NativePopulationLedgerCoverage(
    string SourceProfileId,
    IReadOnlyList<Fo2NativePopulationMapRow> Maps,
    int PresentElevations,
    int ScriptRecords,
    int LiveScripts,
    int TopLevelObjects,
    int InventoryObjects,
    int FullLayoutObjects,
    int CompactLayoutObjects,
    IReadOnlyDictionary<int, int> ObjectsByType,
    int UniquePids,
    int ValidatedPros,
    int NonProPids,
    int UnsupportedMaps);

internal static class Fo2NativePopulationLedger
{
    private const int ScriptListCount = 5;
    private const int ScriptSlotsPerExtent = 16;
    private const int ScriptExtentRounding = ScriptSlotsPerExtent - 1;
    private const int ScriptOrdinaryRecordBytes = 64;
    private const int ScriptSpatialRecordBytes = 72;
    private const int ScriptTimedRecordBytes = 68;
    private const int ScriptSpatialType = 1;
    private const int ScriptTimedType = 2;
    private const int FullObjectWords = 21;
    private const int CompactObjectWords = 17;
    private const int FullObjectBytes = FullObjectWords * sizeof(int);
    private const int CompactObjectBytes = CompactObjectWords * sizeof(int);
    private const int ObjectTypeShift = 24;
    private const uint ObjectIndexMask = 0x00ffffffU;
    private const uint BuiltinDudePid = 0x01000000U;
    private const uint SourceItemSentinelPid = 0x000000ffU;
    private const uint SourceCritterSentinelPid = 0x010001ffU;
    private const int MaximumObjectCount = 100000;
    private const int MaximumInventoryDepth = 16;
    private const int MaximumInventoryCount = 10000;
    private const int MaximumMapTile = 40000;
    private const int MaximumRotation = 5;
    private const int MaximumObjectType = 5;
    private const int ItemWeaponSubtype = 3;
    private const int ItemAmmoSubtype = 4;
    private const int ItemMiscSubtype = 5;
    private const int ItemKeySubtype = 6;
    private const int MaximumItemSubtype = ItemKeySubtype;
    private const int MaximumScenerySubtype = 5;
    private const int MiscObjectType = 5;
    private const int FullTileWord = 1;
    private const int FullRotationWord = 7;
    private const int FullFidWord = 8;
    private const int FullElevationWord = 10;
    private const int FullPidWord = 11;
    private const int FullInventoryLengthWord = 18;
    private const int CompactTileWord = 1;
    private const int CompactFidWord = 4;
    private const int CompactElevationWord = 6;
    private const int CompactPidWord = 7;
    private const int CompactInventoryLengthWord = 14;
    private const int MinimumProHeaderBytes = 12;
    private const int TypedProSubtypeOffset = 32;
    private const int TypedProMinimumBytes = 36;
    private const int CritterInstanceWords = 11;
    private const int Fallout1MapVersion = 19;
    private const int ExitGridFirstRangeStart = 1600;
    private const int ExitGridFirstRangeEnd = 2400;
    private const int ExitGridSecondRangeStart = 3100;
    private const int ExitGridSecondRangeEnd = 4700;
    private const int ExitGridMessageStep = 100;

    private static readonly string[] TypeDirectories =
        ["items", "critters", "scenery", "walls", "tiles", "misc"];

    internal static Fo2NativePopulationLedgerCoverage Build(IFalloutClassicOwnedSource source)
    {
        var rows = new List<Fo2NativePopulationMapRow>();
        var allPids = new HashSet<uint>();
        var allPros = new HashSet<uint>();
        var allNonProPids = new HashSet<uint>();
        var aggregateTypes = new Dictionary<int, int>();
        foreach (var logicalPath in source.EffectiveLogicalPaths("maps", ".map"))
        {
            var data = source.Read(logicalPath, out _);
            Fo2NativeMap map;
            try
            {
                map = Fo2NativeMapReader.Read(data);
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            {
                rows.Add(new Fo2NativePopulationMapRow(
                    logicalPath, -1, Path.GetFileName(logicalPath), 0, 0, 0, 0, 0, 0, 0,
                    new Dictionary<int, int>(), new Dictionary<int, int>(), 0, 0,
                    $"header/layout: {error.Message}"));
                continue;
            }
            try
            {
                var resolver = new PrototypeResolver(source);
                var parser = new ObjectParser(data, map, resolver);
                var result = parser.Parse();
                foreach (var pid in result.Pids) allPids.Add(pid);
                foreach (var pid in resolver.ValidatedPros) allPros.Add(pid);
                foreach (var pid in resolver.NonProPids) allNonProPids.Add(pid);
                foreach (var (type, count) in result.ObjectsByType)
                    aggregateTypes[type] = aggregateTypes.GetValueOrDefault(type) + count;
                rows.Add(new Fo2NativePopulationMapRow(
                    logicalPath,
                    map.MapIndex,
                    map.Name,
                    map.Elevations.Count,
                    result.ScriptRecords,
                    result.LiveScripts,
                    result.TopLevelObjects,
                    result.InventoryObjects,
                    result.FullLayoutObjects,
                    result.CompactLayoutObjects,
                    result.TopLevelObjectsByElevation,
                    result.ObjectsByType,
                    result.Pids.Count,
                    resolver.ValidatedPros.Count,
                    null));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                rows.Add(new Fo2NativePopulationMapRow(
                    logicalPath, map.MapIndex, map.Name, map.Elevations.Count, 0, 0, 0, 0, 0, 0,
                    new Dictionary<int, int>(), new Dictionary<int, int>(), 0, 0, error.Message));
            }
        }
        return new Fo2NativePopulationLedgerCoverage(
            source.ProfileId,
            rows,
            rows.Sum(row => row.PresentElevations),
            rows.Sum(row => row.ScriptRecords),
            rows.Sum(row => row.LiveScripts),
            rows.Sum(row => row.TopLevelObjects),
            rows.Sum(row => row.InventoryObjects),
            rows.Sum(row => row.FullLayoutObjects),
            rows.Sum(row => row.CompactLayoutObjects),
            aggregateTypes,
            allPids.Count,
            allPros.Count,
            allNonProPids.Count,
            rows.Count(row => row.Unsupported is not null));
    }

    private sealed record Prototype(
        uint Pid,
        int ObjectType,
        int? MessageNumber,
        int? Subtype,
        bool RequiresPro);

    private sealed class PrototypeResolver
    {
        private readonly IFalloutClassicOwnedSource _source;
        private readonly Dictionary<uint, Prototype> _cache = [];
        private readonly Dictionary<string, string[]> _lists = new(StringComparer.OrdinalIgnoreCase);

        internal PrototypeResolver(IFalloutClassicOwnedSource source) => _source = source;
        internal IReadOnlyCollection<uint> ValidatedPros => _cache.Values
            .Where(row => row.RequiresPro).Select(row => row.Pid).ToArray();
        internal IReadOnlyCollection<uint> NonProPids => _cache.Values
            .Where(row => !row.RequiresPro).Select(row => row.Pid).ToArray();

        internal Prototype Resolve(uint pid)
        {
            if (_cache.TryGetValue(pid, out var cached)) return cached;
            var objectType = checked((int)(pid >> ObjectTypeShift));
            var listIndex = checked((int)(pid & ObjectIndexMask));
            if (objectType is < 0 or > MaximumObjectType)
                throw new NotSupportedException($"Unsupported Fallout 2 PID type {objectType}: {pid:x8}.");
            if (pid is BuiltinDudePid or SourceItemSentinelPid or SourceCritterSentinelPid)
                return Add(new Prototype(pid, objectType, null, null, false));
            if (listIndex <= 0)
                throw new InvalidDataException($"Fallout 2 PID has no one-based PRO index: {pid:x8}.");
            var directory = TypeDirectories[objectType];
            var listPath = $"proto\\{directory}\\{directory}.lst";
            var lines = ListLines(listPath);
            if (listIndex > lines.Length)
                throw new InvalidDataException($"PID {pid:x8} exceeds {listPath} ({lines.Length}).");
            var file = lines[listIndex - 1].Split(' ', 2)[0].Trim();
            if (string.IsNullOrWhiteSpace(file))
                throw new InvalidDataException($"PID {pid:x8} resolves to an empty PRO entry.");
            var data = _source.Read($"proto\\{directory}\\{file}", out _);
            if (data.Length < MinimumProHeaderBytes)
                throw new InvalidDataException($"PRO is truncated for PID {pid:x8}.");
            var storedPid = BinaryPrimitives.ReadUInt32BigEndian(data);
            if (storedPid != pid)
                throw new InvalidDataException($"PRO PID mismatch: expected {pid:x8}, got {storedPid:x8}.");
            var message = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(sizeof(uint)));
            int? subtype = null;
            if (objectType is 0 or 2)
            {
                if (data.Length < TypedProMinimumBytes)
                    throw new InvalidDataException($"Typed PRO is truncated for PID {pid:x8}.");
                subtype = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(TypedProSubtypeOffset));
                if (objectType == 0 && subtype is < 0 or > MaximumItemSubtype ||
                    objectType == 2 && subtype is < 0 or > MaximumScenerySubtype)
                    throw new NotSupportedException($"Unsupported PRO subtype {subtype} for PID {pid:x8}.");
            }
            return Add(new Prototype(pid, objectType, message, subtype, true));
        }

        private Prototype Add(Prototype value)
        {
            _cache.Add(value.Pid, value);
            return value;
        }

        private string[] ListLines(string logicalPath)
        {
            if (_lists.TryGetValue(logicalPath, out var cached)) return cached;
            var lines = Encoding.Latin1.GetString(_source.Read(logicalPath, out _))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n').Split('\n');
            if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
            _lists.Add(logicalPath, lines);
            return lines;
        }
    }

    private sealed record ParseResult(
        int ScriptRecords,
        int LiveScripts,
        int TopLevelObjects,
        int InventoryObjects,
        int FullLayoutObjects,
        int CompactLayoutObjects,
        IReadOnlyDictionary<int, int> TopLevelObjectsByElevation,
        IReadOnlyDictionary<int, int> ObjectsByType,
        IReadOnlySet<uint> Pids);

    private sealed class ObjectParser
    {
        private readonly byte[] _data;
        private readonly Fo2NativeMap _map;
        private readonly PrototypeResolver _resolver;
        private int _offset;
        private int _scriptRecords;
        private int _liveScripts;
        private int _inventoryObjects;
        private int _fullLayouts;
        private int _compactLayouts;
        private readonly Dictionary<int, int> _topLevelByElevation = [];
        private readonly Dictionary<int, int> _types = [];
        private readonly HashSet<uint> _pids = [];

        internal ObjectParser(byte[] data, Fo2NativeMap map, PrototypeResolver resolver)
        {
            _data = data;
            _map = map;
            _resolver = resolver;
            _offset = map.ContentOffsetAfterTiles;
        }

        internal ParseResult Parse()
        {
            ParseScripts();
            var total = ReadInt32("total object count");
            if (total is < 0 or > MaximumObjectCount)
                throw new InvalidDataException($"Invalid total MAP object count {total}.");
            var topLevel = 0;
            for (var elevation = 0; elevation < 3; ++elevation)
            {
                var count = ReadInt32($"elevation {elevation} object count");
                if (count < 0 || count > total)
                    throw new InvalidDataException($"Invalid elevation {elevation} object count {count}.");
                for (var index = 0; index < count; ++index)
                    ParseObject(elevation, 0);
                _topLevelByElevation.Add(elevation, count);
                topLevel += count;
            }
            if (topLevel != total)
                throw new InvalidDataException($"MAP top-level object count differs: {topLevel}/{total}.");
            if (_offset != _data.Length)
                throw new NotSupportedException($"MAP object graph leaves {_data.Length - _offset} trailing bytes.");
            return new ParseResult(
                _scriptRecords, _liveScripts, topLevel, _inventoryObjects, _fullLayouts, _compactLayouts,
                _topLevelByElevation, _types, _pids);
        }

        private void ParseScripts()
        {
            for (var list = 0; list < ScriptListCount; ++list)
            {
                var live = ReadInt32($"script list {list} count");
                if (live < 0) throw new InvalidDataException($"Negative script list count {live}.");
                _liveScripts += live;
                var extents = checked((live + ScriptExtentRounding) / ScriptSlotsPerExtent);
                var extentLive = 0;
                for (var extent = 0; extent < extents; ++extent)
                {
                    for (var slot = 0; slot < ScriptSlotsPerExtent; ++slot)
                    {
                        Require(sizeof(int), "script record identity");
                        var sid = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_offset));
                        var type = sid < 0 ? -1 : (int)((uint)sid >> ObjectTypeShift);
                        var bytes = type == ScriptSpatialType
                            ? ScriptSpatialRecordBytes
                            : type == ScriptTimedType ? ScriptTimedRecordBytes : ScriptOrdinaryRecordBytes;
                        Require(bytes, "script record");
                        _offset += bytes;
                        _scriptRecords++;
                    }
                    var length = ReadInt32("script extent length");
                    _ = ReadInt32("script extent next");
                    if (length is < 0 or > ScriptSlotsPerExtent)
                        throw new InvalidDataException($"Invalid MAP script extent length {length}.");
                    extentLive += length;
                }
                if (extentLive != live)
                    throw new InvalidDataException($"MAP script live count differs: {extentLive}/{live}.");
            }
        }

        private void ParseObject(int containingElevation, int depth)
        {
            if (depth > MaximumInventoryDepth)
                throw new NotSupportedException("MAP inventory nesting exceeds the supported format bound.");
            Require(CompactObjectBytes, "object base");
            var full = _offset <= _data.Length - FullObjectBytes
                ? ReadWords(_offset, FullObjectWords)
                : null;
            int[] values;
            int tile;
            int rotation;
            int storedElevation;
            int inventoryLength;
            uint pid;
            if (full is not null && IsStructuralFull(full))
            {
                values = full;
                tile = values[FullTileWord];
                rotation = values[FullRotationWord];
                storedElevation = values[FullElevationWord];
                pid = unchecked((uint)values[FullPidWord]);
                inventoryLength = values[FullInventoryLengthWord];
                _offset += FullObjectBytes;
                _fullLayouts++;
            }
            else
            {
                values = ReadWords(_offset, CompactObjectWords);
                tile = values[CompactTileWord];
                rotation = 0;
                storedElevation = values[CompactElevationWord];
                pid = unchecked((uint)values[CompactPidWord]);
                inventoryLength = values[CompactInventoryLengthWord];
                if (!IsStructuralCompact(values))
                    throw new NotSupportedException($"MAP object at 0x{_offset:x} has an unsupported base layout.");
                _offset += CompactObjectBytes;
                _compactLayouts++;
            }
            if (tile != -1 && tile is < 0 or >= MaximumMapTile ||
                rotation is < 0 or > MaximumRotation ||
                storedElevation is < 0 or > 2 ||
                inventoryLength is < 0 or > MaximumInventoryCount ||
                depth == 0 && storedElevation != containingElevation)
                throw new InvalidDataException($"MAP object fields are invalid at 0x{_offset:x}.");
            var prototype = _resolver.Resolve(pid);
            _pids.Add(pid);
            _types[prototype.ObjectType] = _types.GetValueOrDefault(prototype.ObjectType) + 1;
            var extraWords = InstanceExtraWords(_map.Version, prototype);
            var instanceWords = prototype.ObjectType == 1 ? extraWords : checked(1 + extraWords);
            Require(checked(instanceWords * sizeof(int)), "object subtype instance");
            _offset += instanceWords * sizeof(int);
            for (var inventory = 0; inventory < inventoryLength; ++inventory)
            {
                _ = ReadInt32("inventory quantity");
                _inventoryObjects++;
                ParseObject(containingElevation, depth + 1);
            }
        }

        private static int InstanceExtraWords(int version, Prototype prototype)
        {
            if (prototype.Pid == SourceItemSentinelPid) return 1;
            if (prototype.ObjectType == 1) return CritterInstanceWords;
            if (prototype.ObjectType == 0)
                return prototype.Subtype switch
                {
                    ItemWeaponSubtype => 2,
                    ItemAmmoSubtype or ItemMiscSubtype or ItemKeySubtype => 1,
                    _ => 0,
                };
            if (prototype.ObjectType == 2)
                return prototype.Subtype switch
                {
                    0 => 1,
                    1 or 2 => 2,
                    3 or 4 => version == Fallout1MapVersion ? 1 : 2,
                    _ => 0,
                };
            if (prototype.ObjectType == MiscObjectType && prototype.MessageNumber is int message &&
                (InExitRange(message, ExitGridFirstRangeStart, ExitGridFirstRangeEnd) ||
                 InExitRange(message, ExitGridSecondRangeStart, ExitGridSecondRangeEnd)))
                return 4;
            return 0;
        }

        private static bool InExitRange(int value, int start, int end) =>
            value >= start && value < end && (value - start) % ExitGridMessageStep == 0;

        private static bool IsStructuralFull(int[] values) =>
            (values[FullTileWord] == -1 || values[FullTileWord] is >= 0 and < MaximumMapTile) &&
            values[FullRotationWord] is >= 0 and <= MaximumRotation &&
            values[FullElevationWord] is >= 0 and <= 2 &&
            values[FullInventoryLengthWord] is >= 0 and <= MaximumInventoryCount &&
            IdentityIsStructural(values[FullFidWord], values[FullPidWord]);

        private static bool IsStructuralCompact(int[] values) =>
            (values[CompactTileWord] == -1 || values[CompactTileWord] is >= 0 and < MaximumMapTile) &&
            values[CompactElevationWord] is >= 0 and <= 2 &&
            values[CompactInventoryLengthWord] is >= 0 and <= MaximumInventoryCount &&
            IdentityIsStructural(values[CompactFidWord], values[CompactPidWord]);

        private static bool IdentityIsStructural(int fidSigned, int pidSigned)
        {
            var fidType = (int)((unchecked((uint)fidSigned) >> ObjectTypeShift) & 0x0fU);
            var pid = unchecked((uint)pidSigned);
            var pidType = checked((int)(pid >> ObjectTypeShift));
            var pidIndex = pid & ObjectIndexMask;
            return fidType is >= 0 and <= MaximumObjectType &&
                pidType is >= 0 and <= MaximumObjectType &&
                (pid == BuiltinDudePid || pidIndex > 0);
        }

        private int[] ReadWords(int offset, int count)
        {
            var values = new int[count];
            for (var index = 0; index < count; ++index)
                values[index] = BinaryPrimitives.ReadInt32BigEndian(
                    _data.AsSpan(offset + index * sizeof(int)));
            return values;
        }

        private int ReadInt32(string label)
        {
            Require(sizeof(int), label);
            var value = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_offset));
            _offset += sizeof(int);
            return value;
        }

        private void Require(int bytes, string label)
        {
            if (bytes < 0 || _offset > _data.Length - bytes)
                throw new InvalidDataException($"MAP is truncated at {label} (0x{_offset:x}).");
        }
    }
}
