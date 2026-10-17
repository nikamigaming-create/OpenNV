using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class WeatherCommandContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-weather-command-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Weather.esm"), Join(
                Record("TES4", 0),
                Record("WTHR", 0x15e, Field("NAM0", new byte[240])),
                Record("WTHR", 0x160, Field("EDID", Encoding.ASCII.GetBytes("SyntheticWeather\0")),
                    Field("NAM0", Enumerable.Repeat((byte)128, 240).ToArray())),
                Record("CLMT", 0x15f, Field("TNAM", [24, 48, 96, 120, 0, 0]),
                    Field("WLST", Join(BitConverter.GetBytes(0x15eu), BitConverter.GetBytes(100), new byte[4]))),
                Record("WRLD", 0x800, Field("CNAM", BitConverter.GetBytes(0x15fu))),
                Record("STAT", 0x161), Record("SCPT", 0x900, Field("SCRO", BitConverter.GetBytes(0x160u)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Weather.esm"]);
            var script = records.GetEffective(new("Weather.esm", 0x900));
            var bindings = new FalloutScriptBindings(records, script, script, script.ReadSubrecords());
            var weather = new FalloutFormKey("Weather.esm", 0x160);
            var cell = new FalloutCellDefinition(new("Weather.esm", 0x801), "Synthetic", 0, (0, 0), new("Weather.esm", 0x800), null);
            var sky = new FalloutSkyLightingState(records, 0);
            foreach (var arguments in new string[][] { ["SyntheticWeather"], ["SyntheticWeather", "0"] })
            {
                Apply(sky, bindings, arguments);
                Check(sky.ActiveWeather.Form == weather && sky.ForcedWeather is null,
                    "ForceWeather without an override did not immediately change the shared sky.");
                var cold = Cold(records, sky);
                Check(cold.ActiveWeather.Form == weather && cold.ForcedWeather is null,
                    "Unheld weather did not survive a cold save.");
                cold.EnterCell(cell);
                Check(cold.ActiveWeather.Form == cold.DefaultWeather, "Unheld weather prevented normal climate selection.");
            }
            Apply(sky, bindings, ["SyntheticWeather", "1"]);
            sky.EnterCell(cell);
            Check(sky.ActiveWeather.Form == weather && sky.ForcedWeather == weather,
                "Normal climate selection replaced an active weather override.");
            var held = Cold(records, sky);
            Check(held.ActiveWeather.Form == weather && held.ForcedWeather == weather,
                "Cold save lost the explicit weather override.");
            held.ReleaseWeatherOverride();
            Check(held.ForcedWeather is null && held.ActiveWeather.Form == held.DefaultWeather,
                "ReleaseWeatherOverride failed to expose the normal selected weather.");
            Apply(sky, bindings, ["SyntheticWeather"]);
            Check(sky.ForcedWeather is null, "A later ForceWeather without a flag retained the prior override.");
            foreach (var arguments in new string[][] { [], ["SyntheticWeather", "1", "0"],
                ["SyntheticWeather", "2"], ["SyntheticWeather", "NaN"], ["SyntheticWeather", "missing"] })
            {
                var before = JsonSerializer.Serialize(sky.Capture());
                Reject(() => Apply(sky, bindings, arguments));
                Check(JsonSerializer.Serialize(sky.Capture()) == before, "Invalid weather arguments changed the shared sky.");
            }
            var unchanged = JsonSerializer.Serialize(sky.Capture());
            Reject(() => sky.ForceWeather(new("Weather.esm", 0x161), true));
            Check(JsonSerializer.Serialize(sky.Capture()) == unchanged, "A non-weather form partially changed the sky.");
            Console.WriteLine("OPENNV_WEATHER_COMMAND_PASS optionalFlag=true immediate=true held=true normalSelection=true release=true cold=true invalidAtomic=true");
        }
        finally { directory.Delete(true); }
    }

    internal static void RunOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var script = records.GetEffective(new("FalloutNV.esm", 0x11e65e));
        var fields = script.ReadSubrecords().ToArray();
        var sourceHash = SHA256.HashData(script.ReadData());
        var source = FalloutDialogueTopic.ScriptText(fields.Single(field => field.Signature == "SCTX").Data.Span);
        var lines = FalloutDialogueTopic.CodeLines(source).ToArray();
        var command = lines.Single(line => line.StartsWith("ForceWeather ", StringComparison.OrdinalIgnoreCase));
        var arguments = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1..];
        Check(arguments.Length == 2 && arguments[1] == "1" &&
            lines.Contains("ReleaseWeatherOverride", StringComparer.OrdinalIgnoreCase),
            "Owned exterior trigger no longer declares its held weather and release pair.");
        var bindings = new FalloutScriptBindings(records, script, script, fields);
        var weather = bindings.Form(arguments[0]).FormKey;
        var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension"));
        Apply(sky, bindings, arguments);
        var cold = Cold(records, sky);
        Check(sky.ForcedWeather == weather && sky.ActiveWeather.Form == weather &&
            cold.ForcedWeather == weather && cold.ActiveWeather.Form == weather,
            "Owned weather command did not bind its authored weather through a cold save.");
        cold.ReleaseWeatherOverride();
        Check(cold.ForcedWeather is null && sourceHash.SequenceEqual(SHA256.HashData(script.ReadData())),
            "Owned weather release failed or source bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_WEATHER_COMMAND_PASS script={script.FormKey} weather={weather} " +
            "optionalFlag=true compiledBinding=true held=true cold=true release=true sourceReadonly=true campaign=unverified parity=unverified");
    }

    private static void Apply(FalloutSkyLightingState sky, FalloutScriptBindings bindings, IReadOnlyList<string> arguments)
    {
        var held = FalloutSkyLightingState.ReadForceWeatherOverride(arguments);
        sky.ForceWeather(bindings.Form(arguments[0]).FormKey, held);
    }
    private static FalloutSkyLightingState Cold(FalloutPluginStack records, FalloutSkyLightingState sky)
    {
        var cold = new FalloutSkyLightingState(records, sky.DaytimeExtension);
        cold.Restore(JsonSerializer.Deserialize<FalloutSkyLightingSnapshot>(JsonSerializer.Serialize(sky.Capture()))!);
        return cold;
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid weather command was accepted.");
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
