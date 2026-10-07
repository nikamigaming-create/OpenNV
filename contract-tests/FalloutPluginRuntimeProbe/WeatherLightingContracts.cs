using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class WeatherLightingContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-weather-palette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var count in new[] { 4, 6 })
            {
                var data = Enumerable.Range(0, 10 * count * 4).Select(value => (byte)value).ToArray();
                var field = new byte[6 + data.Length]; Encoding.ASCII.GetBytes("NAM0").CopyTo(field, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(field.AsSpan(4), (ushort)data.Length); data.CopyTo(field, 6);
                var record = new byte[24 + field.Length]; Encoding.ASCII.GetBytes("WTHR").CopyTo(record, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)field.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), 1); field.CopyTo(record, 24);
                var header = new byte[42]; Encoding.ASCII.GetBytes("TES4").CopyTo(header, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 18);
                Encoding.ASCII.GetBytes("HEDR").CopyTo(header, 24);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 12);
                BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(30), 1.34f);
                File.WriteAllBytes(Path.Combine(directory, "Palette.esm"), header.Concat(record).ToArray());
                using var records = FalloutPluginStack.Load(directory, ["Palette.esm"]);
                var weather = FalloutWeatherLighting.Read(records.GetEffective(new("Palette.esm", 1)));
                Require(weather.TimeSamples == count && weather.Colors.SequenceEqual(data) &&
                    weather.SunlightRgba.SequenceEqual(data.Skip(4 * count * 4).Take(count * 4)),
                    "Weather palette lost its source stride or canonical bytes.");
                var climate = new FalloutClimateLighting(new("Palette.esm", 2), 5, 7, 17, 19, "synthetic");
                var noon = FalloutWeatherTimeWeights.Sample(climate, 12, 0, count);
                var sample = weather.Sample(noon, 4);
                var selected = count == 4 ? 1 : 4;
                Require(noon.First == selected && noon.SecondWeight == 0 &&
                    sample[0] == (float)(data[4 * count * 4 + selected * 4] * (double)(1f / 255)),
                    "Weather invented a noon palette entry or sampled another class.");
                for (var hour = 0; hour <= 24; ++hour)
                    Require(weather.Sample(FalloutWeatherTimeWeights.Sample(climate, hour, 0, count)).All(float.IsFinite),
                        "Weather time weights exceeded the selected source palette.");
                Reject(() => weather.Sample(new(count, 0, 1, 0)));
                Reject(() => weather.Sample(noon, 10));
                Reject(() => FalloutWeatherTimeWeights.Sample(climate, 12, 0, 5));
            }
            Console.WriteLine("OPENNV_WEATHER_PALETTE_CONTRACT_PASS fourAndSix=true canonicalBytes=true classStride=true timeWeights=true invalidRefused=true");
        }
        finally { File.Delete(Path.Combine(directory, "Palette.esm")); Directory.Delete(directory); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Malformed weather palette request was admitted.");
    }
}
