using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckExtendedDetour()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var body = new StaticBody3D { Position = position };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(body);
        }
        Box(new(0, -.5f, 0), new(32, 1, 24));
        Box(new(0, 1, 0), new(21, 2, .4f));
        var player = new CharacterBody3D { FloorSnapLength = .32f, FloorMaxAngle = FloorAngle, Position = new(0, .1f, 1) };
        player.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f } });
        scene.AddChild(player);
        try
        {
            for (var frame = 0; frame < 15; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                player.Velocity = Vector3.Down; player.MoveAndSlide();
            }
            var start = player.GlobalPosition;
            var target = new Vector3(0, 0, -1);
            var bounded = false;
            try { _ = NativeCapsuleNavigation.Find(player, start, target, .4f, .6f, _ => true, 5000); }
            catch (InvalidOperationException) { bounded = true; }
            if (!bounded) throw new InvalidDataException("Short search escaped its bounded neighborhood.");
            IReadOnlyList<Vector3>? route = null;
            foreach (var result in NativeCapsuleNavigation.Search(player, start, target, .4f, .9f,
                _ => true, 1200, minimumSearchRadius: 14))
                if (result is not null) { route = result; break; }
            if (route is null || !route.Any(point => Math.Abs(point.X) > 10.8f) || player.GlobalPosition != start)
                throw new InvalidDataException("Extended search did not plan an unwarped route around the long wall.");
            foreach (var waypoint in route)
            {
                var arrived = false;
                for (var frame = 0; frame < 900; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    var delta = waypoint - player.GlobalPosition; delta.Y = 0;
                    if (delta.Length() < .12f) { arrived = true; break; }
                    player.Velocity = delta.Normalized() * 3 + Vector3.Down; player.MoveAndSlide();
                }
                if (!arrived) throw new InvalidDataException("Native player could not execute the extended detour.");
            }
            if (new Vector2(player.GlobalPosition.X - target.X, player.GlobalPosition.Z - target.Z).Length() > .15f)
                throw new InvalidDataException("Extended detour did not physically reach its target.");
            GD.Print("OPENNV_NATIVE_EXTENDED_DETOUR_PASS boundedSearchRefused=true wideGridNodes=1200 nativeMovement=true capsuleWallIntact=true");
        }
        finally { scene.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }
}
