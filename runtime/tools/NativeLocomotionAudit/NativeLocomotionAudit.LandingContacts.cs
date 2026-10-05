using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    // A capsule touches the corner of a low curb before its root reaches the
    // top. Its downward contact is steep, but the mover still has the flat
    // floor within snap reach. The geometry is an anonymous synthetic box.
    private async Task CheckRoundedLandingContact()
    {
        var scene = new Node3D(); AddChild(scene);
        LandingBox(scene, new(0, -.5f, 0), new(10, 1, 10));
        var curb = LandingBox(scene, new(-1, .15f, 0), new(2, .3f, 4));
        var body = CorridorBody(scene, CornerPose(.314f));
        var desired = CornerPose(.26f); var goal = new Vector3(-.7f, .3f, 0);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var pose = body.GlobalTransform;
            var geometry = scene.GetChildren().OfType<StaticBody3D>().Select(node => (node, node.GlobalTransform)).ToArray();
            var probe = new NativeNavigationProbe(null);
            _ = NativeCapsuleNavigation.Find(body, pose.Origin, desired, .4f, .32f, _ => true,
                maximumNodes: 1, probe: probe, corridor: [desired]);
            if (probe.FirstReconciledLanding is not { } support || probe.ReconciledLandings == 0 ||
                !support.Capsule.Collided || support.Capsule.Contacts.Count == 0 ||
                support.Capsule.Contacts.Any(contact => contact.Normal.Dot(Vector3.Up) >= MathF.Cos(body.FloorMaxAngle)) ||
                support.Capsule.Contacts.All(contact => contact.Collider != curb.GetInstanceId()) ||
                support.Floor.Normal.Dot(Vector3.Up) < MathF.Cos(body.FloorMaxAngle) ||
                Math.Abs(support.Floor.Point.Y) > .001f || support.Floor.From.Y - support.Floor.Point.Y > body.FloorSnapLength + body.SafeMargin ||
                body.GlobalTransform != pose)
                throw new InvalidOperationException("The rounded-corner query did not retain its steep capsule contact and actual bounded flat root support.");
            var unavailable = new NativeNavigationProbe(null); var residentRefused = false;
            try
            {
                _ = NativeCapsuleNavigation.Find(body, pose.Origin, desired, .4f, .32f, point => point.X >= .3f,
                    maximumNodes: 1, probe: unavailable, corridor: [desired]);
            }
            catch (InvalidOperationException) { residentRefused = true; }
            if (!residentRefused || unavailable.GuideRejection?.Reason != "destination-not-resident" ||
                unavailable.GuideDownwardSweep is not null ||
                unavailable.FirstReconciledLanding is { } admitted && (admitted.Capsule.From + admitted.Capsule.Travel).X < .3f ||
                body.GlobalTransform != pose)
                throw new InvalidOperationException($"A root-supported corner overrode destination residency; refused={residentRefused}. {unavailable.DescribeFailure()}");
            var forward = NativeCapsuleNavigation.Find(body, pose.Origin, goal, .4f, .32f, _ => true,
                corridor: [desired, goal]);
            if (body.GlobalTransform != pose) throw new InvalidOperationException("Rounded-corner planning published its hypothetical pose.");
            await WalkCorridorBody(body, forward, .025f);
            await SettleCorridorBody(body);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > .04f)
                throw new InvalidOperationException("The ordinary mover did not execute its root-supported corner ascent.");
            var reversePose = body.GlobalTransform;
            var reverse = NativeCapsuleNavigation.Find(body, reversePose.Origin, pose.Origin, .4f, .32f, _ => true,
                corridor: [desired, pose.Origin]);
            if (body.GlobalTransform != reversePose) throw new InvalidOperationException("Rounded-corner reverse planning moved the controller.");
            await WalkCorridorBody(body, reverse, .025f);
            if (new Vector2(body.GlobalPosition.X - pose.Origin.X, body.GlobalPosition.Z - pose.Origin.Z).Length() > .04f ||
                Math.Abs(body.GlobalPosition.Y - pose.Origin.Y) > .12f ||
                !NativeCharacterStep.TrySupport(body, body.GlobalPosition, .4f, out _) ||
                geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("The ordinary mover did not return over the unchanged, root-supported corner.");
            GD.Print("OPENNV_NATIVE_ROUNDED_LANDING_PASS steepCapsuleContact=true boundedRootFloor=true controllerBothDirections=true unavailableRefused=true noQueryMovement=true geometryUnchanged=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }

    private async Task CheckUnsupportedLandingContact()
    {
        var scene = new Node3D(); AddChild(scene);
        // Start support ends before the target. Keep the same contacted curb
        // corner, but leave a real gap beneath the destination root.
        LandingBox(scene, new(2.65f, -.5f, 0), new(4.7f, 1, 10));
        LandingBox(scene, new(-1, .15f, 0), new(2, .3f, 4));
        var body = CorridorBody(scene, CornerPose(.314f));
        var desired = CornerPose(.26f);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var pose = body.GlobalTransform;
            if (!NativeCharacterStep.TrySupport(body, pose.Origin, .4f, out _))
                throw new InvalidOperationException("The gap fixture has no actual floor beneath its starting root.");
            var probe = new NativeNavigationProbe(null);
            var refused = false;
            try
            {
                _ = NativeCapsuleNavigation.Find(body, pose.Origin, desired, .4f, .32f, _ => true,
                    maximumNodes: 1, probe: probe, corridor: [desired]);
            }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || probe.GuideDownwardSweep is not { Collided: true, Contacts.Count: > 0 } sweep ||
                sweep.Contacts.Any(contact => contact.Normal.Dot(Vector3.Up) >= MathF.Cos(body.FloorMaxAngle)) ||
                NativeCharacterStep.TrySupport(body, sweep.From + sweep.Travel, .4f, out _) ||
                probe.GuideRejection?.Reason != "landing-slope" || body.GlobalTransform != pose)
                throw new InvalidOperationException($"A rounded edge over a root-floor gap bypassed the mover's real support gate or lost its native contact; refused={refused}. {probe.DescribeFailure()}");
            GD.Print("OPENNV_NATIVE_LANDING_GAP_PASS steepCapsuleContact=true initialRootSupported=true missingDestinationRootRefused=true exactContactRetained=true noQueryMovement=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }

    private static Vector3 CornerPose(float x)
    {
        const float radius = .32f;
        var marginRadius = radius + .004f;
        return new(x, .3f + MathF.Sqrt(marginRadius * marginRadius - x * x) - radius, 0);
    }

    private static StaticBody3D LandingBox(Node3D scene, Vector3 position, Vector3 size)
    {
        var box = new StaticBody3D { Position = position };
        box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        scene.AddChild(box); return box;
    }
}
