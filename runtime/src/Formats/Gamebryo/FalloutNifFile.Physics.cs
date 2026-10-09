namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifBlendController(FalloutNifBlock Block,
    FalloutNifTimeController Time, uint Keys) : FalloutNifObject(Block);

internal abstract record FalloutNifConstraintMotor(byte Type, float MinimumForce,
    float MaximumForce, bool Enabled);

internal sealed record FalloutNifPositionConstraintMotor(float MinimumForce, float MaximumForce,
    float Tau, float Damping, float ProportionalRecoveryVelocity, float ConstantRecoveryVelocity,
    bool Enabled) : FalloutNifConstraintMotor(1, MinimumForce, MaximumForce, Enabled);

internal sealed record FalloutNifVelocityConstraintMotor(float MinimumForce, float MaximumForce,
    float Tau, float TargetVelocity, bool UseVelocityTarget, bool Enabled)
    : FalloutNifConstraintMotor(2, MinimumForce, MaximumForce, Enabled);

internal sealed record FalloutNifSpringConstraintMotor(float MinimumForce, float MaximumForce,
    float SpringConstant, float SpringDamping, bool Enabled)
    : FalloutNifConstraintMotor(3, MinimumForce, MaximumForce, Enabled);

internal sealed record FalloutNifRagdollConstraint(FalloutNifConstraintHeader Header,
    FalloutNifVector3 TwistA, FalloutNifVector3 PlaneA, FalloutNifVector3 MotorA, FalloutNifVector3 PivotA,
    FalloutNifVector3 TwistB, FalloutNifVector3 PlaneB, FalloutNifVector3 MotorB, FalloutNifVector3 PivotB,
    float Cone, float PlaneMinimum, float PlaneMaximum, float TwistMinimum, float TwistMaximum,
    float Friction, float Strength) : FalloutNifObject(Header.Block)
{
    internal FalloutNifConstraintMotor? Motor { get; init; }
    internal uint[] VectorPadding { get; init; } = [];
}

internal sealed record FalloutNifHingeConstraint(FalloutNifConstraintHeader Header,
    FalloutNifVector3 AxisA, FalloutNifVector3 PerpendicularA1, FalloutNifVector3 PerpendicularA2, FalloutNifVector3 PivotA,
    FalloutNifVector3 AxisB, FalloutNifVector3 PerpendicularB1, FalloutNifVector3 PerpendicularB2, FalloutNifVector3 PivotB,
    float? Minimum = null, float? Maximum = null, float Friction = 0) : FalloutNifObject(Header.Block)
{
    internal uint[] VectorPadding { get; init; } = [];
}

internal sealed partial class FalloutNifFile
{
    private FalloutNifBlendController ReadBlendController(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, ReadTimeController(ref cursor, "Havok blend controller"), cursor.ReadUInt32("Havok blend keys"));

    private FalloutNifRagdollConstraint ReadRagdollDeclaration(FalloutNifBlock block, ref NifCursor cursor)
    {
        var header = ReadConstraintHeader(block, ref cursor);
        if (header.WrappedType is not (2 or 7))
            throw new NotSupportedException($"NIF constraint {block.Index} wrapped type {header.WrappedType} has no joint descriptor owner.");
        var padding = new uint[8];
        var twistA = ReadJointVector(ref cursor, "twist A", padding, 0);
        var planeA = ReadJointVector(ref cursor, "plane A", padding, 1);
        var motorA = ReadJointVector(ref cursor, "motor A", padding, 2);
        var pivotA = ReadJointVector(ref cursor, "pivot A", padding, 3);
        var twistB = ReadJointVector(ref cursor, "twist B", padding, 4);
        var planeB = ReadJointVector(ref cursor, "plane B", padding, 5);
        var motorB = ReadJointVector(ref cursor, "motor B", padding, 6);
        var pivotB = ReadJointVector(ref cursor, "pivot B", padding, 7);
        var cone = header.WrappedType == 7 ? cursor.ReadFiniteSingle("cone angle") : 0;
        var planeMin = header.WrappedType == 7 ? cursor.ReadFiniteSingle("plane minimum") : 0;
        var planeMax = header.WrappedType == 7 ? cursor.ReadFiniteSingle("plane maximum") : 0;
        var twistMin = cursor.ReadFiniteSingle("twist minimum");
        var twistMax = cursor.ReadFiniteSingle("twist maximum");
        var friction = cursor.ReadFiniteSingle("joint friction");
        var motor = ReadConstraintMotor(ref cursor);
        var strength = block.TypeName == "bhkMalleableConstraint" ? cursor.ReadFiniteSingle("malleable strength") : 1;
        if (cone is < 0 or > MathF.PI || planeMin < -MathF.PI || planeMax > MathF.PI || planeMin > planeMax ||
            twistMin < -MathF.PI || twistMax > MathF.PI || twistMin > twistMax || friction < 0 || strength is < 0 or > 1)
            throw new InvalidDataException("Ragdoll joint limits are invalid.");
        return new(header, twistA, planeA, motorA, pivotA, twistB, planeB, motorB, pivotB,
            cone, planeMin, planeMax, twistMin, twistMax, friction, strength)
        { Motor = motor, VectorPadding = padding };
    }

    private FalloutNifHingeConstraint ReadHingeDeclaration(FalloutNifBlock block, ref NifCursor cursor)
    {
        var header = ReadConstraintHeader(block, ref cursor);
        if (header.Block.TypeName != "bhkHingeConstraint" || header.WrappedType != 1)
            throw new NotSupportedException("This model joint requires its own constraint owner.");
        var padding = new uint[8];
        var axisA = ReadJointVector(ref cursor, "hinge axis A", padding, 0);
        var perpendicularA1 = ReadJointVector(ref cursor, "hinge perpendicular A1", padding, 1);
        var perpendicularA2 = ReadJointVector(ref cursor, "hinge perpendicular A2", padding, 2);
        var pivotA = ReadJointVector(ref cursor, "hinge pivot A", padding, 3);
        var axisB = ReadJointVector(ref cursor, "hinge axis B", padding, 4);
        var perpendicularB1 = ReadJointVector(ref cursor, "hinge perpendicular B1", padding, 5);
        var perpendicularB2 = ReadJointVector(ref cursor, "hinge perpendicular B2", padding, 6);
        var pivotB = ReadJointVector(ref cursor, "hinge pivot B", padding, 7);
        ValidateHingeAxes(axisA, perpendicularA1, perpendicularA2);
        ValidateHingeAxes(axisB, perpendicularB1, perpendicularB2);
        return new(header, axisA, perpendicularA1, perpendicularA2, pivotA,
            axisB, perpendicularB1, perpendicularB2, pivotB)
        { VectorPadding = padding };
    }

    private static FalloutNifVector3 ReadJointVector(ref NifCursor cursor, string name, uint[] padding, int ordinal)
    {
        var result = new FalloutNifVector3(cursor.ReadFiniteSingle(name + " X"),
            cursor.ReadFiniteSingle(name + " Y"), cursor.ReadFiniteSingle(name + " Z"));
        // This word is padding, not a fourth floating component. Original
        // joint pivots commonly retain 0xffffffff here.
        padding[ordinal] = cursor.ReadUInt32(name + " padding");
        return result;
    }

    private static FalloutNifConstraintMotor? ReadConstraintMotor(ref NifCursor cursor)
    {
        var type = cursor.ReadByte("motor type");
        return type switch
        {
            0 => null,
            1 => new FalloutNifPositionConstraintMotor(cursor.ReadFiniteSingle("motor minimum force"),
                cursor.ReadFiniteSingle("motor maximum force"), cursor.ReadFiniteSingle("motor tau"),
                cursor.ReadFiniteSingle("motor damping"), cursor.ReadFiniteSingle("motor proportional recovery velocity"),
                cursor.ReadFiniteSingle("motor constant recovery velocity"), cursor.ReadBoolean("motor enabled")),
            2 => new FalloutNifVelocityConstraintMotor(cursor.ReadFiniteSingle("motor minimum force"),
                cursor.ReadFiniteSingle("motor maximum force"), cursor.ReadFiniteSingle("motor tau"),
                cursor.ReadFiniteSingle("motor target velocity"), cursor.ReadBoolean("motor use velocity target"),
                cursor.ReadBoolean("motor enabled")),
            3 => new FalloutNifSpringConstraintMotor(cursor.ReadFiniteSingle("motor minimum force"),
                cursor.ReadFiniteSingle("motor maximum force"), cursor.ReadFiniteSingle("motor spring constant"),
                cursor.ReadFiniteSingle("motor spring damping"), cursor.ReadBoolean("motor enabled")),
            _ => throw new NotSupportedException($"NIF constraint motor type {type} has no source descriptor owner."),
        };
    }

    private static void RequireNativeMotor(FalloutNifRagdollConstraint declaration)
    {
        // Parsing a complete motor declaration does not invent a native drive.
        // Disabled motors exert no drive; retain their fields without enabling one.
        if (declaration.Motor is { Enabled: true } motor)
            throw new NotSupportedException($"NIF constraint {declaration.Block.Index} enabled motor type {motor.Type} has no native drive owner.");
    }

    private static void ValidateHingeAxes(FalloutNifVector3 axis, FalloutNifVector3 perpendicular1, FalloutNifVector3 perpendicular2)
    {
        static System.Numerics.Vector3 Convert(FalloutNifVector3 value) => new(value.X, value.Y, value.Z);
        var a = Convert(axis); var p = Convert(perpendicular1); var q = Convert(perpendicular2);
        const float tolerance = .001f;
        if (MathF.Abs(a.LengthSquared() - 1) > tolerance || MathF.Abs(p.LengthSquared() - 1) > tolerance ||
            MathF.Abs(q.LengthSquared() - 1) > tolerance || MathF.Abs(System.Numerics.Vector3.Dot(a, p)) > tolerance ||
            System.Numerics.Vector3.DistanceSquared(System.Numerics.Vector3.Cross(a, p), q) > tolerance * tolerance)
            throw new InvalidDataException("Source hinge axes are not an orthonormal frame.");
    }
}

internal static class FalloutNifNodeControllerChain
{
    internal static IReadOnlyList<FalloutNifObject> Read(FalloutNifFile source, FalloutNifNode owner)
    {
        if (!ReferenceEquals(source.ReadNode(owner.Block.Index), owner))
            throw new InvalidDataException("NIF controller owner belongs to another source object.");
        var chain = new List<FalloutNifObject>();
        var seen = new HashSet<int>();
        var index = owner.Controller;
        while (index != -1)
        {
            if (!seen.Add(index))
                throw new InvalidDataException($"NIF node {owner.Block.Index} controller chain repeats block {index}.");
            var declaration = source.ReadObject(index);
            var time = Time(declaration);
            if (time.Target != owner.Block.Index || time.UnknownInteger != 0)
                throw new InvalidDataException($"NIF node {owner.Block.Index} controller {index} has a foreign or unknown target declaration.");
            chain.Add(declaration);
            index = time.NextController;
        }
        return chain;
    }

    internal static FalloutNifTimeController Time(FalloutNifObject declaration) => declaration switch
    {
        FalloutNifBlendController value => value.Time,
        FalloutNifTransformController value => value.Time,
        FalloutNifVisibilityController value => value.Time,
        FalloutNifBoneLodController value => value.Time,
        FalloutNifControllerManager value => value.Time,
        FalloutNifMultiTargetTransformController value => value.Time,
        FalloutNifFloatExtraDataController value => value.Time,
        FalloutNifMaterialColorController value => value.Time,
        FalloutNifTextureTransformController value => value.Time,
        FalloutNifAlphaController value => value.Time,
        FalloutNifEmittanceController value => value.Time,
        FalloutNifRefractionController value => value.Time,
        FalloutNifParticleController value => value.Time,
        FalloutNifMorphController value => value.Time,
        _ => throw new NotSupportedException($"NIF block {declaration.Block.Index} {declaration.Block.TypeName} has no time-controller chain owner."),
    };

    internal static void RequireDormantBlend(FalloutNifNode owner, FalloutNifBlendController declaration)
    {
        var time = declaration.Time;
        if (time.Flags != 0x004c || time.Frequency != 1 || time.Phase != 0 ||
            time.StartTime != float.MaxValue || time.StopTime != float.MinValue ||
            time.Target != owner.Block.Index || time.UnknownInteger != 0 || declaration.Keys != 0)
            throw new NotSupportedException($"NIF blend controller {declaration.Block.Index} requires an active physics/animation blend owner.");
    }
}
