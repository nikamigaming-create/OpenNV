using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class DirectionalLightProbe
{
    internal static void Run()
    {
        foreach (var (x, z, expected) in new (float X, float Z, Vector3 Expected)[]
        {
            (0, 0, Vector3.UnitX), (90, 0, -Vector3.UnitY), (180, 0, -Vector3.UnitX),
            (270, 0, Vector3.UnitY), (0, 90, Vector3.UnitZ), (0, 270, -Vector3.UnitZ),
            (60, 30, new(MathF.Sqrt(3) / 4, -0.75f, 0.5f)),
        })
            if (Vector3.Distance(FalloutCellDirectionalLight.RayDirection(x, z), expected) > 0.000001f)
                throw new Exception("CELL directional axes or emitted-ray sign changed.");
        foreach (var x in new[] { -180f, -45f, 0f, 45f, 135f, 360f })
            foreach (var z in new[] { -270f, -45f, 0f, 45f, 135f, 360f })
                if (MathF.Abs(FalloutCellDirectionalLight.RayDirection(x, z).Length() - 1) > 0.000001f)
                    throw new Exception("CELL directional rotation lost its unit axis.");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            foreach (var first in new[] { true, false })
            {
                try { _ = FalloutCellDirectionalLight.RayDirection(first ? invalid : 0, first ? 0 : invalid); }
                catch (InvalidDataException) { continue; }
                throw new Exception("Non-finite CELL directional rotation was accepted.");
            }
        Console.WriteLine("OPENNV_DIRECTIONAL_LIGHT_CONTRACT_PASS cardinalAxes=true mixedRotations=true invalidInputsRejected=true");
    }

    internal static void Owned(string root, uint cell)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var scene = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(cell));
        var lighting = scene.Cell.Lighting ?? throw new InvalidDataException("Selected CELL has no lighting.");
        var ray = FalloutCellDirectionalLight.RayDirection(lighting.DirectionalXDegrees, lighting.DirectionalZDegrees);
        Console.WriteLine("OPENNV_OWNED_DIRECTIONAL_LIGHT " + JsonSerializer.Serialize(new
        {
            cell = scene.Cell.FormKey.ToString(), x = lighting.DirectionalXDegrees, z = lighting.DirectionalZDegrees,
            ray = new[] { ray.X, ray.Y, ray.Z },
            rayBits = new[] { ray.X, ray.Y, ray.Z }.Select(BitConverter.SingleToUInt32Bits).ToArray(),
            observation = "source-derived-emitted-ray;native-transform-and-pixels-require-independent-comparison",
        }));
    }
}
