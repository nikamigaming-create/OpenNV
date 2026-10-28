using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckAirborneContact()
    {
        var scene = new Node3D(); AddChild(scene);
        LandingBox(scene, new(0, -.5f, 0), new(10, 1, 10));
        LandingBox(scene, new(1, 2, 0), new(.2f, 6, 4));
        var body = CorridorBody(scene, new(0, 2, 0));
        var registrations = NativeCapsuleNavigation.RegisteredSearches;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var original = body.GlobalTransform;
            NativeAirContact Find(Vector3 horizontal)
            {
                foreach (var contact in NativeCapsuleNavigation.AirborneContact(body, original.Origin, horizontal,
                    0, 9.8f, 1f / 60, 240, .4f, _ => true))
                    if (contact is not null) return contact;
                throw new InvalidOperationException("Airborne query returned no contact.");
            }
            var wall = Find(Vector3.Right * 3.6f);
            var floor = Find(Vector3.Zero);
            var floorSnap = NativeCharacterFloorSnap.Query(body, new(0, .1f, 0));
            var emptySnap = NativeCharacterFloorSnap.Query(body, new(20, 2, 0));
            if (wall.WalkableContact != false || wall.RootSupported || wall.Normals.All(normal => normal.Dot(Vector3.Up) >= MathF.Cos(body.FloorMaxAngle)) ||
                floor.WalkableContact != true || !floor.RootSupported || floor.RootSupport is null ||
                floorSnap.OnFloor != true || emptySnap.OnFloor != false ||
                NativeCharacterFloorSnap.Classify([new(.8f, .6f, 0), new(-.8f, .6f, 0)], Vector3.Up, body.FloorMaxAngle) != true ||
                NativeCharacterFloorSnap.Classify([Vector3.Down], Vector3.Up, body.FloorMaxAngle) != false ||
                NativeCharacterFloorSnap.Classify([], Vector3.Up, body.FloorMaxAngle) is not null ||
                body.GlobalTransform != original || NativeCapsuleNavigation.RegisteredSearches != registrations)
                throw new InvalidOperationException("First-air contact confused a side wall with supported floor or mutated its body.");
            using var stale = NativeCapsuleNavigation.AirborneContact(body, original.Origin, Vector3.Zero,
                0, 9.8f, 1f / 60, 240, .4f, _ => true).GetEnumerator();
            body.GlobalPosition += Vector3.Right;
            var staleRefused = false;
            try { stale.MoveNext(); }
            catch (InvalidOperationException) { staleRefused = true; }
            body.GlobalTransform = original;
            if (!staleRefused || NativeCapsuleNavigation.RegisteredSearches != registrations)
                throw new InvalidOperationException("Airborne query retained work after its native owner moved.");
            body.GlobalPosition = new(0, .1f, 0);
            await SettleCorridorBody(body);
            var grounded = body.GlobalTransform; var blocked = false;
            try
            {
                foreach (var ignored in NativeCapsuleNavigation.GroundDeparture(body, Vector3.Right * 3.6f,
                    1f / 60, 240, _ => true)) { }
            }
            catch (NotSupportedException) { blocked = true; }
            if (!blocked || body.GlobalTransform != grounded || NativeCapsuleNavigation.RegisteredSearches != registrations)
                throw new InvalidOperationException("Ground departure crossed a wall or changed its source body.");
            using (var cancelled = new NativeFallCandidateSearch(body, Vector3.Right * 3.6f, 9.8f, 1f / 60,
                240, 240, .4f, _ => true))
            {
                cancelled.Dispose();
                if (NativeCapsuleNavigation.RegisteredSearches != registrations)
                    throw new InvalidOperationException("Cancelled fall candidate retained scheduled work.");
            }
            var candidateBlocked = false;
            using (var candidate = new NativeFallCandidateSearch(body, Vector3.Right * 3.6f, 9.8f, 1f / 60,
                240, 240, .4f, _ => true))
            {
                try
                {
                    for (var frame = 0; frame < 240; frame++)
                    {
                        if (candidate.Advance(out _)) throw new InvalidOperationException("Fall candidate crossed a wall.");
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    }
                }
                catch (NotSupportedException) { candidateBlocked = true; }
            }
            if (!candidateBlocked || body.GlobalTransform != grounded || NativeCapsuleNavigation.RegisteredSearches != registrations)
                throw new InvalidOperationException("Fall candidate failed to reject or clean up its wall query.");
            GD.Print("OPENNV_NATIVE_AIRBORNE_CONTACT_PASS wallNotFloor=true missingRoot=true floorAndRoot=true snapFloorAndVoid=true queryReadOnly=true movedOwnerRefused=true departureWallRefused=true candidateCancelAndWallRefused=true fixture=synthetic gameplay=unverified parity=unverified");
        }
        finally { scene.Free(); }
    }
}
