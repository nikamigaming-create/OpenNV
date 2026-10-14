using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Source-space arithmetic. Presentation unit/axis conversion is deliberately
// absent; callers retain each Float32 store before the next operation.
internal static class FalloutNifSceneMath
{
    internal static FalloutNifTransform Identity => new(new(0, 0, 0), [1, 0, 0, 0, 1, 0, 0, 0, 1], 1);
    internal static FalloutNifTransform Copy(FalloutNifTransform value) => new(value.Translation, (float[])value.RotationRowMajor.Clone(), value.Scale);
    internal static FalloutNifTransform Compose(FalloutNifTransform parent, FalloutNifTransform child)
    {
        Validate(parent); Validate(child);
        var rotation = new float[9];
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                rotation[row * 3 + column] = Component(child.RotationRowMajor[column], child.RotationRowMajor[3 + column],
                    child.RotationRowMajor[6 + column], parent.RotationRowMajor, row);
        var moved = Rotate(child.Translation, parent.RotationRowMajor);
        return new(new(F.Add(F.Multiply(moved.X, parent.Scale), parent.Translation.X),
            F.Add(F.Multiply(moved.Y, parent.Scale), parent.Translation.Y),
            F.Add(F.Multiply(moved.Z, parent.Scale), parent.Translation.Z)), rotation, F.Multiply(parent.Scale, child.Scale));
    }
    internal static FalloutNifTransform Inverse(FalloutNifTransform value)
    {
        Validate(value);
        var rotation = value.RotationRowMajor;
        var transposed = new[] { rotation[0], rotation[3], rotation[6], rotation[1], rotation[4], rotation[7], rotation[2], rotation[5], rotation[8] };
        var scale = F.Divide(1, value.Scale); var position = Rotate(value.Translation, transposed);
        return new(new(F.Multiply(-position.X, scale), F.Multiply(-position.Y, scale), F.Multiply(-position.Z, scale)), transposed, scale);
    }
    internal static FalloutNifVector3 Rotate(FalloutNifVector3 vector, float[] rotation) =>
        new(Component(vector.X, vector.Y, vector.Z, rotation, 0), Component(vector.X, vector.Y, vector.Z, rotation, 1),
            Component(vector.X, vector.Y, vector.Z, rotation, 2));
    private static float Component(float x, float y, float z, float[] rotation, int row) =>
        F.Add(F.Add(F.Multiply(y, rotation[row * 3 + 1]), F.Multiply(x, rotation[row * 3])), F.Multiply(z, rotation[row * 3 + 2]));
    internal static FalloutNifSceneSphere Transform(FalloutNifSceneSphere sphere, FalloutNifTransform transform)
    {
        Validate(transform); var point = Rotate(sphere.Center, transform.RotationRowMajor);
        return new(new(F.Add(F.Multiply(point.X, transform.Scale), transform.Translation.X),
            F.Add(F.Multiply(point.Y, transform.Scale), transform.Translation.Y),
            F.Add(F.Multiply(point.Z, transform.Scale), transform.Translation.Z)), F.Multiply(sphere.Radius, transform.Scale));
    }
    internal static FalloutNifSceneSphere Merge(FalloutNifSceneSphere current, FalloutNifSceneSphere other)
    {
        var x = F.Subtract(current.Center.X, other.Center.X); var y = F.Subtract(current.Center.Y, other.Center.Y);
        var z = F.Subtract(current.Center.Z, other.Center.Z);
        var distanceSquared = F.Add(F.Add(F.Multiply(y, y), F.Multiply(x, x)), F.Multiply(z, z));
        var radiusDifference = F.Subtract(other.Radius, current.Radius); var differenceSquared = F.Multiply(radiusDifference, radiusDifference);
        if (radiusDifference >= 0)
        {
            if (!(differenceSquared < distanceSquared)) return other;
        }
        else if (distanceSquared <= differenceSquared || float.IsNaN(distanceSquared) || float.IsNaN(differenceSquared)) return current;
        var distance = (float)Math.Sqrt((double)distanceSquared);
        var center = current.Center;
        if (distance > BitConverter.UInt32BitsToSingle(MergeCenterEpsilonBits))
        {
            var ratio = F.Divide(F.Subtract(distance, radiusDifference), F.Add(distance, distance));
            center = new(F.Add(F.Multiply(ratio, x), other.Center.X), F.Add(F.Multiply(ratio, y), other.Center.Y),
                F.Add(F.Multiply(ratio, z), other.Center.Z));
        }
        return new(center, F.Multiply(F.Add(F.Add(other.Radius, distance), current.Radius), .5f));
    }
    internal const uint MergeCenterEpsilonBits = 0x358637bd;
    internal static float[] Rotation(FalloutNifQuaternion value)
    {
        var length = F.Add(F.Add(F.Multiply(value.X, value.X), F.Multiply(value.Y, value.Y)),
            F.Add(F.Multiply(value.Z, value.Z), F.Multiply(value.W, value.W)));
        if (!float.IsFinite(length) || length <= 0) throw new InvalidDataException("Logical scene pose has an invalid source quaternion.");
        var inverse = F.Divide(1, (float)Math.Sqrt(length));
        var x = F.Multiply(value.X, inverse); var y = F.Multiply(value.Y, inverse);
        var z = F.Multiply(value.Z, inverse); var w = F.Multiply(value.W, inverse);
        var xx = F.Multiply(x, x); var yy = F.Multiply(y, y); var zz = F.Multiply(z, z);
        var xy = F.Multiply(x, y); var xz = F.Multiply(x, z); var yz = F.Multiply(y, z);
        var wx = F.Multiply(w, x); var wy = F.Multiply(w, y); var wz = F.Multiply(w, z);
        return [F.Subtract(1, F.Multiply(2, F.Add(yy, zz))), F.Multiply(2, F.Subtract(xy, wz)), F.Multiply(2, F.Add(xz, wy)),
            F.Multiply(2, F.Add(xy, wz)), F.Subtract(1, F.Multiply(2, F.Add(xx, zz))), F.Multiply(2, F.Subtract(yz, wx)),
            F.Multiply(2, F.Subtract(xz, wy)), F.Multiply(2, F.Add(yz, wx)), F.Subtract(1, F.Multiply(2, F.Add(xx, yy)))];
    }
    internal static void Validate(FalloutNifTransform transform)
    {
        if (transform is null || transform.RotationRowMajor is not { Length: 9 } || transform.RotationRowMajor.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(transform.Translation.X) || !float.IsFinite(transform.Translation.Y) || !float.IsFinite(transform.Translation.Z) ||
            !float.IsFinite(transform.Scale) || transform.Scale <= 0)
            throw new InvalidDataException("Logical NIF scene requires its finite source-space uniform transform.");
    }
}
