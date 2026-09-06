namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlacedLight(
    FalloutFormKey Reference,
    FalloutFormKey Base,
    float RadiusGameUnits,
    byte[] ColorRgb,
    float Intensity,
    float[] ShaderColorRgb,
    FalloutFormKey? Emittance);

internal static class FalloutPlacedLightResolver
{
    private const int StaticDuration = -1;
    private const uint StaticPointFlags = 0;
    private const float StaticFalloff = 1.0f;
    private const float StaticFieldOfViewDegrees = 90.0f;
    private const uint StaticNearClip = 0;
    private const float StaticPeriod = 0.0f;
    private const byte StaticColorAlpha = 0;

    internal static FalloutPlacedLight Resolve(
        FalloutPlacedReference reference,
        FalloutBaseObjectDefinition baseObject,
        FalloutPluginStack? records = null,
        Func<FalloutFormKey, float[]>? regionEmittance = null)
    {
        if (baseObject.Signature != "LIGH" || baseObject.Light is not { } source)
            throw new InvalidDataException(
                $"Native light reference {reference.FormKey} does not target decoded LIGH data.");
        if (reference.Base != baseObject.FormKey)
            throw new InvalidDataException(
                $"Native light reference {reference.FormKey} base identity differs.");
        if (reference.Scale != 1.0f)
            throw new NotSupportedException(
                $"Native light reference {reference.FormKey} has unsupported XSCL {reference.Scale:R}.");
        if (reference.EnableParent is not null)
            throw new NotSupportedException(
                $"Native light reference {reference.FormKey} has an unresolved enable parent.");
        if (source.Duration != StaticDuration || source.Flags != StaticPointFlags ||
            source.Falloff != StaticFalloff ||
            source.FieldOfViewDegrees != StaticFieldOfViewDegrees ||
            source.NearClip != StaticNearClip || source.Period != StaticPeriod ||
            source.ColorAlpha != StaticColorAlpha)
            throw new NotSupportedException(
                $"Native LIGH {baseObject.FormKey} is outside the evidenced static point-light contract: " +
                $"duration={source.Duration} flags=0x{source.Flags:x8} falloff={source.Falloff:R} " +
                $"fov={source.FieldOfViewDegrees:R} near={source.NearClip} period={source.Period:R} " +
                $"alpha={source.ColorAlpha}.");
        if (!float.IsFinite(source.Intensity) || source.Intensity <= 0.0f)
            throw new InvalidDataException(
                $"Native LIGH {baseObject.FormKey} has invalid intensity {source.Intensity:R}.");
        var adjustment = reference.RadiusAdjustmentGameUnits ?? 0.0f;
        // In the admitted Doc Mitchell corpus every base radius is 200, while XRDS is
        // signed (including ten negative values). Treating XRDS as an absolute radius
        // would be invalid, so this lane admits the evidenced additive override only.
        var radius = source.RadiusGameUnits + adjustment;
        if (!float.IsFinite(adjustment) || !float.IsFinite(radius) || radius <= 0.0f)
            throw new InvalidDataException(
                $"Native light reference {reference.FormKey} has invalid effective radius {radius:R}.");
        float[]? emittance = null;
        if (reference.Emittance is { } form)
        {
            if (records is null) throw new InvalidDataException($"Light {reference.FormKey} has no XEMI source resolver.");
            emittance = new FalloutExternalEmittance(records, form, regionEmittance).Sample();
        }
        return new FalloutPlacedLight(reference.FormKey, baseObject.FormKey, radius, source.ColorRgb,
            source.Intensity, ModulateColor(source.ColorRgb, emittance), reference.Emittance);
    }

    // REFR.XEMI modulates the base light's encoded RGB. The emittance record's
    // radius and dimmer do not replace or multiply the placed light's values.
    internal static float[] ComposeColor(IReadOnlyList<byte> source, IReadOnlyList<byte>? emittance)
        => ModulateColor(source, emittance is null ? null : NormalizeLightColor(emittance));

    internal static float[] NormalizeLightColor(IReadOnlyList<byte> color) => ModulateColor(color, null);

    internal static float[] ModulateColor(IReadOnlyList<byte> source, IReadOnlyList<float>? emittance)
    {
        if (source.Count != 3 || emittance is not null && emittance.Count != 3)
            throw new InvalidDataException("Light RGB requires three source channels.");
        if (emittance is not null && emittance.Any(value => !float.IsFinite(value) || value < 0))
            throw new InvalidDataException("Light emittance is non-finite or negative.");
        // The light colour conversion retains the engine's Float32 reciprocal
        // and extended multiplication until the final channel store.
        const float reciprocal = 1.0f / byte.MaxValue;
        return Enumerable.Range(0, 3).Select(index => (float)(source[index] * (double)reciprocal *
            (emittance is null ? 1.0f : emittance[index]))).ToArray();
    }
}
