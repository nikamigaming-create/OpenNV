namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifTextureAddressing
{
    internal static (bool U, bool V) WrapAxes(uint clampMode) => clampMode switch
    {
        0 => (false, false),
        1 => (false, true),
        2 => (true, false),
        3 => (true, true),
        _ => throw new InvalidDataException($"Unknown NIF texture clamp mode {clampMode}."),
    };

    internal static uint LegacyTrilinearClampMode(ushort flags)
    {
        // TexturingMapFlags stores UV set in bits 0-7, filter in 8-11 and
        // independent S/T addressing in 12-13. The other bits are undeclared.
        if ((flags & 0xc000) != 0)
            throw new InvalidDataException($"NIF legacy texture flags 0x{flags:x4} contain undeclared bits.");
        var filter = (flags >> 8) & 15;
        if (filter != 2)
            throw new NotSupportedException($"NIF legacy filter mode {filter} has no trilinear material owner.");
        return (uint)((flags >> 12) & 3);
    }

    internal static float ClampAtMip(float coordinate, int size)
    {
        if (!float.IsFinite(coordinate) || size <= 0) throw new ArgumentOutOfRangeException(nameof(coordinate));
        var halfTexel = 0.5f / size;
        return Math.Clamp(coordinate, halfTexel, 1.0f - halfTexel);
    }

    // The Godot spatial sampler hint controls both axes. A repeat sampler
    // retains hardware bilinear wrap across the periodic seam; clamping the
    // other axis to that mip's texel centres retains its edge texels. Clamp
    // separately at the two mip levels before trilinear blending. A single
    // base-level UV clamp would bleed the opposite edge at smaller mips.
    internal const string MixedTrilinearShaderSource = """
        vec4 owned_mixed_mip(sampler2D source, vec2 uv, int level, int clamp_mode) {
            vec2 edge = vec2(0.5) / vec2(textureSize(source, level));
            if (clamp_mode == 1) uv.x = clamp(uv.x, edge.x, 1.0 - edge.x);
            else uv.y = clamp(uv.y, edge.y, 1.0 - edge.y);
            return textureLod(source, uv, float(level));
        }
        vec4 owned_mixed_trilinear(sampler2D source, vec2 uv, int clamp_mode) {
            float last_level = float(max(textureQueryLevels(source) - 1, 0));
            // Query before addressing, so an animated wrap or clamped edge
            // cannot create a discontinuity in the source UV derivatives.
            float lod = clamp(textureQueryLod(source, uv).y, 0.0, last_level);
            int first = int(floor(lod));
            int second = min(first + 1, int(last_level));
            return mix(owned_mixed_mip(source, uv, first, clamp_mode),
                owned_mixed_mip(source, uv, second, clamp_mode), fract(lod));
        }
        """;

    internal static bool RepeatForGodot(uint clampMode) => clampMode switch
    {
        0 => false, // CLAMP_S_CLAMP_T
        3 => true,  // WRAP_S_WRAP_T
        1 or 2 => throw new NotSupportedException(
            $"NIF texture clamp mode {clampMode} requires independent U/V sampler addressing."),
        _ => throw new InvalidDataException($"Unknown NIF texture clamp mode {clampMode}."),
    };
}
