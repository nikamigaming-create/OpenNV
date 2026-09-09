namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifRagdollConstraint(FalloutNifConstraintHeader Header,
    FalloutNifVector3 TwistA, FalloutNifVector3 PlaneA, FalloutNifVector3 MotorA, FalloutNifVector3 PivotA,
    FalloutNifVector3 TwistB, FalloutNifVector3 PlaneB, FalloutNifVector3 MotorB, FalloutNifVector3 PivotB,
    float Cone, float PlaneMinimum, float PlaneMaximum, float TwistMinimum, float TwistMaximum,
    float Friction, float Strength);

internal sealed partial class FalloutNifFile
{
    // Fallout's Havok 660 descriptor. Vector padding is not a floating value:
    // source pivots commonly store 0xffffffff in their unused fourth word.
    internal FalloutNifRagdollConstraint ReadRagdollConstraint(int blockIndex)
    {
        var header = ReadConstraintHeader(blockIndex);
        if (header.WrappedType is not (2 or 7)) throw new NotSupportedException("Wrapped actor constraint is not a limited hinge or ragdoll joint.");
        var cursor = BlockCursor(header.Block);
        cursor.Skip(header.Block.Size - header.UndecodedPayloadBytes, "constraint identity");
        static FalloutNifVector3 Vector(ref NifCursor cursor, string name)
        {
            var result = new FalloutNifVector3(cursor.ReadFiniteSingle(name + " X"), cursor.ReadFiniteSingle(name + " Y"), cursor.ReadFiniteSingle(name + " Z"));
            _ = cursor.ReadUInt32(name + " padding");
            return result;
        }
        var twistA = Vector(ref cursor, "twist A"); var planeA = Vector(ref cursor, "plane A");
        var motorA = Vector(ref cursor, "motor A"); var pivotA = Vector(ref cursor, "pivot A");
        var twistB = Vector(ref cursor, "twist B"); var planeB = Vector(ref cursor, "plane B");
        var motorB = Vector(ref cursor, "motor B"); var pivotB = Vector(ref cursor, "pivot B");
        var cone = header.WrappedType == 7 ? cursor.ReadFiniteSingle("cone angle") : 0;
        var planeMin = header.WrappedType == 7 ? cursor.ReadFiniteSingle("plane minimum") : 0;
        var planeMax = header.WrappedType == 7 ? cursor.ReadFiniteSingle("plane maximum") : 0;
        var twistMin = cursor.ReadFiniteSingle("twist minimum"); var twistMax = cursor.ReadFiniteSingle("twist maximum");
        var friction = cursor.ReadFiniteSingle("joint friction");
        if (cursor.ReadByte("motor type") != 0) throw new NotSupportedException("Motor-driven ragdoll joint is unbound.");
        var strength = header.Block.TypeName == "bhkMalleableConstraint" ? cursor.ReadFiniteSingle("malleable strength") : 1;
        cursor.RequireEnd();
        if (cone is < 0 or > MathF.PI || planeMin < -MathF.PI || planeMax > MathF.PI || planeMin > planeMax ||
            twistMin < -MathF.PI || twistMax > MathF.PI || twistMin > twistMax || friction < 0 || strength is < 0 or > 1)
            throw new InvalidDataException("Ragdoll joint limits are invalid.");
        return new(header, twistA, planeA, motorA, pivotA, twistB, planeB, motorB, pivotB,
            cone, planeMin, planeMax, twistMin, twistMax, friction, strength);
    }
}
