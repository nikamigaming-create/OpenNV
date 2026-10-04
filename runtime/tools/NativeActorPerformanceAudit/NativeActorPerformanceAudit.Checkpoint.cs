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
    private void NpcCheckpoint(string game, string mod, string root, string actorId, string questId,
        short stage, string expected, string[] dependencies)
    {
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var identity = actorId.Split(':');
            var caller = identity is { Length: 2 } ? new FalloutFormKey(identity[0], Convert.ToUInt32(identity[1], 16)) :
                FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
            var globals = FalloutGlobalState.Read(records);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            var rootReference = caller; var enabled = true; var parents = new HashSet<FalloutFormKey>();
            while (world.Get(rootReference).EnableParent is { } parent)
            {
                if (!parents.Add(rootReference)) throw new InvalidDataException("Fixture enable chain is cyclic.");
                enabled ^= parent.Opposite; rootReference = parent.Reference;
            }
            world.SetEnabled(rootReference, enabled); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            // Explicit source state for this isolated continuation fixture.
            // No prior campaign, dialogue or quest result is claimed.
            world.Get(caller).TalkedToPlayer = true;
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference reference)
            {
                var location = owner.Placement(reference.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(location.RotationRadians[0],
                    location.RotationRadians[1], location.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(location.Position[0], location.Position[1], location.Position[2])) * units);
            }
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var templates = owner.InitializeActorTemplates(caller, 1, globals);
                var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                    (_, _, _, _) => new StandardMaterial3D(), owner.EquippedArmor(caller, 1, globals), templates);
                actor.Transform = Placement(owner, placed); fixture.AddChild(actor);
                actor.SetProcess(false); actor.SetPhysicsProcess(false);
                try
                {
                    actor.ConfigureAi(records, quests, cell, reference => Placement(owner, reference),
                        clock: clock, globals: globals, world: owner);
                }
                catch { actor.Free(); throw; }
                return actor;
            }
            var warm = Assemble(world);
            using var warmLifetime = new PackageFixtureLifetime(warm);
            warm._Process(.125);
            if (expected == "dialogue")
            {
                for (var frame = 0; frame < 7200 && !world.Get(caller).DialogueCaptureReady; ++frame)
                    warm._Process(1d / 60);
            }
            var saved = world.Capture(); var actorState = saved.Single(value => value.Reference == caller);
            bool HasOwner(FalloutReferenceSnapshot state) => expected switch
            {
                "furniture" => state.FurnitureContinuation is not null,
                "selection" => state.SelectionFailure is not null,
                "dialogue" => state.DialogueContinuation is not null,
                "binding" => state.PackageBindingFailure is not null,
                _ => throw new ArgumentException("Unknown checkpoint owner fixture."),
            };
            if (!HasOwner(actorState) || world.PendingProcedureCaptureCount != 0)
                throw new InvalidDataException("Source fixture did not reach the required owned checkpoint continuation.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(saved))!);
            cold.LoadCell(cell);
            var resumed = Assemble(cold);
            using var resumedLifetime = new PackageFixtureLifetime(resumed);
            if (resumed.Transform != warm.Transform || resumed.AiError != warm.AiError ||
                resumed.SittingState != warm.SittingState || resumed.CurrentPackage != warm.CurrentPackage ||
                JsonSerializer.Serialize(cold.Get(caller).Capture()) != JsonSerializer.Serialize(world.Get(caller).Capture()))
                throw new InvalidDataException("Native cold NPC assembly changed its pose, source fault, random queue or procedure state.");
            foreach (var delta in new[] { 0d, .01, .125, .25 })
            {
                warm._Process(delta); resumed._Process(delta);
                if (resumed.Transform != warm.Transform || resumed.AiError != warm.AiError ||
                    JsonSerializer.Serialize(cold.Get(caller).Capture()) != JsonSerializer.Serialize(world.Get(caller).Capture()))
                    throw new InvalidDataException("Native cold NPC continuation diverged after identical clock advancement.");
            }
            GD.Print($"OPENNV_NATIVE_NPC_CHECKPOINT_PASS actor={caller} owner={expected} phase={warm.SittingState} " +
                "nativeCold=true exactPose=true clock=true randomAndBlink=true sourceEffectsNotReplayed=true " +
                "fixture=isolated-owned-records campaignAndParity=unverified recording=false");
        }
        finally { if (GodotObject.IsInstanceValid(fixture)) fixture.Free(); }
    }
}
