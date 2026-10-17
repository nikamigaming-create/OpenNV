using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutLivingJointFrame(FalloutPhysicalVector3 Axis, FalloutPhysicalVector3 Plane,
    FalloutPhysicalVector3 ThirdAxis, FalloutPhysicalVector3 Pivot);
internal sealed record FalloutLivingJointLimit(uint Lower, uint Upper, bool Enabled);
internal sealed record FalloutLivingJointParameters(int Block, uint Type, FalloutLivingJointFrame First,
    FalloutLivingJointFrame Second, IReadOnlyList<FalloutLivingJointLimit> Angular, uint Friction, uint Strength)
{
    internal static FalloutLivingJointParameters Construct(FalloutNifPhysicalConstraint source)
    {
        var declaration = source.Declaration;
        var result = declaration switch
        {
            FalloutNifRagdollConstraint joint => new FalloutLivingJointParameters(source.Header.Block.Index, source.Header.WrappedType,
                Frame(joint.TwistA, joint.PlaneA, joint.MotorA, joint.PivotA), Frame(joint.TwistB, joint.PlaneB, joint.MotorB, joint.PivotB),
                [Limit(joint.TwistMinimum, joint.TwistMaximum), Limit(joint.PlaneMinimum, joint.PlaneMaximum), Limit(-joint.Cone, joint.Cone)],
                Bits(joint.Friction), Bits(joint.Strength)),
            FalloutNifHingeConstraint joint => new FalloutLivingJointParameters(source.Header.Block.Index, source.Header.WrappedType,
                Frame(joint.AxisA, joint.PerpendicularA1, joint.PerpendicularA2, joint.PivotA), Frame(joint.AxisB, joint.PerpendicularB1, joint.PerpendicularB2, joint.PivotB),
                [new(Bits(joint.Minimum ?? 0), Bits(joint.Maximum ?? 0), joint.Minimum.HasValue && joint.Maximum.HasValue), Limit(0, 0), Limit(0, 0)],
                Bits(joint.Friction), 0x3f800000),
            _ => throw new NotSupportedException("Living joint parameter construction has no actual angular descriptor owner.")
        };
        result.Validate(); return result;

        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        static FalloutPhysicalVector3 Vector(FalloutNifVector3 value) => FalloutPhysicalVector3.FromValues(value.X, value.Y, value.Z);
        static FalloutLivingJointFrame Frame(FalloutNifVector3 axis, FalloutNifVector3 plane, FalloutNifVector3 third, FalloutNifVector3 pivot) =>
            new(Vector(axis), Vector(plane), Vector(third), Vector(pivot));
        static FalloutLivingJointLimit Limit(float lower, float upper) => new(Bits(lower), Bits(upper), true);
    }
    internal void Validate()
    {
        if (Block < 0 || Type is not (1 or 2 or 7) || First is null || Second is null || Angular is not { Count: 3 } ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(Friction)) || !float.IsFinite(BitConverter.UInt32BitsToSingle(Strength)) ||
            Angular.Any(limit => limit is null || !float.IsFinite(BitConverter.UInt32BitsToSingle(limit.Lower)) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(limit.Upper)) ||
                BitConverter.UInt32BitsToSingle(limit.Lower) > BitConverter.UInt32BitsToSingle(limit.Upper)))
            throw new InvalidDataException("Living joint lost its actual constructor frames or angular fields.");
        foreach (var frame in new[] { First, Second })
            foreach (var vector in new[] { frame.Axis, frame.Plane, frame.ThirdAxis, frame.Pivot })
                if (!float.IsFinite(vector.XValue) || !float.IsFinite(vector.YValue) || !float.IsFinite(vector.ZValue))
                    throw new InvalidDataException("Living joint contains nonfinite constructor geometry.");
    }
}
