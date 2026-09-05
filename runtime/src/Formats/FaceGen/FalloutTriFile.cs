using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenNV.Runtime.Formats.FaceGen;

internal sealed record FalloutTriDeltaMorph(string Name, float Scale, ReadOnlyMemory<byte> PackedDeltas);
internal sealed record FalloutTriStatMorph(string Name, int AddedVertexStart, int[] VertexIndices);

/// <summary>FaceGen FRTRI003. Source vertex order includes the statistical target suffix.</summary>
internal sealed class FalloutTriFile
{
    internal required ReadOnlyMemory<byte> SourceBytes { get; init; }
    internal required int VertexCount { get; init; }
    internal required Vector3[] Vertices { get; init; }
    internal required int[][] Faces { get; init; }
    internal required IReadOnlyList<FalloutTriDeltaMorph> DeltaMorphs { get; init; }
    internal required IReadOnlyList<FalloutTriStatMorph> StatMorphs { get; init; }

    internal static FalloutTriFile Read(ReadOnlyMemory<byte> bytes)
    {
        var reader = new Cursor(bytes);
        if (!reader.Take(8).Span.SequenceEqual("FRTRI003"u8))
            throw new InvalidDataException("TRI requires FRTRI003.");
        var vertexCount = reader.Count(12);
        if (vertexCount == 0) throw new InvalidDataException("TRI has no base vertices.");
        var triangles = reader.Count(12);
        var quads = reader.Count(16);
        var vertexLabels = reader.Count(8);
        var surfaceLabels = reader.Count(20);
        var uvCount = reader.Count(8);
        var flags = reader.Int();
        if ((flags & ~3) != 0) throw new NotSupportedException($"TRI extension flags {flags:x} are unbound.");
        var deltaCount = reader.Count(checked(8 + vertexCount * 6));
        var statCount = reader.Count(8);
        var addedCount = reader.Count(12);
        reader.Take(16); // Reserved bytes are retained in SourceBytes.
        var vertices = new Vector3[checked(vertexCount + addedCount)];
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = new(reader.Float(), reader.Float(), reader.Float());
        int[] Indices(int count, int maximum)
        {
            var values = new int[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = reader.Int();
                if ((uint)values[index] >= maximum) throw new InvalidDataException("TRI index exceeds its source table.");
            }
            return values;
        }
        var faces = new int[checked(triangles + quads)][];
        for (var index = 0; index < faces.Length; index++) faces[index] = Indices(index < triangles ? 3 : 4, vertexCount);
        for (var index = 0; index < vertexLabels; index++)
        {
            _ = Indices(1, vertexCount);
            reader.Label(false, false);
        }
        for (var index = 0; index < surfaceLabels; index++)
        {
            _ = Indices(1, faces.Length);
            reader.Float(); reader.Float(); reader.Float();
            reader.Label((flags & 2) != 0, false);
        }
        if ((flags & 1) != 0)
        {
            for (var index = 0; index < (uvCount == 0 ? vertexCount : uvCount); index++)
            {
                reader.Float(); reader.Float();
            }
            if (uvCount != 0)
                foreach (var face in faces) _ = Indices(face.Length, uvCount);
        }
        else if (uvCount != 0) throw new InvalidDataException("TRI declares texture indices without texture coordinates.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        string Name()
        {
            var name = reader.Label(false, true);
            if (name.Length == 0 || !names.Add(name)) throw new InvalidDataException("TRI morph names must be nonempty and unique.");
            return name;
        }
        var deltas = new FalloutTriDeltaMorph[deltaCount];
        for (var index = 0; index < deltas.Length; index++)
            deltas[index] = new(Name(), reader.Float(), reader.Take(checked(vertexCount * 6)));
        var stat = new FalloutTriStatMorph[statCount];
        var added = vertexCount;
        for (var index = 0; index < stat.Length; index++)
        {
            var name = Name();
            var affected = Indices(reader.Count(4), vertexCount);
            if (affected.Distinct().Count() != affected.Length || affected.Length > vertices.Length - added)
                throw new InvalidDataException("TRI statistical target indices overlap or exceed their vertex suffix.");
            stat[index] = new(name, added, affected);
            added += affected.Length;
        }
        if (added != vertices.Length || reader.Remaining != 0)
            throw new InvalidDataException("TRI morph tables do not cover the exact source extent.");
        return new() { SourceBytes = bytes, VertexCount = vertexCount, Vertices = vertices, Faces = faces, DeltaMorphs = deltas, StatMorphs = stat };
    }

    internal IReadOnlyDictionary<string, Vector3[]> BuildDeltas(IReadOnlyList<Vector3> shapedVertices)
    {
        if (shapedVertices.Count != Vertices.Length || shapedVertices.Any(value => !Finite(value)))
            throw new InvalidDataException("TRI requires all shaped base and statistical target vertices in source order.");
        var result = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
        foreach (var morph in DeltaMorphs)
        {
            var values = new Vector3[VertexCount];
            var bytes = morph.PackedDeltas.Span;
            for (var index = 0; index < values.Length; index++)
            {
                var at = index * 6;
                values[index] = new Vector3(BinaryPrimitives.ReadInt16LittleEndian(bytes[at..]),
                    BinaryPrimitives.ReadInt16LittleEndian(bytes[(at + 2)..]),
                    BinaryPrimitives.ReadInt16LittleEndian(bytes[(at + 4)..])) * morph.Scale;
            }
            result.Add(morph.Name, values);
        }
        foreach (var morph in StatMorphs)
        {
            var values = new Vector3[VertexCount];
            for (var index = 0; index < morph.VertexIndices.Length; index++)
            {
                var vertex = morph.VertexIndices[index];
                values[vertex] = shapedVertices[morph.AddedVertexStart + index] - shapedVertices[vertex];
            }
            result.Add(morph.Name, values);
        }
        if (result.Values.Any(values => values.Any(value => !Finite(value))))
            throw new InvalidDataException("TRI produced a non-finite morph delta.");
        return result;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private sealed class Cursor(ReadOnlyMemory<byte> bytes)
    {
        private int _offset;
        internal int Remaining => bytes.Length - _offset;
        internal ReadOnlyMemory<byte> Take(int count)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException("TRI source is truncated.");
            var result = bytes.Slice(_offset, count);
            _offset += count;
            return result;
        }
        internal int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4).Span);
        internal int Count(int minimumBytes)
        {
            var value = Int();
            if (value < 0 || value > Remaining / minimumBytes) throw new InvalidDataException("TRI count exceeds its source extent.");
            return value;
        }
        internal float Float()
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(Take(4).Span);
            return float.IsFinite(value) ? value : throw new InvalidDataException("TRI scalar is non-finite.");
        }
        internal string Label(bool wide, bool terminated)
        {
            var count = Count(wide ? 2 : 1);
            var label = Take(checked(count * (wide ? 2 : 1))).Span;
            if (terminated)
            {
                if (label.Length == 0 || label[^1] != 0) throw new InvalidDataException("TRI morph label is not terminated.");
                label = label[..^1];
            }
            return (wide ? Encoding.Unicode : Encoding.Latin1).GetString(label);
        }
    }
}
