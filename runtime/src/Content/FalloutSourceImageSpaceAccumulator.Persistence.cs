using System.Numerics;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageSpaceAccumulatorSnapshot(uint[] Profile, uint[] Multiply, uint[] Add,
    uint[] Colors, uint[] Scalars, uint[] Radial, uint OptionMode, uint OptionBatchMode, bool OptionAlternate, bool OptionBatchAlternate,
    bool ResetCompleted, [property: JsonRequired] ulong DefinedWords);

internal sealed partial class FalloutSourceImageSpaceAccumulator
{
    internal FalloutSourceImageSpaceAccumulatorSnapshot Capture()
    {
        static uint[] Bits(params float[] values) => values.Select(BitConverter.SingleToUInt32Bits).ToArray();
        return new((uint[])Profile.Clone(), Bits(Multiply), Bits(Add),
            Bits(Tint.X, Tint.Y, Tint.Z, Tint.W, TintBatch.X, TintBatch.Y, TintBatch.Z, TintBatch.W,
                Fade.X, Fade.Y, Fade.Z, Fade.W, FadeBatch.X, FadeBatch.Y, FadeBatch.Z, FadeBatch.W),
            Bits(StrongestTint, StrongestFade, Blur, BlurBatch, DoubleVision, DoubleVisionBatch,
                RadialStrength, RadialBatchStrength, OptionStrength, OptionBatchStrength, OptionFirst, OptionBatchFirst,
                OptionSecond, OptionBatchSecond, OptionThird, OptionBatchThird, Scalar, ScalarBatch, DofStrength),
            Bits(RadialParameters.X, RadialParameters.Y, RadialParameters.Z, RadialParameters.W,
                RadialBatchParameters.X, RadialBatchParameters.Y, RadialBatchParameters.Z, RadialBatchParameters.W,
                RadialCenter.X, RadialCenter.Y, RadialBatchCenter.X, RadialBatchCenter.Y),
            OptionMode, OptionBatchMode, OptionAlternate, OptionBatchAlternate, ResetCompleted, DefinedWords);
    }
    internal static void Validate(FalloutSourceImageSpaceAccumulatorSnapshot value)
    {
        if (value.Profile.Length != 38 || value.Multiply.Length != 38 || value.Add.Length != 38 ||
            value.Colors.Length != 16 || value.Scalars.Length != 19 || value.Radial.Length != 12)
            throw new InvalidDataException("Source image accumulators lost their exact field extents.");
        FalloutSourceImageSpaceProfile.ValidateDefinedWords(value.Profile, value.DefinedWords);
    }
    internal void Restore(FalloutSourceImageSpaceAccumulatorSnapshot value)
    {
        Validate(value); Profile = (uint[])value.Profile.Clone(); ResetCompleted = value.ResetCompleted; DefinedWords = value.DefinedWords;
        for (var index = 0; index < 38; index++)
        { Multiply[index] = BitConverter.UInt32BitsToSingle(value.Multiply[index]); Add[index] = BitConverter.UInt32BitsToSingle(value.Add[index]); }
        var colors = value.Colors.Select(BitConverter.UInt32BitsToSingle).ToArray();
        static Vector4 Vector(float[] input, int at) => new(input[at], input[at + 1], input[at + 2], input[at + 3]);
        Tint = Vector(colors, 0); TintBatch = Vector(colors, 4); Fade = Vector(colors, 8); FadeBatch = Vector(colors, 12);
        var scalars = value.Scalars.Select(BitConverter.UInt32BitsToSingle).ToArray();
        (StrongestTint, StrongestFade, Blur, BlurBatch, DoubleVision, DoubleVisionBatch, RadialStrength, RadialBatchStrength,
            OptionStrength, OptionBatchStrength, OptionFirst, OptionBatchFirst, OptionSecond, OptionBatchSecond,
            OptionThird, OptionBatchThird, Scalar, ScalarBatch, DofStrength) =
            (scalars[0], scalars[1], scalars[2], scalars[3], scalars[4], scalars[5], scalars[6], scalars[7], scalars[8],
             scalars[9], scalars[10], scalars[11], scalars[12], scalars[13], scalars[14], scalars[15], scalars[16], scalars[17], scalars[18]);
        var radial = value.Radial.Select(BitConverter.UInt32BitsToSingle).ToArray();
        RadialParameters = Vector(radial, 0); RadialBatchParameters = Vector(radial, 4);
        RadialCenter = new(radial[8], radial[9]); RadialBatchCenter = new(radial[10], radial[11]);
        OptionMode = value.OptionMode; OptionBatchMode = value.OptionBatchMode;
        OptionAlternate = value.OptionAlternate; OptionBatchAlternate = value.OptionBatchAlternate;
    }
}
