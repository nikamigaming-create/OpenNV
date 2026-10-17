using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal enum FalloutLivingPrimitiveShapeKind { Box, Sphere, Capsule }
internal sealed record FalloutLivingPrimitiveShapeParameters(int Block, FalloutLivingPrimitiveShapeKind Kind,
    uint Scale, uint Radius, uint SourceRadius, FalloutPhysicalVector3? First,
    FalloutPhysicalVector3? Second, FalloutPhysicalVector3? HalfExtents)
{
    internal static FalloutLivingPrimitiveShapeParameters? Construct(FalloutNifFile source, int block, uint scaleBits)
    {
        var scale = BitConverter.UInt32BitsToSingle(scaleBits);
        if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Primitive shape requires its actual positive physical scale.");
        var declaration = source.ReadObject(block);
        return declaration switch
        {
            FalloutNifBoxShape box => new(block, FalloutLivingPrimitiveShapeKind.Box, scaleBits, Number(box.Radius), Number(box.Radius), null, null, Vector(box.Dimensions)),
            FalloutNifSphereShape sphere => new(block, FalloutLivingPrimitiveShapeKind.Sphere, scaleBits, Number(sphere.Radius), Number(sphere.Radius), null, null, null),
            FalloutNifCapsuleShape capsule when MathF.Abs(capsule.FirstRadius - capsule.SecondRadius) <= float.Epsilon => new(block, FalloutLivingPrimitiveShapeKind.Capsule,
                scaleBits, Number(capsule.FirstRadius), Number(capsule.Radius), Vector(capsule.First), Vector(capsule.Second), null),
            // The existing general factory owns compound and convex resources.
            // Their source construction-info layouts still require a separate
            // owner; no primitive is substituted for one of those shapes.
            _ => null
        };

        uint Number(float value) => BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Multiply(value, scale));
        FalloutPhysicalVector3 Vector(FalloutNifVector3 value) => new(Number(value.X), Number(value.Y), Number(value.Z));
    }
    internal void Validate()
    {
        if (Block < 0 || !float.IsFinite(BitConverter.UInt32BitsToSingle(Scale)) || BitConverter.UInt32BitsToSingle(Scale) <= 0 ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(Radius)) || BitConverter.UInt32BitsToSingle(Radius) < 0 ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(SourceRadius)) || (Kind switch
            {
                FalloutLivingPrimitiveShapeKind.Box => HalfExtents is not { } size || First is not null || Second is not null ||
                    size.XValue <= 0 || size.YValue <= 0 || size.ZValue <= 0,
                FalloutLivingPrimitiveShapeKind.Sphere => First is not null || Second is not null || HalfExtents is not null || BitConverter.UInt32BitsToSingle(Radius) <= 0,
                FalloutLivingPrimitiveShapeKind.Capsule => First is null || Second is null || HalfExtents is not null || First == Second || BitConverter.UInt32BitsToSingle(Radius) <= 0,
                _ => true
            }))
            throw new InvalidDataException("Living primitive shape lost its actual constructor fields.");
        foreach (var vector in new[] { First, Second, HalfExtents }.OfType<FalloutPhysicalVector3>())
            if (!float.IsFinite(vector.XValue) || !float.IsFinite(vector.YValue) || !float.IsFinite(vector.ZValue))
                throw new InvalidDataException("Living primitive shape has nonfinite constructor geometry.");
    }
}

internal sealed record FalloutLivingShapeInfo(Guid Identity, FalloutReferenceModelDataInvocation Writer,
    string Body, ulong NativeBody, FalloutLivingPrimitiveShapeParameters Parameters)
{
    internal void Validate(FalloutLivingBodySnapshot body, FalloutLivingRagdollBindingSnapshot binding)
    {
        Parameters.Validate();
        if (Identity == Guid.Empty || Writer != binding.Load || Body != body.Key || Parameters.Scale != body.ShapeScaleBits || NativeBody == 0 ||
            !binding.Bodies.Any(actual => actual.CurrentBody == Body && actual.NativeBody == NativeBody))
            throw new InvalidDataException("Current shape-info cache borrowed another actual body, constructor or source invocation.");
    }
}

internal sealed partial class FalloutLivingPhysicalBody
{
    private FalloutLivingShapeInfo? _shapeInfo;
    internal FalloutLivingShapeInfo ReadRagdollShapeInfo(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_model.CaptureRagdollController() is not { Returned: true } controller || controller.Load != load)
            throw new InvalidOperationException("Current shape-info read borrowed another controller/model load.");
        if (ConstructionShape is not { } parameters || !ReferenceEquals(parameters, Backend.PrimitiveShape) || !Backend.HasPhysicalObject)
            throw new NotSupportedException("source-current-physical-shape-construction-info-layout-unowned");
        parameters.Validate();
        if (_shapeInfo is null) _shapeInfo = new(Guid.NewGuid(), load, Key, Backend.PhysicalObjectId, parameters);
        return _shapeInfo;
    }
}
