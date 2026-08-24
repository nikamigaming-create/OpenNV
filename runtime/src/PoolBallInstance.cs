using Godot;

namespace OpenNV.Runtime;

internal partial class PoolBallInstance : RigidBody3D
{
    private Transform3D _authoredTransform;
    private readonly HashSet<string> _ballCollisionReferences =
        new(StringComparer.OrdinalIgnoreCase);

    internal string ReferenceFormId { get; private set; } = "";
    internal string Role { get; private set; } = "";
    internal float CollisionRadiusMeters { get; private set; }
    internal bool IsPocketed { get; private set; }
    internal Transform3D AuthoredTransform => _authoredTransform;
    internal int BallCollisionCount => _ballCollisionReferences.Count;
    internal PoolTableInstance Table { get; set; } = null!;

    internal void Configure(
        string referenceFormId,
        string role,
        VerifiedGltfLoader.DynamicBodyContract physics,
        Node3D visual,
        float unitsToMeters,
        float referenceScale,
        PoolConfiguration configuration)
    {
        ReferenceFormId = referenceFormId;
        Role = role;
        Name = $"POOL_BALL_{referenceFormId}";
        Mass = physics.Mass;
        LinearDamp = physics.LinearDamping;
        AngularDamp = physics.AngularDamping;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        CollisionLayer = configuration.CollisionLayer;
        CollisionMask = configuration.CollisionMask;
        ContinuousCd = true;
        ContactMonitor = true;
        MaxContactsReported = configuration.MaximumReportedContacts;
        BodyEntered += body =>
        {
            if (body is PoolBallInstance ball)
                _ballCollisionReferences.Add(ball.ReferenceFormId);
        };
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = physics.Friction,
            Bounce = physics.Restitution,
        };

        visual.Name = "AuthoredBallVisual";
        visual.Scale = Vector3.One * unitsToMeters * referenceScale;
        AddChild(visual);
        var hullScale = unitsToMeters * referenceScale;
        foreach (var hull in physics.Hulls)
        {
            var points = hull.PointsGodotGameUnits
                .Select(point => point * hullScale)
                .ToArray();
            if (points.Length < 4)
                throw new InvalidOperationException($"Pool ball convex hull is incomplete: {referenceFormId}");
            AddChild(new CollisionShape3D
            {
                Name = "AuthoredConvexHull",
                Shape = new ConvexPolygonShape3D { Points = points },
            });
            CollisionRadiusMeters = MathF.Max(
                CollisionRadiusMeters,
                points.Max(point => point.Length()));
        }
        if (CollisionRadiusMeters <= 0.0f)
            throw new InvalidOperationException($"Pool ball collision radius is empty: {referenceFormId}");
    }

    internal void CaptureAuthoredTransform()
    {
        _authoredTransform = GlobalTransform;
    }

    internal void ResetAuthored()
    {
        Freeze = true;
        GlobalTransform = _authoredTransform;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Visible = true;
        IsPocketed = false;
        _ballCollisionReferences.Clear();
        Freeze = false;
        Sleeping = false;
    }

    internal void SetPocketed()
    {
        if (IsPocketed)
            return;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Freeze = true;
        Visible = false;
        IsPocketed = true;
    }

    internal void ClearBallCollisionEvidence() => _ballCollisionReferences.Clear();

    internal BallState CaptureState() => new(
        ReferenceFormId,
        GlobalPosition,
        GlobalBasis.GetRotationQuaternion(),
        LinearVelocity,
        AngularVelocity,
        IsPocketed);

    internal void RestoreState(BallState state)
    {
        if (!state.ReferenceFormId.Equals(ReferenceFormId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pool ball state belongs to another reference.");
        Freeze = true;
        GlobalTransform = new Transform3D(new Basis(state.Rotation), state.Position);
        LinearVelocity = state.LinearVelocity;
        AngularVelocity = state.AngularVelocity;
        IsPocketed = state.Pocketed;
        Visible = !state.Pocketed;
        Freeze = state.Pocketed;
        if (!state.Pocketed)
            Sleeping = false;
    }

    internal readonly record struct BallState(
        string ReferenceFormId,
        Vector3 Position,
        Quaternion Rotation,
        Vector3 LinearVelocity,
        Vector3 AngularVelocity,
        bool Pocketed);
}
