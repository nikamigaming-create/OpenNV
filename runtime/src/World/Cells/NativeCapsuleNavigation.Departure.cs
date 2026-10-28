using Godot;

namespace OpenNV.Runtime.World.Cells;

// An observed grounded pose can be projected only across clear capsule motion
// and individually verified snap states. Sliding and ambiguous contacts refuse.
internal sealed record NativeGroundDeparture(int Frames, Vector3 Position, Vector3 Velocity);

internal static partial class NativeCapsuleNavigation
{
    internal static IEnumerable<NativeGroundDeparture?> GroundDeparture(CharacterBody3D body,
        Vector3 horizontalVelocity, float delta, int maximumFrames, Func<Vector3, bool> resident)
    {
        var schedule = WorkSchedule;
        if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion() || !body.IsOnFloor())
            throw new InvalidOperationException("Ground departure requires an observed grounded native body.");
        if (!horizontalVelocity.IsFinite() || horizontalVelocity.Y != 0 || horizontalVelocity.LengthSquared() == 0 ||
            !float.IsFinite(delta) || delta <= 0 || maximumFrames <= 0 || body.FloorConstantSpeed || body.Velocity.Y != 0)
            throw new InvalidDataException("Ground departure requires finite horizontal motion and supported controller settings.");
        var identity = body.GetInstanceId(); var rid = body.GetRid(); var world = body.GetWorld3D().GetInstanceId();
        var transform = body.GlobalTransform; var velocity = body.Velocity;
        bool OwnerValid() => GodotObject.IsInstanceValid(body) && body.IsInsideTree() && !body.IsQueuedForDeletion() &&
            body.GetInstanceId() == identity && body.GetRid().Equals(rid) && body.GetWorld3D().GetInstanceId() == world &&
            body.GlobalTransform == transform && body.Velocity == velocity && body.IsOnFloor();
        Action BindRetirement(Action retired)
        {
            body.TreeExiting += retired;
            return () => { if (GodotObject.IsInstanceValid(body)) body.TreeExiting -= retired; };
        }
        return new NativeNavigationScheduledEnumerable<NativeGroundDeparture?>(schedule,
            _ => GroundDepartureCore(body, transform, horizontalVelocity, delta, maximumFrames, resident).GetEnumerator(),
            OwnerValid, result => result is not null, BindRetirement);
    }

    private static IEnumerable<NativeGroundDeparture?> GroundDepartureCore(CharacterBody3D body, Transform3D start,
        Vector3 horizontalVelocity, float delta, int maximumFrames, Func<Vector3, bool> resident)
    {
        using var parameters = new PhysicsTestMotionParameters3D
        { Margin = body.SafeMargin, MaxCollisions = 6, RecoveryAsCollision = true };
        using var result = new PhysicsTestMotionResult3D();
        var position = start.Origin;
        for (var frame = 1; frame <= maximumFrames; frame++)
        {
            var next = position + horizontalVelocity * delta;
            if (!next.IsFinite() || !resident(position) || !resident(next))
                throw new InvalidOperationException("Ground departure left resident collision geometry.");
            parameters.From = new(start.Basis, position); parameters.Motion = horizontalVelocity * delta;
            if (PhysicsServer3D.BodyTestMotion(body.GetRid(), parameters, result))
                throw new NotSupportedException("Ground departure encountered motion requiring collision or sliding ownership.");
            yield return null;
            var snap = NativeCharacterFloorSnap.Query(body, next);
            if (snap.OnFloor is null) throw new NotSupportedException("Ground departure floor contact is indeterminate.");
            if (!resident(snap.Position)) throw new InvalidOperationException("Ground departure snap left resident collision geometry.");
            if (snap.OnFloor == false)
            {
                yield return new(frame, next, horizontalVelocity);
                yield break;
            }
            position = snap.Position;
            yield return null;
        }
        throw new InvalidOperationException("Ground departure reached its frame bound while still grounded.");
    }
}
