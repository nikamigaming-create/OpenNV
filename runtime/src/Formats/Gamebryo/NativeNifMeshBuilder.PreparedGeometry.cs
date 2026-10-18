using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed record PreparedGeometry(Vector3[] Vertices, Vector3[] Normals, float[] Tangents,
        Color[] Colors, Vector2[] Uv, Vector2[] MorphHeights, int[] Indices);

    // Managed arrays follow the decoded source lifetime, never a persistent
    // transformed asset. Native resource creation remains on its engine owner.
    private static readonly ConditionalWeakTable<FalloutNifFile,
        ConcurrentDictionary<(int Block, float Units), PreparedGeometry>> PreparedGeometryCache = new();

    internal static void PrepareStaticGeometry(FalloutNifFile source, float units, CancellationToken cancellation)
    {
        var state = new BuildState(source, units, null);
        foreach (var block in source.Blocks)
        {
            cancellation.ThrowIfCancellationRequested();
            if (block.TypeName is not ("NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")) continue;
            var geometry = source.ReadGeometry(block.Index);
            if (geometry.Controller != -1 || geometry.SkinInstance != -1 || geometry.Data < 0) continue;
            var data = source.ReadMeshData(geometry.Data);
            var morph = data.AdditionalData < 0 ? null : source.ReadObject(data.AdditionalData) as FalloutNifLandscapeMorphData;
            _ = state.PreparedArrays(geometry, data, morph, cache: true);
        }
    }

    private sealed partial class BuildState
    {
        internal PreparedGeometry PreparedArrays(FalloutNifGeometry geometry, FalloutNifMeshData data,
            FalloutNifLandscapeMorphData? morph, bool cache)
        {
            return cache ? PreparedGeometryCache.GetOrCreateValue(_source)
                .GetOrAdd((geometry.Block.Index, _unitsToMetres), _ => Convert()) : Convert();

            PreparedGeometry Convert()
            {
                RequireAttributeCount(data.Normals.Length, data.Vertices.Length, "normals", data.Block.Index);
                RequireAttributeCount(data.Tangents.Length, data.Vertices.Length, "tangents", data.Block.Index);
                RequireAttributeCount(data.Bitangents.Length, data.Vertices.Length, "bitangents", data.Block.Index);
                RequireAttributeCount(data.Colors.Length, data.Vertices.Length, "colors", data.Block.Index);
                var vertices = new Vector3[data.Vertices.Length];
                for (var i = 0; i < vertices.Length; i++) vertices[i] = ConvertVector(data.Vertices[i]) * _unitsToMetres;
                var normals = new Vector3[data.Normals.Length];
                for (var i = 0; i < normals.Length; i++) normals[i] = ConvertVector(data.Normals[i]).Normalized();
                return new(vertices, normals, data.Tangents.Length == 0 ? [] : BuildTangents(data),
                    data.Colors.Length == 0 ? [] : VertexColors(geometry, data),
                    data.TextureCoordinates.Length == 1 ? data.TextureCoordinates[0].Select(uv => new Vector2(uv.U, uv.V)).ToArray() : [],
                    morph?.Heights.Select(height => new Vector2(height * _unitsToMetres, 0)).ToArray() ?? [],
                    FalloutNifTriangleWinding.ToGodotIndices(data.Triangles));
            }
        }
    }
}
