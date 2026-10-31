using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

// A reusable source mesh, never a transformed asset cache.
internal sealed class RuntimeNativeGrassModel(ArrayMesh mesh, Texture2D texture,
    float alphaCutoff, float radius, float maximumAlphaSquared, bool hasNormals) : IDisposable
{
    internal ArrayMesh Mesh { get; } = mesh;
    internal Texture2D Texture { get; } = texture;
    internal float AlphaCutoff { get; } = alphaCutoff;
    internal float Radius { get; } = radius;
    internal float MaximumAlphaSquared { get; } = maximumAlphaSquared;
    internal bool HasNormals { get; } = hasNormals;

    internal static RuntimeNativeGrassModel Read(RuntimeLiveContentSource source, string model)
    {
        if (!source.TryRead(model, null, out var bytes, out _))
            throw new FileNotFoundException("Source grass model is missing.", model);
        var nif = FalloutNifFile.Read(bytes);
        if (nif.Roots.Count != 1 || nif.ReadObject(nif.Roots[0]) is not FalloutNifNode root)
            throw new NotSupportedException($"Grass {model} has no qualified single node root.");
        // Native grass preparation selects the first direct triangle geometry.
        // It copies mesh-data channels into its own batches; it does not flatten
        // the source node/geometry transforms as the ordinary NIF renderer does.
        var geometry = root.Children.Where(index => index >= 0)
            .Select(nif.ReadObject).OfType<FalloutNifGeometry>().FirstOrDefault()
            ?? throw new InvalidDataException($"Grass {model} has no direct triangle geometry.");
        if (root.Controller != -1 || geometry.Controller != -1 || geometry.SkinInstance != -1)
            throw new NotSupportedException($"Grass {model} controller/skin ownership is not qualified.");
        var data = nif.ReadObject(geometry.Data) as FalloutNifMeshData
            ?? throw new InvalidDataException($"Grass {model} has no triangle data.");
        var properties = geometry.Properties.Select(nif.ReadObject).ToArray();
        var tallGrass = properties.OfType<FalloutNifTallGrassShaderProperty>().Single();
        var alpha = properties.OfType<FalloutNifAlphaProperty>().Single();
        if (tallGrass.Controller != -1 || alpha.Controller != -1 || data.AdditionalData != -1 ||
            data.Vertices.Length == 0 || data.Colors.Length != data.Vertices.Length ||
            data.TextureCoordinates.Length == 0 || data.TextureCoordinates[0].Length != data.Vertices.Length)
            throw new NotSupportedException($"Grass {model} has unqualified model channels/controllers.");
        var path = tallGrass.FileName.Replace('\\', '/');
        if (!source.TryRead(path, null, out var textureBytes, out var textureSource))
            throw new FileNotFoundException("Source grass diffuse texture is missing.", path);
        var texture = NativeDdsTexture.LoadCached(source, textureBytes, textureSource, logicalPath: path);
        var mesh = new ArrayMesh();
        try
        {
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = data.Vertices.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
            if (data.Normals.Length != 0)
            {
                if (data.Normals.Length != data.Vertices.Length)
                    throw new InvalidDataException("Grass normals do not cover the source vertices.");
                arrays[(int)Godot.Mesh.ArrayType.Normal] = data.Normals.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
            }
            arrays[(int)Godot.Mesh.ArrayType.Color] = data.Colors.Select(c => new Color(c.R, c.G, c.B, c.A)).ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = data.TextureCoordinates[0].Select(p => new Vector2(p.U, p.V)).ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Index] = FalloutNifTriangleWinding.ToGodotIndices(data.Triangles);
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            var radius = data.Vertices.Max(p => new Vector3(p.X, p.Y, p.Z).Length());
            return new(mesh, texture, alpha.Threshold / 255f, radius, data.Colors.Max(c => c.A * c.A), data.Normals.Length != 0);
        }
        catch { mesh.Dispose(); throw; }
    }

    public void Dispose() => Mesh.Dispose(); // Shared DDS cache/materials retain their own texture ownership.
}
