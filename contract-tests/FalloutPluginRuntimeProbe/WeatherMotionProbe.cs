using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class WeatherMotionProbe
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opennv-weather-motion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "Weather.esm");
            var overridePath = Path.Combine(directory, "Override.esp");
            File.WriteAllBytes(basePath, [.. Record("TES4", 0, []), .. Setting(.1f),
                .. Weather(1, 255, [255, 0, 128, 64]), .. Weather(2, 0, [255, 255, 255, 255]),
                .. Weather(3, 50, [52, 0, 0, 65]), .. Weather(4, 255, [1, 2, 3])]);
            File.WriteAllBytes(overridePath, [.. Record("TES4", 0, [.. Field("MAST", "Weather.esm\0"u8.ToArray()), .. Field("DATA", new byte[8])]),
                .. Setting(.2f), .. Weather(1, 128, [255, 0, 128, 64])]);
            using (var records = FalloutPluginStack.Load([new("Weather.esm", basePath)]))
            {
                var maximum = FalloutGameSettingFloats.Read(records, "fWeatherCloudSpeedMax");
                var full = Read(1); var calm = Read(2); var light = Read(3);
                Require(full.WindSpeed == 1 && MathF.Abs(full.CloudUvPerSecond[0] - .1f) < 1e-7 && full.CloudUvPerSecond[1] == 0,
                    "Full wind or a stopped cloud layer changed.");
                Require(calm.CloudUvPerSecond.All(value => value == 0), "Calm weather still moved cloud textures.");
                Require(MathF.Abs(light.CloudUvPerSecond[0] - .003998462f) < 1e-8 &&
                    MathF.Abs(light.CloudUvPerSecond[3] - .0049980776f) < 1e-8,
                    "Cloud speed omitted weather wind or used the calendar timescale.");
                Reject(() => Read(4));
                foreach (var invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
                    Reject(() => FalloutWeatherMotion.Read(records.GetEffective(new("Weather.esm", 1)), invalid));
                FalloutWeatherMotion Read(uint id) => FalloutWeatherMotion.Read(records.GetEffective(new("Weather.esm", id)), maximum);
            }
            using (var records = FalloutPluginStack.Load([new("Weather.esm", basePath), new("Override.esp", overridePath)]))
            {
                var motion = FalloutWeatherMotion.Read(records.GetEffective(new("Weather.esm", 1)),
                    FalloutGameSettingFloats.Read(records, "fWeatherCloudSpeedMax"));
                Require(MathF.Abs(motion.CloudUvPerSecond[0] - .10039216f) < 1e-7,
                    "Winning weather or cloud-speed GMST override was ignored.");
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_WEATHER_MOTION_CONTRACT_PASS calm=true windScale=true layerScale=true winningOverrides=true invalidRefused=true");
    }

    internal static void Owned(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var maximum = FalloutGameSettingFloats.Read(records, "fWeatherCloudSpeedMax");
        var weather = records.EffectiveRecords("WTHR").Select(record => new
        {
            form = record.FormKey.ToString(),
            motion = FalloutWeatherMotion.Read(record, maximum)
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-weather-motion/v1", maximum, weather, parity = "unmatched" }));
        Console.WriteLine($"OPENNV_OWNED_WEATHER_MOTION_PASS weather={weather.Length} maximum={maximum:R}");
    }

    private static byte[] Weather(uint id, byte wind, byte[] clouds) =>
        Record("WTHR", id, [.. Field("DATA", [wind, .. new byte[14]]), .. Field("ONAM", clouds)]);
    private static byte[] Setting(float maximum) => Record("GMST", 5,
        [.. Field("EDID", "fWeatherCloudSpeedMax\0"u8.ToArray()), .. Field("DATA", BitConverter.GetBytes(maximum))]);
    private static byte[] Record(string signature, uint id, byte[] data)
    {
        var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
        return [.. header, .. data];
    }
    private static byte[] Field(string signature, byte[] data) =>
        [.. Encoding.ASCII.GetBytes(signature), .. BitConverter.GetBytes((ushort)data.Length), .. data];
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid weather motion was admitted.");
    }
}
