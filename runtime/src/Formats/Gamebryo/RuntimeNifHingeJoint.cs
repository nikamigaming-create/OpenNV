using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal partial class RuntimeNifHingeJoint : Node
{
    private PhysicsBody3D _first = null!, _second = null!;
    private Transform3D _frameA, _frameB;
    private Rid _joint;
    private float? _minimum, _maximum;
    private float _friction, _frictionTorque;
    private int _frictionRate;
    internal bool Bound => _joint.IsValid;
    internal bool Unrestricted => _minimum is null && _friction == 0;
    internal Vector3 Axis => _first.GlobalBasis.Orthonormalized() * _frameA.Basis.Z;
    internal float PivotDistance => Pivot(_first, _frameA).DistanceTo(Pivot(_second, _frameB));
    internal float AxisAgreement => Axis.Dot(_second.GlobalBasis.Orthonormalized() * _frameB.Basis.Z);
    internal object State => new
    {
        source = GetMeta("opennv_nif_constraint").AsInt32(),
        bound = Bound,
        firstBody = _first.GetMeta("opennv_nif_collision_body", -1).AsInt32(),
        secondBody = _second.GetMeta("opennv_nif_collision_body", -1).AsInt32(),
        pivotDistance = PivotDistance,
        axisAgreement = AxisAgreement,
        minimum = _minimum,
        maximum = _maximum,
        frictionTorque = _frictionTorque,
    };

    private static Vector3 Pivot(PhysicsBody3D body, Transform3D frame) => body.GlobalTransform * frame.Origin;

    internal void Configure(FalloutNifHingeConstraint source, PhysicsBody3D first, PhysicsBody3D second, float unitsToMetres)
    {
        Name = $"NifHinge{source.Header.Block.Index}";
        _first = first; _second = second;
        _frameA = Frame(source.AxisA, source.PerpendicularA1, source.PivotA, unitsToMetres);
        _frameB = Frame(source.AxisB, source.PerpendicularB1, source.PivotB, unitsToMetres);
        _minimum = source.Minimum; _maximum = source.Maximum;
        _friction = source.Friction * MathF.Pow(7 * unitsToMetres, 2);
        SetMeta("opennv_nif_constraint", source.Header.Block.Index);
    }

    private static Transform3D Frame(FalloutNifVector3 axis, FalloutNifVector3 perpendicular, FalloutNifVector3 pivot, float units)
    {
        static Vector3 Convert(FalloutNifVector3 value) => GamebryoCoordinate.ConvertVector(new(value.X, value.Y, value.Z));
        // Godot's hinge rotates about local Z. The source's declared
        // perpendicular and axis define its proper orthonormal frame.
        var x = Convert(perpendicular).Normalized(); var z = Convert(axis).Normalized();
        return new(new Basis(x, z.Cross(x).Normalized(), z), Convert(pivot) * (7 * units));
    }

    public override void _Ready()
    {
        static float UniformScale(PhysicsBody3D body)
        {
            var scale = body.GlobalBasis.Scale;
            if (MathF.Abs(scale.X - scale.Y) > .001f || MathF.Abs(scale.X - scale.Z) > .001f)
                throw new NotSupportedException("Nonuniform model hinge scale is unsupported.");
            return scale.X;
        }
        var first = _frameA; first.Origin *= UniformScale(_first);
        var second = _frameB; second.Origin *= UniformScale(_second);
        _joint = PhysicsServer3D.JointCreate();
        try
        {
            PhysicsServer3D.JointMakeHinge(_joint, _first.GetRid(), first, _second.GetRid(), second);
            PhysicsServer3D.HingeJointSetFlag(_joint, PhysicsServer3D.HingeJointFlag.UseLimit, _minimum is not null);
            if (_minimum is { } lower && _maximum is { } upper)
            {
                PhysicsServer3D.HingeJointSetParam(_joint, PhysicsServer3D.HingeJointParam.LimitLower, lower);
                PhysicsServer3D.HingeJointSetParam(_joint, PhysicsServer3D.HingeJointParam.LimitUpper, upper);
            }
            PhysicsServer3D.HingeJointSetFlag(_joint, PhysicsServer3D.HingeJointFlag.EnableMotor, _friction != 0);
            _frictionTorque = _friction * MathF.Pow(UniformScale(_first), 2);
            PublishFriction();
            PhysicsServer3D.JointDisableCollisionsBetweenBodies(_joint, true);
        }
        catch
        {
            PhysicsServer3D.FreeRid(_joint);
            _joint = default;
            throw;
        }
    }

    private void PublishFriction()
    {
        // Source maximum friction is a torque. Godot's zero-speed motor
        // bounds the opposing angular impulse for one physics step.
        _frictionRate = Engine.PhysicsTicksPerSecond;
        PhysicsServer3D.HingeJointSetParam(_joint, PhysicsServer3D.HingeJointParam.MotorTargetVelocity, 0);
        PhysicsServer3D.HingeJointSetParam(_joint, PhysicsServer3D.HingeJointParam.MotorMaxImpulse, _frictionTorque / _frictionRate);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_joint.IsValid && _frictionRate != Engine.PhysicsTicksPerSecond) PublishFriction();
    }

    public override void _ExitTree()
    {
        if (_joint.IsValid) PhysicsServer3D.FreeRid(_joint);
        _joint = default;
        RequestReady();
    }
}
