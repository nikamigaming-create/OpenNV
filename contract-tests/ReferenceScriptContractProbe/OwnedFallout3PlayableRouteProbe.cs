using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

// Source and shared-state preflight for the user's route. It deliberately keeps
// ordinary campaign execution separate from readable doors or cold components.
internal static class OwnedFallout3PlayableRouteProbe
{
    internal static int Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var start = FalloutNewGameBootstrap.StartingQuest(records, FalloutInstallationSettings.Read(content));
        var gridDiameter = checked((int)FalloutInstallationSettings.Read(content).Unsigned("General", "uGridsToLoad"));
        var cells = records.EffectiveRecords("CELL").Where(record =>
            EditorId(record).StartsWith("Vault101", StringComparison.OrdinalIgnoreCase) ||
            EditorId(record).Equals("Megaton", StringComparison.OrdinalIgnoreCase)).ToList();
        var grid = new FalloutExteriorGrid(records);
        foreach (var world in records.EffectiveRecords("WRLD").Where(record => EditorId(record).Contains("Megaton", StringComparison.OrdinalIgnoreCase)))
        {
            var persistent = grid.PersistentCell(world.FormKey);
            if (cells.All(value => value.FormKey != persistent)) cells.Add(records.GetEffective(persistent));
        }
        foreach (var cell in cells.ToArray())
        {
            if (FalloutCellSceneReader.ReadDefinition(records, cell.FormKey).Worldspace is not { } world) continue;
            var persistent = grid.PersistentCell(world);
            if (cells.All(value => value.FormKey != persistent)) cells.Add(records.GetEffective(persistent));
        }
        var checks = new List<object>();
        var failures = new List<object>();
        var gridChecks = new List<object>();
        var sourceHashes = new Dictionary<FalloutFormKey, string>(FalloutFormKeyComparer.Instance);
        foreach (var cell in cells)
        {
            Observe(cell);
            try
            {
                var scene = FalloutCellSceneReader.Read(records, cell.FormKey);
                foreach (var door in scene.References.Where(reference => reference.Teleport is not null))
                {
                    Observe(records.GetEffective(door.FormKey));
                    var target = FalloutDoorDestinationResolver.Resolve(records, door);
                    Observe(records.GetEffective(target.Destination.FormKey));
                    Observe(records.GetEffective(target.DestinationScene.Cell.FormKey));
                    if (target.DestinationScene.Cell.Worldspace is { } targetWorld)
                    {
                        var persistent = grid.PersistentCell(targetWorld);
                        var landing = grid.Resolve(targetWorld, persistent, door.Teleport!.Position[0], door.Teleport.Position[1], gridDiameter);
                        if (landing.Scene.References.Count(reference => reference.FormKey == target.Destination.FormKey) != 1)
                            throw new InvalidDataException("Door landing grid omitted or duplicated the real destination reference.");
                        gridChecks.Add(new { door = door.FormKey, world = targetWorld, spatialCell = landing.Scene.Cell.FormKey });
                    }
                    checks.Add(new
                    {
                        source = door.FormKey, sourceCell = scene.Cell.FormKey,
                        sourceCellName = EditorId(cell), destination = target.Destination.FormKey,
                        destinationCell = target.DestinationScene.Cell.FormKey,
                        destinationCellName = EditorId(records.GetEffective(target.DestinationScene.Cell.FormKey)),
                        destinationWorld = target.DestinationScene.Cell.Worldspace,
                        destinationPosition = door.Teleport!.Position,
                        destinationRotation = door.Teleport.RotationRadians,
                        noAlarm = door.Teleport.NoAlarm,
                        coldSharedState = CheckColdDoor(door.FormKey)
                    });
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            {
                failures.Add(new { cell = cell.FormKey, name = EditorId(cell), error = error.Message });
            }
        }
        if (cells.Count == 0 || !cells.Any(cell => EditorId(cell).Equals("Megaton", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Selected Fallout 3 source lacks the requested route endpoints.");
        if (sourceHashes.Any(pair => Hash(records.GetEffective(pair.Key)) != pair.Value))
            throw new InvalidDataException("Route preflight modified an owned source record.");
        var sourceRoutes = new List<object>();
        var megaton = FalloutDialogueTopic.Find(records, "WRLD", "MegatonWorld");
        var graph = new FalloutCampaignPortalGraph(records);
        var destinationCell = grid.PersistentCell(megaton.FormKey);
        foreach (var name in new[] { "Vault101a", "Vault101b", "Vault101d" })
        {
            var origin = FalloutDialogueTopic.Find(records, "CELL", name);
            try
            {
                // This predicate measures directed source topology. Access,
                // quest conditions and physical walking remain independent.
                var route = graph.Find(origin.FormKey, destinationCell, _ => true);
                sourceRoutes.Add(new { origin = origin.FormKey, destination = destinationCell, doors = route });
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { failures.Add(new { cell = origin.FormKey, name, error = error.Message }); }
        }
        Console.WriteLine("OPENNV_FO3_ROUTE_PREFLIGHT " + JsonSerializer.Serialize(new
        {
            schema = "opennv-fo3-playable-route-preflight/v1", source = content.StackId,
            startingQuest = start.FormKey, startingQuestName = EditorId(start),
            cells = cells.Select(cell => new { cell.FormKey, name = EditorId(cell) }),
            doors = checks, exteriorLandingGrids = gridChecks, directedSourceRoutes = sourceRoutes,
            accessAndConditionsEvaluated = false, physicalWalking = "UNEXECUTED", failures, sourceUnchanged = true,
            freshPlayableSave = "UNEXECUTED-requires-returned-production-Player-update",
            coldPlayableLoad = "UNEXECUTED", ordinaryVaultExit = "UNEXECUTED",
            ordinaryMegatonArrival = "UNEXECUTED", gameLaunched = false, framesRecorded = false
        }));
        return failures.Count == 0 ? 0 : 1;

        void Observe(FalloutPluginRecord record) => sourceHashes.TryAdd(record.FormKey, Hash(record));
        bool CheckColdDoor(FalloutFormKey door)
        {
            using var world = new FalloutReferenceWorld(records);
            _ = world.Get(door);
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(snapshots);
            if (JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(snapshots))
                throw new InvalidDataException("Door shared-state cold restoration changed the source reference.");
            return true;
        }
    }

    private static string EditorId(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == "EDID").ToArray();
        return fields.Length == 0 ? "" : FalloutDialogueTopic.Text(fields.Single().Data.Span);
    }
    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
