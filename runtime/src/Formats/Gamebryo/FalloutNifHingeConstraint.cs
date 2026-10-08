using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifHingeConstraint(FalloutNifConstraintHeader Header,
    FalloutNifVector3 AxisA, FalloutNifVector3 PerpendicularA1, FalloutNifVector3 PerpendicularA2, FalloutNifVector3 PivotA,
    FalloutNifVector3 AxisB, FalloutNifVector3 PerpendicularB1, FalloutNifVector3 PerpendicularB2, FalloutNifVector3 PivotB,
    float? Minimum = null, float? Maximum = null, float Friction = 0);

internal sealed partial class FalloutNifFile
{
    internal FalloutNifHingeConstraint ReadHingeConstraint(int blockIndex)
    {
        var header = ReadConstraintHeader(blockIndex);
        if (header.Block.TypeName == "bhkLimitedHingeConstraint")
        {
            var limited = ReadRagdollConstraint(blockIndex);
            ValidateHingeAxes(limited.TwistA, limited.PlaneA, limited.MotorA);
            ValidateHingeAxes(limited.TwistB, limited.PlaneB, limited.MotorB);
            return new(header, limited.TwistA, limited.PlaneA, limited.MotorA, limited.PivotA,
                limited.TwistB, limited.PlaneB, limited.MotorB, limited.PivotB,
                limited.TwistMinimum, limited.TwistMaximum, limited.Friction);
        }
        if (header.Block.TypeName != "bhkHingeConstraint" || header.WrappedType != 1)
            throw new NotSupportedException("This model joint requires its own constraint owner.");
        var cursor = BlockCursor(header.Block);
        cursor.Skip(header.Block.Size - header.UndecodedPayloadBytes, "constraint identity");
        static FalloutNifVector3 Vector(ref NifCursor cursor, string name)
        {
            var result = new FalloutNifVector3(cursor.ReadFiniteSingle(name + " X"),
                cursor.ReadFiniteSingle(name + " Y"), cursor.ReadFiniteSingle(name + " Z"));
            _ = cursor.ReadUInt32(name + " padding");
            return result;
        }
        var axisA = Vector(ref cursor, "hinge axis A");
        var perpendicularA1 = Vector(ref cursor, "hinge perpendicular A1");
        var perpendicularA2 = Vector(ref cursor, "hinge perpendicular A2");
        var pivotA = Vector(ref cursor, "hinge pivot A");
        var axisB = Vector(ref cursor, "hinge axis B");
        var perpendicularB1 = Vector(ref cursor, "hinge perpendicular B1");
        var perpendicularB2 = Vector(ref cursor, "hinge perpendicular B2");
        var pivotB = Vector(ref cursor, "hinge pivot B");
        cursor.RequireEnd();
        ValidateHingeAxes(axisA, perpendicularA1, perpendicularA2);
        ValidateHingeAxes(axisB, perpendicularB1, perpendicularB2);
        return new(header, axisA, perpendicularA1, perpendicularA2, pivotA,
            axisB, perpendicularB1, perpendicularB2, pivotB);
    }

    private static void ValidateHingeAxes(FalloutNifVector3 axis, FalloutNifVector3 perpendicular1, FalloutNifVector3 perpendicular2)
    {
        static Vector3 Convert(FalloutNifVector3 value) => new(value.X, value.Y, value.Z);
        var a = Convert(axis); var p = Convert(perpendicular1); var q = Convert(perpendicular2);
        const float tolerance = .001f;
        if (MathF.Abs(a.LengthSquared() - 1) > tolerance || MathF.Abs(p.LengthSquared() - 1) > tolerance ||
            MathF.Abs(q.LengthSquared() - 1) > tolerance || MathF.Abs(Vector3.Dot(a, p)) > tolerance ||
            Vector3.DistanceSquared(Vector3.Cross(a, p), q) > tolerance * tolerance)
            throw new InvalidDataException("Source hinge axes are not an orthonormal frame.");
    }
}
