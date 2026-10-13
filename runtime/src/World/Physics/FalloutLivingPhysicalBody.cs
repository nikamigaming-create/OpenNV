using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed class FalloutLivingPhysicalBody : IFalloutPenetrationBody
{
    private readonly FalloutLivingActorModel _model;
    private readonly List<IFalloutPenetrationConstraint> _masters = [], _slaves = [];
    private readonly IReadOnlyList<IFalloutPenetrationConstraint> _readMasters, _readSlaves;
    private readonly string _name;
    private readonly bool _weapon;
    private readonly uint _shapeScaleBits;
    private byte _motion;
    private uint _filter;

    internal FalloutLivingPhysicalBody(FalloutLivingActorModel model, FalloutNifRigidBody source,
        FalloutNifCollisionObject collision, FalloutRigidBodyConstructionInputs inputs,
        FalloutLivingBodyConstruction current, IFalloutLivingBodyBackend backend, string key,
        FalloutLivingCollisionObject collisionProperty)
    {
        _model = model; Source = source; Collision = collision; Construction = inputs;
        Backend = backend; Key = key; _name = current.Name; _weapon = current.IsWeaponObject;
        _shapeScaleBits = current.ShapeScaleBits;
        CollisionProperty = collisionProperty; Wrapper = new(model, this);
        _motion = inputs.InitialMotion;
        _filter = source.Filter.Layer | (uint)source.Filter.Flags << 8 | (uint)source.Filter.Group << 16;
        _readMasters = _masters.AsReadOnly(); _readSlaves = _slaves.AsReadOnly();
    }

    public string Key { get; }
    public string Name { get { RequireCurrent(); return _name; } }
    public bool IsWeaponObject { get { RequireCurrent(); return _weapon; } }
    public byte Motion { get { RequireCurrent(); return _motion; } }
    public uint CollisionFilter { get { RequireCurrent(); return _filter; } }
    public string? WorldKey { get { RequireCurrent(); return CurrentWorld?.Key; } }
    public IReadOnlyList<IFalloutPenetrationConstraint> IncomingConstraints { get { RequireCurrent(); return _readMasters; } }
    public IReadOnlyList<IFalloutPenetrationConstraint> OutgoingConstraints { get { RequireCurrent(); return _readSlaves; } }
    public IReadOnlyList<IFalloutPenetrationConstraint?> LinkedConstraints { get { RequireCurrent(); return Wrapper.LinkedConstraints; } }
    internal FalloutLivingActorModel Model => _model;
    internal FalloutNifRigidBody Source { get; }
    internal FalloutNifCollisionObject Collision { get; }
    internal FalloutLivingCollisionObject CollisionProperty { get; }
    internal FalloutLivingRigidBodyWrapper Wrapper { get; }
    internal FalloutRigidBodyConstructionInputs Construction { get; }
    internal IFalloutLivingBodyBackend Backend { get; }
    internal FalloutLivingPhysicalWorld? CurrentWorld { get; set; }
    internal FalloutLivingPhysicalWorld? PendingWorld { get; set; }
    internal FalloutLivingPhysicalIsland? Island { get; set; }
    internal uint? EntityId { get; set; }
    internal List<IFalloutPenetrationConstraint> MasterMemberships => _masters;
    internal List<IFalloutPenetrationConstraint> SlaveMemberships => _slaves;

    internal void RequireCurrent()
    {
        _model.RequireCurrent(); Backend.RequireCurrent();
        if (CurrentWorld is not null) CurrentWorld.RequireCurrent();
    }

    internal void BindLinkedConstraints(FalloutLivingPhysicalConstraint[] constraints) =>
        Wrapper.BindLinks(constraints);

    public FalloutCharacterControllerProxyTransform ReadTransform() => Read().Transform;
    public FalloutPhysicalVector3 ReadCenterOfMass() => Read().CenterOfMass;
    public FalloutPhysicalVector3 ReadLinearVelocity() => Read().LinearVelocity;
    public FalloutPhysicalVector3 ReadAngularVelocity() => Read().AngularVelocity;
    public uint ReadInverseMassBits() => Read().InverseMassBits;
    private FalloutLivingBodyDynamics Read() { RequireCurrent(); return Backend.ReadDynamics(); }
    public FalloutPenetrationBounds ReadBounds() { RequireCurrent(); return Backend.ReadBounds(); }

    public FalloutPenetrationTerminalShape ReadTerminalShape()
    {
        RequireCurrent();
        var shape = _model.Source.Skeleton.Source.ReadObject(Source.Shape);
        return shape switch
        {
            FalloutNifSphereShape sphere => Terminal(2, sphere.Radius),
            FalloutNifBoxShape box => Terminal(5, box.Radius),
            FalloutNifCapsuleShape capsule => new(6, Vector(capsule.First), Vector(capsule.Second), Radius(capsule.Radius)),
            FalloutNifConvexVerticesShape convex => Terminal(7, convex.Radius),
            _ => throw new NotSupportedException($"Living terminal-shape runtime binding is unowned for {shape.Block.TypeName}."),
        };
        FalloutPenetrationTerminalShape Terminal(byte kind, float radius) => new(kind, default, default, Radius(radius));
        uint Radius(float radius) => Bits(FalloutPhysicalFloat32.Multiply(radius, BitConverter.UInt32BitsToSingle(_shapeScaleBits)));
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        FalloutPhysicalVector3 Vector(FalloutNifVector3 value)
        {
            var scale = BitConverter.UInt32BitsToSingle(_shapeScaleBits);
            return FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Multiply(value.X, scale),
                FalloutPhysicalFloat32.Multiply(value.Y, scale), FalloutPhysicalFloat32.Multiply(value.Z, scale));
        }
    }

    internal void PublishMotion(byte motion)
    {
        RequireCurrent();
        if (CurrentWorld is not null || PendingWorld is not null)
            throw new NotSupportedException("Living current-island motion conversion is unowned.");
        Backend.PublishMotion(motion); _motion = motion;
    }

    public void Activate() { RequireCurrent(); Backend.Activate(); }
    public void PublishCollisionFilter(uint filter)
    { RequireCurrent(); Backend.PublishCollisionFilter(filter); _filter = filter; }
    public void ApplyPointImpulse(FalloutPhysicalVector3 impulse, FalloutPhysicalVector3 point)
    { RequireCurrent(); Backend.ApplyPointImpulse(impulse, point); }

    internal FalloutLivingBodySnapshot Capture() => new(Key, Motion, CollisionFilter, Read(), _shapeScaleBits);
    internal void ValidateRestore(FalloutLivingBodySnapshot saved)
    {
        RequireCurrent();
        if (saved.Key != Key || saved.ShapeScaleBits != _shapeScaleBits || saved.Motion is < 1 or > 8 || saved.Dynamics is null ||
            !Finite(saved.Dynamics.Transform.Translation) || !Finite(saved.Dynamics.Transform.Rotation.Column0) ||
            !Finite(saved.Dynamics.Transform.Rotation.Column1) || !Finite(saved.Dynamics.Transform.Rotation.Column2) ||
            !Finite(saved.Dynamics.LinearVelocity) || !Finite(saved.Dynamics.AngularVelocity) ||
            !Finite(saved.Dynamics.CenterOfMass) || !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.Dynamics.InverseMassBits)) ||
            BitConverter.UInt32BitsToSingle(saved.Dynamics.InverseMassBits) < 0 || !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.Dynamics.MassBits)) ||
            BitConverter.UInt32BitsToSingle(saved.Dynamics.MassBits) <= 0)
            throw new InvalidDataException("Cold living physical body contains invalid source/current fields.");
        // The backend derives inverse mass from its saved motion and mass.
        // Validate that redundant field before any row is published.
        Backend.ValidateRestore(saved.Motion, saved.Dynamics);
        static bool Finite(FalloutPhysicalVector3 value) =>
            float.IsFinite(value.XValue) && float.IsFinite(value.YValue) && float.IsFinite(value.ZValue);
    }
    internal void Restore(FalloutLivingBodySnapshot saved)
    {
        PublishMotion(saved.Motion); PublishCollisionFilter(saved.CollisionFilter);
        Backend.RestoreDynamics(saved.Dynamics);
    }
}

internal sealed class FalloutLivingPhysicalConstraint : IFalloutPenetrationConstraint
{
    private readonly FalloutLivingActorModel _model;
    internal FalloutLivingPhysicalConstraint(FalloutLivingActorModel model, FalloutNifPhysicalConstraint source,
        FalloutLivingPhysicalBody first, FalloutLivingPhysicalBody second, IFalloutLivingConstraintBackend backend, string key)
    {
        _model = model; Source = source; FirstBody = first; SecondBody = second; Backend = backend; Key = key;
        var type = source.Header.Block.TypeName == "bhkMalleableConstraint" ? 13 : checked((int)source.Header.WrappedType);
        Wrapper = new(model, this, type);
    }
    public string Key { get; }
    public int? DataType
    {
        get
        {
            RequireCurrent();
            // Malleable data retains its actual outer type, independently of
            // the wrapped angular descriptor used by the backend joint.
            return Wrapper.CurrentData?.Type;
        }
    }
    public IFalloutPenetrationBody First { get { RequireCurrent(); return FirstBody; } }
    public IFalloutPenetrationBody Second { get { RequireCurrent(); return SecondBody; } }
    internal FalloutNifPhysicalConstraint Source { get; }
    internal FalloutLivingPhysicalBody FirstBody { get; }
    internal FalloutLivingPhysicalBody SecondBody { get; }
    internal IFalloutLivingConstraintBackend Backend { get; }
    internal FalloutLivingConstraintWrapper Wrapper { get; }
    internal FalloutLivingPhysicalWorld? CurrentWorld { get; set; }
    internal FalloutLivingPhysicalWorld? PendingWorld { get; set; }
    internal FalloutLivingPhysicalBody? Master { get; set; }
    internal FalloutLivingPhysicalBody? Slave { get; set; }
    internal int? MasterIndex { get; set; }
    internal int? SlaveIndex { get; set; }
    internal void RequireCurrent() { _model.RequireCurrent(); Backend.RequireCurrent(); }
}
