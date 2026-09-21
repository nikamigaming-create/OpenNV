using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutDdsMipLevel(int Width, int Height, int Offset, int Bytes);
internal sealed record FalloutDdsMipChain(string Format, IReadOnlyList<FalloutDdsMipLevel> Levels)
{
    /// <summary>Reads an authored partial BC mip chain. Godot Image only represents complete pyramids.</summary>
    internal static FalloutDdsMipChain? ReadPartial(ReadOnlySpan<byte> bytes)
    {
        NativeOwnedMediaFormat.ValidateDds(bytes);
        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]));
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]));
        var fullCount = 1;
        for (var extent = Math.Max(width, height); extent > 1; extent /= 2) fullCount++;
        if (count <= 1 || count >= fullCount) return null;
        if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) & 0x20000) == 0)
            throw new InvalidDataException("DDS partial mip count is present without its header flag.");
        if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes[80..]) & 4) == 0) return null;
        var fourCc = bytes.Slice(84, 4);
        var format = fourCc.SequenceEqual("DXT1"u8) ? "BC1" : fourCc.SequenceEqual("DXT3"u8) ? "BC2" :
            fourCc.SequenceEqual("DXT5"u8) ? "BC3" : null;
        if (format is null) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[112..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) > 1)
            throw new NotSupportedException("Partial DDS cube/volume mip chains are not bound to the 2D texture owner.");
        var blockBytes = format == "BC1" ? 8 : 16;
        var levels = new List<FalloutDdsMipLevel>();
        var offset = 128;
        for (var level = 0; level < count; level++)
        {
            var extent = checked(((long)width + 3) / 4 * (((long)height + 3) / 4) * blockBytes);
            if (extent > bytes.Length - offset) throw new InvalidDataException("DDS authored mip payload is truncated.");
            levels.Add(new(width, height, offset, (int)extent));
            offset += (int)extent;
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        if (offset != bytes.Length) throw new InvalidDataException("DDS partial mip payload has unexplained trailing bytes.");
        return new(format, levels);
    }
}
