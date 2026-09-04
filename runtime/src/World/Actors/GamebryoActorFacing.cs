using Godot;

namespace OpenNV.Runtime.World.Actors;

internal static class GamebryoActorFacing
{
    internal static Basis ModelFrontBasis(Vector3 direction, Vector3 up)
    {
        if (!direction.IsFinite() || !up.IsFinite() ||
            direction.LengthSquared() <= Mathf.Epsilon ||
            up.LengthSquared() <= Mathf.Epsilon)
            throw new InvalidOperationException("Source actor facing vectors are invalid.");
        var forward = direction.Normalized();
        var right = up.Normalized().Cross(forward);
        if (right.LengthSquared() <= Mathf.Epsilon)
            throw new InvalidOperationException("Source actor facing is parallel to its up axis.");
        right = right.Normalized();
        var correctedUp = forward.Cross(right).Normalized();
        return new Basis(right, correctedUp, forward);
    }

    internal static void FaceModelFrontToward(Node3D actor, Vector3 globalTarget)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var origin = actor.GlobalPosition;
        var levelTarget = new Vector3(globalTarget.X, origin.Y, globalTarget.Z);
        var direction = levelTarget - origin;
        if (!direction.IsFinite())
            throw new InvalidOperationException("Source actor facing target is invalid.");
        if (direction.LengthSquared() <= Mathf.Epsilon)
            return;
        _ = ModelFrontBasis(direction, Vector3.Up);
        actor.LookAt(levelTarget, Vector3.Up, useModelFront: true);
        var agreement = actor.GlobalBasis.Z.Normalized().Dot(direction.Normalized());
        if (agreement < 0.999f)
            throw new InvalidOperationException(
                $"Source actor model front disagrees with travel direction: {agreement:R}.");
    }
}
