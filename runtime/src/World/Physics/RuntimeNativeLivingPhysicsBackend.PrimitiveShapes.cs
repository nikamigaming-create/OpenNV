using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class RuntimeNativeLivingPhysicsBackend
{
    private IReadOnlyList<RuntimeNativeLivingBodyShape> BuildPrimitiveShape(FalloutLivingPrimitiveShapeParameters parameters)
    {
        parameters.Validate();
        var radius = BitConverter.UInt32BitsToSingle(parameters.Radius) * _physicalToNative;
        Shape3D shape;
        var transform = Transform3D.Identity;
        switch (parameters.Kind)
        {
            case FalloutLivingPrimitiveShapeKind.Box:
                var size = ToNative(parameters.HalfExtents!.Value).Abs() * 2;
                shape = new BoxShape3D { Size = size, Margin = radius };
                break;
            case FalloutLivingPrimitiveShapeKind.Sphere:
                shape = new SphereShape3D { Radius = radius };
                break;
            case FalloutLivingPrimitiveShapeKind.Capsule:
                var first = ToNative(parameters.First!.Value); var second = ToNative(parameters.Second!.Value);
                var axis = second - first;
                if (axis.LengthSquared() <= 0) throw new InvalidDataException("Current capsule has coincident native endpoints.");
                transform = new(NativeNifCollisionBuilder.BasisLookingAlongY(axis.Normalized()), (first + second) * .5f);
                shape = new CapsuleShape3D { Radius = radius, Height = axis.Length() + 2 * radius };
                break;
            default: throw new NotSupportedException("Current primitive shape has no native factory.");
        }
        return [new(shape, transform)];
    }
}
