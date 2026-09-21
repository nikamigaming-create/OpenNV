using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedEncounterZoneProbe
{
    internal static void Run(string root, string cellId, string savePath, string output)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var zones = records.EffectiveRecords("ECZN").Select(FalloutEncounterZone.Read).ToArray();
        var save = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(savePath))!;
        using var world = new FalloutReferenceWorld(records);
        world.RestoreEncounterZones(save.EncounterZones); world.Restore(save.References!); world.RestoreActorOverrides(save.ActorOverrides);
        var globals = FalloutGlobalState.Read(records); globals.Restore(save.Globals!);
        var level = save.Vitals!.Level;
        var center = FalloutCellSceneReader.ReadDefinition(records, records.RuntimeFormKey(Convert.ToUInt32(cellId, 16)));
        var gridOwner = new FalloutExteriorGrid(records);
        var coords = center.Coordinates!.Value; var space = center.Worldspace!.Value;
        var grid = gridOwner.Resolve(space, gridOwner.PersistentCell(space), (coords.X + .5f) * 4096, (coords.Y + .5f) * 4096, 7);
        var pending = new Queue<FalloutCellScene>(); pending.Enqueue(grid.Scene);
        var visited = new HashSet<FalloutFormKey>(grid.Cells.Select(cell => cell.FormKey));
        var actors = new List<object>(); var cells = new List<object>();
        while (pending.TryDequeue(out var authored))
        {
            var scene = world.ComposeResidency(authored, authored == grid.Scene ? grid.Cells : null);
            world.EnterEncounterCell(scene.Cell.FormKey, level);
            cells.Add(new { cell = scene.Cell.FormKey.ToString(), scene.Cell.EditorId, references = scene.References.Count });
            foreach (var reference in scene.References)
            {
                if (reference.Teleport is not null)
                {
                    var destination = FalloutDoorDestinationResolver.Resolve(records, reference).DestinationScene;
                    if (destination.Cell.Worldspace is null && visited.Add(destination.Cell.FormKey)) pending.Enqueue(destination);
                }
                if (scene.BaseObjects[reference.Base].Signature is not ("NPC_" or "CREA")) continue;
                string? error = null; var enabled = world.IsEnabled(reference.FormKey); var absent = false; var resources = false;
                try
                {
                    if (enabled)
                    {
                        var selection = world.InitializeActorTemplates(reference.FormKey, level, globals); absent = selection.Absent;
                        if (!absent && records.GetEffective(reference.Base).Signature == "NPC_")
                        {
                            var armor = world.EquippedArmor(reference.FormKey, level, globals);
                            var appearance = FalloutNpcAppearanceResolver.Resolve(records, reference.Base, reference.FormKey, armor, selection: selection);
                            if (!appearance.CanConstruct) throw new NotSupportedException(string.Join(';', appearance.Blockers));
                            _ = FalloutNpcPreparedGeometry.Read(appearance, source, CancellationToken.None);
                            resources = true;
                        }
                        else if (!absent)
                        {
                            var appearance = FalloutCreatureAppearanceResolver.Resolve(records, reference.Base, reference.FormKey, selection);
                            foreach (var path in appearance.Models.Append(appearance.SkeletonPath))
                                if (!source.TryResolve(path, null, out _)) throw new FileNotFoundException($"Creature resource is absent: {path}");
                            resources = true;
                        }
                    }
                }
                catch (Exception failure) when (failure is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
                { error = failure.Message; }
                actors.Add(new { reference = reference.FormKey.ToString(), cell = scene.Cell.FormKey.ToString(),
                    baseId = scene.BaseObjects[reference.Base].EditorId, enabled, absent, resources, error });
            }
        }
        var snapshots = world.CaptureEncounterZones();
        using var cold = new FalloutReferenceWorld(records); cold.RestoreEncounterZones(snapshots); cold.Restore(world.Capture());
        if (JsonSerializer.Serialize(cold.CaptureEncounterZones()) != JsonSerializer.Serialize(snapshots))
            throw new InvalidDataException("Owned encounter levels changed across a cold restore.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { schema = "opennv-owned-encounter-audit/v1",
            sourceZones = zones.Length, zones = snapshots, cells, actors }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"OPENNV_OWNED_ENCOUNTER_AUDIT zones={zones.Length} cells={cells.Count} actors={actors.Count} cold=true report={output}");
    }
}
