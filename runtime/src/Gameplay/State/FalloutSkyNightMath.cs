using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyNightTimes(uint ExtendedSunriseStartBits, uint SunriseEndBits,
    uint SunsetStartBits, uint ExtendedSunsetEndBits);
internal sealed record FalloutSkyNightSettings(uint PeriodBits, uint AxisXBits, uint AxisYBits, uint AxisZBits)
{
    internal static FalloutSkyNightSettings Read(FalloutPluginStack records)
    {
        uint Bits(string name) => BitConverter.SingleToUInt32Bits(FalloutGameSettingFloats.ReadRetained(records, name,
            nameof(FalloutSkyNightSettings)));
        return new(Bits("fStarsRotateDays"), Bits("fStarsRotateXAxis"), Bits("fStarsRotateYAxis"), Bits("fStarsRotateZAxis"));
    }
}

// These fields consume the original standalone time getters/GMSTs. A logical
// weather sample cannot supply the missing native NightSky texture flag, source
// weather pointer, stars palette or returned whole-frame prefix.
internal static class FalloutSkyNightMath
{
    internal static float Alpha(FalloutSkyTransferDeclaration source, float hour, FalloutSkyNightTimes times)
    {
        source.Validate();
        if (!source.IsStandalone) throw new NotSupportedException("source-FNV-NightSky-frame-consumer-unowned");
        var start = BitConverter.UInt32BitsToSingle(times.ExtendedSunriseStartBits);
        var sunriseEnd = BitConverter.UInt32BitsToSingle(times.SunriseEndBits);
        var sunsetStart = BitConverter.UInt32BitsToSingle(times.SunsetStartBits);
        var end = BitConverter.UInt32BitsToSingle(times.ExtendedSunsetEndBits);
        if (new[] { hour, start, sunriseEnd, sunsetStart, end }.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("NightSky lost its original finite time fields.");
        var eveningMiddle = end - (end - sunsetStart) * .5f;
        var morningMiddle = sunriseEnd - (sunriseEnd - start) * .5f;
        float result;
        if (hour > start && hour < morningMiddle) result = (morningMiddle - hour) / (morningMiddle - start);
        else if (hour >= morningMiddle && hour <= eveningMiddle) result = 0f;
        else if (hour > eveningMiddle && hour < end) result = (hour - eveningMiddle) / (end - eveningMiddle);
        else result = 1f;
        if (!float.IsFinite(result)) throw new NotSupportedException("source-NightSky-nonfinite-opacity-arm-unowned");
        return result;
    }
    internal static FalloutCelestialMatrix Rotation(FalloutSkyTransferDeclaration source, uint days, float hour,
        FalloutSkyNightSettings settings)
    {
        source.Validate();
        if (!source.IsStandalone) throw new NotSupportedException("source-FNV-NightSky-rotation-consumer-unowned");
        float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        var period = F(settings.PeriodBits); var axisX = F(settings.AxisXBits); var axisY = F(settings.AxisYBits); var axisZ = F(settings.AxisZBits);
        if (new[] { hour, period, axisX, axisY, axisZ }.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("NightSky rotation lost its finite original GMST/clock inputs.");
        if (period == 0f) throw new NotSupportedException("source-NightSky-zero-period-floating-status-arm-unowned");
        var dayFraction = hour / 24f;
        var elapsed = dayFraction + (float)days;
        var remainder = (float)((double)elapsed % (double)period);
        var angle = remainder * 6.2831854820251465f / period;
        if (!float.IsFinite(angle) || Math.Abs((double)angle) >= 9223372036854775808.0)
            throw new NotSupportedException("source-NightSky-FSINCOS-status-arm-unowned");
        var lengthSquared = (axisY * axisY + axisX * axisX) + axisZ * axisZ;
        var length = (float)Math.Sqrt(lengthSquared);
        if (!float.IsFinite(length)) throw new NotSupportedException("source-NightSky-axis-floating-status-arm-unowned");
        if (length > 1e-6f)
        {
            var inverse = 1f / length; axisX *= inverse; axisY *= inverse; axisZ *= inverse;
        }
        else { axisX = 0f; axisY = 0f; axisZ = 0f; }
        var cosine = (float)Math.Cos(angle); var sine = (float)Math.Sin(angle); var complement = 1f - cosine;
        var xy = axisX * axisY * complement; var xz = axisX * axisZ * complement; var yz = axisY * axisZ * complement;
        var xs = axisX * sine; var ys = axisY * sine; var zs = axisZ * sine;
        return FalloutCelestialMatrix.Read(axisX * axisX * complement + cosine, xy + zs, xz - ys,
            xy - zs, axisY * axisY * complement + cosine, yz + xs,
            xz + ys, yz - xs, axisZ * axisZ * complement + cosine);
    }
}

internal sealed partial class FalloutSkyTransferState
{
    internal FalloutSkyNightTimes ReadSourceStandaloneNightTimes()
    {
        BindPresentationThread(); RequireWriter();
        if (!Source.IsStandalone) throw new NotSupportedException("source-FNV-NightSky-time-getters-unowned");
        if (_process == Guid.Empty || CurrentWeather is null || Mode is not (2 or 3))
            throw new InvalidOperationException("NightSky time getters are outside their actual process/weather/mode source branch.");
        // Getter order is observable because each consumes distinct dirty and
        // underlying-cache bits. It differs from the image-space getter order.
        var sunsetStart = ReadStandaloneTimeCache(2);
        var end = ReadStandaloneTimeCache(3);
        var sunriseEnd = ReadStandaloneTimeCache(1);
        var start = ReadStandaloneTimeCache(0);
        return new(BitConverter.SingleToUInt32Bits(start), BitConverter.SingleToUInt32Bits(sunriseEnd),
            BitConverter.SingleToUInt32Bits(sunsetStart), BitConverter.SingleToUInt32Bits(end));
    }
}
