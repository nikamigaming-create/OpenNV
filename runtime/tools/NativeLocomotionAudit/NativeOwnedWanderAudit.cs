using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class NativeOwnedWanderAudit
{
    internal static void Run(string[] arguments)
    {
        if (arguments.Length != 3) throw new ArgumentException("Owned wander needs installation, actor reference and package IDs.");
        RuntimeLiveContentSource.Configure(arguments[0], RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: ["FalloutNV.esm"], includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = records.RuntimeFormKey(Convert.ToUInt32(arguments[1], 16));
        var package = records.GetEffective(records.RuntimeFormKey(Convert.ToUInt32(arguments[2], 16)));
        var source = FalloutScriptPackage.Read(package);
        _ = FalloutSandboxActivities.Read(package);
        if (source.LocationType != 3) throw new NotSupportedException("This selected audit requires an editor-location source package.");
        var placement = world.Placement(actor);
        var center = new Vector3(placement.Position[0], placement.Position[1], placement.Position[2]);
        var settings = new FalloutNumericGameSettings(records, content);
        var globals = FalloutGlobalState.Read(records);
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        var current = placement;
        var spatialQueries = 0;
        world.Get(actor).QuerySpatialPlacement = () => { spatialQueries++; return current; };
        FalloutWanderTriangle? previous = null;
        for (var sample = 0; sample < 4; sample++)
        {
            var result = world.SamplePackageWander(actor, center, source.LocationRadius, settings, globals, units);
            if (!result.Point.IsFinite() || result.Path.Count == 0 || result.Path[^1] != result.Triangle)
                throw new InvalidDataException("Owned wander returned an invalid destination or predecessor path.");
            if (spatialQueries != sample + 1 || previous is not null &&
                (result.Path[0].Mesh != previous.Mesh || result.Path[0].Triangle != previous.Triangle))
                throw new InvalidDataException("Owned wander ignored the current spatial query or started at an old triangle.");
            GD.Print(JsonSerializer.Serialize(new
            {
                kind = "owned-package-wander",
                actor = actor.ToString(),
                package = package.FormKey.ToString(),
                radius = source.LocationRadius,
                sample,
                point = new[] { result.Point.X, result.Point.Y, result.Point.Z },
                triangle = new
                {
                    result.Triangle.Mesh,
                    result.Triangle.Triangle,
                    result.Triangle.Flags,
                    center = new[] { result.Triangle.Center.X, result.Triangle.Center.Y, result.Triangle.Center.Z }
                },
                result.ReachedDistance,
                pathLength = result.Path.Count,
                spatialQueries,
                gameplay = "unverified",
                parity = "unverified",
                recording = false
            }));
            current = current with { Position = [result.Point.X, result.Point.Y, result.Point.Z] };
            previous = result.Triangle;
        }
    }
}
