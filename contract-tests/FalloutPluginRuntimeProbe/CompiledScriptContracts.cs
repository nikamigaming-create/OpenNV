using OpenNV.Runtime.Content;

internal static class CompiledScriptContracts
{
    internal static void Run()
    {
        // Synthetic commands deliberately contain opcode-looking payload bytes.
        // The declared extent, never a byte-pattern scan, determines boundaries.
        byte[] bytes = [0x1d, 0, 0, 0, 0x1c, 0, 7, 0, 0x34, 0x12, 4, 0, 0x1e, 0, 0, 0, 0x1e, 0, 0, 0];
        var before = bytes.ToArray();
        var rows = FalloutCompiledScript.Read(bytes);
        Require(rows.Count == 3 && rows[0] == new FalloutCompiledInstruction(0, 4, 0, 0x1d, null) &&
            rows[1] == new FalloutCompiledInstruction(4, 12, 4, 0x1234, 7) &&
            rows[2].Offset == 16 && rows[2].End == bytes.Length && bytes.SequenceEqual(before),
            "Compiled instruction extents, explicit reference or source bytes changed.");
        Require(FalloutCompiledScript.Read([]).Count == 0, "Empty compiled body acquired an instruction.");
        Require(FalloutCompiledScript.Read([0xff, 0xff, 0, 0])[0].Opcode == ushort.MaxValue,
            "Unknown opcode was silently removed from the structural inventory.");
        foreach (var malformed in new byte[][] { [1], [1, 0, 0], [1, 0, 1, 0], [1, 0, 0xff, 0xff],
                     [0x1c, 0], [0x1c, 0, 1, 0], [0x1c, 0, 1, 0, 2, 0], [0x1c, 0, 1, 0, 0x1c, 0, 0, 0],
                     [0x1e, 0, 0, 0, 1] })
        {
            try { FalloutCompiledScript.Read(malformed); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Malformed SCDA framing was admitted.");
        }
        Console.WriteLine("Compiled script framing contracts passed; operands and execution remain unverified.");
    }

    private static void Require(bool value, string error)
    {
        if (!value) throw new InvalidOperationException(error);
    }
}
