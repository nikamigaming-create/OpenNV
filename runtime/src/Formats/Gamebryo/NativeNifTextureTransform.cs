using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifTextureTransform
{
    // Column-vector order from nifxml TransformMethod. Scale, translation and
    // center are texture-space values; Maya additionally inverts the V axis.
    internal const string ShaderSource = """
        uniform vec2 source_uv_scale = vec2(1.0);
        uniform float source_uv_rotation = 0.0;
        uniform vec2 source_uv_center = vec2(0.0);
        uniform int source_uv_method = 0;
        vec2 owned_texture_transform(vec2 uv) {
            float c = cos(source_uv_rotation), s = sin(source_uv_rotation);
            mat2 rotation = mat2(vec2(c, s), vec2(-s, c));
            if (source_uv_method == 1)
                return source_uv_center + source_uv_scale * (rotation * (uv + source_uv_offset - source_uv_center));
            vec2 value = uv * source_uv_scale + source_uv_offset;
            if (source_uv_method == 2) value.y = 1.0 - value.y;
            return source_uv_center + rotation * (value - source_uv_center);
        }
        """;

    internal static bool Valid(FalloutNifTextureTransform value) => value.TransformType <= 2 &&
        float.IsFinite(value.Translation.U) && float.IsFinite(value.Translation.V) &&
        float.IsFinite(value.Tiling.U) && float.IsFinite(value.Tiling.V) && float.IsFinite(value.Rotation) &&
        float.IsFinite(value.Center.U) && float.IsFinite(value.Center.V);

    internal static void Configure(ShaderMaterial material, FalloutNifTextureTransform transform)
    {
        if (!Valid(transform)) throw new NotSupportedException("Source texture transformation is invalid or unsupported.");
        material.SetShaderParameter("source_uv_offset", new Vector2(transform.Translation.U, transform.Translation.V));
        material.SetShaderParameter("source_uv_scale", new Vector2(transform.Tiling.U, transform.Tiling.V));
        material.SetShaderParameter("source_uv_rotation", transform.Rotation);
        material.SetShaderParameter("source_uv_center", new Vector2(transform.Center.U, transform.Center.V));
        material.SetShaderParameter("source_uv_method", (int)transform.TransformType);
    }

    internal static void Apply(ShaderMaterial material, uint operation, float value)
    {
        if (material.ResourceName != NativeNifEffectMaterial.ResourceIdentity || operation > 4 || !float.IsFinite(value))
            throw new NotSupportedException("Animated texture operation has no material owner.");
        if (operation == 2) { material.SetShaderParameter("source_uv_rotation", value); return; }
        var name = operation < 2 ? "source_uv_offset" : "source_uv_scale";
        var pair = material.GetShaderParameter(name).AsVector2();
        if (operation is 0 or 3) pair.X = value; else pair.Y = value;
        material.SetShaderParameter(name, pair);
    }
}
