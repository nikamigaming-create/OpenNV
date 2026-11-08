using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Structural SCDA records, independent of editor text. Payloads are retained as
// source offsets: framing a command does not implement its operands or effects.
internal readonly record struct FalloutCompiledInstruction(int Offset, int PayloadOffset,
    int PayloadLength, ushort Opcode, ushort? ReferenceIndex)
{
    internal int End => PayloadOffset + PayloadLength;
}

internal static class FalloutCompiledScript
{
    internal static IReadOnlyList<FalloutCompiledInstruction> Read(ReadOnlySpan<byte> data)
    {
        var instructions = new List<FalloutCompiledInstruction>();
        var cursor = 0;
        while (cursor < data.Length)
        {
            var start = cursor;
            var opcode = Word(data, ref cursor, start);
            ushort? reference = null;
            if (opcode == 0x1c)
            {
                reference = Word(data, ref cursor, start);
                opcode = Word(data, ref cursor, start);
                if (opcode == 0x1c)
                    throw new InvalidDataException($"SCDA instruction at {start} repeats its reference prefix.");
            }
            var length = unchecked((short)Word(data, ref cursor, start));
            if (length < 0 || length > data.Length - cursor)
                throw new InvalidDataException($"SCDA instruction at {start} has an invalid payload extent {length}.");
            instructions.Add(new(start, cursor, length, opcode, reference));
            cursor += length;
        }
        return instructions;
    }

    private static ushort Word(ReadOnlySpan<byte> data, ref int cursor, int start)
    {
        if (data.Length - cursor < 2)
            throw new InvalidDataException($"SCDA instruction at {start} has a truncated header.");
        var result = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        cursor += 2;
        return result;
    }
}
