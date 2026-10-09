namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class FalloutNifFile
{
    internal FalloutNifHingeConstraint ReadHingeConstraint(int blockIndex)
    {
        var declaration = ReadObject(blockIndex);
        if (declaration is FalloutNifHingeConstraint hinge) return hinge;
        if (declaration is not FalloutNifRagdollConstraint limited || limited.Block.TypeName != "bhkLimitedHingeConstraint")
            throw new NotSupportedException("This model joint requires its own constraint owner.");
        RequireNativeMotor(limited);
        ValidateHingeAxes(limited.TwistA, limited.PlaneA, limited.MotorA);
        ValidateHingeAxes(limited.TwistB, limited.PlaneB, limited.MotorB);
        return new(limited.Header, limited.TwistA, limited.PlaneA, limited.MotorA, limited.PivotA,
            limited.TwistB, limited.PlaneB, limited.MotorB, limited.PivotB,
            limited.TwistMinimum, limited.TwistMaximum, limited.Friction)
        { VectorPadding = limited.VectorPadding };
    }
}
