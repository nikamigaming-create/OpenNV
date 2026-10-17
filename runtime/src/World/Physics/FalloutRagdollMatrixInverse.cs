namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollMatrixInverse(IReadOnlyList<uint> Input, IReadOnlyList<uint> Cofactors,
    uint Determinant, bool Inverted, IReadOnlyList<uint> Result)
{
    internal const uint DeterminantTolerance = 0x358637bd;
    internal static IReadOnlyList<uint> Identity { get; } = Array.AsReadOnly(new uint[]
        { 0x3f800000, 0, 0, 0, 0x3f800000, 0, 0, 0, 0x3f800000 });

    internal static FalloutRagdollMatrixInverse Evaluate(IReadOnlyList<uint> input)
    {
        if (input is not { Count: 9 }) throw new InvalidDataException("Matrix inverse requires nine actual matrix elements.");
        var m = input.Select(BitConverter.UInt32BitsToSingle).ToArray();
        var c = new[]
        {
            Minor(4, 8, 7, 5), Minor(7, 2, 8, 1), Minor(1, 5, 4, 2),
            Minor(5, 6, 8, 3), Minor(0, 8, 6, 2), Minor(3, 2, 5, 0),
            Minor(7, 3, 4, 6), Minor(6, 1, 7, 0), Minor(0, 4, 3, 1)
        };
        var determinant = FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Add(
            FalloutPhysicalFloat32.Multiply(c[0], m[0]), FalloutPhysicalFloat32.Multiply(c[3], m[1])),
            FalloutPhysicalFloat32.Multiply(c[6], m[2]));
        var inverted = !(BitConverter.UInt32BitsToSingle(DeterminantTolerance) >= MathF.Abs(determinant));
        var reciprocal = inverted ? FalloutPhysicalFloat32.Divide(1, determinant) : 0;
        // The cofactors reach the destination before the singularity test.
        // The source caller ignores the Boolean; a singular input retains
        // those real stores rather than an invented identity or zero matrix.
        return new(input.ToArray(), c.Select(BitConverter.SingleToUInt32Bits).ToArray(),
            BitConverter.SingleToUInt32Bits(determinant), inverted, c.Select(value =>
                BitConverter.SingleToUInt32Bits(inverted ? FalloutPhysicalFloat32.Multiply(value, reciprocal) : value)).ToArray());

        float Minor(int a, int b, int c, int d) => FalloutPhysicalFloat32.Subtract(
            FalloutPhysicalFloat32.Multiply(m[a], m[b]), FalloutPhysicalFloat32.Multiply(m[c], m[d]));
    }

    internal void Validate()
    {
        var expected = Evaluate(Input);
        if (Cofactors is null || Result is null || !Cofactors.SequenceEqual(expected.Cofactors) || Determinant != expected.Determinant ||
            Inverted != expected.Inverted || !Result.SequenceEqual(expected.Result))
            throw new InvalidDataException("Ragdoll inverse model axes changed source arithmetic or its singular-store prefix.");
    }
}
