using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

/// <summary>Winning world-map bounds and source marker positions, independent of residency.</summary>
internal sealed record FalloutWorldMap(FalloutFormKey World, string Texture, int Width, int Height,
    short West, short North, short East, short South)
{
    private static readonly ConditionalWeakTable<FalloutPluginStack, Dictionary<FalloutFormKey, FalloutWorldMap>> Maps = new();
    private static readonly ConditionalWeakTable<FalloutPluginStack, FalloutWorldMapMarker[]> Markers = new();

    internal Vector2 Project(float x, float y) => new(
        (x / 4096 - West) / (East - West), (North - y / 4096) / (North - South));

    internal static FalloutWorldMap Read(FalloutPluginStack records, FalloutFormKey world)
    {
        var cache = Maps.GetValue(records, _ => []);
        if (cache.TryGetValue(world, out var found)) return found;
        var seen = new HashSet<FalloutFormKey>();
        var current = world;
        while (seen.Add(current))
        {
            var record = records.GetEffective(current);
            if (record.Signature != "WRLD") throw new InvalidDataException("Map owner is not WRLD.");
            var fields = record.ReadSubrecords().ToArray();
            var parent = fields.SingleOrDefault(field => field.Signature == "WNAM").Data;
            var flags = fields.SingleOrDefault(field => field.Signature == "PNAM").Data;
            if (!flags.IsEmpty && flags.Length != 2) throw new InvalidDataException("World map parent flags require two bytes.");
            if (!parent.IsEmpty && !flags.IsEmpty && (BinaryPrimitives.ReadUInt16LittleEndian(flags.Span) & 4) != 0)
            {
                if (parent.Length != 4) throw new InvalidDataException("World map parent requires one FormID.");
                current = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(parent.Span));
                continue;
            }
            var data = fields.Single(field => field.Signature == "MNAM").Data.Span;
            if (data.Length != 16) throw new InvalidDataException("World map MNAM requires sixteen bytes.");
            var map = new FalloutWorldMap(current,
                "textures/" + FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "ICON").Data.Span).Replace('\\', '/'),
                BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data[4..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[8..]), BinaryPrimitives.ReadInt16LittleEndian(data[10..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[12..]), BinaryPrimitives.ReadInt16LittleEndian(data[14..]));
            if (map.Width <= 0 || map.Height <= 0 || map.East <= map.West || map.North <= map.South)
                throw new InvalidDataException("World map dimensions or bounds are invalid.");
            cache.Add(world, map);
            return map;
        }
        throw new InvalidDataException("World map inheritance cycle.");
    }

    internal static Vector2 Position(FalloutPluginStack records, FalloutFormKey world, float x, float y)
    {
        var map = Read(records, world);
        // A child world has its own coordinate frame. ONAM supplies the map
        // scale and offsets; it does not alter gameplay coordinates.
        if (world != map.World)
        {
            var data = records.GetEffective(world).ReadSubrecords().Single(field => field.Signature == "ONAM").Data.Span;
            if (data.Length != 12) throw new InvalidDataException("World map offset requires twelve bytes.");
            var scale = BinaryPrimitives.ReadSingleLittleEndian(data);
            var dx = BinaryPrimitives.ReadSingleLittleEndian(data[4..]);
            var dy = BinaryPrimitives.ReadSingleLittleEndian(data[8..]);
            if (!float.IsFinite(scale) || scale <= 0 || !float.IsFinite(dx) || !float.IsFinite(dy))
                throw new InvalidDataException("Invalid world map scale/offset.");
            x = x * scale + dx * 4096; y = y * scale + dy * 4096;
        }
        return map.Project(x, y);
    }

    internal static IReadOnlyList<FalloutWorldMapMarker> SourceMarkers(FalloutPluginStack records) => Markers.GetValue(records, stack =>
        stack.EffectiveRecords("REFR").Where(record => record.ReadSubrecords().Any(field => field.Signature == "XMRK"))
            .Select(record =>
            {
                var data = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
                if (data.Length != 24) throw new InvalidDataException("Map marker requires a placed transform.");
                var x = BinaryPrimitives.ReadSingleLittleEndian(data); var y = BinaryPrimitives.ReadSingleLittleEndian(data[4..]);
                if (!float.IsFinite(x) || !float.IsFinite(y)) throw new InvalidDataException("Non-finite map marker position.");
                return new FalloutWorldMapMarker(FalloutMapMarker.Read(record), FalloutCellSceneReader.ParentWorldspace(record), x, y);
            }).ToArray());
}

internal sealed record FalloutWorldMapMarker(FalloutMapMarker Marker, FalloutFormKey? World, float X, float Y);
