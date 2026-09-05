using System.Buffers.Binary;
using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNavigationTriangle(ushort[] Vertices, short[] Edges, ushort Flags, ushort CoverFlags);
internal sealed record FalloutNavigationEdge(uint Type, FalloutFormKey Mesh, ushort Triangle);
internal sealed record FalloutNavigationDoor(FalloutFormKey Door, ushort Triangle);
internal sealed record FalloutNavigationMesh(FalloutFormKey Form, FalloutFormKey Cell, uint Version,
    Vector3[] Vertices, FalloutNavigationTriangle[] Triangles, FalloutNavigationEdge[] Edges,
    ushort[] CoverTriangles, FalloutNavigationDoor[] Doors)
{
    internal static IReadOnlyList<FalloutNavigationMesh> ReadCell(FalloutPluginStack stack, FalloutFormKey cell)
    {
        var meshes = new List<FalloutNavigationMesh>();
        foreach (var record in stack.EffectiveRecords("NAVM"))
        {
            if ((record.Flags & 0x800) != 0) continue;
            var header = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "DATA").Data;
            if (header.Length < 4) throw new InvalidDataException($"NAVM {record.FormKey} has no CELL identity.");
            if (record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(header.Span)) == cell) meshes.Add(Read(record));
        }
        return meshes;
    }

    internal static FalloutNavigationMesh Read(FalloutPluginRecord record)
    {
        if (record.Signature != "NAVM") throw new InvalidDataException("Navigation source is not NAVM.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Field(string signature, int extent, bool optional = false)
        {
            var matches = fields.Where(field => field.Signature == signature).ToArray();
            if (optional && extent == 0 && matches.Length == 0) return ReadOnlyMemory<byte>.Empty;
            if (matches.Length != 1 || matches[0].Data.Length != extent)
                throw new InvalidDataException($"NAVM {record.FormKey} has an invalid {signature} extent.");
            return matches[0].Data;
        }
        var version = BinaryPrimitives.ReadUInt32LittleEndian(Field("NVER", 4).Span);
        if (version != 11) throw new NotSupportedException($"NAVM {record.FormKey} version {version} has no established layout.");
        var header = Field("DATA", 24);
        int Count(int index)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(header.Span[(index * 4)..]);
            if (value > int.MaxValue) throw new InvalidDataException("NAVM count exceeds its source buffer.");
            return (int)value;
        }
        int Extent(int count, int stride)
        {
            if (count > int.MaxValue / stride) throw new InvalidDataException("NAVM extent overflows its source buffer.");
            return count * stride;
        }
        var vertexBytes = Field("NVVX", Extent(Count(1), 12));
        var triangleBytes = Field("NVTR", Extent(Count(2), 16));
        var edgeBytes = Field("NVEX", Extent(Count(3), 10), true);
        var coverBytes = Field("NVCA", Extent(Count(4), 2), true);
        var doorBytes = Field("NVDP", Extent(Count(5), 8), true);
        var vertices = new Vector3[Count(1)];
        for (var index = 0; index < vertices.Length; index++)
        {
            var bytes = vertexBytes.Span[(index * 12)..];
            vertices[index] = new(BinaryPrimitives.ReadSingleLittleEndian(bytes),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
            if (!float.IsFinite(vertices[index].X) || !float.IsFinite(vertices[index].Y) || !float.IsFinite(vertices[index].Z))
                throw new InvalidDataException("NAVM vertex is not finite.");
        }
        var triangles = new FalloutNavigationTriangle[Count(2)];
        for (var index = 0; index < triangles.Length; index++)
        {
            var bytes = triangleBytes.Span[(index * 16)..];
            var corners = new[] { BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) };
            var neighbors = new[] { BinaryPrimitives.ReadInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadInt16LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]) };
            if (corners.Distinct().Count() != 3 || corners.Any(value => value >= vertices.Length) || neighbors.Any(value => value < -1))
                throw new InvalidDataException("NAVM triangle has invalid vertices or edge indices.");
            triangles[index] = new(corners, neighbors, BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]));
        }
        var edges = new FalloutNavigationEdge[Count(3)];
        for (var index = 0; index < edges.Length; index++)
        {
            var bytes = edgeBytes.Span[(index * 10)..];
            edges[index] = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));
        }
        var covers = new ushort[Count(4)];
        for (var index = 0; index < covers.Length; index++)
        {
            covers[index] = BinaryPrimitives.ReadUInt16LittleEndian(coverBytes.Span[(index * 2)..]);
            if (covers[index] >= triangles.Length) throw new InvalidDataException("NAVM cover index exceeds its triangle array.");
        }
        var doors = new FalloutNavigationDoor[Count(5)];
        for (var index = 0; index < doors.Length; index++)
        {
            var bytes = doorBytes.Span[(index * 8)..];
            var triangle = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
            if (triangle >= triangles.Length) throw new InvalidDataException("NAVM door index exceeds its triangle array.");
            doors[index] = new(record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes)), triangle);
        }
        if (vertices.Length == 0 || triangles.Length == 0) throw new InvalidDataException("Active NAVM has no pathable geometry.");
        return new(record.FormKey, record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(header.Span)),
            version, vertices, triangles, edges, covers, doors);
    }
}
