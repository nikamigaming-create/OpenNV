using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class GamebryoActorCollision : Area3D
{
    private const float HalfExtentScale = 0.5f;
    private readonly Node3D _actor;
    private readonly Vector3 _localCenter;
    private readonly Vector3 _sizeMeters;

    private GamebryoActorCollision(
        Node3D actor,
        Aabb localBounds,
        uint collisionLayer)
    {
        _actor = actor;
        _localCenter = localBounds.GetCenter();
        Name = "SourceActorCollision";
        CollisionLayer = collisionLayer;
        CollisionMask = 0u;
        var scale = actor.GlobalBasis.Scale;
        _sizeMeters = new Vector3(
            localBounds.Size.X * scale.X,
            localBounds.Size.Y * scale.Y,
            localBounds.Size.Z * scale.Z);
        AddChild(new CollisionShape3D
        {
            Name = "SourceActorBounds",
            Shape = new BoxShape3D
            {
                Size = _sizeMeters,
            },
        });
    }

    internal static GamebryoActorCollision Start(
        Node3D actor,
        Aabb localBounds,
        uint collisionLayer)
    {
        if (localBounds.Size.X <= 0.0f ||
            localBounds.Size.Y <= 0.0f ||
            localBounds.Size.Z <= 0.0f || collisionLayer == 0u)
            throw new InvalidOperationException(
                "Owned actor collision contract is incomplete.");
        var collision = new GamebryoActorCollision(
            actor,
            localBounds,
            collisionLayer);
        actor.AddChild(collision);
        collision.TopLevel = true;
        collision.Publish();
        return collision;
    }

    internal static void Synchronize(Node3D actor)
    {
        var collisions = actor.GetChildren()
            .OfType<GamebryoActorCollision>()
            .ToArray();
        if (collisions.Length != 1)
            throw new InvalidOperationException(
                "Owned actor collision identity is absent or ambiguous.");
        collisions[0].Publish();
    }

    internal static Vector3 Center(Node3D actor)
    {
        Synchronize(actor);
        return actor.GetChildren()
            .OfType<GamebryoActorCollision>()
            .Single().GlobalPosition;
    }

    public override void _PhysicsProcess(double delta) => Publish();

    internal bool IntersectsSegment(
        Vector3 from,
        Vector3 to,
        out float distanceMeters)
    {
        var inverse = GlobalTransform.AffineInverse();
        var localFrom = inverse * from;
        var localTo = inverse * to;
        var bounds = new Aabb(-_sizeMeters * HalfExtentScale, _sizeMeters);
        var direction = localTo - localFrom;
        var minimum = bounds.Position;
        var maximum = bounds.End;
        var near = 0.0f;
        var far = 1.0f;
        if (!IntersectAxis(localFrom.X, direction.X, minimum.X, maximum.X, ref near, ref far) ||
            !IntersectAxis(localFrom.Y, direction.Y, minimum.Y, maximum.Y, ref near, ref far) ||
            !IntersectAxis(localFrom.Z, direction.Z, minimum.Z, maximum.Z, ref near, ref far))
        {
            distanceMeters = float.PositiveInfinity;
            return false;
        }
        var position = localFrom + direction * near;
        distanceMeters = from.DistanceTo(GlobalTransform * position);
        return true;
    }

    private static bool IntersectAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float near,
        ref float far)
    {
        if (direction == 0.0f)
            return origin >= minimum && origin <= maximum;
        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        if (first > second)
            (first, second) = (second, first);
        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far;
    }

    private void Publish()
    {
        GlobalTransform = new Transform3D(
            _actor.GlobalBasis.Orthonormalized(),
            _actor.ToGlobal(_localCenter));
    }
}
