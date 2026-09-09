using Godot;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

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
    internal const string MaterialIdentity = "Owned LAND lighting";
    private const int QuadrantSide = 2;
    private const int QuadrantLastVertex = FalloutLandscapeTransportResolver.QuadrantVertexSide - 1;
    private const int TriangleIndicesPerQuad = 6;
    private const float ColorChannelMaximum = byte.MaxValue;
    private static readonly Dictionary<string, Shader> Shaders = new(StringComparer.Ordinal);

    internal static RuntimeNativeLandscapeTransport Build(
        FalloutLandscapeTransport source,
        float gameUnitsToMeters,
        IDictionary<string, ImageTexture>? decodedTextures = null)
    {
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gameUnitsToMeters), "Native LAND scale must be finite and positive.");
        if (source.BaseLayers.Count != QuadrantSide * QuadrantSide ||
            source.BaseLayers.Select(value => value.Quadrant).Distinct().Count() !=
            QuadrantSide * QuadrantSide)
            throw new NotSupportedException(
                $"Native LAND {source.Landscape} has incomplete base-quadrant transport.");
        decodedTextures ??= new Dictionary<string, ImageTexture>(StringComparer.OrdinalIgnoreCase);
        ImageTexture Texture(string path)
        {
            if (!decodedTextures.TryGetValue(path, out var texture))
                decodedTextures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
            return texture;
        }
        var textures = source.Textures.ToDictionary(
            pair => pair.Key,
            pair => new RuntimeLandscapeTextureResources(
                Texture(pair.Value.DiffusePath),
                pair.Value.NormalPath is null
                    ? null
                    : Texture(pair.Value.NormalPath)));
        var mesh = new ArrayMesh();
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Landscape has no owned installation settings."));
        var tiling = settings.Number("Landscape", "fLandTextureTilingMult");
        var quadrantTiling = FalloutLandscapeMaterialInputs.QuadrantTiling(tiling);
        var faceMaterials = new List<int>();
        for (byte quadrant = 0; quadrant < QuadrantSide * QuadrantSide; ++quadrant)
        {
            var arrays = BuildQuadrant(source, quadrant, gameUnitsToMeters);
            mesh.AddSurfaceFromArrays(
                Mesh.PrimitiveType.Triangles,
                arrays);
            mesh.SurfaceSetMaterial(quadrant, Material(source, quadrant, textures, quadrantTiling));
            var layers = source.AlphaLayers.Where(layer => layer.Quadrant == quadrant).OrderBy(layer => layer.LayerIndex).ToArray();
            var basis = source.BaseLayers.Single(layer => layer.Quadrant == quadrant);
            var materials = new[] { basis }.Concat(layers).Select(layer => (int?)source.Textures[layer.Texture].Physics?.Material ?? -1).ToArray();
            faceMaterials.AddRange(FalloutLandscapeMaterialInputs.CollisionMaterials(FalloutLandscapeMaterialInputs.Weights(layers),
                materials, arrays[(int)Mesh.ArrayType.Index].AsInt32Array()));
        }
        mesh.RegenNormalMaps();
        var geometry = new MeshInstance3D
        {
            Name = $"LAND_Geometry_{source.Landscape}",
            Mesh = mesh,
        };
        geometry.SetMeta("opennv_land_render_status", "source-layers-and-vertex-alpha; retail-pixels-unverified");
        geometry.SetMeta("opennv_land_surfaces", mesh.GetSurfaceCount());
        geometry.SetMeta("opennv_land_alpha_layers", source.AlphaLayers.Count);
        geometry.SetMeta("opennv_land_quadrant_tiling", quadrantTiling);
        geometry.SetMeta("opennv_land_weight_owner", "normalized-at-source-vertices;weighted-diffuse-and-decoded-normals");
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
        var collision = new StaticBody3D { Name = "LandscapeCollision" };
        var collisionShape = new CollisionShape3D { Shape = mesh.CreateTrimeshShape() };
        collisionShape.SetMeta("opennv_havok_face_materials", faceMaterials.ToArray());
        geometry.SetMeta("opennv_land_impact_unbound_triangles", faceMaterials.Count(material => material < 0));
        collision.AddChild(collisionShape);
        root.AddChild(collision);
        return root;
    }

    private static ShaderMaterial Material(FalloutLandscapeTransport source, byte quadrant,
        IReadOnlyDictionary<FalloutFormKey, RuntimeLandscapeTextureResources> textures, float tiling)
    {
        var layers = source.AlphaLayers.Where(layer => layer.Quadrant == quadrant).OrderBy(layer => layer.LayerIndex).ToArray();
        var weights = FalloutLandscapeMaterialInputs.Weights(layers);
        var material = new ShaderMaterial { ResourceName = MaterialIdentity };
        var parameters = new Dictionary<string, Variant>();
        var shader = new StringBuilder("""
            shader_type spatial;
            render_mode cull_back, ambient_light_disabled, specular_disabled;
            uniform sampler2D base_texture : filter_linear_mipmap_anisotropic, repeat_enable;
            uniform float tiling;
            instance uniform vec3 source_ambient : instance_index(0);
            instance uniform vec3 source_fog_color : instance_index(1);
            instance uniform vec3 source_fog_range : instance_index(2);
            instance uniform float source_fog_game_units_per_meter : instance_index(3);
            varying float source_fog_factor;
            """);
        shader.AppendLine(RetailVertexFog.ShaderSource);
        var vertex = new StringBuilder("void vertex() {\nsource_fog_factor = owned_vertex_fog(MODELVIEW_MATRIX * vec4(VERTEX, 1.0), PROJECTION_MATRIX, source_fog_range, source_fog_game_units_per_meter);\n");
        for (var pack = 0; pack * 4 < weights.Length; pack++)
        {
            shader.AppendLine($"uniform sampler2D weights_{pack} : filter_nearest, repeat_disable;\nvarying vec4 layer_weights_{pack};");
            vertex.AppendLine($"layer_weights_{pack} = textureLod(weights_{pack}, (UV * 16.0 + 0.5) / 17.0, 0.0);");
            parameters.Add($"weights_{pack}", WeightTexture(weights, pack));
        }
        static string Weight(int layer) => $"layer_weights_{layer / 4}.{"xyzw"[layer % 4]}";
        var fragment = new StringBuilder($"void fragment() {{\nvec2 tiled_uv = UV * tiling;\nvec3 albedo = texture(base_texture, tiled_uv).rgb * {Weight(0)};\nvec3 normal_sample = vec3(0.0,0.0,1.0) * {Weight(0)};\n");
        var basis = source.BaseLayers.Single(layer => layer.Quadrant == quadrant);
        parameters.Add("base_texture", textures[basis.Texture].Diffuse);
        parameters.Add("tiling", tiling);
        if (textures[basis.Texture].Normal is { } baseNormal)
        {
            shader.AppendLine("uniform sampler2D base_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;");
            parameters.Add("base_normal", baseNormal);
            fragment.AppendLine($"normal_sample = (texture(base_normal, tiled_uv).rgb * 2.0 - 1.0) * {Weight(0)};");
        }
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            shader.AppendLine($"uniform sampler2D color_{index} : filter_linear_mipmap_anisotropic, repeat_enable;");
            fragment.AppendLine($"albedo += texture(color_{index}, tiled_uv).rgb * {Weight(index + 1)};");
            parameters.Add($"color_{index}", textures[layer.Texture].Diffuse);
            if (textures[layer.Texture].Normal is { } normal)
            {
                shader.AppendLine($"uniform sampler2D normal_{index} : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;");
                parameters.Add($"normal_{index}", normal);
                fragment.AppendLine($"normal_sample += (texture(normal_{index}, tiled_uv).rgb * 2.0 - 1.0) * {Weight(index + 1)};");
            }
            else fragment.AppendLine($"normal_sample += vec3(0.0,0.0,1.0) * {Weight(index + 1)};");
        }
        shader.Append(vertex).AppendLine("}").Append(fragment)
            .AppendLine("ALBEDO = albedo * COLOR.rgb; EMISSION = ALBEDO * source_ambient; NORMAL_MAP = normalize(normal_sample) * 0.5 + 0.5; FOG = vec4(source_fog_color, source_fog_factor); }");
        RetailLighting.AppendDiffuseLightFunction(shader);
        var code = shader.ToString();
        if (!Shaders.TryGetValue(code, out var compiled)) Shaders.Add(code, compiled = new Shader { Code = code });
        material.Shader = compiled;
        foreach (var (name, value) in parameters) material.SetShaderParameter(name, value);
        return material;
    }

    private static ImageTexture WeightTexture(float[][] weights, int pack)
    {
        const int side = FalloutLandscapeTransportResolver.QuadrantVertexSide;
        var bytes = new byte[side * side * 4 * sizeof(float)];
        for (var vertex = 0; vertex < side * side; vertex++)
            for (var channel = 0; channel < 4; channel++)
            {
                var layer = pack * 4 + channel;
                var value = layer < weights.Length ? weights[layer][vertex] : 0;
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((vertex * 4 + channel) * sizeof(float)), value);
            }
        using var image = Image.CreateFromData(side, side, false, Image.Format.Rgbaf, bytes);
        return ImageTexture.CreateFromImage(image);
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
                indices[cursor++] = upperLeft;
                indices[cursor++] = lowerRight;
                indices[cursor++] = lowerRight;
                indices[cursor++] = upperLeft;
                indices[cursor++] = upperRight;
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
