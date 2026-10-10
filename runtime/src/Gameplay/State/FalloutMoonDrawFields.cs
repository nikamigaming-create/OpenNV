using System.Numerics;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutCelestialRgb(uint RedBits, uint GreenBits, uint BlueBits)
{
    internal static FalloutCelestialRgb Read(Vector3 value) => new(BitConverter.SingleToUInt32Bits(value.X),
        BitConverter.SingleToUInt32Bits(value.Y), BitConverter.SingleToUInt32Bits(value.Z));
    internal Vector3 Value => new(BitConverter.UInt32BitsToSingle(RedBits), BitConverter.UInt32BitsToSingle(GreenBits),
        BitConverter.UInt32BitsToSingle(BlueBits));
    internal void Validate()
    {
        if (!float.IsFinite(Value.X) || !float.IsFinite(Value.Y) || !float.IsFinite(Value.Z))
            throw new InvalidDataException("Celestial RGB lost its actual finite Float32 source fields.");
    }
    internal static FalloutCelestialRgb White => Read(Vector3.One);
}

// Source rows are retained, not a serialised Godot Basis or native address. The
// coordinate bridge is applied only by the actual scene owner.
internal sealed record FalloutCelestialMatrix(uint M00, uint M01, uint M02, uint M10, uint M11, uint M12,
    uint M20, uint M21, uint M22)
{
    internal uint[] Words => [M00, M01, M02, M10, M11, M12, M20, M21, M22];
    internal static FalloutCelestialMatrix Read(params float[] values)
    {
        if (values.Length != 9 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Celestial rotation omitted its finite source matrix rows.");
        var bits = values.Select(BitConverter.SingleToUInt32Bits).ToArray();
        return new(bits[0], bits[1], bits[2], bits[3], bits[4], bits[5], bits[6], bits[7], bits[8]);
    }
    internal void Validate()
    {
        if (Words.Any(word => !float.IsFinite(BitConverter.UInt32BitsToSingle(word))))
            throw new InvalidDataException("Celestial rotation retained a non-finite source store.");
    }
    internal static FalloutCelestialMatrix Identity => Read(1, 0, 0, 0, 1, 0, 0, 0, 1);
}

internal sealed record FalloutMoonProgramIdentity(int Role, string SourceContract, string PackagePath,
    string ResourceSource, string PackageSha256, string VertexName, string VertexSha256,
    string PixelName, string PixelSha256, uint DepthWord, string Identity);

// This is a returned read of the distinct source shader field, not an INI HDR
// choice, a renderer registration, captured exposure or logical colour scalar.
// No producer is inferred where the selected original writer is not owned.
internal sealed record FalloutMoonShaderFieldSample(string EngineSha256, string SourceContract, Guid Sky,
    Guid Process, Guid Frame, long Changed, bool HdrEnabled, uint RgbMultiplierBits, FalloutSkyHdrShaderRead? HdrRead = null);

internal sealed record FalloutMoonNativeFrameFields(uint AngleBits, uint PrimaryFadeBits, uint ShadowFadeBits,
    uint NightAlphaBits, FalloutCelestialRgb UpperSkyColor, int Mode, FalloutMoonShaderFieldSample? ShaderField,
    bool PrimaryHasTexture, bool ShadowHasTexture, bool SourceFieldsReturned, bool ShaderInputsReturned);
internal sealed record FalloutMoonNativeDrawFields(FalloutMoonProgramIdentity PrimaryProgram,
    FalloutMoonProgramIdentity ShadowProgram, FalloutCelestialMatrix ParentMatrix,
    FalloutCelestialRgb PrimaryColor, FalloutCelestialRgb ShadowColor,
    bool PrimaryDrawAdmitted, bool ShadowDrawAdmitted, FalloutMoonNativeFrameFields? LastFrame,
    FalloutMoonNativeFrameFields? PrimaryWrite, FalloutMoonNativeFrameFields? ShadowWrite,
    FalloutMoonNativeFrameFields? MatrixWrite, bool ColdAwaitingFrame);

internal static class FalloutMoonDrawMath
{
    internal static FalloutCelestialMatrix ParentRotation(FalloutMoonSource source, float angle, float zOffset)
    {
        if (!float.IsFinite(angle) || !float.IsFinite(zOffset)) throw new InvalidDataException("Moon rotation lost its live source angles.");
        // FO3 stores the two products before FSINCOS. FNV's independently
        // selected consumers multiply by the original double, store each input
        // angle, then store returned sine/cosine in Float32 matrix fields.
        const double degrees = 0.017453292519943295;
        var x = source.IsStandalone ? angle * -0.01745329238474369f : (float)(angle * -degrees);
        var z = source.IsStandalone ? zOffset * 0.01745329238474369f : (float)(zOffset * degrees);
        if (Math.Abs((double)x) >= 9223372036854775808.0 || Math.Abs((double)z) >= 9223372036854775808.0)
            throw new NotSupportedException("source-Moon-FSINCOS-range-status-arm-unowned");
        var sinX = (float)Math.Sin(x); var cosX = (float)Math.Cos(x);
        var sinZ = (float)Math.Sin(z); var cosZ = (float)Math.Cos(z);
        // The selected standalone writer retains its zero products and ordered
        // adds. This keeps signed-zero stores instead of algebraically dropping
        // the source identity rows. FNV's matrix child stores each final cell.
        if (source.IsStandalone)
            return FalloutCelestialMatrix.Read(((-sinZ * 0f) + cosZ) + 0f, ((cosZ * 0f) + sinZ) + 0f, 0f,
                ((-sinZ * cosX) + (cosZ * 0f)) + (sinX * 0f),
                ((cosZ * cosX) + (sinZ * 0f)) + (sinX * 0f), ((cosX * 0f) + 0f) + sinX,
                ((-sinZ * -sinX) + (cosZ * 0f)) + (cosX * 0f),
                ((-sinX * cosZ) + (sinZ * 0f)) + (cosX * 0f), ((-sinX * 0f) + 0f) + cosX);
        float Sum(float a, float b, float c, float d, float e, float f) => (float)((double)a * b + (double)c * d + (double)e * f);
        return FalloutCelestialMatrix.Read(Sum(cosZ, 1, -sinZ, 0, 0, 0), Sum(sinZ, 1, cosZ, 0, 0, 0), 0,
            Sum(-sinZ, cosX, cosZ, 0, sinX, 0), Sum(cosZ, cosX, sinZ, 0, sinX, 0), Sum(cosX, 0, 0, 0, sinX, 1),
            Sum(-sinZ, -sinX, cosZ, 0, cosX, 0), Sum(-sinX, cosZ, sinZ, 0, cosX, 0), Sum(-sinX, 0, 0, 0, cosX, 1));
    }
    internal static void RequireShaderRead(FalloutMoonShaderFieldSample? sample, FalloutMoonSource source,
        Guid sky, Guid process)
    {
        if (sample is null) throw new NotSupportedException("source-Sky-HDR-conditioned-shader-field-current-writer-unowned");
        if (sample.EngineSha256 != source.EngineSha256 || sample.SourceContract != source.SkyContract ||
            sample.Sky != sky || sample.Process != process || sample.Frame == Guid.Empty || sample.Changed < 1 ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(sample.RgbMultiplierBits)))
            throw new InvalidDataException("Moon draw borrowed another source/frame/HDR shader-field owner.");
        // Disabled HDR preserves the shader's prior field; it does not write
        // a zero/one fallback. That retained field needs its own real producer.
        if (sample.HdrRead is not null) FalloutSkyHdrState.RequireReturnedShaderRead(sample);
        else if (!sample.HdrEnabled) throw new NotSupportedException("source-Sky-HDR-disabled-retained-shader-field-writer-unowned");
    }
}
