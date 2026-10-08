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
    private void ExerciseOwnedActivationParent(string gameRoot, string mod, string modRoot,
        string parentSelector, string childSelector, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(gameRoot);
        RuntimeLiveContentSource.Configure(gameRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        try { Exercise(); }
        finally { RuntimeLiveContentSource.Clear(); }
        void Exercise()
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var parentRecord = FalloutDialogueTopic.Find(records, "REFR", parentSelector);
            var childRecord = FalloutDialogueTopic.Find(records, "REFR", childSelector);
            var parent = parentRecord.FormKey; var child = childRecord.FormKey;
            var declaration = FalloutActivationParents.Read(childRecord).Single(value => value.Parent == parent);
            Require(declaration.DelaySeconds > 0 && world.Get(parent).Cell == world.Get(child).Cell,
                "Selected positive activation-parent relation requires two real references in the same source cell.");
            var sourceCell = FalloutCellSceneReader.Read(records, world.Get(parent).Cell);
            var references = sourceCell.References.Where(reference => reference.FormKey == parent || reference.FormKey == child).ToArray();
            Require(references.Length == 2 && sourceCell.BaseObjects[world.Get(parent).Base].Signature == "ACTI" &&
                sourceCell.BaseObjects[world.Get(child).Base].Signature == "DOOR" && world.Get(parent).Script is null && world.Get(child).Script is not null,
                "Selected native relay requires its unchanged default ACTI parent and scripted DOOR child.");
            var cell = sourceCell with { References = references }; world.LoadCell(cell);
            var sources = references.Select(reference => records.GetEffective(reference.FormKey))
                .Concat(references.Select(reference => records.GetEffective(reference.Base)))
                .Append(world.Get(child).Script!.Record).DistinctBy(record => record.FormKey).ToArray();
            var hashes = sources.Select(record => Convert.ToHexString(SHA256.HashData(record.ReadData()))).ToArray();
            var prototypes = new Dictionary<FalloutFormKey, RuntimeNativeNifPrototype>();
            var resources = new List<object>(); var roots = new List<Node3D>(); var coldWorlds = new List<FalloutReferenceWorld>();
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var defaults = new List<(FalloutFormKey Reference, FalloutFormKey? Action)>();
            var interactions = 0;
            Transform3D Placement(FalloutPlacedReference reference) => new(
                GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            (RuntimeNativeReferencePresentation Presentation, RuntimeNativeReferenceEvents Events) Bind(FalloutReferenceWorld owner)
            {
                var root = new Node3D(); AddChild(root); roots.Add(root);
                var presentation = new RuntimeNativeReferencePresentation(owner, references, reference =>
                {
                    if (!prototypes.TryGetValue(reference.Base, out var prototype))
                    {
                        var model = sourceCell.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Selected relay has no source model.");
                        Require(content.TryRead(model, null, out var bytes, out var identity), "Selected relay model is absent.");
                        prototype = new RuntimeNativeNifPrototype(bytes, units); prototypes.Add(reference.Base, prototype);
                        resources.Add(new { reference = reference.FormKey.ToString(), model, identity, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                    }
                    var node = prototype.InstantiatePlaced(Placement(reference));
                    node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString()); root.AddChild(node);
                    var controllers = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node).ToArray();
                    RuntimeNativeDoorMotion.Attach(node, owner.Get(reference.FormKey), controllers, () => { });
                    return node;
                });
                root.AddChild(presentation);
                foreach (var reference in references) _ = presentation.Resolve(reference.FormKey);
                foreach (var node in NodeTraversal.SelfAndDescendants<Node>(root)) { node.SetProcess(false); node.SetPhysicsProcess(false); }
                var events = new RuntimeNativeReferenceEvents { ReportDivergence = error => throw new InvalidDataException(error) };
                events.SetProcess(false);
                events.Interact = (reference, node, signature, action) =>
                {
                    Require(reference.FormKey == child && signature == "DOOR" && action == parent && owner.IsActivationParent(child, action),
                        "Relay interaction did not retain its actual source parent/default door action.");
                    node!.GetChildren().OfType<RuntimeNativeDoorMotion>().Single().Activate(); interactions++;
                };
                events.Configure(records, owner, new(records), cell, root, new((_, _) => false, effect =>
                {
                    Require(effect.Kind == FalloutReferenceEffectKind.DefaultActivate, "Selected relay emitted an unsupported component effect.");
                    var target = effect.Target ?? effect.Source; defaults.Add((target, effect.Argument));
                    events.DefaultActivate(target, effect.Argument);
                }), Placement, units, 1);
                root.AddChild(events);
                return (presentation, events);
            }
            static void Pump(RuntimeNativeReferenceEvents events, float seconds)
            {
                events.SetProcess(true);
                try { events._Process(seconds); }
                finally { events.SetProcess(false); }
            }
            static RuntimeNifControllerPlayer Controller(Node3D node) => NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node)
                .Single(value => value.HasSequence("Open") && value.HasSequence("Close"));
            static string Pose(Node3D node) => JsonSerializer.Serialize(NodeTraversal.SelfAndDescendants<Node3D>(node).Select((value, index) => new
            {
                index,
                geometry = value.GetMeta("opennv_nif_geometry_block", -1).AsInt32(),
                collision = value.GetMeta("opennv_nif_collision_body", -1).AsInt32(),
                transform = TransformValues(value.Transform)
            }));
            static string CollisionPose(Node3D node) => JsonSerializer.Serialize(NodeTraversal.SelfAndDescendants<CollisionObject3D>(node).Select(value => new
            {
                collisionObject = value.GetMeta("opennv_nif_collision_object", -1).AsInt32(),
                collisionBody = value.GetMeta("opennv_nif_collision_body", -1).AsInt32(),
                transform = TransformValues(value.GlobalTransform)
            }));
            static float[] TransformValues(Transform3D value) => [value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z,
                value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z, value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z,
                value.Origin.X, value.Origin.Y, value.Origin.Z];
            try
            {
                var live = Bind(world); var liveChild = live.Presentation.Nodes[child];
                var collider = NodeTraversal.SelfAndDescendants<CollisionObject3D>(liveChild).First();
                var closed = CollisionPose(liveChild);
                Require(live.Events.TryActivate(collider), "Selected child's actual native collider did not admit ordinary activation.");
                Pump(live.Events, 0);
                Require(defaults.Count == 0 && !world.Get(child).DoorOpen && world.Get(child).ScriptError is null,
                    "Selected unchanged OnActivate did not suppress the direct player action.");
                var parentCollider = NodeTraversal.SelfAndDescendants<CollisionObject3D>(live.Presentation.Nodes[parent]).First();
                Require(live.Events.TryActivate(parentCollider), "Selected real switch did not admit its ordinary native activation.");
                Pump(live.Events, 0);
                Require(defaults.Count == 1 && defaults[0].Reference == parent && world.Get(parent).DoorOpen &&
                    world.Get(parent).ActivationRelay is { Children.Count: 1 }, "Default switch action did not arm its source child once.");
                Pump(live.Events, declaration.DelaySeconds / 2);
                Require(!world.Get(child).DoorOpen && interactions == 0, "Native delayed child opened before its authored threshold.");
                var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
                var cold = new FalloutReferenceWorld(records); coldWorlds.Add(cold); cold.Restore(saved); cold.LoadCell(cell);
                var restored = Bind(cold); var coldChild = restored.Presentation.Nodes[child];
                Require(Pose(coldChild) == Pose(liveChild) && JsonSerializer.Serialize(cold.Get(parent).ActivationRelay) ==
                    JsonSerializer.Serialize(world.Get(parent).ActivationRelay), "Cold source pose or parent cycle changed before delivery.");
                Pump(live.Events, declaration.DelaySeconds / 2); Pump(restored.Events, declaration.DelaySeconds / 2);
                Require(world.Get(child).DoorOpen && cold.Get(child).DoorOpen && interactions == 2 &&
                    defaults.Where(value => value.Reference == child).All(value => value.Action == parent),
                    "Actual native child did not receive one parent action per world at the source crossing.");
                var liveController = Controller(liveChild); var coldController = Controller(coldChild);
                var admittedClock = JsonSerializer.Serialize(coldController.CaptureScriptState());
                for (var frame = 0; frame < 4; frame++) { Pump(live.Events, 0); Pump(restored.Events, 0); }
                Require(interactions == 2 && admittedClock == JsonSerializer.Serialize(coldController.CaptureScriptState()) &&
                    cold.Get(parent).ActivationRelay is { Children.Count: 0 }, "Reentrant child Activate repeated the due relay or restarted its clock.");
                liveController._Process(liveController.FiniteEffectDuration / 2); coldController._Process(coldController.FiniteEffectDuration / 2);
                liveChild.GetChildren().OfType<RuntimeNativeDoorMotion>().Single().Synchronize();
                coldChild.GetChildren().OfType<RuntimeNativeDoorMotion>().Single().Synchronize();
                Require(Pose(liveChild) == Pose(coldChild) && CollisionPose(liveChild) == CollisionPose(coldChild) &&
                    JsonSerializer.Serialize(liveController.CaptureScriptState()) == JsonSerializer.Serialize(coldController.CaptureScriptState()),
                    "Warm/cold delivered source collision, pose or animation clock diverged.");
                liveController._Process(liveController.FiniteEffectDuration); coldController._Process(coldController.FiniteEffectDuration);
                liveChild.GetChildren().OfType<RuntimeNativeDoorMotion>().Single().Synchronize();
                coldChild.GetChildren().OfType<RuntimeNativeDoorMotion>().Single().Synchronize();
                Pump(restored.Events, declaration.DelaySeconds + 1);
                Require(cold.Get(child).DoorMotion?.OpenState == 1 && interactions == 2 && CollisionPose(coldChild) != closed,
                    "Settled actual child collision did not open, or a consumed relay closed it again.");
                for (var index = 0; index < sources.Length; index++)
                    Require(Convert.ToHexString(SHA256.HashData(sources[index].ReadData())) == hashes[index], "Owned relay source bytes changed.");
                GD.Print(JsonSerializer.Serialize(new
                {
                    schema = "opennv-owned-activation-parent/v1",
                    runtimeMvid = typeof(RuntimeNativeDoorMotion).Module.ModuleVersionId,
                    parent = parent.ToString(),
                    child = child.ToString(),
                    cell = cell.Cell.FormKey.ToString(),
                    sourceDelay = declaration.DelaySeconds,
                    sources = sources.Select((record, index) => new
                    {
                        identity = record.FormKey.ToString(),
                        signature = record.Signature,
                        winner = record.Plugin.Name,
                        masters = record.Plugin.Masters,
                        sha256 = hashes[index]
                    }),
                    resources,
                    sourcePlayerSuppression = true,
                    sourceParentAction = true,
                    reentrantNoReplay = true,
                    coldCycle = true,
                    coldPoseClockCollision = true,
                    sourceUnchanged = true,
                    interactions,
                    fixture = true,
                    recording = false,
                    boundary = "Positive selected source delay/native component; immediate synchronous timing, sibling registration order, off-cell semantics, campaign saving/traversal and matched parity remain unverified."
                }));
                GD.Print("OPENNV_OWNED_ACTIVATION_PARENT_PASS sourceDelay=true parentAction=true coldCycle=true nativeCollision=true reentrantNoReplay=true sourceUnchanged=true fixture=true recording=false campaign=unverified parity=unverified");
            }
            finally
            {
                foreach (var root in roots) root.Free();
                foreach (var prototype in prototypes.Values) prototype.Scene.Root.Free();
                foreach (var cold in coldWorlds) cold.Dispose();
            }
        }
    }
}
