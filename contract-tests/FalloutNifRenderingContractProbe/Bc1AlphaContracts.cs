using System.Buffers.Binary;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class Bc1AlphaContracts
{
    internal static void Run()
    {
        var mixed = Block(0, ushort.MaxValue, 0xe4e4e4e4);
        Check(FalloutBc1Alpha.ContainsTransparency(mixed, 4, 4, 1), "Three-colour selector alpha is absent.");
        Check(!FalloutBc1Alpha.ContainsTransparency(Block(ushort.MaxValue, 0, uint.MaxValue), 4, 4, 1),
            "Four-colour interpolation became transparent.");
        Check(FalloutBc1Alpha.ContainsTransparency(Block(1234, 1234, 3), 4, 4, 1),
            "Equal endpoints lost the transparent selector.");
        Check(!FalloutBc1Alpha.ContainsTransparency(Block(0, ushort.MaxValue, 0xaaaaaaaa), 4, 4, 1),
            "Three-colour interpolation without selector three became transparent.");
        Check(!FalloutBc1Alpha.ContainsTransparency(Block(0, 1, 0xfffffffc), 1, 1, 1),
            "Padding texels changed one-texel alpha admission.");
        Check(FalloutBc1Alpha.ContainsTransparency(Block(0, 1, 3U << 10), 2, 2, 1),
            "Partial-block alpha was omitted.");
        byte[] wide = [.. Block(1, 0, uint.MaxValue), .. mixed];
        Check(FalloutBc1Alpha.ContainsTransparency(wide, 8, 4, 1), "The second block was omitted.");
        byte[] levels = [.. new byte[48], .. Block(0, 1, 3)];
        Check(FalloutBc1Alpha.ContainsTransparency(levels, 8, 8, 4), "Lower-mip alpha was omitted.");
        Check(!FalloutBc1Alpha.ContainsTransparency(new byte[56], 8, 8, 4), "Opaque mip chain was expanded.");
        Reject(() => FalloutBc1Alpha.ContainsTransparency(mixed[..7], 4, 4, 1));
        Reject(() => FalloutBc1Alpha.ContainsTransparency([.. mixed, 0], 4, 4, 1));
        Reject(() => FalloutBc1Alpha.ContainsTransparency(new byte[16], 1, 1, 2));
        Reject(() => FalloutBc1Alpha.ContainsTransparency(mixed, 0, 4, 1));
        Reject(() => FalloutBc1Alpha.ContainsTransparency(mixed, 4, 4, 0));
        // Finding alpha early must not suppress validation of later mip bytes.
        Reject(() => FalloutBc1Alpha.ContainsTransparency(mixed, 4, 4, 2));
        Console.WriteLine("OPENNV_BC1_ALPHA_CONTRACT_PASS selectors=true endpointModes=true partialBlocks=true allMips=true malformedRejected=true");
    }

    private static byte[] Block(ushort first, ushort second, uint selectors)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, first);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), second);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), selectors);
        return bytes;
    }

    private static void Check(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed BC1 image was admitted.");
    }
}
