using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class VertexFogProbe
{
    internal static void Owned(string root, uint cell)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var scene = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(cell));
        var fog = scene.Cell.Lighting ?? throw new InvalidDataException("Selected CELL has no lighting.");
        var values = new[] { fog.FogFar, fog.FogFar - fog.FogNear, fog.FogPower };
        if (values.Any(value => !float.IsFinite(value)) || values[1] <= 0 || fog.FogNear < 0 || fog.FogPower <= 0)
            throw new InvalidDataException("Selected CELL has unsupported fog parameters.");
        Console.WriteLine("OPENNV_OWNED_CELL_FOG " + JsonSerializer.Serialize(new
        {
            cell = scene.Cell.FormKey.ToString(), near = fog.FogNear, far = fog.FogFar, power = fog.FogPower,
            parameterBits = values.Select(BitConverter.SingleToUInt32Bits).ToArray(), color = fog.FogRgb,
            observation = "source-record-inputs;native-shader-binding-and-pixels-require-independent-comparison",
        }));
    }
}
