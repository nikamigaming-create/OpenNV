namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlacedLight(
    FalloutFormKey Reference,
    FalloutFormKey Base,
    float RadiusGameUnits,
    byte[] ColorRgb,
    float Intensity);

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
        FalloutBaseObjectDefinition baseObject)
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
        return new FalloutPlacedLight(
            reference.FormKey, baseObject.FormKey, radius, source.ColorRgb, source.Intensity);
    }
}
