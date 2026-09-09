using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class WorldRoute
{
    internal static int Run(FalloutPluginStack records, string requestPath)
    {
        using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
        var data = request.RootElement;
        var world = records.RuntimeFormKey(Convert.ToUInt32(data.GetProperty("world").GetString(), 16));
        if (records.GetEffective(world).Signature != "WRLD") throw new InvalidDataException("Route world is not WRLD.");
        Vector3 Point(string name)
        {
            var value = data.GetProperty(name).EnumerateArray().Select(component => component.GetSingle()).ToArray();
            if (value.Length != 3 || value.Any(component => !float.IsFinite(component)))
                throw new InvalidDataException("Route points require three finite game-unit coordinates.");
            return new(value[0], value[1], value[2]);
        }
        var cells = records.EffectiveRecords("CELL").Where(record => FalloutCellSceneReader.ParentWorldspace(record) == world)
            .Select(record => record.FormKey).ToHashSet();
        var unavailable = new Dictionary<string, string>();
        var graph = CellNavigationGraph.LoadOwned(records, cells, (key, error) => unavailable.Add(key.ToString(), error.Message));
        var start = Point("start"); var end = Point("end");
        var path = graph.FindPath(start, end);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-world-route/v1",
            world = world.ToString(),
            meshes = graph.NavMeshes,
            triangles = graph.Triangles,
            unavailable,
            startProjectionError = graph.FindNearestPoint(start).DistanceTo(start),
            endProjectionError = path[^1].DistanceTo(end),
            lengthGameUnits = new[] { start }.Concat(path).Zip(path).Sum(pair => pair.First.DistanceTo(pair.Second)),
            waypoints = path.Select(point => new[] { point.X, point.Y, point.Z }),
            boundary = "source-NAVM-route; execute ordinary input against resident capsule collision; stop on obstruction"
        }));
        return 0;
    }
}
