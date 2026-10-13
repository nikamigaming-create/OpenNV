using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class RuntimeNativeLivingPhysicsBackend : IFalloutLivingPhysicsBackend
{
    internal const string PhysicalBoundary = "actual-Godot-body-shape-joint;source-Ni-Bhk-units-Havok-motion-inertia-angular-envelope-malleable-filter-contact-and-solver-parity-unverified";
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly Func<Rid> _readCurrentSpace;
    private readonly Rid _space;
    private readonly float _units;
    private readonly float _physicalToSource;
    private readonly float _physicalToNative;

    internal RuntimeNativeLivingPhysicsBackend(string worldKey, Func<Rid> readCurrentSpace, float unitsToMetres,
        uint physicalToSourceBits = FalloutPhysicalConstructorInputs.PhysicalToSourceBits)
    {
        ArgumentNullException.ThrowIfNull(readCurrentSpace);
        if (string.IsNullOrWhiteSpace(worldKey) || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentException("Living backend requires actual world identity and finite positive units.");
        _space = readCurrentSpace();
        if (!_space.IsValid) throw new InvalidDataException("Living backend has no actual current physics space.");
        WorldKey = worldKey; _readCurrentSpace = readCurrentSpace; _units = unitsToMetres;
        _physicalToSource = BitConverter.UInt32BitsToSingle(physicalToSourceBits);
        if (!float.IsFinite(_physicalToSource) || _physicalToSource <= 0)
            throw new InvalidDataException("Living physical-to-source conversion is invalid.");
        _physicalToNative = _physicalToSource * unitsToMetres;
    }

    public string WorldKey { get; }
    public string Boundary => PhysicalBoundary;
    public void RequireCurrent()
    {
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native living physics requires its owning thread.");
        if (_readCurrentSpace() != _space)
            throw new InvalidOperationException("Native living physics space changed or retired.");
    }

    public IFalloutLivingBodyBackend CreateBody(FalloutNifFile source, FalloutNifCollisionObject collision,
        FalloutRigidBodyConstructionInputs inputs, FalloutLivingBodyConstruction current)
    {
        RequireCurrent();
        if (collision.Body != inputs.Source.Block.Index)
            throw new InvalidDataException("Living body constructor lost its actual collision property.");
        return new NativeBody(this, source, inputs, current);
    }

    public IFalloutLivingConstraintBackend CreateConstraint(FalloutNifPhysicalConstraint source,
        IFalloutLivingBodyBackend first, IFalloutLivingBodyBackend second)
    {
        RequireCurrent();
        if (first is not NativeBody a || second is not NativeBody b || a.Owner != this || b.Owner != this)
            throw new InvalidDataException("Living joint endpoints belong to another native factory.");
        if (source.Declaration is not (FalloutNifRagdollConstraint or FalloutNifHingeConstraint))
            throw new NotSupportedException("Living joint has no actual physical descriptor owner.");
        if (source.Declaration is FalloutNifRagdollConstraint { Motor.Enabled: true })
            throw new NotSupportedException("Living joint enabled motor has no physical drive owner.");
        return new NativeConstraint(this, source, a, b);
    }

    public void AddBody(IFalloutLivingBodyBackend body)
    {
        RequireCurrent(); var current = RequireBody(body); current.RequireCurrent();
        if (current.IsWorldAttached) throw new InvalidOperationException("Native body is already in a physics space.");
        PhysicsServer3D.BodySetSpace(current.Handle, _space);
    }
    public void RemoveBody(IFalloutLivingBodyBackend body)
    {
        RequireCurrent(); var current = RequireBody(body); current.RequireCurrent();
        if (PhysicsServer3D.BodyGetSpace(current.Handle) != _space)
            throw new InvalidOperationException("Native body residency changed before removal.");
        PhysicsServer3D.BodySetSpace(current.Handle, default);
    }
    public void AddConstraint(IFalloutLivingConstraintBackend constraint)
    {
        RequireCurrent(); var current = RequireConstraint(constraint); current.RequireCurrent();
        if (current.IsWorldAttached || PhysicsServer3D.BodyGetSpace(current.First.Handle) != _space ||
            PhysicsServer3D.BodyGetSpace(current.Second.Handle) != _space)
            throw new InvalidOperationException("Native joint admission requires two actual current endpoint residencies.");
        current.Admit();
    }
    public void RemoveConstraint(IFalloutLivingConstraintBackend constraint)
    {
        RequireCurrent(); var current = RequireConstraint(constraint); current.RequireCurrent();
        if (!current.IsWorldAttached) throw new InvalidOperationException("Native joint is not attached.");
        PhysicsServer3D.JointClear(current.Handle); current.Attached = false;
    }
    private NativeBody RequireBody(IFalloutLivingBodyBackend body) => body is NativeBody result && result.Owner == this
        ? result : throw new InvalidDataException("Living body is from another backend/world.");
    private NativeConstraint RequireConstraint(IFalloutLivingConstraintBackend constraint) => constraint is NativeConstraint result && result.Owner == this
        ? result : throw new InvalidDataException("Living constraint is from another backend/world.");

    private Vector3 ToNative(FalloutPhysicalVector3 source, bool distance = true) =>
        GamebryoCoordinate.ConvertVector(new(source.XValue, source.YValue, source.ZValue)) * (distance ? _physicalToNative : 1);
    private FalloutPhysicalVector3 FromNative(Vector3 native, bool distance = true)
    {
        var scale = distance ? _physicalToNative : 1;
        return FalloutPhysicalVector3.FromValues(native.X / scale, -native.Z / scale, native.Y / scale);
    }
    private Transform3D ToNative(FalloutCharacterControllerProxyTransform source)
    {
        var rotation = source.Rotation;
        return new(GamebryoCoordinate.ConvertBasis([
            rotation.Column0.XValue, rotation.Column1.XValue, rotation.Column2.XValue,
            rotation.Column0.YValue, rotation.Column1.YValue, rotation.Column2.YValue,
            rotation.Column0.ZValue, rotation.Column1.ZValue, rotation.Column2.ZValue], 1, "living body pose"), ToNative(source.Translation));
    }
    private FalloutCharacterControllerProxyTransform FromNative(Transform3D native)
    {
        var x = FromNative(native.Basis.X, false);
        var y = FromNative(-native.Basis.Z, false);
        var z = FromNative(native.Basis.Y, false);
        return new(new(x, y, z), FromNative(native.Origin));
    }

    private sealed class NativeBody : IFalloutLivingBodyBackend
    {
        private readonly IReadOnlyList<RuntimeNativeLivingBodyShape> _shapes;
        private bool _retired;
        private uint? _filter;
        internal NativeBody(RuntimeNativeLivingPhysicsBackend owner, FalloutNifFile source,
            FalloutRigidBodyConstructionInputs inputs, FalloutLivingBodyConstruction current)
        {
            Owner = owner; var declaration = inputs.Source;
            if (declaration.Mass == 0 && inputs.InitialMotion != FalloutLivingPhysicalWorld.FixedMotion)
                throw new NotSupportedException("Living zero-mass nonfixed body has no source-equivalent native mass owner.");
            var shapeScale = BitConverter.UInt32BitsToSingle(current.ShapeScaleBits);
            if (!float.IsFinite(shapeScale) || shapeScale <= 0) throw new InvalidDataException("Living physical shape scale is invalid.");
            _shapes = NativeNifCollisionBuilder.BuildLivingBodyShapes(source, declaration, owner._units * shapeScale, owner._physicalToSource);
            Handle = PhysicsServer3D.BodyCreate();
            try
            {
                PublishMotion(inputs.InitialMotion);
                foreach (var shape in _shapes) PhysicsServer3D.BodyAddShape(Handle, shape.Shape.GetRid(), shape.Transform);
                if (declaration.Mass > 0) PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.Mass, declaration.Mass);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.CenterOfMass,
                    owner.ToNative(FalloutPhysicalVector3.FromValues(declaration.Center.X * shapeScale, declaration.Center.Y * shapeScale, declaration.Center.Z * shapeScale)));
                // Godot accepts principal inertia only. Preserve all original
                // matrix components in the C# construction owner; off-diagonal
                // Havok inertia equivalence remains in the explicit boundary.
                var scaleSquared = owner._physicalToNative * owner._physicalToNative * shapeScale * shapeScale;
                var diagonal = new Vector3(declaration.Inertia.Row0.X, declaration.Inertia.Row2.Z, declaration.Inertia.Row1.Y) * scaleSquared;
                if (diagonal.X > 0 && diagonal.Y > 0 && diagonal.Z > 0)
                    PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.Inertia, diagonal);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.Friction, declaration.Friction);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.Bounce, inputs.Restitution);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.LinearDampMode, (int)RigidBody3D.DampMode.Replace);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.LinearDamp, declaration.LinearDamping);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.AngularDampMode, (int)RigidBody3D.DampMode.Replace);
                PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.AngularDamp, declaration.AngularDamping);
                PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.Transform, owner.ToNative(current.Transform));
                PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.LinearVelocity,
                    owner.ToNative(FalloutPhysicalVector3.FromValues(declaration.LinearVelocity.X, declaration.LinearVelocity.Y, declaration.LinearVelocity.Z)));
                PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.AngularVelocity,
                    owner.ToNative(FalloutPhysicalVector3.FromValues(declaration.AngularVelocity.X, declaration.AngularVelocity.Y, declaration.AngularVelocity.Z), false));
                PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.Sleeping, !current.Active);
                PublishCollisionFilter(declaration.Filter.Layer | (uint)declaration.Filter.Flags << 8 | (uint)declaration.Filter.Group << 16);
            }
            catch { Dispose(); throw; }
        }
        internal RuntimeNativeLivingPhysicsBackend Owner { get; }
        internal Rid Handle { get; private set; }
        public string Boundary => PhysicalBoundary;
        public bool HasPhysicalObject
        { get { if (_retired) return false; RequireCurrent(); return Handle.IsValid && PhysicsServer3D.BodyGetShapeCount(Handle) == _shapes.Count; } }
        public bool IsWorldAttached { get { RequireCurrent(); return PhysicsServer3D.BodyGetSpace(Handle).IsValid; } }
        public ulong PhysicalObjectId { get { RequireCurrent(); return Handle.Id; } }
        public int PhysicalShapeCount { get { RequireCurrent(); return PhysicsServer3D.BodyGetShapeCount(Handle); } }
        public void RequireCurrent() { Owner.RequireCurrent(); ObjectDisposedException.ThrowIf(_retired, this); }

        public FalloutLivingBodyDynamics ReadDynamics()
        {
            RequireCurrent();
            var transform = PhysicsServer3D.BodyGetState(Handle, PhysicsServer3D.BodyState.Transform).AsTransform3D();
            var center = PhysicsServer3D.BodyGetParam(Handle, PhysicsServer3D.BodyParameter.CenterOfMass).AsVector3();
            var mass = PhysicsServer3D.BodyGetParam(Handle, PhysicsServer3D.BodyParameter.Mass).AsSingle();
            var inverse = NativeInverseMassBits(PhysicsServer3D.BodyGetMode(Handle), mass);
            return new(Owner.FromNative(transform), Owner.FromNative(transform * center),
                Owner.FromNative(PhysicsServer3D.BodyGetState(Handle, PhysicsServer3D.BodyState.LinearVelocity).AsVector3()),
                Owner.FromNative(PhysicsServer3D.BodyGetState(Handle, PhysicsServer3D.BodyState.AngularVelocity).AsVector3(), false),
                inverse, PhysicsServer3D.BodyGetState(Handle, PhysicsServer3D.BodyState.Sleeping).AsBool())
            { MassBits = BitConverter.SingleToUInt32Bits(mass) };
        }

        public void ValidateRestore(byte motion, FalloutLivingBodyDynamics state)
        {
            RequireCurrent();
            ValidateMass(ModeForMotion(motion), state);
        }

        public void RestoreDynamics(FalloutLivingBodyDynamics state)
        {
            RequireCurrent();
            var mass = ValidateMass(PhysicsServer3D.BodyGetMode(Handle), state);
            var transform = Owner.ToNative(state.Transform);
            PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.Transform, transform);
            PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.CenterOfMass,
                transform.AffineInverse() * Owner.ToNative(state.CenterOfMass));
            PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.LinearVelocity, Owner.ToNative(state.LinearVelocity));
            PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.AngularVelocity, Owner.ToNative(state.AngularVelocity, false));
            PhysicsServer3D.BodySetParam(Handle, PhysicsServer3D.BodyParameter.Mass, mass);
            PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.Sleeping, state.Sleeping);
        }

        public void PublishMotion(byte motion)
        {
            RequireCurrent();
            PhysicsServer3D.BodySetMode(Handle, ModeForMotion(motion));
        }
        private static PhysicsServer3D.BodyMode ModeForMotion(byte motion) => motion switch
        {
            7 => PhysicsServer3D.BodyMode.Static,
            6 => PhysicsServer3D.BodyMode.Kinematic,
            1 or 2 or 3 or 4 or 5 or 8 => PhysicsServer3D.BodyMode.Rigid,
            _ => throw new NotSupportedException($"Living physical motion {motion} has no backend body mode."),
        };
        private static uint NativeInverseMassBits(PhysicsServer3D.BodyMode mode, float mass)
        {
            if (!float.IsFinite(mass) || mass <= 0)
                throw new InvalidDataException("Living native body mass must be finite and positive.");
            var inverse = mode is PhysicsServer3D.BodyMode.Static or PhysicsServer3D.BodyMode.Kinematic ? 0f : 1f / mass;
            if (!float.IsFinite(inverse))
                throw new InvalidDataException("Living native inverse mass is not finite.");
            return BitConverter.SingleToUInt32Bits(inverse);
        }
        private static float ValidateMass(PhysicsServer3D.BodyMode mode, FalloutLivingBodyDynamics state)
        {
            var mass = BitConverter.UInt32BitsToSingle(state.MassBits);
            if (state.InverseMassBits != NativeInverseMassBits(mode, mass))
                throw new InvalidDataException("Cold living inverse mass disagrees with native motion and mass.");
            return mass;
        }
        public void PublishCollisionFilter(uint filter)
        {
            RequireCurrent();
            if (_filter is { } previous && ((previous ^ filter) & 0xffffff00u) != 0)
                throw new NotSupportedException("Living source filter group/flag publication has no native filter-update owner.");
            var sourceLayer = (byte)filter;
            if (sourceLayer >= sizeof(uint) * 8)
                throw new NotSupportedException("Living source collision layer exceeds native backend layer bits.");
            PhysicsServer3D.BodySetCollisionLayer(Handle, 1u << sourceLayer);
            PhysicsServer3D.BodySetCollisionMask(Handle, uint.MaxValue);
            _filter = filter;
            // Group, source flags and filter-update semantics remain retained
            // by the C# owner and visibly unverified by this native adapter.
        }
        public void Activate() { RequireCurrent(); PhysicsServer3D.BodySetState(Handle, PhysicsServer3D.BodyState.Sleeping, false); }
        public void ApplyPointImpulse(FalloutPhysicalVector3 impulse, FalloutPhysicalVector3 point)
        {
            RequireCurrent();
            if (!IsWorldAttached) throw new InvalidOperationException("Physical point impulse needs actual current world residency.");
            var origin = PhysicsServer3D.BodyGetState(Handle, PhysicsServer3D.BodyState.Transform).AsTransform3D().Origin;
            PhysicsServer3D.BodyApplyImpulse(Handle, Owner.ToNative(impulse), Owner.ToNative(point) - origin);
        }
        public FalloutPenetrationBounds ReadBounds()
        {
            RequireCurrent();
            var transform = PhysicsServer3D.BodyGetState(Handle, PhysicsServer3D.BodyState.Transform).AsTransform3D();
            Aabb? union = null;
            foreach (var shape in _shapes)
            {
                var bounds = ShapeBounds(shape.Shape);
                var current = transform * shape.Transform * bounds;
                union = union is { } previous ? previous.Merge(current) : current;
            }
            var result = union ?? throw new InvalidDataException("Actual physical body lost its shape bounds.");
            // Axis conversion negates native Z; transform all eight corners.
            var sourceCorners = Enumerable.Range(0, 8).Select(index => Owner.FromNative(result.GetEndpoint(index))).ToArray();
            return new(FalloutPhysicalVector3.FromValues(sourceCorners.Min(value => value.XValue), sourceCorners.Min(value => value.YValue), sourceCorners.Min(value => value.ZValue)),
                FalloutPhysicalVector3.FromValues(sourceCorners.Max(value => value.XValue), sourceCorners.Max(value => value.YValue), sourceCorners.Max(value => value.ZValue)));
        }
        private static Aabb ShapeBounds(Shape3D shape)
        {
            var half = shape switch
            {
                BoxShape3D box => box.Size / 2,
                SphereShape3D sphere => Vector3.One * sphere.Radius,
                CapsuleShape3D capsule => new Vector3(capsule.Radius, capsule.Height / 2, capsule.Radius),
                _ => Vector3.Zero,
            };
            if (half != Vector3.Zero) return new(-half, half * 2);
            if (shape is ConvexPolygonShape3D convex && convex.Points.Length != 0)
            {
                var min = convex.Points[0]; var max = min;
                foreach (var point in convex.Points) { min = min.Min(point); max = max.Max(point); }
                var margin = Vector3.One * convex.Margin;
                return new(min - margin, max - min + margin * 2);
            }
            throw new NotSupportedException("Living current bounds have no physical source shape owner.");
        }
        public void Dispose()
        {
            if (_retired) return; Owner.RequireCurrent();
            if (Handle.IsValid) { PhysicsServer3D.FreeRid(Handle); Handle = default; }
            foreach (var shape in _shapes) shape.Shape.Dispose(); _retired = true;
        }
    }
}
