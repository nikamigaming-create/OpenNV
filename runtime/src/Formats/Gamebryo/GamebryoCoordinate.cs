using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class GamebryoCoordinate
{
    internal const int SpatialDimensions = 3;

    private static readonly float[] GameToGodot =
        [1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, -1.0f, 0.0f];

    internal static Vector3 ConvertVector(Vector3 source) =>
        new(source.X, source.Z, -source.Y);

    internal static Vector3 ReferenceEuler(Basis godot)
    {
        var axes = new Basis(Vector3.Right, Vector3.Forward, Vector3.Up);
        var source = axes.Transposed() * godot.Orthonormalized() * axes;
        var result = -source.GetEuler(EulerOrder.Xyz);
        if (!result.IsFinite()) throw new InvalidDataException("Player reference rotation is not finite.");
        return result;
    }

    internal static Basis ConvertReferenceEuler(Vector3 sourceRadians, float scale)
    {
        if (!sourceRadians.IsFinite() || !float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException("Gamebryo reference transform must be finite and positive.");
        var x = new Basis(Vector3.Right, -sourceRadians.X);
        var y = new Basis(Vector3.Up, -sourceRadians.Y);
        var z = new Basis(Vector3.Back, -sourceRadians.Z);
        // Placed-reference angles are clockwise XYZ, independent of the NIF
        // serialization convention. The column-vector transform is Rx * Ry * Rz;
        // changing only Z's sign works for upright props but tilts road pieces
        // and other references with mixed rotations away from their source join.
        var source = x * y * z;
        return ConvertBasis(
            [source.X.X, source.Y.X, source.Z.X,
             source.X.Y, source.Y.Y, source.Z.Y,
             source.X.Z, source.Y.Z, source.Z.Z],
            scale,
            "reference Euler transform");
    }

    internal static Basis ConvertCameraBasis(
        IReadOnlyList<float> gameRowMajor,
        string label)
    {
        if (!FiniteMatrix(gameRowMajor))
            throw new InvalidOperationException(
                $"Gamebryo {label} must contain nine finite row-major values.");
        var forward = ConvertVector(new Vector3(
            gameRowMajor[0], gameRowMajor[SpatialDimensions],
            gameRowMajor[SpatialDimensions * 2]));
        var up = ConvertVector(new Vector3(
            gameRowMajor[1], gameRowMajor[SpatialDimensions + 1],
            gameRowMajor[SpatialDimensions * 2 + 1]));
        var right = ConvertVector(new Vector3(
            gameRowMajor[2], gameRowMajor[SpatialDimensions + 2],
            gameRowMajor[SpatialDimensions * 2 + 2]));
        // NiCamera local columns are Forward/Up/Right. Godot Camera3D local
        // columns are Right/Up/Back because it looks down -Z.
        return new Basis(right, up, -forward);
    }

    internal static Basis ConvertBasis(
        IReadOnlyList<float> gameRowMajor,
        float scale,
        string label)
    {
        if (!FiniteMatrix(gameRowMajor))
            throw new InvalidOperationException(
                $"Gamebryo {label} must contain nine finite row-major values.");
        if (!float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException($"Gamebryo {label} has invalid scale.");

        Span<float> source = stackalloc float[SpatialDimensions * SpatialDimensions];
        for (var row = 0; row < SpatialDimensions; row++)
            for (var column = 0; column < SpatialDimensions; column++)
                source[row * SpatialDimensions + column] = gameRowMajor[row * SpatialDimensions + column];
        // Runtime NiTransform world/local composition is parent * child: the
        // observed matrix is already a column-vector transform. The NIF file
        // exporter has a separate row-vector serialization boundary and
        // performs its transpose there.
        Span<float> transpose = stackalloc float[SpatialDimensions * SpatialDimensions];
        Span<float> intermediate = stackalloc float[SpatialDimensions * SpatialDimensions];
        Span<float> converted = stackalloc float[SpatialDimensions * SpatialDimensions];
        Transpose(GameToGodot, transpose);
        Multiply(source, transpose, intermediate);
        Multiply(GameToGodot, intermediate, converted);
        return new Basis(
            new Vector3(converted[0], converted[SpatialDimensions], converted[SpatialDimensions * 2]),
            new Vector3(converted[1], converted[SpatialDimensions + 1], converted[SpatialDimensions * 2 + 1]),
            new Vector3(converted[2], converted[SpatialDimensions + 2], converted[SpatialDimensions * 2 + 2]))
            .Scaled(Vector3.One * scale);
    }

    private static bool FiniteMatrix(IReadOnlyList<float> matrix)
    {
        if (matrix.Count != SpatialDimensions * SpatialDimensions) return false;
        for (var index = 0; index < matrix.Count; ++index)
            if (!float.IsFinite(matrix[index])) return false;
        return true;
    }

    private static void Multiply(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> result)
    {
        for (var row = 0; row < SpatialDimensions; row++)
            for (var column = 0; column < SpatialDimensions; column++)
            {
                result[row * SpatialDimensions + column] = 0;
                for (var axis = 0; axis < SpatialDimensions; axis++)
                    result[row * SpatialDimensions + column] +=
                        left[row * SpatialDimensions + axis] * right[axis * SpatialDimensions + column];
            }
    }

    private static void Transpose(ReadOnlySpan<float> source, Span<float> result)
    {
        for (var row = 0; row < SpatialDimensions; row++)
            for (var column = 0; column < SpatialDimensions; column++)
                result[row * SpatialDimensions + column] = source[column * SpatialDimensions + row];
    }
}
