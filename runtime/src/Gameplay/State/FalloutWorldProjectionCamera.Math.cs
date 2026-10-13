using OpenNV.Runtime.World.Physics;
using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutWorldProjectionCameraMath
{
    internal static IReadOnlyList<uint> Perspective(FalloutWorldProjectionPose pose, FalloutWorldProjectionFrustum frustum)
    {
        pose.Validate();
        if (frustum.Orthographic) throw new NotSupportedException("source-current-camera-orthographic-matrix-unowned");
        var rightInverse = F.Divide(1, F.Subtract(Value(frustum.Right), Value(frustum.Left)));
        var upInverse = F.Divide(1, F.Subtract(Value(frustum.Top), Value(frustum.Bottom)));
        var depth = F.Multiply(F.Divide(1, F.Subtract(Value(frustum.Far), Value(frustum.Near))), Value(frustum.Far));
        var horizontal = F.Multiply(Negate(F.Add(Value(frustum.Left), Value(frustum.Right))), rightInverse);
        var vertical = F.Multiply(Negate(F.Add(Value(frustum.Top), Value(frustum.Bottom))), upInverse);
        var horizontalScale = F.Add(rightInverse, rightInverse); var verticalScale = F.Add(upInverse, upInverse);
        var direction = Column(0); var up = Column(1); var right = Column(2);
        var directionTranslation = NegativeDot(direction); var upTranslation = NegativeDot(up); var rightTranslation = NegativeDot(right);
        var matrix = new uint[16];
        for (var column = 0; column < 3; column++)
        {
            matrix[column] = Bits(F.Add(F.Multiply(right[column], horizontalScale), F.Multiply(direction[column], horizontal)));
            matrix[4 + column] = Bits(F.Add(F.Multiply(up[column], verticalScale), F.Multiply(direction[column], vertical)));
            matrix[8 + column] = Bits(F.Multiply(direction[column], depth));
            matrix[12 + column] = Bits(direction[column]);
        }
        matrix[3] = Bits(F.Add(F.Multiply(directionTranslation, horizontal), F.Multiply(rightTranslation, horizontalScale)));
        matrix[7] = Bits(F.Add(F.Multiply(directionTranslation, vertical), F.Multiply(upTranslation, verticalScale)));
        matrix[11] = Bits(F.Add(F.Multiply(Negate(Value(frustum.Near)), depth), F.Multiply(depth, directionTranslation)));
        matrix[15] = Bits(directionTranslation);
        if (matrix.Any(bits => !float.IsFinite(Value(bits))))
            throw new NotSupportedException("source-camera-nonfinite-projection-machine-floating-state-unowned");
        return Array.AsReadOnly(matrix);
        float[] Column(int column) => new[] { Value(pose.Rotation[column]), Value(pose.Rotation[3 + column]), Value(pose.Rotation[6 + column]) };
        float NegativeDot(float[] axis) => Negate(F.Add(F.Add(F.Multiply(axis[1], pose.Translation.YValue),
            F.Multiply(axis[0], pose.Translation.XValue)), F.Multiply(axis[2], pose.Translation.ZValue)));
    }
    internal static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    internal static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    internal static float Negate(float value) => Value(Bits(value) ^ 0x80000000);
}
