using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            if (arguments.FirstOrDefault() == "--owned-contact")
            {
                await NativeOwnedContactAudit.Run(this, arguments.Skip(1).ToArray());
                GetTree().Quit();
                return;
            }
            NativeNavigationContracts.Run();
            await CheckNavigation();
            await CheckNavigation(lowCeiling: true);
            await Check(.3f, false, true);
            await Check(2, false, false);
            await Check(.3f, true, false);
            GD.Print("OPENNV_NATIVE_LOCOMOTION_PASS curb=true tallWall=true lowCeiling=true airborne=true sourceStepHeightAndCameraParity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CheckNavigation(bool lowCeiling = false)
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box);
        }
        Box(new(0, -.5f, -2), new(16, 1, 16));
        if (lowCeiling) Box(new(0, 2.5f, -2), new(16, 1, 16));
        else Box(new(0, 1, -2), new(4, 2, 2));
        var body = new CharacterBody3D { FloorSnapLength = .32f, Position = new(0, .1f, 1) };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f } });
        scene.AddChild(body);
        try
        {
            for (var frame = 0; frame < 15; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            var initial = body.GlobalTransform;
            using var placement = new NativeCapsulePlacementQuery(body);
            if (!placement.CanStand(body.GlobalPosition) || !lowCeiling && placement.CanStand(new(0, 0, -2)))
                throw new InvalidOperationException("Source waypoint clearance lost the floor or accepted the wall's occupied capsule.");
            if (!lowCeiling)
            {
                scene.ProcessMode = ProcessModeEnum.Disabled;
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                using (var removed = new NativeCapsulePlacementQuery(body))
                    if (removed.CanStand(body.GlobalPosition)) throw new InvalidOperationException("Disabled destination retained default collision unexpectedly.");
                var bodies = scene.FindChildren("*", "", true, false).OfType<CollisionObject3D>().ToArray();
                foreach (var collider in bodies) collider.DisableMode = CollisionObject3D.DisableModeEnum.KeepActive;
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                using (var staged = new NativeCapsulePlacementQuery(body))
                    if (!staged.CanStand(body.GlobalPosition) || staged.CanStand(new(0, 0, -2)))
                        throw new InvalidOperationException("Staged destination lost its floor or wall while gameplay was disabled.");
                scene.ProcessMode = ProcessModeEnum.Inherit;
                foreach (var collider in bodies) collider.DisableMode = CollisionObject3D.DisableModeEnum.Remove;
                GD.Print("OPENNV_STAGED_COLLISION_PASS gameplayDisabled=true floor=true wallRefused=true");
            }
            var path = NativeCapsuleNavigation.Find(body, body.GlobalPosition, new(0, 0, -5), .4f, .64f, _ => true);
            if (body.GlobalTransform != initial || !lowCeiling && !path.Any(point => Math.Abs(point.X) > 2.3f))
                throw new InvalidOperationException("Navigation moved the query body or cut through the capsule obstruction.");
            foreach (var waypoint in path)
            {
                var arrived = false;
                for (var frame = 0; frame < 90; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    var delta = waypoint - body.GlobalPosition; delta.Y = 0;
                    if (delta.Length() < .12f) { arrived = true; break; }
                    body.Velocity = delta.Normalized() * 3 + Vector3.Down;
                    body.MoveAndSlide();
                }
                if (!arrived) throw new InvalidOperationException("Actual capsule movement could not execute the refined route.");
            }
            var refused = false;
            try { _ = NativeCapsuleNavigation.Find(body, body.GlobalPosition, new(0, -4, -5), .4f, .64f, _ => true, 200); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidOperationException("Navigation joined a different floor at the same X/Z.");
            refused = false;
            try { _ = NativeCapsuleNavigation.Find(body, body.GlobalPosition, new(3, 0, -5), .4f, .64f, _ => false, 100); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidOperationException("Navigation accepted unloaded collision.");
            GD.Print($"OPENNV_NATIVE_CAPSULE_ROUTE_PASS waypoints={path.Count} lowCeiling={lowCeiling} ordinaryPhysicsArrival=true bodyNotWarped=true stackedFloorRefused=true unloadedRefused=true");
        }
        finally { scene.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }

    private async Task Check(float height, bool ceiling, bool shouldClimb)
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box);
        }
        Box(new(0, -.5f, -3), new(10, 1, 20));
        Box(new(0, height / 2, -5), new(4, height, 8));
        if (ceiling) Box(new(0, 2.5f, -3), new(10, 1, 20));
        var body = new CharacterBody3D { FloorSnapLength = .32f, Position = new(0, .1f, 0) };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f } });
        scene.AddChild(body);
        try
        {
            if (NativeCharacterStep.TryStep(body, new(0, 0, -.06f), .4f)) throw new InvalidOperationException("Airborne controller climbed a step.");
            var steps = 0;
            for (var frame = 0; frame < 90; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = new(0, -.2f, -3.6f);
                if (NativeCharacterStep.TryStep(body, new(0, 0, -.06f), .4f))
                { ++steps; body.Velocity = Vector3.Down * .01f; }
                body.MoveAndSlide();
            }
            GD.Print($"OPENNV_NATIVE_LOCOMOTION_CASE height={height} ceiling={ceiling} steps={steps} position={body.Position}");
            if (shouldClimb ? steps == 0 || body.Position.Z > -3 || body.Position.Y < height - .02f : steps != 0 || body.Position.Z < -.9f)
                throw new InvalidOperationException("Capsule step/obstruction traversal differs from the synthetic collision scene.");
        }
        finally { scene.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }
}
