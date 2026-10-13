using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal static class FalloutLivingModelConstruction
{
    // Fresh model construction consumes the authored local hierarchy and the
    // actual reference's current placement. Later animation/physical pose
    // publication has its own lifetime and cannot re-read these rest inputs.
    internal static IReadOnlyDictionary<int, FalloutLivingBodyConstruction> ReadSourceRestPoses(
        FalloutNifPhysicalModel source, FalloutPhysicalConstructorInputs inputs,
        FalloutCharacterControllerProxyTransform physicalRoot, float uniformScale, bool active,
        Func<FalloutNifPhysicalSceneObject, bool> isWeaponObject)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(isWeaponObject);
        if (!float.IsFinite(uniformScale) || uniformScale <= 0)
            throw new InvalidDataException("Living model constructor has invalid actual reference scale.");
        var poses = new Dictionary<int, (FalloutCharacterControllerProxyTransform Pose, float Scale)>();
        var bodies = new Dictionary<int, FalloutLivingBodyConstruction>();
        foreach (var scene in source.Scene)
        {
            var (name, transform) = scene.Declaration switch
            {
                FalloutNifNode node => (node.Name, node.Transform),
                FalloutNifGeometry geometry => (geometry.Name, geometry.Transform),
                FalloutNifParticleSystem particle => (particle.Geometry.Name, particle.Geometry.Transform),
                _ when scene.Collision is null => ("", (FalloutNifTransform?)null),
                _ => throw new NotSupportedException("Living collision scene has no source pose/name construction owner."),
            };
            var parent = scene.Parent is { } index ? poses[index] : (physicalRoot, uniformScale);
            if (transform is null)
            { poses.Add(scene.Declaration.Block.Index, parent); continue; }
            var scale = FalloutPhysicalFloat32.Multiply(parent.Item2, transform.Scale);
            if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Living inherited shape scale is invalid.");
            var local = inputs.ScaleReferencePosition(FalloutPhysicalVector3.FromValues(transform.Translation.X, transform.Translation.Y, transform.Translation.Z));
            local = Scale(local, parent.Item2);
            var composed = new FalloutCharacterControllerProxyTransform(Multiply(parent.Item1.Rotation, Rotation(transform.RotationRowMajor)),
                Add(parent.Item1.Translation, Rotate(local, parent.Item1.Rotation)));
            poses.Add(scene.Declaration.Block.Index, (composed, scale));
            if (scene.Collision is not { Body: not -1 } collision) continue;
            if (source.FindAttachedBody(collision.Body) is not { } body)
                throw new NotSupportedException("Living source-rest phantom factory is unowned.");
            if (body.Block.TypeName == "bhkRigidBodyT")
            {
                var quaternion = body.Rotation;
                var rotation = QuaternionRotation(quaternion);
                var translation = Scale(FalloutPhysicalVector3.FromValues(body.Translation.X, body.Translation.Y, body.Translation.Z), scale);
                composed = new(Multiply(composed.Rotation, rotation), Add(composed.Translation, Rotate(translation, composed.Rotation)));
            }
            bodies.Add(scene.Declaration.Block.Index, new(name, isWeaponObject(scene), composed, active)
            { ShapeScaleBits = BitConverter.SingleToUInt32Bits(scale) });
        }
        return bodies;
    }

    private static FalloutPhysicalRotation Rotation(IReadOnlyList<float> matrix)
    {
        if (matrix.Count != 9 || matrix.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Living scene rotation is not nine finite source components.");
        return new(FalloutPhysicalVector3.FromValues(matrix[0], matrix[3], matrix[6]),
            FalloutPhysicalVector3.FromValues(matrix[1], matrix[4], matrix[7]), FalloutPhysicalVector3.FromValues(matrix[2], matrix[5], matrix[8]));
    }
    private static FalloutPhysicalRotation QuaternionRotation(FalloutNifQuaternion value)
    {
        const float normalizedTolerance = .0001f;
        var length = Add(Add(Multiply(value.X, value.X), Multiply(value.Y, value.Y)), Add(Multiply(value.Z, value.Z), Multiply(value.W, value.W)));
        if (MathF.Abs(length - 1) > normalizedTolerance) throw new InvalidDataException("Living body attachment quaternion is not normalized.");
        var xx = Multiply(value.X, value.X); var yy = Multiply(value.Y, value.Y); var zz = Multiply(value.Z, value.Z);
        var xy = Multiply(value.X, value.Y); var xz = Multiply(value.X, value.Z); var yz = Multiply(value.Y, value.Z);
        var wx = Multiply(value.W, value.X); var wy = Multiply(value.W, value.Y); var wz = Multiply(value.W, value.Z);
        return Rotation([
            Subtract(1, Multiply(2, Add(yy, zz))), Multiply(2, Subtract(xy, wz)), Multiply(2, Add(xz, wy)),
            Multiply(2, Add(xy, wz)), Subtract(1, Multiply(2, Add(xx, zz))), Multiply(2, Subtract(yz, wx)),
            Multiply(2, Subtract(xz, wy)), Multiply(2, Add(yz, wx)), Subtract(1, Multiply(2, Add(xx, yy)))]);
    }
    private static FalloutPhysicalRotation Multiply(FalloutPhysicalRotation parent, FalloutPhysicalRotation child) =>
        new(Rotate(child.Column0, parent), Rotate(child.Column1, parent), Rotate(child.Column2, parent));
    private static FalloutPhysicalVector3 Rotate(FalloutPhysicalVector3 vector, FalloutPhysicalRotation rotation) =>
        FalloutCharacterControllerTransformState.RotateOffset(vector, rotation);
    private static FalloutPhysicalVector3 Scale(FalloutPhysicalVector3 vector, float scalar) =>
        FalloutPhysicalVector3.FromValues(Multiply(vector.XValue, scalar), Multiply(vector.YValue, scalar), Multiply(vector.ZValue, scalar));
    private static FalloutPhysicalVector3 Add(FalloutPhysicalVector3 first, FalloutPhysicalVector3 second) =>
        FalloutPhysicalVector3.FromValues(Add(first.XValue, second.XValue), Add(first.YValue, second.YValue), Add(first.ZValue, second.ZValue));
    private static float Add(float first, float second) => FalloutPhysicalFloat32.Add(first, second);
    private static float Multiply(float first, float second) => FalloutPhysicalFloat32.Multiply(first, second);
    private static float Subtract(float first, float second) => FalloutPhysicalFloat32.Subtract(first, second);
}
