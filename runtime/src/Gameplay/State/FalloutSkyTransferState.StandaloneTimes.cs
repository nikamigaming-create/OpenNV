using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// These two base caches are source process cells, separate from the extended
// values consumed by interpolation. The single current Sky owner retains all
// six cells across calls and hands them to the genuinely new cold process.
internal sealed record FalloutSkyStandaloneBaseTimes(uint SunriseStartBits, uint SunsetEndBits);

internal sealed partial class FalloutSkyTransferState
{
    private FalloutSkyStandaloneBaseTimes _standaloneBaseTimes = new(0, 0);

    private void RequireStandaloneExtensionSource()
    {
        if (Source.IsStandalone && BitConverter.SingleToUInt32Bits(_daytimeExtension) !=
            BitConverter.SingleToUInt32Bits(FalloutGameSettingFloats.Read(_records, "fDaytimeColorExtension")))
            throw new InvalidDataException("Sky extended time cache lost its actual selected GMST value.");
    }

    private FalloutSkyImageWeights ReadStandaloneImageWeights()
    {
        var start = ReadStandaloneTimeCache(0);
        var sunriseEnd = ReadStandaloneTimeCache(1);
        var sunsetStart = ReadStandaloneTimeCache(2);
        var end = ReadStandaloneTimeCache(3);
        return FalloutSkyStandaloneInterpolation.Sample(BitConverter.UInt32BitsToSingle(HourBits), start, sunriseEnd, sunsetStart, end);
    }

    private float ReadStandaloneTimeCache(int index)
    {
        if (!Source.IsStandalone) throw new InvalidOperationException("Another selected source cannot consume the standalone cache transport.");
        var bit = index switch { 0 => 0x1000u, 1 => 0x200u, 2 => 0x400u, 3 => 0x2000u, _ => throw new InvalidDataException("Unknown Sky time getter.") };
        if ((Flags & bit) != 0)
        {
            float? value = null;
            var climate = Climate is { } form ? FalloutClimateLighting.Read(_records.GetEffective(form)) : null;
            if (index == 0)
            {
                if ((Flags & 0x100) != 0 && climate is not null)
                {
                    _standaloneBaseTimes = _standaloneBaseTimes with { SunriseStartBits = BitConverter.SingleToUInt32Bits(climate.SunriseStart) };
                    Flags &= ~0x100u; Next();
                }
                value = FalloutSkyStandaloneInterpolation.ExtendedStart(BitConverter.UInt32BitsToSingle(_standaloneBaseTimes.SunriseStartBits), _daytimeExtension);
            }
            else if (index == 3)
            {
                if ((Flags & 0x800) != 0 && climate is not null)
                {
                    _standaloneBaseTimes = _standaloneBaseTimes with { SunsetEndBits = BitConverter.SingleToUInt32Bits(climate.SunsetEnd) };
                    Flags &= ~0x800u; Next();
                }
                value = FalloutSkyStandaloneInterpolation.ExtendedEnd(BitConverter.UInt32BitsToSingle(_standaloneBaseTimes.SunsetEndBits), _daytimeExtension,
                    BitConverter.UInt32BitsToSingle(Source.LastHourBits));
            }
            else if (climate is not null) value = index == 1 ? climate.SunriseEnd : climate.SunsetStart;

            // The two middle getters keep both cache and dirty flag while the
            // climate pointer is null; the extended getters still return.
            if (value is { } actual)
            {
                if (!float.IsFinite(actual)) throw new InvalidDataException("Source Sky time getter stored a non-finite cell.");
                var bits = BitConverter.SingleToUInt32Bits(actual);
                TimeCaches = index switch
                {
                    0 => TimeCaches with { SunriseStart = bits },
                    1 => TimeCaches with { SunriseEnd = bits },
                    2 => TimeCaches with { SunsetStart = bits },
                    3 => TimeCaches with { SunsetEnd = bits },
                    _ => throw new InvalidDataException()
                };
                Flags &= ~bit; Next();
            }
        }
        return BitConverter.UInt32BitsToSingle(index switch
        {
            0 => TimeCaches.SunriseStart,
            1 => TimeCaches.SunriseEnd,
            2 => TimeCaches.SunsetStart,
            3 => TimeCaches.SunsetEnd,
            _ => throw new InvalidDataException("Unknown Sky time cache.")
        });
    }

    private FalloutSkyStandaloneBaseTimes? CaptureStandaloneBaseTimes() => Source.IsStandalone ? _standaloneBaseTimes : null;

    private static void ValidateStandaloneTimes(FalloutSkyTransferSnapshot saved, FalloutPluginStack records)
    {
        if (!saved.Source.IsStandalone)
        {
            if (saved.StandaloneBaseTimes is not null) throw new InvalidDataException("Sky retained another selected source's underlying time cells.");
            return;
        }
        var baseTimes = saved.StandaloneBaseTimes ?? throw new InvalidDataException("Standalone Sky lost its two actual underlying time caches.");
        var extension = FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension");
        if (!float.IsFinite(extension) || extension < 0) throw new InvalidDataException("Standalone Sky extended-time GMST is invalid.");
        if (saved.Climate is not { } form)
        {
            if (baseTimes != new FalloutSkyStandaloneBaseTimes(0, 0) || saved.TimeCaches != new FalloutSkyTimeCaches(0, 0, 0, 0) ||
                saved.Flags is not (0x20 or 0x21))
                throw new InvalidDataException("Sky introduced a cache/flag writer before actual climate selection.");
            return;
        }
        var climate = FalloutClimateLighting.Read(records.GetEffective(form));
        if (baseTimes.SunriseStartBits != BitConverter.SingleToUInt32Bits(climate.SunriseStart) ||
            baseTimes.SunsetEndBits != BitConverter.SingleToUInt32Bits(climate.SunsetEnd) || saved.Flags != 0x61 ||
            saved.TimeCaches != new FalloutSkyTimeCaches(
                BitConverter.SingleToUInt32Bits(FalloutSkyStandaloneInterpolation.ExtendedStart(climate.SunriseStart, extension)),
                BitConverter.SingleToUInt32Bits(climate.SunriseEnd), BitConverter.SingleToUInt32Bits(climate.SunsetStart),
                BitConverter.SingleToUInt32Bits(FalloutSkyStandaloneInterpolation.ExtendedEnd(climate.SunsetEnd, extension,
                    BitConverter.UInt32BitsToSingle(saved.Source.LastHourBits)))))
            throw new InvalidDataException("Sky cache/flag continuation does not match its admitted source climate writer and getters.");
    }

    private void RestoreStandaloneTimes(FalloutSkyTransferSnapshot saved)
    {
        if (Source.IsStandalone)
            _standaloneBaseTimes = saved.StandaloneBaseTimes ?? throw new InvalidDataException("Cold standalone Sky omitted underlying time cells.");
    }
}

internal static class FalloutSkyStandaloneInterpolation
{
    internal static float ExtendedStart(float basis, float extension)
    {
        var difference = basis - extension;
        // The original maximum/minimum selects the right operand on equality.
        return 0f > difference ? 0f : difference;
    }
    internal static float ExtendedEnd(float basis, float extension, float lastHour)
    {
        var sum = extension + basis;
        return lastHour < sum ? lastHour : sum;
    }
    internal static float Primary(float weight, float blend) => weight * blend;
    internal static float Secondary(float weight, float blend)
    {
        var complement = 1f - weight; return complement * blend;
    }
    internal static FalloutSkyImageWeights Sample(float time, float start, float sunriseEnd, float sunsetStart, float end)
    {
        if (!float.IsFinite(time) || !float.IsFinite(start) || !float.IsFinite(sunriseEnd) || !float.IsFinite(sunsetStart) || !float.IsFinite(end))
            throw new InvalidDataException("Sky interpolation has no actual finite source time cells.");
        if (time > start && time < sunriseEnd) return Half(0, 3, 1, time, start, sunriseEnd);
        if (time >= sunriseEnd && time <= sunsetStart) return new(1, 0, 1, false);
        if (time > sunsetStart && time < end) return Half(2, 1, 3, time, sunsetStart, end);
        return new(time >= end || time <= start ? 3 : 1, 0, 1, false);
    }
    private static FalloutSkyImageWeights Half(int primary, int before, int after, float time, float lower, float upper)
    {
        var span = upper - lower;
        var half = span * .5f;
        var middle = half + lower;
        var firstHalf = time < middle;
        var distance = firstHalf ? middle - time : time - middle;
        var fraction = distance / half;
        var weight = 1f - fraction;
        if (!float.IsFinite(weight)) throw new InvalidDataException("Source Sky interval produced a non-finite Float32 weight.");
        return new(primary, firstHalf ? before : after, weight, true);
    }
}
