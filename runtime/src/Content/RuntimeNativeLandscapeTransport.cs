using Godot;

namespace OpenNV.Runtime.Content;

internal sealed record RuntimeLandscapeTextureResources(
    ImageTexture Diffuse,
    ImageTexture? Normal);

internal partial class RuntimeNativeLandscapeTransport : Node3D
{
    internal required FalloutLandscapeTransport Source { get; init; }
    internal required MeshInstance3D Geometry { get; init; }
    internal required IReadOnlyDictionary<FalloutFormKey, RuntimeLandscapeTextureResources> Textures
    {
        get;
        init;
    }
}

internal static class RuntimeNativeLandscapeTransportBuilder
{
    private const int QuadrantSide = 2;
    private const int QuadrantLastVertex = FalloutLandscapeTransportResolver.QuadrantVertexSide - 1;
    private const int TriangleIndicesPerQuad = 6;
    private const float ColorChannelMaximum = byte.MaxValue;

    internal static RuntimeNativeLandscapeTransport Build(
        FalloutLandscapeTransport source,
        float gameUnitsToMeters)
    {
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gameUnitsToMeters), "Native LAND scale must be finite and positive.");
        if (source.BaseLayers.Count != QuadrantSide * QuadrantSide ||
            source.BaseLayers.Select(value => value.Quadrant).Distinct().Count() !=
            QuadrantSide * QuadrantSide)
            throw new NotSupportedException(
                $"Native LAND {source.Landscape} has incomplete base-quadrant transport.");
        var textures = source.Textures.ToDictionary(
            pair => pair.Key,
            pair => new RuntimeLandscapeTextureResources(
                NativeOwnedMediaLoader.LoadTexture(pair.Value.DiffusePath),
                pair.Value.NormalPath is null
                    ? null
                    : NativeOwnedMediaLoader.LoadTexture(pair.Value.NormalPath)));
        var mesh = new ArrayMesh();
        for (byte quadrant = 0; quadrant < QuadrantSide * QuadrantSide; ++quadrant)
            mesh.AddSurfaceFromArrays(
                Mesh.PrimitiveType.Triangles,
                BuildQuadrant(source, quadrant, gameUnitsToMeters));
        var geometry = new MeshInstance3D
        {
            Name = $"LAND_Geometry_{source.Landscape}",
            Mesh = mesh,
            // Geometry and every material input are transported exactly. Rendering stays
            // disabled until the live LAND weight shader consumes the authored VTXT rows.
            Visible = false,
        };
        geometry.SetMeta("opennv_land_render_status", "transported-material-pending");
        geometry.SetMeta("opennv_land_surfaces", mesh.GetSurfaceCount());
        geometry.SetMeta("opennv_land_alpha_layers", source.AlphaLayers.Count);
        var root = new RuntimeNativeLandscapeTransport
        {
            Name = $"NativeLAND_{source.Landscape}",
            Source = source,
            Geometry = geometry,
            Textures = textures,
        };
        root.SetMeta("opennv_land", source.Landscape.ToString());
        root.SetMeta("opennv_land_cell", source.ActiveCell.ToString());
        root.SetMeta("opennv_land_world", source.Worldspace.ToString());
        root.SetMeta("opennv_land_texture_count", textures.Count);
        root.SetMeta("opennv_source", "live-retail-files");
        root.AddChild(geometry);
        return root;
    }

    private static Godot.Collections.Array BuildQuadrant(
        FalloutLandscapeTransport source,
        byte quadrant,
        float gameUnitsToMeters)
    {
        var vertexCount = FalloutLandscapeTransportResolver.QuadrantVertexSide *
            FalloutLandscapeTransportResolver.QuadrantVertexSide;
        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var colors = new Color[vertexCount];
        var uvs = new Vector2[vertexCount];
        var quadrantX = quadrant % QuadrantSide;
        var quadrantY = quadrant / QuadrantSide;
        var offsetX = quadrantX * QuadrantLastVertex;
        var offsetY = quadrantY * QuadrantLastVertex;
        var cellX = source.ActiveCoordinates.X *
            FalloutLandscapeTransportResolver.ExteriorCellSideGameUnits;
        var cellY = source.ActiveCoordinates.Y *
            FalloutLandscapeTransportResolver.ExteriorCellSideGameUnits;
        for (var localY = 0; localY < FalloutLandscapeTransportResolver.QuadrantVertexSide; ++localY)
        {
            for (var localX = 0; localX < FalloutLandscapeTransportResolver.QuadrantVertexSide; ++localX)
            {
                var sourceX = offsetX + localX;
                var sourceY = offsetY + localY;
                var sourceIndex = sourceY * FalloutLandscapeTransportResolver.VertexSide + sourceX;
                var localIndex = localY * FalloutLandscapeTransportResolver.QuadrantVertexSide + localX;
                vertices[localIndex] = new Vector3(
                    cellX + sourceX * FalloutLandscapeTransportResolver.VertexSpacingGameUnits,
                    source.Heights[sourceIndex],
                    -(cellY + sourceY * FalloutLandscapeTransportResolver.VertexSpacingGameUnits)) *
                    gameUnitsToMeters;
                var normalOffset = sourceIndex * 3;
                normals[localIndex] = new Vector3(
                    source.Normals[normalOffset],
                    source.Normals[normalOffset + 2],
                    -source.Normals[normalOffset + 1]);
                var colorOffset = sourceIndex * 3;
                colors[localIndex] = new Color(
                    source.Colors[colorOffset] / ColorChannelMaximum,
                    source.Colors[colorOffset + 1] / ColorChannelMaximum,
                    source.Colors[colorOffset + 2] / ColorChannelMaximum,
                    1.0f);
                uvs[localIndex] = new Vector2(
                    localX / (float)QuadrantLastVertex,
                    localY / (float)QuadrantLastVertex);
            }
        }
        var indices = new int[
            QuadrantLastVertex * QuadrantLastVertex * TriangleIndicesPerQuad];
        var cursor = 0;
        for (var y = 0; y < QuadrantLastVertex; ++y)
        {
            for (var x = 0; x < QuadrantLastVertex; ++x)
            {
                var lowerLeft = y * FalloutLandscapeTransportResolver.QuadrantVertexSide + x;
                var lowerRight = lowerLeft + 1;
                var upperLeft = lowerLeft + FalloutLandscapeTransportResolver.QuadrantVertexSide;
                var upperRight = upperLeft + 1;
                indices[cursor++] = lowerLeft;
                indices[cursor++] = lowerRight;
                indices[cursor++] = upperLeft;
                indices[cursor++] = lowerRight;
                indices[cursor++] = upperRight;
                indices[cursor++] = upperLeft;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        return arrays;
    }
}
