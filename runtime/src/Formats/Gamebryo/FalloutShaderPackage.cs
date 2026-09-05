using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutPackagedShader(string Name, ReadOnlyMemory<byte> Bytecode);

internal static class FalloutShaderPackage
{
    internal static IReadOnlyList<FalloutPackagedShader> Read(ReadOnlyMemory<byte> payload)
    {
        var bytes = payload.Span;
        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 100 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != bytes.Length - 12)
            throw new InvalidDataException("Owned shader package header or extent is invalid.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var result = new List<FalloutPackagedShader>();
        var offset = 12;
        for (var index = 0U; index < count; index++)
        {
            if (bytes.Length - offset < 260) throw new InvalidDataException("Shader package entry is truncated.");
            var name = bytes.Slice(offset, 256);
            var end = name.IndexOf((byte)0);
            if (end <= 0 || name[..end].IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0)
                throw new InvalidDataException("Shader package name is invalid.");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 256)..]);
            offset += 260;
            if (size > bytes.Length - offset || size < 4) throw new InvalidDataException("Shader bytecode extent is invalid.");
            result.Add(new(Encoding.ASCII.GetString(name[..end]), payload.Slice(offset, (int)size)));
            offset += (int)size;
        }
        if (offset != bytes.Length) throw new InvalidDataException("Shader package has unaccounted bytes.");
        return result;
    }
}
