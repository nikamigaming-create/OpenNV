using System.Buffers.Binary;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutBc1Alpha
{
    // BC1's fourth selector is transparent only in the three-colour mode.
    // DDS header alpha flags are not authoritative for these encoded texels.
    internal static bool ContainsTransparency(ReadOnlySpan<byte> blocks, int width, int height, int levels)
    {
        if (width <= 0 || height <= 0 || levels <= 0)
            throw new InvalidDataException("Invalid BC1 image dimensions or mip count.");
        var transparent = false;
        for (var level = 0; level < levels; level++)
        {
            var columns = (width - 1) / 4 + 1;
            var rows = (height - 1) / 4 + 1;
            var extent = (long)columns * rows * 8;
            if (extent > blocks.Length)
                throw new InvalidDataException("BC1 mip payload is truncated.");
            var offset = 0;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++, offset += 8)
                {
                    var block = blocks.Slice(offset, 8);
                    if (transparent || BinaryPrimitives.ReadUInt16LittleEndian(block) >
                        BinaryPrimitives.ReadUInt16LittleEndian(block[2..])) continue;
                    var selectors = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
                    var alphaBits = selectors & (selectors >> 1) & 0x55555555U;
                    if (alphaBits == 0) continue;
                    var validWidth = Math.Min(4, width - column * 4);
                    var validHeight = Math.Min(4, height - row * 4);
                    for (var y = 0; y < validHeight; y++)
                        for (var x = 0; x < validWidth; x++)
                            transparent |= (alphaBits & (1U << ((y * 4 + x) * 2))) != 0;
                }
            }
            blocks = blocks[(int)extent..];
            if (level + 1 < levels && width == 1 && height == 1)
                throw new InvalidDataException("BC1 mip chain extends beyond its one-texel level.");
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        if (!blocks.IsEmpty) throw new InvalidDataException("BC1 payload has bytes outside its mip chain.");
        return transparent;
    }
}
