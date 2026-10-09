using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private sealed record InterfaceInstruction(int At, int Next, byte Opcode, byte Escaped,
        byte ModRm, byte Sib, int Displacement, int? Immediate, byte Segment, InterfaceInstruction? Previous);

    // First-party bounded x86 declaration transport. Every inspected immediate
    // is on a decoded instruction boundary; original instruction bytes are
    // inputs, never executable authority or serialized gameplay assets.
    private static InterfaceInstruction InterfaceDecode(byte[] code, int start, InterfaceInstruction? previous)
    {
        var at = start; byte segment = 0; var operand16 = false;
        byte Read() => at < code.Length ? code[at++] : throw new InvalidDataException("Interface source instruction is truncated.");
        var opcode = Read();
        while (opcode is 0x26 or 0x2e or 0x36 or 0x3e or 0x64 or 0x65 or 0x66 or 0x67 or 0xf0 or 0xf2 or 0xf3)
        {
            if (at - start >= 15 || opcode == 0x67)
                throw new NotSupportedException("Interface source instruction has an unowned address/prefix domain.");
            if (opcode is 0x26 or 0x2e or 0x36 or 0x3e or 0x64 or 0x65) segment = opcode;
            if (opcode == 0x66) operand16 = true;
            opcode = Read();
        }
        byte escaped = 0, modrm = 0, sib = 0; int displacement = 0, immediateSize = 0; var hasModRm = false;
        var relative = false;
        if (opcode == 0x0f)
        {
            escaped = Read();
            if (escaped is >= 0x80 and <= 0x8f) { immediateSize = 4; relative = true; }
            else if (escaped is 0x10 or 0x11 or 0x28 or 0x29 or 0x2a or 0x2c or 0x2d or 0x2e or 0x2f or 0x54 or 0x55 or 0x56 or 0x57 or
                0x58 or 0x59 or 0x5a or 0x5b or 0x5c or 0x5d or 0x5e or 0x5f or 0xaf or 0xb6 or 0xb7 or 0xbe or 0xbf || escaped is >= 0x90 and <= 0x9f)
                hasModRm = true;
            else throw new NotSupportedException($"Interface source escaped instruction {escaped:x2} is unowned.");
        }
        else if (opcode is 0xe8 or 0xe9) { immediateSize = 4; relative = true; }
        else if (opcode == 0xeb || opcode is >= 0x70 and <= 0x7f) { immediateSize = 1; relative = true; }
        else if (opcode is 0x68 or 0xa1 or 0xa3 or 0x05 or 0x0d or 0x25 or 0x2d or 0x35 or 0x3d or 0xa9 || opcode is >= 0xb8 and <= 0xbf)
            immediateSize = operand16 ? 2 : 4;
        else if (opcode is 0x6a or 0xa0 or 0xa2 or 0x04 or 0x0c or 0x24 or 0x2c or 0x34 or 0x3c or 0xa8 || opcode is >= 0xb0 and <= 0xb7)
            immediateSize = opcode is 0xa0 or 0xa2 ? 4 : 1;
        else if (opcode == 0xc2) immediateSize = 2;
        else if (opcode is 0x80 or 0x82 or 0x83 or 0xc0 or 0xc1 or 0xc6 or 0x6b)
        { hasModRm = true; immediateSize = 1; }
        else if (opcode is 0x81 or 0xc7 or 0x69) { hasModRm = true; immediateSize = operand16 ? 2 : 4; }
        else if (opcode is 0x00 or 0x01 or 0x02 or 0x03 or 0x08 or 0x09 or 0x0a or 0x0b or 0x10 or 0x11 or 0x12 or 0x13 or
            0x18 or 0x19 or 0x1a or 0x1b or 0x20 or 0x21 or 0x22 or 0x23 or 0x28 or 0x29 or 0x2a or 0x2b or 0x30 or 0x31 or 0x32 or 0x33 or
            0x38 or 0x39 or 0x3a or 0x3b or 0x84 or 0x85 or 0x86 or 0x87 or 0x88 or 0x89 or 0x8a or 0x8b or 0x8c or 0x8d or 0x8e or 0x8f or
            0xd0 or 0xd1 or 0xd2 or 0xd3 or 0xf6 or 0xf7 or 0xfe or 0xff || opcode is >= 0xd8 and <= 0xdf) hasModRm = true;
        else if (opcode is 0x90 or 0x98 or 0x99 or 0x9c or 0x9d or 0xc3 or 0xc9 or 0xcc or 0xfc or 0xfd ||
            opcode is >= 0x40 and <= 0x5f || opcode is >= 0xa4 and <= 0xa7 || opcode is >= 0xaa and <= 0xaf) { }
        else throw new NotSupportedException($"Interface source instruction {opcode:x2} is unowned.");
        if (hasModRm)
        {
            modrm = Read(); var mode = modrm >> 6; var operand = modrm & 7;
            if (mode != 3 && operand == 4) { sib = Read(); operand = sib & 7; }
            var size = mode == 0 && operand == 5 || mode == 2 ? 4 : mode == 1 ? 1 : 0;
            if (size == 1) displacement = unchecked((sbyte)Read());
            else if (size == 4)
            {
                if (at > code.Length - 4) throw new InvalidDataException("Interface source memory displacement is incomplete.");
                displacement = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at)); at += 4;
            }
            if (opcode is 0xf6 or 0xf7 && (modrm >> 3 & 7) == 0) immediateSize = opcode == 0xf6 ? 1 : operand16 ? 2 : 4;
        }
        if (at > code.Length - immediateSize) throw new InvalidDataException("Interface source immediate is incomplete.");
        int? immediate = immediateSize switch
        {
            1 => relative || opcode is 0x6a or 0x83 ? unchecked((sbyte)code[at]) : code[at],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(at)),
            4 => BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at)),
            _ => null
        };
        at += immediateSize;
        if (at - start > 15) throw new InvalidDataException("Interface source instruction exceeds the x86 extent.");
        return new(start, at, opcode, escaped, modrm, sib, displacement, immediate, segment, previous);
    }

    private static IReadOnlyList<InterfaceInstruction> InterfaceSoundInstructions(byte[] code, uint codeBase, uint entry)
    {
        var rows = new Dictionary<int, InterfaceInstruction>(); var queue = new Queue<int>();
        int Offset(uint value) => value >= codeBase && value - codeBase < code.Length ? checked((int)(value - codeBase)) :
            throw new InvalidDataException("Interface source branch is outside its original code.");
        queue.Enqueue(Offset(entry));
        while (queue.TryDequeue(out var at))
        {
            InterfaceInstruction? previous = null;
            while (!rows.ContainsKey(at))
            {
                if (rows.Values.Any(existing => existing.At < at && at < existing.Next))
                    throw new InvalidDataException("Interface source branch enters an original instruction operand.");
                if (rows.Count >= 16384) throw new NotSupportedException("Interface source method exceeds its explicit inspection resource budget.");
                var row = InterfaceDecode(code, at, previous);
                if (rows.Values.Any(existing => at < existing.At && existing.At < row.Next))
                    throw new InvalidDataException("Interface source instruction overlaps an already decoded branch.");
                rows.Add(at, row);
                if (row.Opcode is 0xc2 or 0xc3 || row.Opcode == 0xff && (row.ModRm >> 3 & 7) == 4) break;
                if (row.Opcode is 0xe9 or 0xeb || row.Opcode is >= 0x70 and <= 0x7f || row.Opcode == 0x0f && row.Escaped is >= 0x80 and <= 0x8f)
                {
                    queue.Enqueue(Offset(InterfaceRelativeTarget(row, codeBase)));
                    if (row.Opcode is 0xe9 or 0xeb) break;
                }
                previous = row; at = row.Next;
                if (at >= code.Length) throw new InvalidDataException("Interface source method lacks a complete return/branch.");
            }
        }
        return rows.Values.OrderBy(row => row.At).ToArray();
    }

    private static IReadOnlyList<InterfaceInstruction> InterfaceSoundLinearBranch(byte[] code, uint codeBase, uint entry)
    {
        if (entry < codeBase || entry - codeBase >= code.Length) throw new InvalidDataException("Interface case entry exceeds its source.");
        var at = checked((int)(entry - codeBase)); var rows = new List<InterfaceInstruction>();
        while (rows.Count < 16384)
        {
            var row = InterfaceDecode(code, at, rows.Count == 0 ? null : rows[^1]); rows.Add(row);
            if (row.Opcode is 0xc2 or 0xc3 or 0xe9 or 0xeb) return rows;
            if (row.Opcode is >= 0x70 and <= 0x7f || row.Opcode == 0x0f && row.Escaped is >= 0x80 and <= 0x8f || row.Opcode == 0xff && (row.ModRm >> 3 & 7) is 2 or 4)
                throw new NotSupportedException("Interface sound case has an unowned conditional/indirect continuation.");
            at = row.Next;
        }
        throw new NotSupportedException("Interface case exceeds its explicit inspection resource budget.");
    }
}
