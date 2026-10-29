namespace OpenNV.Runtime.Content;

internal static class FalloutVatsMiss
{
    // Independently recovered ordinary ranged miss rule. The selected source
    // node owns radius/center; miss yaw/pitch are added after aim correction.
    internal static (float Yaw, float Pitch) Angles(FalloutPluginStack records, float radius,
        float distance, Func<uint> nextRandom)
    {
        if (!float.IsFinite(radius) || radius < 0 || !float.IsFinite(distance) || distance < 0)
            throw new InvalidDataException("VATS miss node geometry is invalid.");
        var low = FalloutGameSettingFloats.Read(records, "fAutoAimMissRatioLow");
        var high = FalloutGameSettingFloats.Read(records, "fAutoAimMissRatioHigh");
        var degrees = FalloutGameSettingFloats.Read(records, "fAutoAimMaxDegreesMiss");
        if (!float.IsFinite(low) || !float.IsFinite(high) || high < low || !float.IsFinite(degrees) || degrees < 0)
            throw new InvalidDataException("VATS source miss settings are invalid.");
        var ratio = (float)(low + (high - (double)low) * (nextRandom() / 4294967296.0));
        var r = ratio * (radius == 0 ? 16 : radius);
        var magnitude = (float)(r / Math.Sqrt(distance * (double)distance + r * (double)r));
        const double sourceTau = 6.2831854820251465;
        const double radiansPerDegree = 0.01745329238474369;
        var phi = (float)(nextRandom() / 4294967296.0 * sourceTau);
        var maximum = (float)(degrees * radiansPerDegree);
        return (Math.Clamp((float)(Math.Cos(phi) * magnitude), -maximum, maximum),
            Math.Clamp((float)(Math.Sin(phi) * magnitude), -maximum, maximum));
    }
}
