using System.Buffers.Binary;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class DdsMipContracts
{
    internal static void Run()
    {
        foreach (var (fourCc, blockBytes) in new[] { ("DXT1", 8), ("DXT3", 16), ("DXT5", 16) })
        {
            var bytes = new byte[128 + 5 * blockBytes];
            "DDS "u8.CopyTo(bytes);
            void UInt(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            UInt(4, 124); UInt(8, 0xa1007); UInt(12, 8); UInt(16, 8); UInt(28, 2); UInt(76, 32); UInt(80, 4);
            System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(bytes, 84);
            var before = bytes.ToArray();
            var chain = FalloutDdsMipChain.ReadPartial(bytes) ?? throw new InvalidOperationException("Partial DDS was rejected.");
            if (!chain.Levels.SequenceEqual([new(8, 8, 128, 4 * blockBytes), new(4, 4, 128 + 4 * blockBytes, blockBytes)]) ||
                !bytes.SequenceEqual(before)) throw new InvalidOperationException("DDS mip extents or original bytes changed.");
            Reject(() => FalloutDdsMipChain.ReadPartial(bytes[..^1]));
            Reject(() => FalloutDdsMipChain.ReadPartial([.. bytes, 0]));
            UInt(112, 0x200);
            Reject(() => FalloutDdsMipChain.ReadPartial(bytes));
            UInt(112, 0);
            UInt(8, 0x1007);
            Reject(() => FalloutDdsMipChain.ReadPartial(bytes));
            UInt(8, 0xa1007);
            UInt(28, 1);
            if (FalloutDdsMipChain.ReadPartial(bytes) is not null) throw new InvalidOperationException("A single DDS level was classified as a partial pyramid.");
        }
        Console.WriteLine("OPENNV_DDS_PARTIAL_MIP_CONTRACT_PASS formats=BC1,BC2,BC3 extents=authored malformed=refused input=unchanged");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Malformed partial DDS was accepted.");
    }
}
