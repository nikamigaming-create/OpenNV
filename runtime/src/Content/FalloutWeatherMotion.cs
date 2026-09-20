using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutWeatherMotion(float WindSpeed, float[] CloudUvPerSecond, string SourceSha256)
{
    internal static FalloutWeatherMotion Read(FalloutPluginRecord weather, float cloudSpeedMaximum)
    {
        if (weather.Signature != "WTHR") throw new InvalidDataException("Weather motion requires WTHR.");
        if (!float.IsFinite(cloudSpeedMaximum) || cloudSpeedMaximum < 0)
            throw new InvalidDataException("Maximum cloud speed must be finite and nonnegative.");
        var fields = weather.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data;
        var clouds = fields.Single(field => field.Signature == "ONAM").Data;
        if (data.Length != 15 || clouds.Length != 4)
            throw new NotSupportedException($"Weather {weather.FormKey} motion has unbound DATA/ONAM sizes {data.Length}/{clouds.Length}.");
        // Layer speed is normalized within the winning GMST range, then
        // scaled by weather wind. These are simulation-second UV rates;
        // the accelerated calendar clock does not multiply cloud motion.
        var wind = data.Span[0] / 255f;
        var rates = clouds.ToArray().Select(speed => speed / 255f * cloudSpeedMaximum * wind).ToArray();
        return new(wind, rates, Convert.ToHexString(SHA256.HashData([.. data.Span, .. clouds.Span])).ToLowerInvariant());
    }
}
