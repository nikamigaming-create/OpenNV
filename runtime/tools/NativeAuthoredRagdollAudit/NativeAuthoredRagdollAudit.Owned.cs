using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeAuthoredRagdollAudit
{
    private async Task ExerciseOwned(string game, string mod, string root, string[] references, string[] dependencies)
    {
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var globals = FalloutGlobalState.Read(records);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            foreach (var text in references)
            {
                var fields = text.Split(':');
                if (fields.Length != 2) throw new ArgumentException("Corpse source identity requires plugin:hex-object-id.");
                var identity = new FalloutFormKey(fields[0], Convert.ToUInt32(fields[1], 16));
                var record = records.GetEffective(identity);
                if (record.Signature != "ACHR") throw new InvalidDataException("Authored corpse fixture requires a winning source ACHR.");
                var authored = FalloutAuthoredRagdoll.Read(record) ?? throw new InvalidDataException("Selected source corpse has no XRGD.");
                if (authored.BipedRotation is null || authored.BipedRotation.All(value => value == 0))
                    throw new InvalidDataException("Owned XRGB proof requires a nonzero authored accumulation rotation.");
                using var world = new FalloutReferenceWorld(records);
                var cell = FalloutCellSceneReader.Read(records, world.Placement(identity).Cell); world.LoadCell(cell);
                var placed = cell.References.Single(value => value.FormKey == identity);
                var state = world.Get(identity);
                var templates = world.InitializeActorTemplates(identity, 1, globals);
                if (records.GetEffective(state.Base).Signature != "NPC_" || !FalloutActorHealthSource.StartsDead(records, state.Base, templates))
                    throw new InvalidDataException("Owned fixture is not an authored source-started-dead NPC.");
                Transform3D Placement(FalloutReferenceWorld owner)
                {
                    var pose = owner.Placement(identity);
                    return new(GamebryoCoordinate.ConvertReferenceEuler(new(pose.RotationRadians[0], pose.RotationRadians[1],
                        pose.RotationRadians[2]), placed.Scale), GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units);
                }
                RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
                {
                    var actor = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
                        owner.EquippedArmor(identity, 1, globals), owner.InitializeActorTemplates(identity, 1, globals), owner.ActorAppearanceOverride(identity));
                    try
                    {
                        actor.Transform = Placement(owner);
                        RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
                        actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                            owner, owner.Get(identity), records, content, 2, 3);
                        fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                        actor.Combat.SetProcess(false); actor.Combat.SetPhysicsProcess(false);
                        return actor;
                    }
                    catch { actor.Free(); throw; }
                }
                var actor = Assemble(world);
                try
                {
                    if (!content.TryRead(actor.Appearance.SkeletonPath, null, out var sourceBytes, out var sourceIdentity))
                        throw new FileNotFoundException("Owned skeleton disappeared.");
                    var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
                    var source = actor.Skeleton.Source;
                    var bound = FalloutNifAuthoredRagdoll.Bind(source, authored);
                    var accumulation = FalloutNifAuthoredRagdoll.BindAccumulationRoot(source);
                    var rootBone = actor.Skeleton.BoneIndex(accumulation.Name);
                    var rootPosition = actor.Skeleton.Node.GetBonePosePosition(rootBone);
                    var rootScale = actor.Skeleton.Node.GetBonePoseScale(rootBone);
                    var referencePose = actor.Transform;
                    var receipts = (state.ScriptError, state.PackageBindingFailure, state.SelectionFailure);
                    var sourceBodies = source.Blocks.Where(block => block.TypeName is "bhkRigidBody" or "bhkRigidBodyT")
                        .Select(block => (FalloutNifRigidBody)source.ReadObject(block.Index)).ToDictionary(body => body.Block.Index,
                            body => SHA256.HashData(body.SourceBytes.Span));
                    await Deferred();
                    Require(actor.Combat!.Error is null && actor.Combat.Dead, "Owned source death presentation still failed: " + actor.Combat.Error);
                    var rig = actor.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
                    rig.SetProcess(false); rig.SetPhysicsProcess(false);
                    var rigid = rig.GetChildren().OfType<RigidBody3D>().ToArray();
                    foreach (var body in rigid) body.Freeze = true;
                    Require(rig.Active && rigid.Length == authored.Bones.Count && rigid.All(body =>
                        body.GetChildren().OfType<CollisionShape3D>().Any(shape => shape.Shape is not null)) &&
                        actor.FindChildren("*", "Area3D", true, false).OfType<Area3D>().Where(area => area.HasMeta("opennv_nif_collision_bone"))
                            .All(area => area.CollisionLayer == 0), "Owned corpse did not replace living queries with actual source bodies/shapes.");
                    Require(actor.Transform == referencePose && actor.Skeleton.Node.GetBonePosePosition(rootBone) == rootPosition &&
                        actor.Skeleton.Node.GetBonePoseScale(rootBone) == rootScale &&
                        BasisNear(new(actor.Skeleton.Node.GetBonePoseRotation(rootBone)), ExpectedRotation(authored.BipedRotation)),
                        "Owned XRGB changed source root components or placed reference transform.");
                    foreach (var (name, pose) in bound)
                    {
                        var bone = actor.Skeleton.BoneIndex(name);
                        var local = actor.Skeleton.Node.GetBonePose(bone);
                        var expectedPosition = new Vector3(pose.Position[0], pose.Position[2], -pose.Position[1]) * units;
                        Require(local.Origin.DistanceTo(expectedPosition) < .0002f &&
                            BasisNear(local.Basis.Orthonormalized(), ExpectedRotation(pose.RotationRadians)),
                            "Owned ordered XRGD did not publish the original local body pose: " + name);
                    }
                    var initial = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(state.Capture()))!;
                    Require(initial.Ragdoll is not null && sourceHash.Equals(initial.Ragdoll.SkeletonSha256, StringComparison.OrdinalIgnoreCase),
                        "Owned source ragdoll omitted its exact skeleton identity.");
                    actor.Free();
                    var retired = state.Capture();
                    Require(JsonSerializer.Serialize(initial) == JsonSerializer.Serialize(retired), "Normal corpse retirement lost its state or failure receipts.");
                    using var coldWorld = new FalloutReferenceWorld(records);
                    coldWorld.Restore([retired]); coldWorld.LoadCell(cell);
                    var cold = Assemble(coldWorld);
                    try
                    {
                        await Deferred();
                        Require(cold.Combat!.Error is null, "Cold authored corpse still failed: " + cold.Combat.Error);
                        var coldRig = cold.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
                        coldRig.SetProcess(false); coldRig.SetPhysicsProcess(false);
                        foreach (var body in coldRig.GetChildren().OfType<RigidBody3D>()) body.Freeze = true;
                        var restored = coldWorld.Get(identity).Capture();
                        Require(coldRig.Active && restored.Ragdoll is not null &&
                            SameBodies(initial.Ragdoll!, restored.Ragdoll) && cold.Transform == referencePose &&
                            cold.Skeleton.Node.GetBonePosePosition(rootBone) == rootPosition &&
                            cold.Skeleton.Node.GetBonePoseScale(rootBone) == rootScale &&
                            BasisNear(new(cold.Skeleton.Node.GetBonePoseRotation(rootBone)), ExpectedRotation(authored.BipedRotation)) &&
                            (state.ScriptError, state.PackageBindingFailure, state.SelectionFailure) == receipts &&
                            restored.ScriptError == initial.ScriptError &&
                            JsonSerializer.Serialize(restored.PackageBindingFailure) == JsonSerializer.Serialize(initial.PackageBindingFailure) &&
                            JsonSerializer.Serialize(restored.SelectionFailure) == JsonSerializer.Serialize(initial.SelectionFailure),
                            "Cold authored corpse replaced saved physical pose, placed reference or source failure receipts.");
                        Require(content.TryRead(cold.Appearance.SkeletonPath, null, out var afterBytes, out _) && sourceBytes.SequenceEqual(afterBytes) &&
                            cold.Skeleton.Source.Blocks.Where(block => sourceBodies.ContainsKey(block.Index)).All(block =>
                                sourceBodies[block.Index].SequenceEqual(SHA256.HashData(((FalloutNifRigidBody)cold.Skeleton.Source.ReadObject(block.Index)).SourceBytes.Span))),
                            "Owned source skeleton or Havok bytes changed.");
                        GD.Print($"OPENNV_OWNED_AUTHORED_CORPSE_PASS reference={identity} source={sourceIdentity} sha256={sourceHash} " +
                            $"firstChild={accumulation.Block.Index} bodies={rigid.Length} rotation=true orderedPose=true retirement=true coldRootAndBodies=true receipts=true sourceUnchanged=true");
                    }
                    finally { cold.Free(); }
                }
                finally { if (GodotObject.IsInstanceValid(actor)) actor.Free(); }
            }
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }

    private static bool SameBodies(FalloutActorRagdollState first, FalloutActorRagdollState second) =>
        first.SkeletonSha256 == second.SkeletonSha256 && first.Bodies.Count == second.Bodies.Count &&
        first.Bodies.OrderBy(body => body.SourceBody).Zip(second.Bodies.OrderBy(body => body.SourceBody)).All(pair =>
            pair.First.SourceBody == pair.Second.SourceBody && pair.First.Sleeping == pair.Second.Sleeping &&
            pair.First.Transform.Zip(pair.Second.Transform).All(value => MathF.Abs(value.First - value.Second) < .0002f) &&
            pair.First.LinearVelocity.SequenceEqual(pair.Second.LinearVelocity) && pair.First.AngularVelocity.SequenceEqual(pair.Second.AngularVelocity));
}
