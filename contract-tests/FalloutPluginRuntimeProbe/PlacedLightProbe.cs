using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlacedLightProbe
{
    internal static void Owned(string dataRoot, string cellId, float? hour = null)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var cell = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(cellId, 16)));
        var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension"));
        var gameHour = hour ?? FalloutGlobalState.Read(records).Get(FalloutGameTimeBindings.Read(records).Hour);
        var lights = cell.References.Where(reference => cell.BaseObjects[reference.Base].Signature == "LIGH")
            .Select(reference =>
            {
                var resolved = FalloutPlacedLightResolver.Resolve(reference, cell.BaseObjects[reference.Base], records,
                    region => sky.RegionEmittance(region, gameHour));
                return new
                {
                    reference = records.RuntimeFormId(reference.FormKey),
                    reference.Position,
                    radius = resolved.RadiusGameUnits,
                    dimmer = resolved.Intensity,
                    diffuse = resolved.ShaderColorRgb,
                    diffuseBits = resolved.ShaderColorRgb.Select(BitConverter.SingleToInt32Bits).ToArray(),
                    emittance = reference.Emittance?.ToString(),
                };
            }).ToArray();
        if (lights.Length == 0) throw new InvalidDataException("Selected CELL contains no lights to audit.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-placed-lights/v1",
            cell = cell.Cell.FormKey.ToString(),
            hour = gameHour,
            climate = sky.Climate,
            sky = sky.Capture(),
            lights,
            parity = "unverified; compare source inputs and matched native shader state separately",
        }));
    }
}
