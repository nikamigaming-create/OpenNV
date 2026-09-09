using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Source device geometry and its ray/UV mapping; owns no player or viewport.</summary>
internal sealed class NativeOwnedDeviceSurface
{
    private readonly FalloutNifFile _source;
    private readonly Node3D _pickModel;
    private readonly ILookup<string, MeshInstance3D> _geometry;
    private readonly MeshInstance3D _screen;
    private sealed record PickSurface(MeshInstance3D Mesh, Vector3[] Vertices, int[] Indices, Vector2[] Uvs,
        BaseMaterial3D.CullModeEnum Cull, bool Occludes);
    private PickSurface[]? _pickSurfaces;
    internal string ScreenName { get; }
    internal Node3D Root => _pickModel;
    internal MeshInstance3D? PickedGeometry { get; private set; }
    internal NativeOwnedDeviceSurface(string path, Node3D model, string screenName)
    {
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned device content is absent.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        _source = FalloutNifFile.Read(bytes); _pickModel = model; ScreenName = screenName;
        _geometry = model.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.HasMeta("opennv_nif_source_name"))
            .ToLookup(mesh => mesh.GetMeta("opennv_nif_source_name").AsString(), StringComparer.Ordinal);
        _screen = Geometry(ScreenName);
    }
    internal MeshInstance3D Geometry(string sourceName) => _geometry[sourceName].Single();
    internal IEnumerable<MeshInstance3D> GeometryParts(string sourceNode) => _geometry
        .Where(group => group.Key.StartsWith(sourceNode + ":", StringComparison.Ordinal)).SelectMany(group => group);
    private PickSurface ScreenSurface => PickSurfaces.Single(surface => surface.Mesh == _screen);
    internal Vector3[] ScreenVertices => ScreenSurface.Vertices;
    internal Vector2[] ScreenUvs => ScreenSurface.Uvs;
    internal int[] ScreenIndices => ScreenSurface.Indices;
    private PickSurface[] PickSurfaces =>
        _pickSurfaces ??= _pickModel.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.Mesh is not null).SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(index =>
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(index);
                var material = mesh.GetActiveMaterial(index);
                var geometry = _source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
                var properties = geometry.Properties.Where(block => block >= 0).Select(_source.ReadObject).ToArray();
                var alpha = properties.OfType<FalloutNifAlphaProperty>().SingleOrDefault();
                var depthWrites = properties.Select(property => property switch
                {
                    FalloutNifShaderProperty shader => (bool?)((shader.ShaderFlags2 & 1) != 0),
                    FalloutNifNoLightingProperty shader => (shader.ShaderFlags2 & 1) != 0,
                    _ => null,
                }).Where(value => value.HasValue).SingleOrDefault() ?? true;
                // Blended overlays which do not write depth decorate the screen;
                // they must not turn its visible controls into an opaque hit wall.
                var occludes = depthWrites || alpha is null ||
                    FalloutNifAlphaState.Read(alpha.Flags, alpha.Threshold).Blend == FalloutNifBlendMode.Opaque;
                var cull = material switch
                {
                    BaseMaterial3D standard => standard.CullMode,
                    ShaderMaterial shader when shader.Shader.Code.Contains("cull_disabled", StringComparison.Ordinal) => BaseMaterial3D.CullModeEnum.Disabled,
                    ShaderMaterial shader when shader.Shader.Code.Contains("cull_front", StringComparison.Ordinal) => BaseMaterial3D.CullModeEnum.Front,
                    ShaderMaterial shader when shader.Shader.Code.Contains("cull_back", StringComparison.Ordinal) => BaseMaterial3D.CullModeEnum.Back,
                    _ => throw new NotSupportedException("Rendered-menu surface has no input culling contract."),
                };
                return new PickSurface(mesh, arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
                    arrays[(int)Mesh.ArrayType.Index].AsInt32Array(), arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array(), cull, occludes);
            })).ToArray();
    internal Vector2? PickScreen(Vector3 origin, Vector3 direction)
    {
        PickedGeometry = null;
        var distance = float.PositiveInfinity; Vector2? result = null;
        var screen = _screen;
        foreach (var surface in PickSurfaces.Where(surface => surface.Mesh.IsVisibleInTree() && (surface.Mesh == screen || surface.Occludes)))
        {
            var mesh = surface.Mesh;
            var inverse = mesh.GlobalTransform.AffineInverse();
            var localOrigin = inverse * origin; var localDirection = inverse.Basis * direction;
            var vertices = surface.Vertices; var indices = surface.Indices; var uvs = surface.Uvs;
            for (var triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var a = indices[triangle]; var b = indices[triangle + 1]; var c = indices[triangle + 2];
                var first = vertices[b] - vertices[a]; var second = vertices[c] - vertices[a];
                var cross = localDirection.Cross(second); var determinant = first.Dot(cross);
                if (MathF.Abs(determinant) < 1e-8f) continue;
                // Godot's front-face triangle winding is clockwise.
                if (surface.Cull == BaseMaterial3D.CullModeEnum.Back && determinant > 0 ||
                    surface.Cull == BaseMaterial3D.CullModeEnum.Front && determinant < 0) continue;
                var offset = localOrigin - vertices[a]; var u = offset.Dot(cross) / determinant;
                if (u is < 0 or > 1) continue;
                var q = offset.Cross(first); var v = localDirection.Dot(q) / determinant;
                if (v < 0 || u + v > 1) continue;
                var hit = second.Dot(q) / determinant;
                if (hit < 0 || hit >= distance) continue;
                distance = hit;
                PickedGeometry = mesh;
                _pickModel.SetMeta("opennv_pointer_surface", mesh.Name.ToString());
                result = mesh == screen && uvs.Length == vertices.Length
                    ? uvs[a] * (1 - u - v) + uvs[b] * u + uvs[c] * v : null;
            }
        }
        return result;
    }

}
