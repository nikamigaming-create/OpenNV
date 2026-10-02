using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void ExerciseOwnedDoorState(string baseRoot, string mod, string modRoot, string referenceId,
        string questId, short stage, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var key = FalloutDialogueTopic.Find(records, "REFR", referenceId).FormKey;
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var questHash = SHA256.HashData(quest.ReadData());
        var cell = FalloutCellSceneReader.Read(records, world.Get(key).Cell); world.LoadCell(cell);
        var placed = cell.References.Single(value => value.FormKey == key);
        var model = cell.BaseObjects[placed.Base].ModelPath ?? throw new InvalidDataException("Selected door has no source model.");
        Require(content.TryRead(model, null, out var bytes, out _), "Selected owned door model is absent.");
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        var prototype = new RuntimeNativeNifPrototype(bytes, units);
        var roots = new List<Node3D>();
        var changes = 0;
        (RuntimeNativeReferencePresentation Presentation, Node3D Node, RuntimeNativeDoorMotion Motion, RuntimeNifControllerPlayer Controller)
            Bind(FalloutReferenceWorld owner)
        {
            var root = new Node3D(); AddChild(root); roots.Add(root);
            var presentation = new RuntimeNativeReferencePresentation(owner, [placed], reference =>
            {
                var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                    new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
                var node = prototype.InstantiatePlaced(transform); node.SetMeta("opennv_reference_form_key", key.ToString());
                root.AddChild(node);
                var controllers = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node).ToArray();
                var motion = new RuntimeNativeDoorMotion(owner.Get(key), controllers, () => changes++);
                node.AddChild(motion);
                return node;
            });
            root.AddChild(presentation);
            var node = presentation.Resolve(key)!;
            return (presentation, node, node.GetChildren().OfType<RuntimeNativeDoorMotion>().Single(),
                NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node).Single(value => value.HasSequence("Open") && value.HasSequence("Close")));
        }
        static string Pose(Node3D node) => JsonSerializer.Serialize(NodeTraversal.SelfAndDescendants<Node3D>(node)
            .Select(value => new { path = node.GetPathTo(value).ToString(), basis = value.Transform.Basis.ToString(), position = value.Transform.Origin.ToString() }).ToArray());
        static string CollisionPose(Node3D node) => JsonSerializer.Serialize(NodeTraversal.SelfAndDescendants<CollisionShape3D>(node)
            .Select(value => new { path = node.GetPathTo(value).ToString(), basis = value.GlobalTransform.Basis.ToString(), position = value.GlobalPosition.ToString() }).ToArray());
        try
        {
            var live = Bind(world);
            Require(live.Presentation.GetOpenState(key) == 3 && !world.Get(key).DoorOpen,
                "Owned door did not initialize at its retained closed source endpoint.");
            Require(NodeTraversal.SelfAndDescendants<CollisionShape3D>(live.Node).Any(), "Owned door has no source collision.");
            var closed = CollisionPose(live.Node);
            live.Presentation.Apply(new(FalloutReferenceEffectKind.DoorOpenState, quest.FormKey, key, Enable: true));
            Require(live.Presentation.GetOpenState(key) == 2 && world.Get(key).DoorOpen, "Owned door did not enter opening state.");
            live.Controller._Process(live.Controller.FiniteEffectDuration / 2); live.Motion.Synchronize();
            var midPose = Pose(live.Node);
            var clock = JsonSerializer.Serialize(live.Controller.CaptureScriptState());
            live.Presentation.Apply(new(FalloutReferenceEffectKind.DoorOpenState, quest.FormKey, key, Enable: true));
            Require(clock == JsonSerializer.Serialize(live.Controller.CaptureScriptState()), "Repeated source target restarted the door clock.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var restored = Bind(cold);
            Require(restored.Presentation.GetOpenState(key) == 2 && Pose(restored.Node) == midPose &&
                JsonSerializer.Serialize(restored.Controller.CaptureScriptState()) == clock,
                "Cold source door target, pose or text-key cursor changed.");
            restored.Controller._Process(restored.Controller.FiniteEffectDuration); restored.Motion.Synchronize();
            Require(restored.Presentation.GetOpenState(key) == 1 && CollisionPose(restored.Node) != closed,
                "Owned opening did not complete or move its source collision.");
            var effects = 0;
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, cold, quests, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.DoorOpenState && effect.Target == key && !effect.Enable,
                    "Owned stage dispatched activation or another unbound effect.");
                restored.Presentation.Apply(effect); effects++;
            }, GetOpenState: restored.Presentation.GetOpenState));
            var stages = new FalloutQuestStages(records, quests, scripts.StageSteps,
                _ => throw new NotSupportedException("Selected stage unexpectedly requires a condition owner."));
            stages.Enter(quest.FormKey, stage);
            Require(effects == 1 && !cold.Get(key).DoorOpen && restored.Presentation.GetOpenState(key) == 4,
                "Winning authored stage did not close the actual source door.");
            stages.Enter(quest.FormKey, stage); Require(effects == 1, "Retained stage repeated its result.");
            restored.Controller._Process(restored.Controller.FiniteEffectDuration); restored.Motion.Synchronize();
            Require(restored.Presentation.GetOpenState(key) == 3 && CollisionPose(restored.Node) == closed,
                "Owned closing did not restore closed source collision.");
            var doorSnapshot = saved.Single(value => value.Reference == key);
            var changedHash = new string('0', 64);
            var drift = doorSnapshot with
            {
                DoorMotion = doorSnapshot.DoorMotion! with { Sha256 = changedHash },
                ObjectAnimations = doorSnapshot.ObjectAnimations!.Select(value => value with { Sha256 = changedHash }).ToArray(),
            };
            using var rejected = new FalloutReferenceWorld(records); rejected.Restore([drift]); rejected.LoadCell(cell);
            try
            {
                RuntimeNativeDoorMotion.RequireSavedSourceMatch(rejected.Get(key), live.Controller);
                throw new InvalidDataException("Door source drift was accepted.");
            }
            catch (NotSupportedException) { }
            live.Motion.Free();
            try
            {
                _ = live.Presentation.GetOpenState(key);
                throw new InvalidDataException("An unbound Open/Close model reported no animation state.");
            }
            catch (NotSupportedException) { }
            Require(SHA256.HashData(quest.ReadData()).SequenceEqual(questHash), "Owned quest source was mutated.");
            GD.Print($"OPENNV_OWNED_DOOR_STATE_PASS reference={key} quest={quest.FormKey} fixtureStage={stage} effects={effects} states=3,2,1,4,3 sourceCollision=true duplicateNoRestart=true coldPoseClock=true sourceDriftRejected=true unboundOwnerRefused=true noActivation=true changes={changes} recording=false fixture=true campaign=unverified parity=unverified");
        }
        finally
        {
            foreach (var root in roots) root.Free();
            prototype.Scene.Root.Free();
        }
    }
}
