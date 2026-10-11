using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageCurveCache(string Program, string Channel, uint Time, int LastKey,
    uint[] Value, long Changed);

internal sealed partial class FalloutImageSpaceState
{
    private const uint InvalidCurveValue = 0xff7fffff;
    private readonly Dictionary<(string Program, string Channel), FalloutSourceImageCurveCache> _sourceImageCurves = [];
    private static string ImageProgram(FalloutImageSpaceModifier program) => program.Form + "/" + program.SourceSha256;
    private void ConstructSourceImageCurves(string program)
    {
        foreach (var channel in Enumerable.Range(0, 21).SelectMany(index => new[] { index + "IAD", (index + 64) + "IAD" })
            .Concat(FalloutImageSpaceModifierReader.EffectCounts.Keys).Concat(["TNAM", "NAM3"]))
        {
            var key = (program, channel);
            if (_sourceImageCurves.ContainsKey(key)) continue;
            _sourceImageCurves.Add(key, new(program, channel, InvalidCurveValue, 0,
                Enumerable.Repeat(InvalidCurveValue, channel is "TNAM" or "NAM3" ? 4 : 1).ToArray(), ++_sourceImageChanged));
        }
    }
    private float SourceFloatCurve(string program, string channel, IReadOnlyList<FalloutImageFloatKey> keys, float time)
    {
        var cache = _sourceImageCurves[(program, channel)];
        if (BitConverter.UInt32BitsToSingle(cache.Time) == time)
        {
            if (cache.Value[0] == InvalidCurveValue) throw new NotSupportedException("Source scalar interpolator leaves its caller output unwritten.");
            return BitConverter.UInt32BitsToSingle(cache.Value[0]);
        }
        if (keys.Count == 0) throw new NotSupportedException("Source scalar interpolator has no actual key-data writer.");
        var last = cache.LastKey; float value;
        if (keys.Count == 1 || time == BitConverter.UInt32BitsToSingle(InvalidCurveValue)) value = keys[0].Value;
        else
        {
            if ((uint)last >= (uint)keys.Count) throw new InvalidDataException("Source scalar key cache escaped its actual data extent.");
            if (!(keys[last].Time <= time)) last = 0;
            var upper = last + 1;
            while (upper < keys.Count && !(keys[upper].Time >= time)) { last = upper; upper++; }
            if (upper == keys.Count) value = keys[last].Value;
            else value = SourceKeyLerp(keys[last].Value, keys[upper].Value, FalloutSourceImageSpaceFloat.Divide(
                FalloutSourceImageSpaceFloat.Subtract(time, keys[last].Time), FalloutSourceImageSpaceFloat.Subtract(keys[upper].Time, keys[last].Time)));
        }
        var bits = BitConverter.SingleToUInt32Bits(value);
        cache = cache with { LastKey = last, Value = [bits], Changed = ++_sourceImageChanged };
        _sourceImageCurves[(program, channel)] = cache;
        if (bits == InvalidCurveValue) throw new NotSupportedException("Source scalar interpolator returned its invalid constructor sentinel.");
        _sourceImageCurves[(program, channel)] = cache with { Time = BitConverter.SingleToUInt32Bits(time), Changed = ++_sourceImageChanged };
        return value;
    }
    private Vector4 SourceColorCurve(string program, string channel, IReadOnlyList<FalloutImageColorKey> keys, float time)
    {
        var cache = _sourceImageCurves[(program, channel)];
        static Vector4 Vector(uint[] value) => new(BitConverter.UInt32BitsToSingle(value[0]), BitConverter.UInt32BitsToSingle(value[1]),
            BitConverter.UInt32BitsToSingle(value[2]), BitConverter.UInt32BitsToSingle(value[3]));
        if (BitConverter.UInt32BitsToSingle(cache.Time) == time)
        {
            if (cache.Value.All(value => value == InvalidCurveValue)) throw new NotSupportedException("Source color interpolator leaves its caller output unwritten.");
            return Vector(cache.Value);
        }
        if (keys.Count == 0) throw new NotSupportedException("Source color interpolator has no actual key-data writer.");
        var last = cache.LastKey; Vector4 value;
        if (keys.Count == 1 || time == BitConverter.UInt32BitsToSingle(InvalidCurveValue)) value = keys[0].Value;
        else
        {
            if ((uint)last >= (uint)keys.Count) throw new InvalidDataException("Source color key cache escaped its actual data extent.");
            if (!(keys[last].Time <= time)) last = 0;
            var upper = last + 1;
            while (upper < keys.Count && !(keys[upper].Time >= time)) { last = upper; upper++; }
            if (upper == keys.Count) throw new NotSupportedException("Source color interpolation requested an unwritten key beyond its data extent.");
            var lower = keys[last]; var higher = keys[upper];
            var amount = FalloutSourceImageSpaceFloat.Divide(FalloutSourceImageSpaceFloat.Subtract(time, lower.Time),
                FalloutSourceImageSpaceFloat.Subtract(higher.Time, lower.Time));
            value = new(SourceKeyLerp(lower.Value.X, higher.Value.X, amount), SourceKeyLerp(lower.Value.Y, higher.Value.Y, amount),
                SourceKeyLerp(lower.Value.Z, higher.Value.Z, amount), SourceKeyLerp(lower.Value.W, higher.Value.W, amount));
        }
        uint[] bits = [BitConverter.SingleToUInt32Bits(value.X), BitConverter.SingleToUInt32Bits(value.Y),
            BitConverter.SingleToUInt32Bits(value.Z), BitConverter.SingleToUInt32Bits(value.W)];
        cache = cache with { LastKey = last, Value = bits, Changed = ++_sourceImageChanged };
        _sourceImageCurves[(program, channel)] = cache;
        if (bits.All(component => component == InvalidCurveValue)) throw new NotSupportedException("Source color interpolator returned its invalid constructor sentinel.");
        _sourceImageCurves[(program, channel)] = cache with { Time = BitConverter.SingleToUInt32Bits(time), Changed = ++_sourceImageChanged };
        return value;
    }
    private static float SourceKeyLerp(float first, float second, float amount) => FalloutSourceImageSpaceFloat.Add(
        FalloutSourceImageSpaceFloat.Multiply(FalloutSourceImageSpaceFloat.Subtract(1, amount), first),
        FalloutSourceImageSpaceFloat.Multiply(amount, second));
}
