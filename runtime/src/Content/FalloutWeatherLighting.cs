using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutClimateLighting(FalloutFormKey Form, float SunriseStart, float SunriseEnd,
    float SunsetStart, float SunsetEnd, string SourceSha256)
{
    internal static FalloutClimateLighting Read(FalloutPluginRecord record)
    {
        if (record.Signature != "CLMT") throw new InvalidDataException("Sky climate does not resolve to CLMT.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "TNAM").Data;
        if (data.Length != 6) throw new NotSupportedException("Climate TNAM requires the six-byte time declaration.");
        var times = data.Span;
        // Climate times are encoded in ten-minute units. The native owner stores
        // each converted hour as Float32 before evaluating time-of-day weights.
        return new(record.FormKey, (float)(times[0] / 6.0), (float)(times[1] / 6.0),
            (float)(times[2] / 6.0), (float)(times[3] / 6.0),
            Convert.ToHexString(SHA256.HashData(data.Span)).ToLowerInvariant());
    }
}

internal readonly record struct FalloutWeatherTimeWeights(int First, int Second, float FirstWeight, float SecondWeight)
{
    internal static FalloutWeatherTimeWeights Sample(FalloutClimateLighting climate, float hour, float daytimeExtension)
    {
        if (!float.IsFinite(hour) || hour < 0 || hour > 24 || !float.IsFinite(daytimeExtension) || daytimeExtension < 0)
            throw new InvalidDataException("Sky time or daytime colour extension is invalid.");
        var start = MathF.Max(0, climate.SunriseStart - daytimeExtension);
        var end = MathF.Min(24, climate.SunsetEnd + daytimeExtension);
        // Noon is the engine's fixed solar midpoint, independent of a CELL.
        const float noon = 12;
        if (!(start < climate.SunriseEnd && climate.SunriseEnd < noon && noon < climate.SunsetStart && climate.SunsetStart < end))
            throw new NotSupportedException("Climate transition times do not define ordered sky intervals.");
        static FalloutWeatherTimeWeights Blend(int first, int second, float firstWeight) =>
            new(first, second, firstWeight, (float)(1.0 - firstWeight));
        if (hour >= start && hour < climate.SunriseEnd)
        {
            var half = (float)((climate.SunriseEnd - (double)start) * 0.5);
            var middle = start + half;
            return hour < middle
                ? Blend(0, 3, (float)(1.0 - (middle - (double)hour) / half))
                : Blend(0, 1, (float)(1.0 - (hour - (double)middle) / half));
        }
        if (hour >= climate.SunriseEnd && hour <= noon)
            return Blend(4, 1, (float)(1.0 - (noon - (double)hour) / (noon - climate.SunriseEnd)));
        if (hour >= noon && hour <= climate.SunsetStart)
            return Blend(1, 4, (float)(1.0 - (climate.SunsetStart - (double)hour) / (climate.SunsetStart - noon)));
        if (hour >= climate.SunsetStart && hour < end)
        {
            var half = (float)((end - (double)climate.SunsetStart) * 0.5);
            var middle = climate.SunsetStart + half;
            return hour < middle
                ? Blend(2, 1, (float)(1.0 - (middle - (double)hour) / half))
                : Blend(2, 3, (float)(1.0 - (hour - (double)middle) / half));
        }
        return Blend(3, 3, 1);
    }
}

internal sealed record FalloutWeatherLighting(FalloutFormKey Form, byte[] SunlightRgba, string SourceSha256)
{
    internal static FalloutWeatherLighting Read(FalloutPluginRecord record)
    {
        if (record.Signature != "WTHR") throw new InvalidDataException("Sky weather does not resolve to WTHR.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "NAM0").Data;
        // FNV NAM0 stores ten colour classes, each with six RGBA time samples.
        if (data.Length != 10 * 6 * 4) throw new NotSupportedException("Weather NAM0 colour layout is unbound.");
        return new(record.FormKey, data.Slice(4 * 6 * 4, 6 * 4).ToArray(),
            Convert.ToHexString(SHA256.HashData(data.Span)).ToLowerInvariant());
    }

    internal float[] Sample(FalloutWeatherTimeWeights weights)
    {
        if (SunlightRgba.Length != 24 || weights.First is < 0 or > 5 || weights.Second is < 0 or > 5 ||
            !float.IsFinite(weights.FirstWeight) || !float.IsFinite(weights.SecondWeight) ||
            weights.FirstWeight < 0 || weights.SecondWeight < 0)
            throw new InvalidDataException("Weather sunlight samples or weights are invalid.");
        var color = new float[3];
        for (var channel = 0; channel < color.Length; channel++)
        {
            // The native accumulator stores after each weighted sample. Its
            // Float64 normalization declaration widens a Float32 reciprocal.
            var first = (float)(SunlightRgba[weights.First * 4 + channel] * (double)weights.FirstWeight);
            var sum = (float)(first + SunlightRgba[weights.Second * 4 + channel] * (double)weights.SecondWeight);
            color[channel] = (float)(sum * (double)(1.0f / byte.MaxValue));
        }
        return color;
    }
}
