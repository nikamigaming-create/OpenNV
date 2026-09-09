using System.Text;
using System.Runtime.CompilerServices;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifLodMaterial
{
    internal const string ResourceIdentity = "Owned distant terrain and object lighting";
    private static readonly Dictionary<bool, Shader> Shaders = [];
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Dictionary<string, WeakReference<Texture2D>>> Textures = new();

    internal static Material? TryBuild(FalloutNifFile nif, FalloutNifGeometry geometry)
    {
        var properties = geometry.Properties.Where(index => index != -1).Select(nif.ReadObject).ToArray();
        var declaration = properties.OfType<FalloutNifShaderProperty>().SingleOrDefault();
        if (declaration is null || (declaration.ShaderFlags2 & 6) == 0) return null;
        var landscape = declaration.ShaderFlags2 == 2;
        if (properties.Length != 1 || declaration.ShaderType != 1 ||
            declaration.ShaderFlags != (landscape ? 0x3000u : 0x2000u) ||
            declaration.ShaderFlags2 is not (2 or 4) || declaration.Controller != -1 ||
            declaration.ExtraData.Any(index => index != -1) || declaration.RefractionStrength != 0 ||
            declaration.RefractionFirePeriod != 0 || geometry.SkinInstance != -1)
            throw new NotSupportedException($"Distant NIF shader {declaration.Block.Index} has an unbound pass declaration.");
        if (nif.ReadObject(declaration.TextureSet) is not FalloutNifShaderTextureSet textures ||
            textures.Textures.Length != 6 || textures.Textures.Skip(2).Any(path => path.Length != 0) ||
            textures.Textures[0].Length == 0)
            throw new NotSupportedException("Distant NIF requires its source diffuse/normal texture pair.");
        // LOD_Landscape/LOD_Building select a separate depth-writing source
        // pass. Their serialized flags do not carry the ordinary material's
        // ZBuffer_Test/Write bits; treating them as ordinary SLS disables depth.
        if (!Shaders.TryGetValue(landscape, out var compiled)) Shaders.Add(landscape, compiled = BuildShader(landscape));
        var material = new ShaderMaterial { Shader = compiled, ResourceName = ResourceIdentity };
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("LOD textures require owned data.");
        var cache = Textures.GetOrCreateValue(source);
        long textureBytes = 0;
        Texture2D Texture(string path)
        {
            path = FalloutNifSurfaceInputs.TexturePath(path);
            if (!source.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
            textureBytes += bytes.Length;
            if (cache.TryGetValue(path, out var weak) && weak.TryGetTarget(out var existing) && GodotObject.IsInstanceValid(existing)) return existing;
            var loaded = NativeOwnedMediaLoader.LoadTexture(path);
            cache[path] = new(loaded); return loaded;
        }
        material.SetShaderParameter("base_map", Texture(textures.Textures[0]));
        if (textures.Textures[1].Length != 0)
            material.SetShaderParameter("normal_map", Texture(textures.Textures[1]));
        material.SetShaderParameter("use_normal_map", textures.Textures[1].Length != 0);
        material.SetShaderParameter("landscape", landscape);
        material.SetMeta("opennv_nif_shader_flags", declaration.ShaderFlags);
        material.SetMeta("opennv_nif_shader_flags2", declaration.ShaderFlags2);
        material.SetMeta("opennv_source_lod_pass", landscape ? "landscape" : "building");
        material.SetMeta("opennv_lod_texture_bytes", textureBytes);
        material.SetMeta("opennv_source_lighting_parity", "unverified-distance-blend-shadows-and-pixels");
        return material;
    }

    private static Shader BuildShader(bool landscapePass)
    {
        var code = new StringBuilder($$"""
            shader_type spatial;
            render_mode cull_back, depth_draw_opaque, ambient_light_disabled, specular_disabled;
            uniform sampler2D base_map : filter_linear_mipmap_anisotropic, repeat_disable;
            uniform sampler2D normal_map : filter_linear_mipmap_anisotropic, repeat_disable;
            uniform bool use_normal_map;
            uniform bool landscape;
            uniform bool detail_mask_enabled = false;
            uniform sampler2D detail_mask : filter_nearest, repeat_disable;
            uniform vec4 detail_bounds;
            uniform vec2 lod_camera_xz;
            uniform vec2 morph_range = vec2(1e10, 1e11);
            instance uniform vec3 source_ambient : instance_index(0);
            instance uniform vec3 source_fog_color : instance_index(1);
            instance uniform vec3 source_fog_range : instance_index(2);
            instance uniform float source_fog_game_units_per_meter : instance_index(3);
            varying vec2 source_world_xz;
            varying float source_fog_factor;
            {{NativeNifPointLighting.ShaderSource}}
            {{RetailVertexFog.ShaderSource}}
            void vertex() {
                vec4 world = MODEL_MATRIX * vec4(VERTEX, 1.0);
                if (landscape) {
                    float distance_xz = max(abs(world.x - lod_camera_xz.x), abs(world.z - lod_camera_xz.y));
                    float morph = smoothstep(morph_range.x, morph_range.y, distance_xz);
                    VERTEX.y = mix(VERTEX.y, UV2.x, morph);
                }
                source_world_xz = world.xz;
                source_fog_factor = owned_vertex_fog(MODELVIEW_MATRIX * vec4(VERTEX, 1.0),
                    PROJECTION_MATRIX, source_fog_range, source_fog_game_units_per_meter);
            }
            void fragment() {
                vec2 detail_uv = (source_world_xz - detail_bounds.xy) / max(detail_bounds.zw - detail_bounds.xy, vec2(0.001));
                if (detail_mask_enabled && all(greaterThanEqual(detail_uv, vec2(0.0))) && all(lessThan(detail_uv, vec2(1.0))) &&
                    texture(detail_mask, detail_uv).r > 0.5) discard;
                vec3 base = texture(base_map, UV).rgb * COLOR.rgb;
                if (use_normal_map) {
                    vec3 decoded = normalize(texture(normal_map, UV).rgb * 2.0 - 1.0);
                    {{(landscapePass ? "NORMAL = normalize((VIEW_MATRIX * vec4(decoded.x, decoded.z, -decoded.y, 0.0)).xyz);" :
                        "NORMAL = normalize(TANGENT * decoded.x + BINORMAL * decoded.y + NORMAL * decoded.z);")}}
                }
                ALBEDO = base;
                EMISSION = base * source_ambient;
                FOG = vec4(source_fog_color, source_fog_factor);
            }
            """);
        RetailLighting.AppendDiffuseLightFunction(code);
        return new Shader { Code = code.ToString() };
    }
}
