using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

// The actual model publication supplies these current fields. A declaration
// pose, visible bone or source block count is not a current physical body.
internal sealed record FalloutLivingBodyConstruction(string Name, bool IsWeaponObject,
    FalloutCharacterControllerProxyTransform Transform, bool Active)
{
    public uint ShapeScaleBits { get; init; } = BitConverter.SingleToUInt32Bits(1);
}

internal sealed record FalloutLivingBodyDynamics(FalloutCharacterControllerProxyTransform Transform,
    FalloutPhysicalVector3 CenterOfMass, FalloutPhysicalVector3 LinearVelocity,
    FalloutPhysicalVector3 AngularVelocity, uint InverseMassBits, bool Sleeping)
{
    public uint MassBits { get; init; }
}

internal interface IFalloutLivingBodyBackend : IDisposable
{
    string Boundary { get; }
    bool HasPhysicalObject { get; }
    bool IsWorldAttached { get; }
    ulong PhysicalObjectId { get; }
    int PhysicalShapeCount { get; }
    void RequireCurrent();
    FalloutLivingBodyDynamics ReadDynamics();
    FalloutPenetrationBounds ReadBounds();
    void ValidateRestore(byte motion, FalloutLivingBodyDynamics state);
    void RestoreDynamics(FalloutLivingBodyDynamics state);
    void PublishMotion(byte motion);
    void PublishCollisionFilter(uint filter);
    void Activate();
    void ApplyPointImpulse(FalloutPhysicalVector3 impulse, FalloutPhysicalVector3 point);
}

internal interface IFalloutLivingConstraintBackend : IDisposable
{
    bool HasPhysicalObject { get; }
    bool IsWorldAttached { get; }
    ulong PhysicalObjectId { get; }
    void RequireCurrent();
}

// Godot's backend is the shared physics server, including --headless. Neither
// construction nor admission requires a rendered node, mesh or scene tree.
internal interface IFalloutLivingPhysicsBackend
{
    string WorldKey { get; }
    string Boundary { get; }
    void RequireCurrent();
    IFalloutLivingBodyBackend CreateBody(FalloutNifFile source,
        FalloutNifCollisionObject collision, FalloutRigidBodyConstructionInputs inputs,
        FalloutLivingBodyConstruction current);
    IFalloutLivingConstraintBackend CreateConstraint(FalloutNifPhysicalConstraint source,
        IFalloutLivingBodyBackend first, IFalloutLivingBodyBackend second);
    void AddBody(IFalloutLivingBodyBackend body);
    void RemoveBody(IFalloutLivingBodyBackend body);
    void AddConstraint(IFalloutLivingConstraintBackend constraint);
    void RemoveConstraint(IFalloutLivingConstraintBackend constraint);
}

internal sealed record FalloutLivingBodySnapshot(string Key, byte Motion, uint CollisionFilter,
    FalloutLivingBodyDynamics Dynamics, uint ShapeScaleBits);
internal sealed record FalloutLivingModelSnapshot(string Key, string SourceSha256,
    IReadOnlyList<FalloutLivingBodySnapshot> Bodies);
