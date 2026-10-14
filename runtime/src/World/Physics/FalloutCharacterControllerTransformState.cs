using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.World.Physics;

internal readonly record struct FalloutPhysicalVector3(uint X, uint Y, uint Z)
{
    internal float XValue => BitConverter.UInt32BitsToSingle(X);
    internal float YValue => BitConverter.UInt32BitsToSingle(Y);
    internal float ZValue => BitConverter.UInt32BitsToSingle(Z);
    internal static FalloutPhysicalVector3 FromValues(float x, float y, float z) =>
        new(BitConverter.SingleToUInt32Bits(x), BitConverter.SingleToUInt32Bits(y), BitConverter.SingleToUInt32Bits(z));
}

internal readonly record struct FalloutPhysicalRotation(FalloutPhysicalVector3 Column0,
    FalloutPhysicalVector3 Column1, FalloutPhysicalVector3 Column2);
internal readonly record struct FalloutCharacterControllerProxyTransform(FalloutPhysicalRotation Rotation,
    FalloutPhysicalVector3 Translation);

// A real proxy provider owns current rotation/translation and verifies its own
// lifetime. This owner performs only the reached controller transform arithmetic.
internal sealed class FalloutCharacterControllerTransformState
{
    private readonly FalloutPhysicalConstructorInputs _inputs;
    private readonly FalloutPhysicalVector3 _offset;
    private readonly Func<FalloutCharacterControllerProxyTransform> _readCurrentProxy;

    internal FalloutCharacterControllerTransformState(FalloutPhysicalConstructorInputs inputs,
        FalloutPhysicalVector3 retainedOffset, Func<FalloutCharacterControllerProxyTransform> readCurrentProxy)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(readCurrentProxy);
        _inputs = inputs; _offset = retainedOffset; _readCurrentProxy = readCurrentProxy;
    }

    internal uint ReadPositionZBits()
    {
        var current = _readCurrentProxy();
        var rotated = RotateOffset(_offset, current.Rotation);
        var physicalZ = FalloutPhysicalFloat32.Subtract(current.Translation.ZValue, rotated.ZValue);
        return BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Multiply(physicalZ,
            BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits)));
    }

    internal FalloutPhysicalVector3 ReadReferencePositionBits(FalloutCharacterControllerProxyTransform current)
    {
        var rotated = RotateOffset(_offset, current.Rotation);
        var scale = BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits);
        return FalloutPhysicalVector3.FromValues(
            FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(current.Translation.XValue, rotated.XValue), scale),
            FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(current.Translation.YValue, rotated.YValue), scale),
            FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(current.Translation.ZValue, rotated.ZValue), scale));
    }

    internal uint ReadHeightDeltaBits(uint scalarBits)
    {
        var scalar = BitConverter.UInt32BitsToSingle(scalarBits);
        if (scalar == 0f) return 0;
        return BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Subtract(scalar,
            BitConverter.UInt32BitsToSingle(ReadPositionZBits())));
    }

    // This produces the input to the actual proxy/phantom position publisher.
    // Writing a translation or its world AABB belongs to that separate owner.
    internal FalloutPhysicalVector3 ProxyTranslationForReferencePosition(FalloutPhysicalVector3 referencePosition,
        FalloutPhysicalRotation currentRotation)
    {
        var scaled = _inputs.ScaleReferencePosition(referencePosition);
        var rotated = RotateOffset(_offset, currentRotation);
        return FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Add(scaled.XValue, rotated.XValue),
            FalloutPhysicalFloat32.Add(scaled.YValue, rotated.YValue),
            FalloutPhysicalFloat32.Add(scaled.ZValue, rotated.ZValue));
    }

    internal static FalloutPhysicalVector3 RotateOffset(FalloutPhysicalVector3 offset, FalloutPhysicalRotation rotation) =>
        FalloutPhysicalVector3.FromValues(Component(offset, rotation.Column0.XValue, rotation.Column1.XValue, rotation.Column2.XValue),
            Component(offset, rotation.Column0.YValue, rotation.Column1.YValue, rotation.Column2.YValue),
            Component(offset, rotation.Column0.ZValue, rotation.Column1.ZValue, rotation.Column2.ZValue));

    private static float Component(FalloutPhysicalVector3 offset, float column0, float column1, float column2) =>
        FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Multiply(offset.YValue, column1),
            FalloutPhysicalFloat32.Multiply(offset.XValue, column0)), FalloutPhysicalFloat32.Multiply(offset.ZValue, column2));
}

internal static class FalloutPhysicalFloat32
{
    // Keep source stores and prevent the JIT from fusing multiply/add operations.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float Add(float left, float right) => left + right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float Subtract(float left, float right) => left - right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float Multiply(float left, float right) => left * right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float Divide(float left, float right) => left / right;
}
