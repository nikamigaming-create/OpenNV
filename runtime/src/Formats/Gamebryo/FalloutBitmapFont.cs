using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutBitmapGlyph(
    uint TextureIndex, float Left, float Top, float Right, float Bottom,
    float Width, float Height, float LeftBearing, float RightBearing, float Ascent)
{
    internal float Advance => Width + LeftBearing + RightBearing;
}

internal sealed record FalloutBitmapFont(float SourceSize, string TextureName, IReadOnlyList<FalloutBitmapGlyph> Glyphs)
{
    private const int HeaderBytes = 296;
    private const int GlyphBytes = 56;
    private const int GlyphCount = 256;

    internal float Height => Glyphs.Max(glyph => glyph.Height);
    internal float Ascent => Glyphs.Max(glyph => glyph.Ascent);
    // Tile text places the baseline using the font's maximum descent extent.
    // Preserve the source line-height arithmetic before deriving that offset.
    internal float TileBaseline => 2 * (Glyphs.Max(glyph => SourceSize - glyph.Ascent + glyph.Height) - SourceSize);

    internal float Measure(string text) => text.Sum(character => Glyph(character).Advance);
    internal FalloutBitmapGlyph Glyph(char character) => character < GlyphCount
        ? Glyphs[character] : throw new InvalidDataException($"Font has no decoded glyph for U+{(int)character:X4}.");

    internal static FalloutBitmapFont Read(ReadOnlySpan<byte> source)
    {
        if (source.Length != HeaderBytes + GlyphCount * GlyphBytes)
            throw new InvalidDataException("Unsupported Fallout bitmap font extent.");
        var size = Float(source, 0);
        if (size <= 0 || BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != 1)
            throw new InvalidDataException("Unsupported Fallout bitmap font header or atlas count.");
        var nameBytes = source.Slice(12, HeaderBytes - 12);
        var terminator = nameBytes.IndexOf((byte)0);
        if (terminator <= 0)
            throw new InvalidDataException("Bitmap font atlas name is not terminated.");
        var name = Encoding.ASCII.GetString(nameBytes[..terminator]);
        if (name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Bitmap font atlas name is not a local resource name.");
        var glyphs = new FalloutBitmapGlyph[GlyphCount];
        for (var index = 0; index < glyphs.Length; ++index)
        {
            var entry = source.Slice(HeaderBytes + index * GlyphBytes, GlyphBytes);
            var texture = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var left = Float(entry, 4);
            var top = Float(entry, 8);
            var right = Float(entry, 12);
            var bottom = Float(entry, 24);
            if (texture != 0 || Float(entry, 16) != top || Float(entry, 20) != left ||
                Float(entry, 28) != right || Float(entry, 32) != bottom ||
                left < 0 || top < 0 || right < left || bottom < top || right > 1 || bottom > 1)
                throw new InvalidDataException($"Unsupported bitmap glyph atlas coordinates: {index}.");
            var glyph = new FalloutBitmapGlyph(texture, left, top, right, bottom,
                Float(entry, 36), Float(entry, 40), Float(entry, 44), Float(entry, 48), Float(entry, 52));
            if (glyph.Width < 0 || glyph.Height < 0 || glyph.Advance < 0)
                throw new InvalidDataException($"Invalid bitmap glyph dimensions: {index}.");
            glyphs[index] = glyph;
        }
        return new FalloutBitmapFont(size, name, glyphs);
    }

    private static float Float(ReadOnlySpan<byte> bytes, int offset)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Nonfinite bitmap font metric.");
    }
}
