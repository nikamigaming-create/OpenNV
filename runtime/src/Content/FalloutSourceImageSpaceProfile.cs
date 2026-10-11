using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageSpaceProfile(FalloutFormKey? Form, string Source, uint[] Words, ushort? FormVersion = null,
    [property: JsonRequired] ulong DefinedWords = FalloutSourceImageSpaceProfile.CompleteWords,
    [property: JsonRequired] int SourceByteLength = 152)
{
    internal const ulong CompleteWords = (1UL << 38) - 1;
    private const ulong ShortSourceWords = (1UL << 34) - 1;
    internal FalloutSourceImageSpaceProfile CloneProfile() => this with { Words = (uint[])Words.Clone() };
    internal void Validate()
    {
        if (Words.Length != 38 || Source.Length != 64 || !Source.All(Uri.IsHexDigit) || (Form is null) != (FormVersion is null))
            throw new InvalidDataException("Source image-space profile lost its exact native extent or source.");
        ValidateDefinedWords(Words, DefinedWords);
        if (SourceByteLength is not (132 or 148 or 152) || Form is null && SourceByteLength != 152 ||
            SourceByteLength != 132 && DefinedWords != CompleteWords || SourceByteLength == 132 &&
            ((DefinedWords & ShortSourceWords) != ShortSourceWords || (DefinedWords & (1UL << 37)) != 0))
            throw new InvalidDataException("Source image-space profile invented unwritten native cells.");
    }
    internal static void ValidateDefinedWords(uint[] words, ulong defined)
    {
        if (words.Length != 38 || (defined & ~CompleteWords) != 0 ||
            Enumerable.Range(0, 38).Any(index => (defined & (1UL << index)) == 0 && words[index] != 0))
            throw new InvalidDataException("Source image-space profile lost its defined-cell boundary.");
    }
    internal uint ReadWord(int index)
    {
        if ((uint)index >= 38) throw new ArgumentOutOfRangeException(nameof(index));
        if ((DefinedWords & (1UL << index)) == 0)
            throw new NotSupportedException($"Source image-space consumer reached unwritten profile cell {index}.");
        return Words[index];
    }
    internal static FalloutSourceImageSpaceProfile Anonymous(FalloutSourceImageSpaceDefaults source)
    { var result = new FalloutSourceImageSpaceProfile(null, source.DeclarationSha256, (uint[])source.Words.Clone()); result.Validate(); return result; }
    internal static FalloutSourceImageSpaceProfile Winning(FalloutImageSpace source)
    {
        var bytes = source.RawTraits.SelectMany(BitConverter.GetBytes).Concat(source.ReservedData).ToArray();
        var words = new uint[38];
        if (bytes.Length == 152)
            for (var index = 0; index < 38; index++) words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
        else if (bytes.Length is 132 or 148)
        {
            for (var index = 0; index < 14; index++) words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
            words[14] = BitConverter.SingleToUInt32Bits(1);
            for (var index = 14; index < bytes.Length / 4; index++) words[index + 1] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
        }
        else throw new NotSupportedException("Source image-space load has an unsupported native extent.");
        // Short reads leave four stack cells unwritten. Their storage is opaque
        // until a real writer defines it; zero-filled C# storage is not a value.
        var result = new FalloutSourceImageSpaceProfile(source.Form, source.DnamSha256, words, source.FormVersion,
            bytes.Length == 132 ? ShortSourceWords : CompleteWords, bytes.Length);
        result.Validate(); return result;
    }
}

// Separate operations prevent an algebraic rewrite or fused multiply/add from
// changing the selected SSE stores. Values are raw IEEE Float32, not sanitized
// rendering parameters. NaN/unordered predicates follow the source branch.
internal static class FalloutSourceImageSpaceFloat
{
    [MethodImpl(MethodImplOptions.NoInlining)] internal static float Add(float left, float right) => left + right;
    [MethodImpl(MethodImplOptions.NoInlining)] internal static float Subtract(float left, float right) => left - right;
    [MethodImpl(MethodImplOptions.NoInlining)] internal static float Multiply(float left, float right) => left * right;
    [MethodImpl(MethodImplOptions.NoInlining)] internal static float Divide(float left, float right) => left / right;
    internal static float Apply(float before, float multiplier, float add, float weight)
    {
        var multiplied = Multiply(multiplier, before);
        var added = Add(add, multiplied);
        var difference = Subtract(before, added);
        return Subtract(before, Multiply(difference, weight));
    }
    internal static float Batch(float before, float multiplier, float add)
    {
        var multiplied = Multiply(multiplier, before);
        var added = Add(add, multiplied);
        return Subtract(before, Subtract(before, added));
    }
    internal static bool Retire(bool animated, float start, float now, float duration, float unsetBoundary) =>
        animated && !(unsetBoundary > start) && Subtract(now, start) > duration;
}

internal sealed partial class FalloutSourceImageSpaceAccumulator
{
    internal bool ResetCompleted { get; private set; }
    internal uint[] Profile { get; private set; } = new uint[38];
    internal ulong DefinedWords { get; private set; } = FalloutSourceImageSpaceProfile.CompleteWords;
    internal float[] Multiply { get; } = new float[38];
    internal float[] Add { get; } = new float[38];
    internal Vector4 Tint, TintBatch, Fade, FadeBatch;
    internal float StrongestTint, StrongestFade, Blur, BlurBatch, DoubleVision, DoubleVisionBatch;
    internal Vector4 RadialParameters, RadialBatchParameters;
    internal Vector2 RadialCenter, RadialBatchCenter;
    internal float RadialStrength, RadialBatchStrength;
    internal float OptionStrength, OptionBatchStrength, OptionFirst, OptionBatchFirst, OptionSecond, OptionBatchSecond;
    internal float OptionThird, OptionBatchThird, Scalar, ScalarBatch, DofStrength;
    internal uint OptionMode, OptionBatchMode;
    internal bool OptionAlternate, OptionBatchAlternate;

    internal void CopyProfile(FalloutSourceImageSpaceProfile source)
    { source.Validate(); Profile = (uint[])source.Words.Clone(); DefinedWords = source.DefinedWords; }
    private bool IsDefined(int index) => (DefinedWords & (1UL << index)) != 0;
    internal float Read(int index) => IsDefined(index) ? BitConverter.UInt32BitsToSingle(Profile[index]) :
        throw new NotSupportedException($"Source image-space accumulator reached unwritten profile cell {index}.");
    internal void Store(int index, float value)
    { Profile[index] = BitConverter.SingleToUInt32Bits(value); DefinedWords |= 1UL << index; }

    internal void Reset()
    {
        // The source copies all 38 profile words first, then resets the entire
        // accumulator range, including the unused profile operand lanes.
        var weight = Read(32); StrongestTint = weight;
        Tint = new(FalloutSourceImageSpaceFloat.Multiply(weight, Read(29)),
            FalloutSourceImageSpaceFloat.Multiply(weight, Read(30)), FalloutSourceImageSpaceFloat.Multiply(weight, Read(31)), weight);
        TintBatch = Fade = FadeBatch = Vector4.Zero; StrongestFade = 0;
        Array.Clear(Multiply); Array.Clear(Add);
        Blur = BlurBatch = DoubleVision = DoubleVisionBatch = 0;
        RadialParameters = RadialBatchParameters = Vector4.Zero; RadialCenter = RadialBatchCenter = Vector2.Zero;
        RadialStrength = RadialBatchStrength = 0;
        OptionStrength = OptionBatchStrength = OptionFirst = OptionBatchFirst = OptionSecond = OptionBatchSecond = 0;
        OptionThird = OptionBatchThird = Scalar = ScalarBatch = DofStrength = 0;
        OptionMode = OptionBatchMode = 3; OptionAlternate = OptionBatchAlternate = false;
        ResetCompleted = true;
    }
    internal void NoActiveBatch()
    {
        foreach (var index in ScalarProfileIndices()) { Multiply[index] = 1; Add[index] = 0; }
    }
    internal static IEnumerable<int> ScalarProfileIndices() => Enumerable.Range(0, 18).Concat(Enumerable.Range(25, 12));
    internal void ApplyScalar(int index, float multiplier, float add, float weight, bool batch)
    {
        if (batch)
        {
            Multiply[index] = FalloutSourceImageSpaceFloat.Add(FalloutSourceImageSpaceFloat.Multiply(weight, multiplier), Multiply[index]);
            Add[index] = FalloutSourceImageSpaceFloat.Add(FalloutSourceImageSpaceFloat.Multiply(weight, add), Add[index]);
        }
        else if (IsDefined(index)) Store(index, FalloutSourceImageSpaceFloat.Apply(Read(index), multiplier, add, weight));
    }
    private static Vector4 ComponentAdd(Vector4 left, Vector4 right) => new(
        FalloutSourceImageSpaceFloat.Add(left.X, right.X), FalloutSourceImageSpaceFloat.Add(left.Y, right.Y),
        FalloutSourceImageSpaceFloat.Add(left.Z, right.Z), FalloutSourceImageSpaceFloat.Add(left.W, right.W));
    private static Vector4 Premultiply(Vector4 value) => new(
        FalloutSourceImageSpaceFloat.Multiply(value.W, value.X), FalloutSourceImageSpaceFloat.Multiply(value.W, value.Y),
        FalloutSourceImageSpaceFloat.Multiply(value.W, value.Z), value.W);
    internal void FinishBatch(Func<IDisposable>? enter = null)
    {
        foreach (var index in ScalarProfileIndices())
        {
            using var context = enter?.Invoke();
            // Scalar arithmetic cannot define an unknown input, even at zero
            // weight. FinishColors owns the later unconditional fade stores.
            if (IsDefined(index)) Store(index, FalloutSourceImageSpaceFloat.Batch(Read(index), Multiply[index], Add[index]));
        }
        if (TintBatch.W > StrongestTint) StrongestTint = TintBatch.W;
        TintBatch = Premultiply(TintBatch); Tint = ComponentAdd(Tint, TintBatch);
        if (FadeBatch.W > StrongestFade) StrongestFade = FadeBatch.W;
        FadeBatch = Premultiply(FadeBatch); Fade = ComponentAdd(Fade, FadeBatch);
        if (BlurBatch > Blur) Blur = BlurBatch;
        if (DoubleVisionBatch > DoubleVision) DoubleVision = DoubleVisionBatch;
        if (RadialBatchStrength > RadialStrength)
        { RadialStrength = RadialBatchStrength; RadialParameters = RadialBatchParameters; RadialCenter = RadialBatchCenter; }
        if (OptionBatchStrength > OptionStrength)
        {
            OptionStrength = OptionBatchStrength; OptionMode = OptionBatchMode; OptionAlternate = false;
            if (OptionMode > 2) { OptionAlternate = true; OptionMode -= 3; }
            OptionFirst = OptionBatchFirst; OptionSecond = OptionBatchSecond; OptionThird = 0;
        }
        if (ScalarBatch > Scalar) Scalar = ScalarBatch;
    }
    internal void FinishColors()
    {
        if (Tint.W > 0) Tint = new(FalloutSourceImageSpaceFloat.Divide(Tint.X, Tint.W),
            FalloutSourceImageSpaceFloat.Divide(Tint.Y, Tint.W), FalloutSourceImageSpaceFloat.Divide(Tint.Z, Tint.W),
            FalloutSourceImageSpaceFloat.Divide(Tint.W, Tint.W));
        Store(29, Tint.X); Store(30, Tint.Y); Store(31, Tint.Z); Store(32, StrongestTint); Tint.W = StrongestTint;
        if (Fade.W > 0) Fade = new(FalloutSourceImageSpaceFloat.Divide(Fade.X, Fade.W),
            FalloutSourceImageSpaceFloat.Divide(Fade.Y, Fade.W), FalloutSourceImageSpaceFloat.Divide(Fade.Z, Fade.W),
            FalloutSourceImageSpaceFloat.Divide(Fade.W, Fade.W));
        Store(33, Fade.X); Store(34, Fade.Y); Store(35, Fade.Z); Store(36, StrongestFade); Fade.W = StrongestFade;
    }
    private static Vector4 WeightColor(Vector4 value, float weight, bool batch)
    {
        if (batch) return new(FalloutSourceImageSpaceFloat.Multiply(value.X, weight),
            FalloutSourceImageSpaceFloat.Multiply(value.Y, weight), FalloutSourceImageSpaceFloat.Multiply(value.Z, weight),
            FalloutSourceImageSpaceFloat.Multiply(value.W, weight));
        value.W = FalloutSourceImageSpaceFloat.Multiply(value.W, weight);
        if (value.W > 1) value.W = 1; else if (0 > value.W) value.W = 0;
        return value;
    }
    internal void AccumulateTint(Vector4 value, float weight, bool batch)
    {
        value = WeightColor(value, weight, batch);
        if (batch) TintBatch = ComponentAdd(TintBatch, value);
        else { if (value.W > StrongestTint) StrongestTint = value.W; Tint = ComponentAdd(Tint, Premultiply(value)); }
    }
    internal void AccumulateFade(Vector4 value, float weight, bool batch)
    {
        value = WeightColor(value, weight, batch);
        if (batch) FadeBatch = ComponentAdd(FadeBatch, value);
        else { if (value.W > StrongestFade) StrongestFade = value.W; Fade = ComponentAdd(Fade, Premultiply(value)); }
    }
    internal void AccumulateColors(Vector4 tint, Vector4 fade, float weight, bool batch)
    { AccumulateTint(tint, weight, batch); AccumulateFade(fade, weight, batch); }
    internal void AccumulateSimpleEffects(float blur, float vision, float weight, bool batch)
    {
        blur = FalloutSourceImageSpaceFloat.Multiply(blur, weight); vision = FalloutSourceImageSpaceFloat.Multiply(vision, weight);
        if (batch) { BlurBatch = FalloutSourceImageSpaceFloat.Add(blur, BlurBatch); DoubleVisionBatch = FalloutSourceImageSpaceFloat.Add(vision, DoubleVisionBatch); }
        else { if (blur > Blur) Blur = blur; if (vision > DoubleVision) DoubleVision = vision; }
    }
}
