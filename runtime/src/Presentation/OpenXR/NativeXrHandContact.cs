using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

/// <summary>Sweeps an anatomical hand and its held shapes through the resident world.</summary>
internal sealed class NativeXrHandContact : IDisposable
{
    private readonly Rid _body;
    private readonly PhysicsTestMotionParameters3D _query = new() { Margin = .001f, MaxCollisions = 4 };
    private readonly PhysicsTestMotionResult3D _result = new();
    private readonly List<Shape3D> _shapes = [];
    private Transform3D? _pose;
    private Transform3D _target;
    private Transform3D? _previousTarget;
    private RigidBody3D? _pushBody;
    private Vector3 _pushPoint, _pushNormal, _pushVelocity;
    private int _handShapes;
    private bool _weaponEnabled;
    internal Transform3D? Pose => _pose;
    internal bool Valid { get; private set; }
    internal bool Blocked { get; private set; }
    internal ulong Collider { get; private set; }
    internal Vector3 Normal { get; private set; }
    internal float ErrorMeters { get; private set; }
    internal int Queries { get; private set; }
    internal int ShapeCount => _shapes.Count;
    internal float PushImpulse { get; private set; }
    internal object State => new
    {
        valid = Valid,
        blocked = Blocked,
        collider = Collider,
        requested = new[] { _target.Origin.X, _target.Origin.Y, _target.Origin.Z },
        resolved = _pose is { } pose ? new[] { pose.Origin.X, pose.Origin.Y, pose.Origin.Z } : null,
        normal = new[] { Normal.X, Normal.Y, Normal.Z },
        errorMeters = ErrorMeters,
        queries = Queries,
        shapes = ShapeCount,
        weaponShapes = ShapeCount - _handShapes,
        pushImpulse = PushImpulse,
        boundary = "swept-resident-body-contact;actor-areas-and-contact-damage-unbound"
    };

    internal NativeXrHandContact(Rid space, Godot.Collections.Array<Rid> exclude, uint worldMask)
    {
        _body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(_body, PhysicsServer3D.BodyMode.Kinematic);
        PhysicsServer3D.BodySetCollisionLayer(_body, 0);
        PhysicsServer3D.BodySetCollisionMask(_body, worldMask);
        PhysicsServer3D.BodySetSpace(_body, space);
        _query.ExcludeBodies = exclude;
    }

    internal void AddShape(Shape3D shape, Transform3D handFromShape, bool weapon = false)
    {
        if (_pose is not null) throw new InvalidOperationException("Hand shapes cannot change after motion begins.");
        if (shape is ConcavePolygonShape3D or WorldBoundaryShape3D or HeightMapShape3D)
            throw new NotSupportedException("Tracked hand contact requires source convex shapes.");
        if (!weapon && _shapes.Count != _handShapes) throw new InvalidOperationException("Hand shapes must precede equipped shapes.");
        _shapes.Add(shape);
        PhysicsServer3D.BodyAddShape(_body, shape.GetRid(), handFromShape, weapon);
        if (!weapon) _handShapes++;
    }

    internal void SetWeaponEnabled(bool enabled)
    {
        if (_weaponEnabled == enabled) return;
        for (var index = _handShapes; index < _shapes.Count; index++)
            PhysicsServer3D.BodySetShapeDisabled(_body, index, !enabled);
        _weaponEnabled = enabled;
    }

    internal Transform3D Resolve(Transform3D target, bool tracked, bool resident, double delta = 0)
    {
        Blocked = false; Collider = 0; Normal = Vector3.Zero; Queries = 0;
        _target = target;
        _pushBody = null; PushImpulse = 0;
        if (_handShapes == 0) throw new InvalidOperationException("Tracked hand has no authored contact shape.");
        if (!tracked || !resident || !target.Origin.IsFinite() || !target.Basis.IsFinite())
        {
            Valid = false;
            _previousTarget = null;
            return _pose ?? target;
        }
        target.Basis = target.Basis.Orthonormalized();
        if (_pose is not { } current)
        {
            Valid = !Test(target, Vector3.Zero, true);
            if (Valid) _pose = target;
            _previousTarget = null;
            return target;
        }
        // Translation is swept continuously. Rotation is bounded to one degree
        // per query and 24 degrees per publication; tracking jumps cannot create
        // unbounded work or teleport an already established contact through a wall.
        var angle = current.Basis.GetRotationQuaternion().AngleTo(target.Basis.GetRotationQuaternion());
        var turn = Math.Min(angle, Mathf.DegToRad(24));
        var steps = Math.Max(1, (int)MathF.Ceiling(turn / Mathf.DegToRad(1)));
        var start = current;
        for (var step = 1; step <= steps; step++)
        {
            var fraction = (float)step / steps;
            var basis = start.Basis.Slerp(target.Basis, angle < .00001f ? 1 : fraction * turn / angle);
            var rotated = new Transform3D(basis, current.Origin);
            if (!basis.IsEqualApprox(current.Basis) && !Test(rotated, Vector3.Zero, true)) current.Basis = basis;
            var motion = start.Origin.Lerp(target.Origin, fraction) - current.Origin;
            for (var slide = 0; slide < 3 && motion.LengthSquared() > .00000001f; slide++)
            {
                var hit = Test(current, motion, false);
                current.Origin += _result.GetTravel();
                if (!hit) break;
                motion = _result.GetRemainder();
                for (var contact = 0; contact < _result.GetCollisionCount(); contact++)
                {
                    var normal = _result.GetCollisionNormal(contact);
                    if (motion.Dot(normal) < 0) motion = motion.Slide(normal);
                }
            }
        }
        _pose = current;
        ErrorMeters = current.Origin.DistanceTo(target.Origin);
        // Occupancy also catches a newly equipped shape or a moving obstacle.
        // An invalid pose cannot fire; pulling back is still allowed to recover.
        Valid = !Test(current, Vector3.Zero, true) || _result.GetCollisionDepth() <= .002f;
        Push(current, target, delta);
        _previousTarget = target;
        PhysicsServer3D.BodySetState(_body, PhysicsServer3D.BodyState.Transform, current);
        return current;
    }

    private bool Test(Transform3D from, Vector3 motion, bool recovery)
    {
        _query.From = from; _query.Motion = motion; _query.RecoveryAsCollision = recovery;
        Queries++;
        var hit = PhysicsServer3D.BodyTestMotion(_body, _query, _result);
        if (hit && _result.GetCollisionCount() != 0)
        {
            Blocked = true; Collider = _result.GetColliderId(); Normal = _result.GetCollisionNormal();
            if (_result.GetCollider() is RigidBody3D { Freeze: false } dynamic)
            {
                _pushBody = dynamic; _pushPoint = _result.GetCollisionPoint();
                _pushNormal = Normal; _pushVelocity = _result.GetColliderVelocity();
            }
        }
        return hit;
    }

    private void Push(Transform3D current, Transform3D target, double delta)
    {
        if (!Valid || _pushBody is null || _previousTarget is not { } previous || delta <= 0 || delta > .05 ||
            previous.Origin.DistanceTo(target.Origin) > .35f ||
            previous.Basis.GetRotationQuaternion().AngleTo(target.Basis.GetRotationQuaternion()) > Mathf.Pi / 4) return;
        // A bounded virtual spring pushes the source rigid body at the actual
        // contact point. Its authored mass, inertia and world physics still own
        // motion. This is prop response, never an inferred combat damage event.
        var desiredPoint = target * current.AffineInverse() * _pushPoint;
        var compression = (desiredPoint - _pushPoint).Dot(-_pushNormal);
        var force = Math.Clamp(compression * 800 - _pushVelocity.Dot(-_pushNormal) * 20, 0, 120);
        PushImpulse = force * (float)delta;
        if (PushImpulse > 0)
            _pushBody.ApplyImpulse(-_pushNormal * PushImpulse, _pushPoint - _pushBody.GlobalPosition);
    }

    public void Dispose()
    {
        PhysicsServer3D.FreeRid(_body);
        _query.Dispose(); _result.Dispose(); _shapes.Clear();
    }
}
