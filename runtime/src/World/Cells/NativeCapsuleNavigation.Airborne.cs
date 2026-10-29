using Godot;

namespace OpenNV.Runtime.World.Cells;

// First native contact only. This is not a route, a landing decision or
// permission to move the player; the caller must own every later transition.
internal sealed record NativeAirContact(int Frames, Vector3 Position, Vector3 Velocity,
    IReadOnlyList<Vector3> Normals, bool? WalkableContact, bool RootSupported,
    NativeNavigationContact? RootSupport);

internal static partial class NativeCapsuleNavigation
{
    internal static IEnumerable<NativeAirContact?> AirborneContact(CharacterBody3D body, Vector3 start,
        Vector3 horizontalVelocity, float verticalVelocity, float gravity, float delta, int maximumFrames, float supportHeight,
        Func<Vector3, bool> resident)
    {
        var schedule = WorkSchedule;
        if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion())
            throw new InvalidOperationException("Airborne contact requires its actual resident native body.");
        if (!start.IsFinite() || !horizontalVelocity.IsFinite() || horizontalVelocity.Y != 0 ||
            !float.IsFinite(verticalVelocity) || !float.IsFinite(gravity) || gravity <= 0 ||
            !float.IsFinite(delta) || delta <= 0 || maximumFrames <= 0 || !float.IsFinite(supportHeight) || supportHeight <= 0)
            throw new InvalidDataException("Airborne contact requires finite motion and a positive frame bound.");
        var identity = body.GetInstanceId(); var rid = body.GetRid(); var world = body.GetWorld3D().GetInstanceId();
        var ownerTransform = body.GlobalTransform; var ownerVelocity = body.Velocity; var ownerFloor = body.IsOnFloor();
        bool OwnerValid() => GodotObject.IsInstanceValid(body) && body.IsInsideTree() && !body.IsQueuedForDeletion() &&
            body.GetInstanceId() == identity && body.GetRid().Equals(rid) && body.GetWorld3D().GetInstanceId() == world &&
            body.GlobalTransform == ownerTransform && body.Velocity == ownerVelocity && body.IsOnFloor() == ownerFloor;
        Action BindRetirement(Action retired)
        {
            body.TreeExiting += retired;
            return () => { if (GodotObject.IsInstanceValid(body)) body.TreeExiting -= retired; };
        }
        return new NativeNavigationScheduledEnumerable<NativeAirContact?>(schedule,
            _ => AirborneContactCore(body, start, horizontalVelocity, verticalVelocity, gravity, delta, maximumFrames, supportHeight, resident).GetEnumerator(),
            OwnerValid, result => result is not null, BindRetirement);
    }

    private static IEnumerable<NativeAirContact?> AirborneContactCore(CharacterBody3D body, Vector3 start,
        Vector3 horizontalVelocity, float verticalVelocity, float gravity, float delta, int maximumFrames, float supportHeight,
        Func<Vector3, bool> resident)
    {
        using var parameters = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 4 };
        using var result = new PhysicsTestMotionResult3D();
        var position = start; var basis = body.GlobalBasis; var rid = body.GetRid();
        for (var frame = 1; frame <= maximumFrames; frame++)
        {
            verticalVelocity = NativeCharacterGravity.AirVelocity(verticalVelocity, gravity, delta);
            var velocity = new Vector3(horizontalVelocity.X, verticalVelocity, horizontalVelocity.Z);
            var destination = position + velocity * delta;
            if (!destination.IsFinite() || !resident(position) || !resident(destination))
                throw new InvalidOperationException("Airborne contact left resident collision geometry.");
            parameters.From = new(basis, position); parameters.Motion = velocity * delta;
            if (PhysicsServer3D.BodyTestMotion(rid, parameters, result))
            {
                var contact = position + result.GetTravel();
                var normals = Enumerable.Range(0, result.GetCollisionCount()).Select(result.GetCollisionNormal).ToArray();
                if (!contact.IsFinite() || !resident(contact) || normals.Length == 0 || normals.Any(normal => !normal.IsFinite()))
                    throw new InvalidDataException("Airborne sweep returned an invalid or nonresident contact.");
                var walkable = NativeCharacterFloorSnap.Classify(normals, body.UpDirection, body.FloorMaxAngle);
                // Keep the independent root support ray inside a separate
                // scheduler slice; contact alone is not a floor decision.
                yield return null;
                var supported = NativeCharacterStep.TrySupport(body, contact, supportHeight, out var support);
                yield return new(frame, contact, velocity, normals, walkable, supported, support);
                yield break;
            }
            position = destination;
            yield return null;
        }
        throw new InvalidOperationException("Airborne sweep reached its frame bound without contact.");
    }
}
