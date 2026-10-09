using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// SCHR declares independent fields. Decoding does not admit an execution type,
// flags, local lifetime, or any relationship between local slots and row counts.
internal readonly record struct FalloutScriptHeader(uint PrefixWord, uint ReferenceCount,
    uint CompiledBytes, uint VariableCount, ushort Type, ushort Flags)
{
    internal static FalloutScriptHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != 20) throw new InvalidDataException("Script SCHR extent is not 20 bytes.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(data),
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[18..]));
    }
}
