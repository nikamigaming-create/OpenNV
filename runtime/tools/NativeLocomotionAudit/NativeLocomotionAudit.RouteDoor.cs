using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckRouteDoorContact()
    {
        var scene = new Node3D(); AddChild(scene);
        StaticBody3D Box(string name, Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Name = name, Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box); return box;
        }
        Box("Floor", new(0, -.5f, 0), new(12, 1, 16));
        Box("LeftWall", new(-3.5f, 1, -2), new(5, 2, .4f));
        Box("RightWall", new(3.5f, 1, -2), new(5, 2, .4f));
        var door = Box("IntendedDoor", new(0, 1, -2), new(2, 2, .4f));
        var doorPart = Box("OtherBodyOfSameSourceDoor", new(0, 5, -2), new(2, 1, .4f));
        var other = Box("UnrelatedDoor", new(2, 1, 0), new(.3f, 2, 2));
        var body = new CharacterBody3D { FloorSnapLength = .32f, FloorMaxAngle = FloorAngle, Position = new(0, .1f, 1) };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f } });
        scene.AddChild(body);
        try
        {
            for (var frame = 0; frame < 15; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            var original = body.GlobalTransform;
            var destination = new Vector3(0, body.GlobalPosition.Y, -5);
            var probe = new NativeNavigationProbe(NativeCapsuleNavigation.FirstCorridorContact(body, body.GlobalPosition, [destination]));
            var refused = false;
            try
            {
                foreach (var _ in NativeCapsuleNavigation.Search(body, body.GlobalPosition, destination, .4f, .64f,
                    point => Math.Abs(point.X) < 5.5f && Math.Abs(point.Z) < 7.5f, 512, probe)) { }
            }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || probe.CorridorContact?.Collider != door.GetInstanceId() ||
                probe.RejectedContact?.Collider != door.GetInstanceId() || probe.Approach.Length == 0 || body.GlobalTransform != original)
                throw new InvalidOperationException("Closed route did not retain its exact source-corridor collider and unmoved reachable approach.");
            var selected = probe.RejectedContact!;
            probe.Record(selected with { Collider = other.GetInstanceId(), From = selected.Point }, () => throw new InvalidOperationException("Unrelated door selected."));
            if (probe.RejectedContact != selected) throw new InvalidOperationException("A lateral search contact replaced the intended door.");
            var sourceDoor = new FalloutFormKey("Fixture.esm", 1);
            var compound = new NativeNavigationProbe(probe.CorridorContact,
                collider => collider == door.GetInstanceId() || collider == doorPart.GetInstanceId() ? sourceDoor : new("Fixture.esm", 2));
            compound.Record(selected with { Collider = doorPart.GetInstanceId() }, () => probe.Approach);
            if (compound.RejectedContact?.Reference != sourceDoor || compound.RejectedContact.Collider != doorPart.GetInstanceId())
                throw new InvalidOperationException("A sibling collision body lost its authoritative source-door identity.");
            compound.Record(selected with { Collider = other.GetInstanceId(), From = selected.Point }, () => throw new InvalidOperationException("Another reference's door selected."));
            if (compound.RejectedContact.Collider != doorPart.GetInstanceId() || compound.RejectedContacts.Count != 2)
                throw new InvalidOperationException("Source-reference grouping merged another door or discarded unmatched collision diagnostics.");
            foreach (var waypoint in probe.Approach)
                for (var frame = 0; frame < 120; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    var offset = waypoint - body.GlobalPosition; offset.Y = 0;
                    if (offset.Length() < .1f) break;
                    if (frame == 119) throw new InvalidOperationException("The controller could not reach the recorded door approach.");
                    body.Velocity = offset.Normalized() * 2 + Vector3.Down; body.MoveAndSlide();
                }
            if (NativeCapsuleNavigation.FirstCorridorContact(body, body.GlobalPosition, [selected.Desired])?.Collider != door.GetInstanceId())
                throw new InvalidOperationException("Actual approach lost the door collision before activation.");
            var otherPose = other.GlobalTransform;
            door.Position += Vector3.Up * 4; // Synthetic source-motion stand-in, never a production clearance override.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var route = NativeCapsuleNavigation.Find(body, body.GlobalPosition, destination, .4f, .64f, _ => true);
            if (route.Count == 0 || other.GlobalTransform != otherPose)
                throw new InvalidOperationException("Opening the exact collider did not restore a route independently of the unrelated door.");
            GD.Print("OPENNV_NATIVE_ROUTE_DOOR_CONTACT_PASS exactCollider=true compoundSourceIdentity=true unmatchedDiagnostics=true noNearbyScan=true reachableApproach=true recheckedContact=true noQueryMovement=true openingClearance=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }
}
