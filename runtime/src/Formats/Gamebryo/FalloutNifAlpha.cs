namespace OpenNV.Runtime.Formats.Gamebryo;

internal enum FalloutNifBlendMode { Opaque, SourceAlpha, Add, Premultiplied, Multiply }

internal readonly record struct FalloutNifAlphaState(
    FalloutNifBlendMode Blend, bool TestEnabled, byte TestFunction, byte Threshold, bool Sort)
{
    internal static FalloutNifAlphaState ForNoLighting(
        FalloutNifNoLightingProperty shader, FalloutNifAlphaProperty? property)
    {
        var state = property is null
            ? new FalloutNifAlphaState(FalloutNifBlendMode.Opaque, false, 0, 0, true)
            : Read(property.Flags, property.Threshold);
        // The no-lighting falloff pass participates in the alpha/decal batches
        // even with the default (blend-disabled) NiAlphaProperty. Its computed
        // opacity includes the authored vertex and angle-falloff channels.
        // Explicit non-opaque blend factors and independent tests still apply.
        return state.Blend == FalloutNifBlendMode.Opaque && (shader.ShaderFlags & (1U << 6)) != 0
            ? state with { Blend = FalloutNifBlendMode.SourceAlpha }
            : state;
    }

    // NiAlphaProperty.AlphaFlags uses independent blend and test fields. An
    // enabled test does not remove the source's fractional-alpha blending.
    internal static FalloutNifAlphaState Read(ushort flags, byte threshold)
    {
        var source = (flags >> 1) & 15;
        var destination = (flags >> 5) & 15;
        if (source > 10 || destination > 10)
            throw new InvalidDataException("NIF alpha property contains an unknown blend factor.");
        var blend = (flags & 1) == 0 ? FalloutNifBlendMode.Opaque : (source, destination) switch
        {
            (6, 7) => FalloutNifBlendMode.SourceAlpha,
            (6, 0) => FalloutNifBlendMode.Add,
            (0, 7) => FalloutNifBlendMode.Premultiplied,
            (1, 2) or (4, 1) => FalloutNifBlendMode.Multiply,
            _ => throw new NotSupportedException($"NIF source/destination blend factors {source}/{destination} have no renderer owner."),
        };
        if ((flags & 0x8000) != 0)
            throw new NotSupportedException("NIF alpha threshold requires an external controller owner.");
        return new(blend, (flags & 0x200) != 0, (byte)((flags >> 10) & 7), threshold, (flags & 0x2000) == 0);
    }
}

internal readonly record struct FalloutNifAngleFalloff(float StartCosine, float StopCosine, float StartOpacity, float StopOpacity)
{
    internal const string ShaderSource = """
        float owned_angle_opacity(float cosine, vec4 endpoints) {
            float span = endpoints.y - endpoints.x;
            if (span == 0.0) return endpoints.z;
            float fraction = clamp((abs(cosine) - endpoints.x) / span, 0.0, 1.0);
            float smooth_fraction = fraction * fraction * (3.0 - 2.0 * fraction);
            return mix(endpoints.z, endpoints.w, smooth_fraction);
        }
        """;

    internal static FalloutNifAngleFalloff Read(FalloutNifNoLightingProperty source)
    {
        var result = new FalloutNifAngleFalloff(source.FalloffStartAngle, source.FalloffStopAngle,
            source.FalloffStartOpacity, source.FalloffStopOpacity);
        if (!float.IsFinite(result.StartCosine) || !float.IsFinite(result.StopCosine) ||
            !float.IsFinite(result.StartOpacity) || !float.IsFinite(result.StopOpacity) ||
            Math.Abs(result.StartCosine) > 1 || Math.Abs(result.StopCosine) > 1 ||
            result.StartOpacity is < 0 or > 1 || result.StopOpacity is < 0 or > 1)
            throw new InvalidDataException("NIF angle-falloff endpoints are outside their source ranges.");
        if (result.StartCosine == result.StopCosine && result.StartOpacity != result.StopOpacity)
            throw new NotSupportedException("Coincident NIF falloff angles with different opacities have no interpolation owner.");
        return result;
    }

    internal float Sample(float cosine)
    {
        if (!float.IsFinite(cosine)) throw new ArgumentOutOfRangeException(nameof(cosine));
        if (StartCosine == StopCosine) return StartOpacity;
        var fraction = Math.Clamp((MathF.Abs(cosine) - StartCosine) / (StopCosine - StartCosine), 0, 1);
        if (fraction == 0) return StartOpacity;
        if (fraction == 1) return StopOpacity;
        var smoothFraction = fraction * fraction * (3 - 2 * fraction);
        return StartOpacity + (StopOpacity - StartOpacity) * smoothFraction;
    }
}
