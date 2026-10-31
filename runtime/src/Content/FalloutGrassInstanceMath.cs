using System.Numerics;

namespace OpenNV.Runtime.Content;

// Source-space placement and appearance math.
// Inputs are owned GRAS fields, qualified LAND samples and authoritative scene state.
// No retail observation is a gameplay input. No per-cell placement seed is introduced.
internal static class FalloutGrassInstanceMath
{
    internal readonly record struct Appearance(Vector4 Packed, uint PositionSeed);
    internal readonly record struct Wind(Vector2 Direction, float Magnitude, float Phase);

    // The owned D3D9 creation path does not request FPU_PRESERVE: source default
    // CPU arithmetic is single-precision, nearest/even. See density-rounding report.
    internal static float SignedPlacementDraw(FalloutSharedUnsignedRandom random) =>
        (float)(random.NextBounded(uint.MaxValue) * (1.0 / 2147483648.0)) - 1f;

    internal static Appearance Pack(Vector3 sourcePosition, Vector3 selectedSourceNormal,
        float heightRange, float colorRange, float terrainLuminance)
    {
        RequireFinite(sourcePosition);
        RequireFinite(selectedSourceNormal);
        if (!float.IsFinite(heightRange) || !float.IsFinite(colorRange) || !float.IsFinite(terrainLuminance))
            throw new InvalidDataException("Grass appearance source is non-finite.");
        // Preserve single-precision addition in source units. At large world coordinates,
        // its quantization affects the normal subsequently recovered by the shader.
        var packedPosition = PackPosition(sourcePosition, selectedSourceNormal);
        var seed = unchecked((uint)((int)MathF.Floor(packedPosition.X) << 16)) |
            (unchecked((uint)(int)MathF.Floor(packedPosition.Y)) & 0xffffu);
        var state = seed;
        var colorDraw = NextAppearanceDraw(ref state);
        var colorVariation = colorDraw / 65534f - 1f;
        var shade = (1f - colorRange + colorRange * .5f * colorVariation) * terrainLuminance;
        if (shade >= 1) shade = .99f;
        else if (shade < 0) shade = 0;
        var heightDraw = NextAppearanceDraw(ref state);
        var heightVariation = heightDraw / 32767f * 2f - 1f;
        var variation = heightVariation * heightRange * 100f;
        return new(new Vector4(packedPosition, MathF.Floor(variation) + shade), seed);
    }

    internal static Vector3 PackPosition(Vector3 sourcePosition, Vector3 normal) =>
        new Vector3(MathF.Floor(sourcePosition.X), MathF.Floor(sourcePosition.Y), MathF.Floor(sourcePosition.Z)) +
        new Vector3(EncodeNormal(normal.X), EncodeNormal(normal.Y), EncodeNormal(normal.Z));

    internal static Vector3 DecodeNormal(Vector4 packed) => new(
        (Fraction(packed.X) - .5f) * 2,
        (Fraction(packed.Y) - .5f) * 2,
        (Fraction(packed.Z) - .5f) * 2);

    internal static float Shade(Vector4 packed) => Fraction(packed.W) * .75f + .25f;

    // The inspected shader uses the full packed W, including its shade fraction.
    internal static Vector3 Scale(Vector4 packed, bool uniformScale)
    {
        var amount = packed.W * .01f;
        return new(uniformScale ? 1 + amount : 1, uniformScale ? 1 + amount : 1, 1 + amount);
    }

    internal static Vector3 TransformVertex(Vector3 authoredVertex, float authoredAlpha,
        Vector4 packed, bool uniformScale, bool fitSlope, Wind wind)
    {
        var point = authoredVertex * Scale(packed, uniformScale);
        if (fitSlope)
        {
            var normal = DecodeNormal(packed);
            var tangent = MathF.Abs(normal.Y) >= MathF.Abs(normal.X) && MathF.Abs(normal.Z) >= MathF.Abs(normal.X)
                ? new Vector3(0, -normal.Z, normal.Y)
                : new Vector3(-normal.Z, 0, normal.X);
            if (tangent.LengthSquared() == 0)
                throw new InvalidDataException("Grass slope basis is degenerate.");
            tangent = Vector3.Normalize(tangent);
            var across = Vector3.Cross(tangent, normal);
            point = across * point.X + tangent * point.Y + normal * point.Z;
        }
        var angle = ((packed.X + packed.Y) * .0078125f + wind.Phase) * .159154937f + .5f;
        angle = Fraction(angle) * 6.28318548f - 3.14159274f;
        var bend = MathF.Sin(angle) * wind.Magnitude * authoredAlpha * authoredAlpha;
        point += new Vector3(wind.Direction * bend, 0);
        return point + new Vector3(packed.X, packed.Y, packed.Z);
    }

    internal static float GameHourPhase(float gameHour, float sourceWavePeriod)
    {
        if (!float.IsFinite(gameHour) || !float.IsFinite(sourceWavePeriod))
            throw new InvalidDataException("Grass wind clock source is non-finite.");
        // Native shader time slot zero is GameHour*60*60. This owner must pause
        // with the authoritative game clock, not Godot TIME or wall-clock time.
        var seconds = gameHour * 60f * 60f;
        const float sourceTau = 6.283180236816406f;
        var phase = seconds / 3600f * sourceTau * sourceWavePeriod;
        return phase >= sourceTau ? phase % sourceTau : phase;
    }

    internal static float TerrainLuminance(Vector3 sourceColor)
    {
        // Preserve each source product's single-precision rounding while the
        // coefficients retain the source double-constant values.
        var red = (float)(sourceColor.X * .31);
        var green = (float)(sourceColor.Y * .37);
        var blue = (float)(sourceColor.Z * .32);
        return red + green + blue;
    }

    private static float EncodeNormal(float value) => Math.Min(.97f, value * .5f + .5f);
    private static float Fraction(float value) => value - MathF.Floor(value);
    private static int NextAppearanceDraw(ref uint state)
    {
        state = unchecked(state * 214013u + 2531011u);
        return (int)((state >> 16) & 0x7fff);
    }
    private static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException("Grass vector source is non-finite.");
    }
}
