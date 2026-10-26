using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditOwnedObjectPhysics(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var configuration = RuntimeConfiguration.Load();
        var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
        var reference = cell.References.Single(value => cell.BaseObjects[value.Base].ModelPath?.Contains("skull", StringComparison.OrdinalIgnoreCase) == true);
        cell = cell with { References = [reference] };
        var path = cell.BaseObjects[reference.Base].ModelPath!;
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var prototype = new RuntimeNativeNifPrototype(bytes, configuration.World.GameUnitsToMeters);
        var scene = new Node3D(); AddChild(scene);
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        try
        {
            var placement = new Transform3D(Basis.FromEuler(new(.2f, .4f, .1f)), new(3, 8, -2));
            var model = prototype.InstantiatePlaced(placement); scene.AddChild(model);
            var presentation = new RuntimeNativeReferencePresentation(world, cell.References, _ => throw new InvalidOperationException("Unexpected materialization."));
            presentation.Register(reference.FormKey, model); scene.AddChild(presentation);
            var body = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(model).Single();
            body.LinearVelocity = new(1.3f, 2, -.7f); body.AngularVelocity = new(.4f, -.6f, .8f);
            await PhysicsFrames(12);
            var before = body.CapturePhysics();
            Require(before.Transform[9] != placement.Origin.X && !before.Sleeping, "Owned dynamic body never moved.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            Require(JsonSerializer.Serialize(saved.Single().ObjectPhysics!.Single()) == JsonSerializer.Serialize(before), "World save lost native physical state.");
            var liveCapture = world.Get(reference.FormKey).CaptureObjectPhysics;
            var preparation = new Node3D();
            try
            {
                var preparedModel = prototype.InstantiatePlaced(placement); preparation.AddChild(preparedModel);
                var prepared = new RuntimeNativeReferencePresentation(world, cell.References, _ => null);
                prepared.Register(reference.FormKey, preparedModel); preparation.AddChild(prepared);
                Require(ReferenceEquals(world.Get(reference.FormKey).CaptureObjectPhysics, liveCapture),
                    "Detached model preparation replaced the live physical save owner.");
            }
            finally { preparation.Free(); }
            Require(ReferenceEquals(world.Get(reference.FormKey).CaptureObjectPhysics, liveCapture),
                "Discarded model preparation retired the live physical save owner.");
            scene.RemoveChild(presentation); presentation.Free(); model.Free();
            Require(world.Get(reference.FormKey).CaptureObjectPhysics is null && world.Get(reference.FormKey).ObjectPhysics is { Count: 1 },
                "Unloading the model did not retain its physical continuation.");

            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            var coldScene = new Node3D();
            // Register while detached, matching source preparation before the
            // actual cell enters the tree. Restore must wait for _Ready.
            var restoredModel = prototype.InstantiatePlaced(placement); coldScene.AddChild(restoredModel);
            var restored = new RuntimeNativeReferencePresentation(cold, cell.References, _ => throw new InvalidOperationException("Unexpected materialization."));
            restored.Register(reference.FormKey, restoredModel); coldScene.AddChild(restored);
            scene.AddChild(coldScene);
            var restoredBody = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(restoredModel).Single();
            Require(JsonSerializer.Serialize(restoredBody.CapturePhysics()) == JsonSerializer.Serialize(before),
                "Cold reconstruction changed transform, velocities, sleep or random state.");
            Require(restoredBody.GlobalPosition.DistanceTo(new(before.Transform[9], before.Transform[10], before.Transform[11])) < .00001f,
                "Cold body returned to authored placement.");
            Refuses(() => restoredBody.RestorePhysics(before with { Sha256 = new string('0', 64) }), "Changed model hash was admitted.");
            Refuses(() => restoredBody.RestorePhysics(before with { Body = before.Body + 1 }), "Changed source body was admitted.");
            Refuses(() => FalloutObjectPhysicsSnapshot.ValidateSet([before, before]), "Duplicate saved body was admitted.");
            Refuses(() => (before with { Transform = Enumerable.Repeat(0f, 12).ToArray() }).Validate(), "Singular saved body was admitted.");
            Refuses(() => (before with { LinearVelocity = [float.NaN, 0, 0] }).Validate(), "Nonfinite velocity was admitted.");
            var position = restoredBody.GlobalPosition;
            await PhysicsFrames(12);
            Require(restoredBody.GlobalPosition.DistanceTo(position) > .05f && restoredBody.LinearVelocity.Y < before.LinearVelocity[1],
                "Cold body did not resume velocity and gravity.");
            restoredBody.LinearVelocity = Vector3.Zero; restoredBody.AngularVelocity = Vector3.Zero; restoredBody.Sleeping = true;
            var sleeping = restoredBody.CapturePhysics();
            restoredBody.RestorePhysics(sleeping);
            Require(restoredBody.Sleeping, "Restoring physical state woke a sleeping body.");
            // Retire the body before its sibling presentation, as happens in
            // whole-cell/session teardown. The last live state must win over
            // the older campaign snapshot even after native body disposal.
            restoredBody.Free();
            coldScene.Free();
            Require(JsonSerializer.Serialize(cold.Get(reference.FormKey).ObjectPhysics!.Single()) == JsonSerializer.Serialize(sleeping),
                "Post-order teardown lost the final physical continuation or reused the old saved state.");
            GD.Print($"OPENNV_NATIVE_OBJECT_PHYSICS_PASS reference={reference.FormKey} source={path} " +
                "jsonRoundtrip=true detachedRestore=true motionContinues=true sleep=true sourceDriftRefused=true unloadRetained=true postOrderRetained=true");
        }
        finally { scene.Free(); prototype.Scene.Root.Free(); }

        var grenade = records.GetEffective(records.RuntimeFormKey(0x1743b2));
        var grenadeCell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(grenade)!.Value);
        var placed = grenadeCell.References.Single(value => value.FormKey == grenade.FormKey);
        grenadeCell = grenadeCell with { References = [placed] };
        var grenadePath = grenadeCell.BaseObjects[placed.Base].ModelPath!;
        if (!content.TryRead(grenadePath, null, out var grenadeBytes, out _)) throw new FileNotFoundException(grenadePath);
        var grenadePrototype = new RuntimeNativeNifPrototype(grenadeBytes, configuration.World.GameUnitsToMeters);
        var grenadeScene = new Node3D(); AddChild(grenadeScene);
        using var grenadeWorld = new FalloutReferenceWorld(records); grenadeWorld.LoadCell(grenadeCell);
        try
        {
            var model = grenadePrototype.InstantiatePlaced(new Transform3D(Basis.Identity, new(0, 4, 0)));
            grenadeScene.AddChild(model);
            var presentation = new RuntimeNativeReferencePresentation(grenadeWorld, grenadeCell.References, _ => null);
            presentation.Register(placed.FormKey, model); grenadeScene.AddChild(presentation);
            await PhysicsFrames(3);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(grenadeWorld.Capture()))!;
            Require(saved.Single().ObjectPhysics is { Count: > 0 }, "Placed grenade has no captured physical body.");
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            Require(JsonSerializer.Serialize(cold.Capture().Single().ObjectPhysics) == JsonSerializer.Serialize(saved.Single().ObjectPhysics),
                "Placed grenade physics changed during world restoration.");
            GD.Print("OPENNV_NATIVE_PLACED_GRENADE_PHYSICS_PASS source=PGRE jsonRoundtrip=true worldRestore=true");
        }
        finally { grenadeScene.Free(); grenadePrototype.Scene.Root.Free(); }

        async Task PhysicsFrames(int count)
        {
            for (var index = 0; index < count; index++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Refuses(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(message);
        }
    }
}
