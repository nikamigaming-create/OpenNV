using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedPatrolProbe
{
    internal static void Run(string root, string output)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var results = new List<object>();
        foreach (var (id, packageId) in new (uint, uint)[] { (0xea2b4, 0x23619), (0x15a794, 0x23619), (0x1073e8, 0x16addc) })
        {
            var actor = records.RuntimeFormKey(id);
            var package = records.GetEffective(records.RuntimeFormKey(packageId));
            var route = FalloutPatrolRoute.Read(records, package, actor);
            if (route.Points.Count < 2 || (id == 0x1073e8 ? route.Repeatable || route.Points.Count != 11 : !route.Repeatable || !route.WeaponDrawn))
                throw new InvalidDataException("Selected owned patrol lost its source route/flags.");
            foreach (var point in route.Points)
            {
                point.Arrival?.ValidateScript();
                point.Arrival?.RequireEmptyTopic();
                foreach (var idle in point.Idles?.Idles ?? [])
                {
                    var animation = FalloutActorIdleSource.Resolve(records, records.GetEffective(idle));
                    if (!source.TryResolve(animation.AnimationPath, null, out _))
                        throw new FileNotFoundException(animation.AnimationPath);
                }
            }
            var state = route.Advance(route.Start(_ => 0), true, 0);
            state = route.Advance(state, true, .25);
            var cold = JsonSerializer.Deserialize<FalloutPatrolProgress>(JsonSerializer.Serialize(state))!;
            route.Validate(cold);
            if (cold != state) throw new InvalidDataException("Owned patrol progress changed on cold restoration.");
            results.Add(new { actor = actor.ToString(), package = route.Package.ToString(), route.SourceSha256,
                route.Circular, route.Repeatable, route.WeaponDrawn, points = route.Points.Select(point => new
                { reference = point.Reference.ToString(), point.Position, point.WaitSeconds, idles = point.Idles?.Idles.Count }), cold });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { schema = "opennv-owned-patrol-audit/v1", results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"OPENNV_OWNED_PATROL_PASS actors={results.Count} sourceLinks=true idleResources=true coldWait=true report={output}");
    }
}
