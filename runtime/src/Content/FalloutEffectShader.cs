using System.Buffers.Binary;
using System.Numerics;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutEffectShaderBlend(uint Source, uint Operation, uint DepthTest, uint Destination);
internal readonly record struct FalloutEffectShaderEnvelope(float FadeIn, float FullTime, float FadeOut, float Persistent, float Full);
internal readonly record struct FalloutEffectShaderPulse(float Amplitude, float Frequency);
internal readonly record struct FalloutEffectShaderColor(byte Red, byte Green, byte Blue, byte Reserved);
internal readonly record struct FalloutEffectShaderColorKey(FalloutEffectShaderColor Color, float Alpha, float Time);
internal readonly record struct FalloutEffectShaderScaleKey(float Scale, float Time);
internal sealed record FalloutEffectShaderMembrane(FalloutEffectShaderBlend Blend, FalloutEffectShaderColor FillColor,
    FalloutEffectShaderEnvelope FillAlpha, FalloutEffectShaderPulse FillPulse, Vector2 TextureScroll, float EdgeFalloff,
    FalloutEffectShaderColor EdgeColor, FalloutEffectShaderEnvelope EdgeAlpha, FalloutEffectShaderPulse EdgePulse);
internal sealed record FalloutEffectShaderParticles(FalloutEffectShaderBlend Blend, FalloutEffectShaderEnvelope Birth,
    float Lifetime, float LifetimeVariation, float NormalSpeed, float NormalAcceleration, Vector3 Velocity, Vector3 Acceleration,
    IReadOnlyList<FalloutEffectShaderScaleKey> ScaleKeys, IReadOnlyList<FalloutEffectShaderColorKey> ColorKeys,
    float NormalSpeedVariation, float? Rotation, float? RotationVariation, float? RotationSpeed, float? RotationSpeedVariation);
internal sealed record FalloutEffectShaderHoles(float? StartTime, float? EndTime, float? StartValue, float? EndValue,
    float? EdgeWidth, FalloutEffectShaderColor? EdgeColor);
internal sealed record FalloutEffectShaderAddons(FalloutFormKey? Debris, float? FadeIn, float? FadeOut,
    float? ScaleStart, float? ScaleEnd, float? ScaleInTime, float? ScaleOutTime);

// Source metadata is independent of renderer admission. Missing optional
// declarations remain absent instead of inheriting invented renderer defaults.
internal sealed record FalloutEffectShader(FalloutFormKey Form, byte Flags, string? FillTexture, string? ParticleTexture,
    string? HolesTexture, FalloutEffectShaderMembrane Membrane, FalloutEffectShaderParticles Particles,
    FalloutEffectShaderHoles Holes, FalloutEffectShaderAddons Addons, float? ExplosionWindSpeed,
    uint? TextureColumns, uint? TextureRows, int DataBytes)
{
    internal bool HasMembrane => (Flags & 1) == 0;
    internal bool HasParticles => (Flags & 8) == 0;
    internal bool InverseEdge => (Flags & 16) != 0;
    internal bool SkinOnly => (Flags & 32) != 0;

    internal static FalloutEffectShader Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature != "EFSH") throw new InvalidDataException($"{form} is not EFSH.");
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Where(field => field.Signature == "DATA").ToArray();
        // FNV DATA uses 57 mandatory words followed by a four-byte aligned
        // optional tail. Owned pulse/fire sources include 284, 300 and 308 bytes.
        if (data.Length != 1 || data[0].Data.Length is < 228 or > 308 || data[0].Data.Length % 4 != 0)
            throw new NotSupportedException($"Effect shader {form} has an unbound DATA layout.");
        var bytes = data[0].Data;
        uint Word(int index) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[(index * 4)..]);
        uint? OptionalWord(int index) => index * 4 < bytes.Length ? Word(index) : null;
        float Value(int index)
        {
            var number = BitConverter.UInt32BitsToSingle(Word(index));
            if (!float.IsFinite(number)) throw new InvalidDataException($"Effect shader {form} contains a non-finite field at {index * 4}.");
            return number;
        }
        float? OptionalValue(int index) => index * 4 < bytes.Length ? Value(index) : null;
        FalloutEffectShaderColor Color(int index)
        {
            var color = bytes.Span.Slice(index * 4, 4);
            return new(color[0], color[1], color[2], color[3]);
        }
        string? Texture(string signature)
        {
            var paths = fields.Where(field => field.Signature == signature).ToArray();
            if (paths.Length == 0) return null;
            if (paths.Length != 1 || paths[0].Data.Length == 0 || paths[0].Data.Span[^1] != 0 ||
                paths[0].Data.Span[..^1].Contains((byte)0))
                throw new InvalidDataException($"Effect shader {form} has an invalid {signature} texture declaration.");
            var text = FalloutDialogueTopic.Text(paths[0].Data.Span).Replace('/', '\\');
            if (text.Length == 0) return null;
            if (text.Split('\\').Any(segment => segment is "" or "." or ".." || segment.Contains(':')) ||
                !text.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Effect shader {form} has an unbound {signature} texture path.");
            return (text.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) ? text : "textures\\" + text).ToLowerInvariant();
        }

        var flags = bytes.Span[0];
        var debris = OptionalWord(61) is { } raw ? record.Plugin.AdjustOptionalFormId(raw) : null;
        if (debris is { } addon && records.GetEffective(addon).Signature != "DEBR")
            throw new InvalidDataException($"Effect shader {form} add-on {addon} is not DEBR.");
        return new(form, flags, Texture("ICON"), Texture("ICO2"), Texture("NAM7"),
            new(new(Word(1), Word(2), Word(3), Word(23)), Color(4), new(Value(5), Value(6), Value(7), Value(8), Value(21)),
                new(Value(9), Value(10)), new(Value(11), Value(12)), Value(13), Color(14),
                new(Value(15), Value(16), Value(17), Value(18), Value(22)), new(Value(19), Value(20))),
            new(new(Word(24), Word(25), Word(26), Word(27)), new(Value(28), Value(29), Value(30), Value(32), Value(31)),
                Value(33), Value(34), Value(35), Value(36), new(Value(37), Value(38), Value(39)), new(Value(40), Value(41), Value(42)),
                [new(Value(43), Value(45)), new(Value(44), Value(46))],
                [new(Color(47), Value(50), Value(53)), new(Color(48), Value(51), Value(54)), new(Color(49), Value(52), Value(55))],
                Value(56), OptionalValue(57), OptionalValue(58), OptionalValue(59), OptionalValue(60)),
            new(OptionalValue(62), OptionalValue(63), OptionalValue(64), OptionalValue(65), OptionalValue(66),
                OptionalWord(67) is not null ? Color(67) : null),
            new(debris, OptionalValue(71), OptionalValue(72), OptionalValue(73), OptionalValue(74), OptionalValue(75), OptionalValue(76)),
            OptionalValue(68), OptionalWord(69), OptionalWord(70), bytes.Length);
    }
}
