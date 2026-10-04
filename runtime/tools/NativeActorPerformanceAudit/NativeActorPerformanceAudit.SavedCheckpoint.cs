using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    // A private reached save is input, never a packaged fixture or stage override.
    private void SavedActorCheckpoint(string game, string mod, string root, string path,
        string[] actors, string[] dependencies)
    {
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(path)) ??
                throw new InvalidDataException("Reached checkpoint is absent.");
            if (saved.Schema != FalloutNativeCampaignSave.ExpectedSchema || saved.SaveCompatibilityId != content.SaveCompatibilityId)
                throw new InvalidDataException("Reached checkpoint belongs to a different schema or source stack.");
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe"))); clock.Restore(saved.GameTime!);
            using var world = new FalloutReferenceWorld(records);
            world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References!);
            world.RestoreActorOverrides(saved.ActorOverrides);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            Transform3D Placement(FalloutPlacedReference reference)
            {
                var location = world.Placement(reference.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(location.RotationRadians[0],
                    location.RotationRadians[1], location.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(location.Position[0], location.Position[1], location.Position[2])) * units);
            }
            var residentCells = new HashSet<FalloutFormKey>();
            foreach (var id in actors)
            {
                var identity = id.Split(':'); var caller = new FalloutFormKey(identity[0], Convert.ToUInt32(identity[1], 16));
                var expected = saved.References!.Single(value => value.Reference == caller);
                var cell = FalloutCellSceneReader.Read(records, world.Placement(caller).Cell);
                if (residentCells.Add(cell.Cell.FormKey)) world.LoadCell(cell);
                var placed = cell.References.Single(value => value.FormKey == caller);
                var templates = world.InitializeActorTemplates(caller, saved.Vitals?.Level ?? 1, globals);
                Node3D actor;
                if (records.GetEffective(expected.Base).Signature == "NPC_")
                {
                    var npc = RuntimeNativeNpc.Create(records, content, placed, units,
                        (_, _, _, _) => new StandardMaterial3D(), world.EquippedArmor(caller, saved.Vitals?.Level ?? 1, globals),
                        templates, world.ActorAppearanceOverride(caller));
                    actor = npc; npc.Transform = Placement(placed);
                    try
                    {
                        // Match product ownership order: combat attaches before AI;
                        // its Ready follows the retried pre-begin continuation.
                        npc.Combat = RuntimeNativeActorCombat.Attach(npc, npc.Skeleton, npc.Appearance.SkeletonPath,
                            world, world.Get(caller), records, content, 1, 1);
                        npc.ConfigureAi(records, quests, cell, Placement, clock: clock, globals: globals, world: world);
                        fixture.AddChild(npc);
                    }
                    catch { npc.Free(); throw; }
                }
                else
                {
                    var creature = RuntimeNativeCreature.Create(records, content, placed, world.Get(caller), units);
                    actor = creature; creature.Transform = Placement(placed); fixture.AddChild(creature);
                    try
                    {
                        creature.Combat = RuntimeNativeActorCombat.Attach(creature, creature.Skeleton, creature.Appearance.SkeletonPath,
                            world, world.Get(caller), records, content, 1, 1);
                        creature.ConfigureAi(records, quests, world, clock, globals);
                    }
                    catch { creature.Free(); throw; }
                }
                try
                {
                    actor.SetProcess(false); actor.SetPhysicsProcess(false);
                    var actual = world.Capture().Single(value => value.Reference == caller);
                    if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(expected))
                        throw new InvalidDataException($"Native reached actor {caller} changed its retained state during cold attachment.");
                    GD.Print($"OPENNV_NATIVE_SAVED_ACTOR_CHECKPOINT_PASS actor={caller} exactState=true nativeAttachOrder=true sourceEffectsNotReplayed=true pixelsAndCampaignAdvance=unverified recording=false");
                }
                finally { actor.Free(); }
            }
        }
        finally { if (GodotObject.IsInstanceValid(fixture)) fixture.Free(); }
    }
}
