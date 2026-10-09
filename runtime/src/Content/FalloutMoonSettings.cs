namespace OpenNV.Runtime.Content;

internal sealed record FalloutMoonSettings(FalloutMoonRole Role, uint FadeStartBits, uint FadeEndBits,
    uint ShadowEarlyBits, uint SpeedBits, uint ZOffsetBits, uint Size)
{
    internal float FadeStart => BitConverter.UInt32BitsToSingle(FadeStartBits);
    internal float FadeEnd => BitConverter.UInt32BitsToSingle(FadeEndBits);
    internal float ShadowEarly => BitConverter.UInt32BitsToSingle(ShadowEarlyBits);
    internal float Speed => BitConverter.UInt32BitsToSingle(SpeedBits);
    internal float ZOffset => BitConverter.UInt32BitsToSingle(ZOffsetBits);
    internal static FalloutMoonSettings Read(FalloutPluginStack records, FalloutMoonRole role)
    {
        if (!Enum.IsDefined(role)) throw new InvalidDataException("Moon settings requested a foreign source role.");
        var owner = nameof(FalloutMoonSettings) + "/" + role;
        uint Float(string suffix) => BitConverter.SingleToUInt32Bits(
            FalloutGameSettingFloats.ReadRetained(records, "f" + role + suffix, owner));
        var result = new FalloutMoonSettings(role, Float("AngleFadeStart"), Float("AngleFadeEnd"),
            Float("AngleShadowEarlyFade"), Float("Speed"), Float("ZOffset"),
            FalloutGameSettingIntegers.ReadRetained(records, "i" + role + "Size", owner));
        result.Validate(); return result;
    }
    internal void Validate()
    {
        if (!Enum.IsDefined(Role) || new[] { FadeStart, FadeEnd, ShadowEarly, Speed, ZOffset }.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Moon constructor has a non-finite or foreign GMST payload.");
    }
}
