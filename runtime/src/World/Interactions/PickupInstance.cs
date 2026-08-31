using Godot;


using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Interactions;

internal partial class PickupInstance : RigidBody3D
{
    private Transform3D _authoredTransform;
    private uint _worldCollisionLayer;
    private uint _worldCollisionMask;

    internal string ReferenceFormId { get; private set; } = "";
    internal string ItemFormId { get; private set; } = "";
    internal string EditorId { get; private set; } = "";
    internal string? DisplayName { get; private set; }
    internal string RecordType { get; private set; } = "";
    internal int Count { get; private set; }
    internal WeaponProfile? Weapon { get; private set; }
    internal string PhysicsSource { get; private set; } = "unsupported";
    internal bool CanGrab { get; private set; }
    internal bool IsHeld { get; private set; }
    internal Transform3D AuthoredTransform => _authoredTransform;

    internal void Configure(
        string referenceFormId,
        string itemFormId,
        string editorId,
        string? displayName,
        string recordType,
        int count,
        WeaponProfile? weapon)
    {
        ReferenceFormId = referenceFormId;
        ItemFormId = itemFormId;
        EditorId = editorId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        RecordType = recordType;
        Count = count;
        Weapon = weapon;
        Name = $"PICKUP_{referenceFormId}_{editorId}";
    }

    internal void ConfigurePhysics(
        VerifiedGltfLoader.DynamicBodyContract physics,
        PickupConfiguration configuration)
    {
        if (physics.Mass <= 0.0f || physics.Hulls.Count == 0)
            throw new InvalidOperationException(
                $"Pickup dynamic body is incomplete: {ReferenceFormId}");
        Mass = physics.Mass;
        LinearDamp = physics.LinearDamping;
        AngularDamp = physics.AngularDamping;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        ContinuousCd = true;
        _worldCollisionLayer = configuration.CollisionLayer;
        _worldCollisionMask = configuration.CollisionMask;
        CollisionLayer = _worldCollisionLayer;
        CollisionMask = _worldCollisionMask;
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = physics.Friction,
            Bounce = physics.Restitution,
        };
        foreach (var hull in physics.Hulls)
        {
            var points = hull.PointsGodotGameUnits.ToArray();
            if (points.Length < 4)
                throw new InvalidOperationException(
                    $"Pickup convex hull is incomplete: {ReferenceFormId}");
            AddChild(new CollisionShape3D
            {
                Name = "AuthoredDynamicHull",
                Shape = new ConvexPolygonShape3D
                {
                    Points = points,
                    Margin = 0.0f,
                },
            });
        }
        PhysicsSource = $"owned-nif-{physics.ShapeType}";
        CanGrab = true;
    }

    internal void CaptureAuthoredTransform() => _authoredTransform = GlobalTransform;

    internal virtual bool BeginHold()
    {
        if (!CanGrab || IsHeld)
            return false;
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        CollisionLayer = 0u;
        CollisionMask = 0u;
        IsHeld = true;
        return true;
    }

    internal void MoveHeld(Vector3 targetGlobalPosition)
    {
        if (!IsHeld)
            throw new InvalidOperationException("Pickup is not held.");
        GlobalPosition = targetGlobalPosition;
    }

    internal virtual void Drop()
    {
        if (!IsHeld)
            return;
        CollisionLayer = _worldCollisionLayer;
        CollisionMask = _worldCollisionMask;
        IsHeld = false;
        Freeze = false;
        Sleeping = false;
    }

    internal PickupState CaptureState() => new(
        ReferenceFormId,
        GlobalPosition,
        GlobalBasis.GetRotationQuaternion(),
        LinearVelocity,
        AngularVelocity);

    internal void RestoreState(PickupState state)
    {
        if (!state.ReferenceFormId.Equals(ReferenceFormId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pickup state belongs to another reference.");
        Freeze = true;
        GlobalTransform = new Transform3D(new Basis(state.Rotation), state.Position);
        LinearVelocity = state.LinearVelocity;
        AngularVelocity = state.AngularVelocity;
        CollisionLayer = _worldCollisionLayer;
        CollisionMask = _worldCollisionMask;
        IsHeld = false;
        Freeze = false;
        Sleeping = false;
    }

    internal readonly record struct WeaponProfile(int Damage, int ClipSize, string? AmmoFormId);

    internal readonly record struct PickupState(
        string ReferenceFormId,
        Vector3 Position,
        Quaternion Rotation,
        Vector3 LinearVelocity,
        Vector3 AngularVelocity);
}
