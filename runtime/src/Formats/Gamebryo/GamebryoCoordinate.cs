using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class GamebryoCoordinate
{
    internal const int SpatialDimensions = 3;

    private static readonly float[,] GameToGodot =
    {
        { 1.0f, 0.0f, 0.0f },
        { 0.0f, 0.0f, 1.0f },
        { 0.0f, -1.0f, 0.0f },
    };

    internal static Vector3 ConvertVector(Vector3 source) =>
        new(source.X, source.Z, -source.Y);

    internal static Basis ConvertCameraBasis(
        IReadOnlyList<float> gameRowMajor,
        string label)
    {
        if (gameRowMajor.Count != SpatialDimensions * SpatialDimensions ||
            gameRowMajor.Any(value => !float.IsFinite(value)))
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
        if (gameRowMajor.Count != SpatialDimensions * SpatialDimensions ||
            gameRowMajor.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Gamebryo {label} must contain nine finite row-major values.");
        if (!float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException($"Gamebryo {label} has invalid scale.");

        var source = new float[SpatialDimensions, SpatialDimensions];
        for (var row = 0; row < SpatialDimensions; row++)
            for (var column = 0; column < SpatialDimensions; column++)
                source[row, column] = gameRowMajor[row * SpatialDimensions + column];
        // Runtime NiTransform world/local composition is parent * child: the
        // observed matrix is already a column-vector transform. The NIF file
        // exporter has a separate row-vector serialization boundary and
        // performs its transpose there.
        var converted = Multiply(
            GameToGodot,
            Multiply(source, Transpose(GameToGodot)));
        return new Basis(
            new Vector3(converted[0, 0], converted[1, 0], converted[2, 0]),
            new Vector3(converted[0, 1], converted[1, 1], converted[2, 1]),
            new Vector3(converted[0, 2], converted[1, 2], converted[2, 2]))
            .Scaled(Vector3.One * scale);
    }

    private static float[,] Multiply(float[,] left, float[,] right)
    {
        var result = new float[SpatialDimensions, SpatialDimensions];
        for (var row = 0; row < SpatialDimensions; row++)
            for (var column = 0; column < SpatialDimensions; column++)
                for (var axis = 0; axis < SpatialDimensions; axis++)
                    result[row, column] += left[row, axis] * right[axis, column];
        return result;
    }

    private static float[,] Transpose(float[,] source)
    {
        var result = new float[SpatialDimensions, SpatialDimensions];
        for (var row = 0; row < SpatialDimensions; row++)
            for (var column = 0; column < SpatialDimensions; column++)
                result[row, column] = source[column, row];
        return result;
    }
}
