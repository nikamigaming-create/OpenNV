using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Keep the distance beside its draw-only index. The default generic value sort
// uses IComparable<T> directly, without an interface comparer/delegate per sort.
internal readonly record struct NativeParticleDrawOrder(float Distance, int Index) : IComparable<NativeParticleDrawOrder>
{
    public int CompareTo(NativeParticleDrawOrder other)
    {
        var distance = other.Distance.CompareTo(Distance);
        return distance != 0 ? distance : Index.CompareTo(other.Index);
    }
}

// Presentation-only memo: source angle remains authoritative. Bitwise keys
// preserve signed zero and distinct NaN representations across reuse.
internal struct NativeParticleAngleCache
{
    private int _bits;
    private float _sin, _cos;
    private bool _initialized;

    internal (float Sin, float Cos) Get(float angle)
    {
        var bits = BitConverter.SingleToInt32Bits(angle);
        if (!_initialized || bits != _bits)
        {
            (_sin, _cos) = MathF.SinCos(angle);
            _bits = bits; _initialized = true;
        }
        return (_sin, _cos);
    }
}

internal static class NativeParticleDrawBuffer
{
    // MultiMesh 3D rows, RGBA, then the four source atlas coordinates.
    internal const int Stride = 20;

    internal static Transform3D Billboard(Basis camera, float angle, float radius, Vector3 position)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return Billboard(camera, sin, cos, radius, position);
    }

    internal static Transform3D Billboard(Basis camera, float sin, float cos, float radius, Vector3 position)
    {
        // camera * Z-rotation * uniform-scale, expanded by columns without
        // two general matrix products for every live particle.
        return new(new Basis((camera.X * cos + camera.Y * sin) * radius,
            (camera.Y * cos - camera.X * sin) * radius, camera.Z * radius), position);
    }

    internal static void Write(Span<float> output, Transform3D transform, Color color, Color atlas)
    {
        var basis = transform.Basis; var origin = transform.Origin;
        output[0] = basis.X.X; output[1] = basis.Y.X; output[2] = basis.Z.X; output[3] = origin.X;
        output[4] = basis.X.Y; output[5] = basis.Y.Y; output[6] = basis.Z.Y; output[7] = origin.Y;
        output[8] = basis.X.Z; output[9] = basis.Y.Z; output[10] = basis.Z.Z; output[11] = origin.Z;
        output[12] = color.R; output[13] = color.G; output[14] = color.B; output[15] = color.A;
        output[16] = atlas.R; output[17] = atlas.G; output[18] = atlas.B; output[19] = atlas.A;
    }
}
