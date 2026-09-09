using System.Buffers.Binary;
using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Direct in-memory AAFF glyphs and COLOR.PAL tint for the original interface.</summary>
internal sealed class ClassicOwnedFont
{
    private readonly (ImageTexture? Texture, int Width, int Height)[] _glyphs = new (ImageTexture?, int, int)[256];
    internal int Height { get; }
    internal int LineHeight { get; }
    private readonly int _letterSpacing, _wordSpacing;

    internal ClassicOwnedFont(byte[] data, byte[] palette, int colorTableIndex)
    {
        const int payload = 2060;
        if (data.Length < payload || BinaryPrimitives.ReadUInt32BigEndian(data) != 0x41414646 || palette.Length < 768 + 32768 || colorTableIndex is < 0 or >= 32768)
            throw new InvalidDataException("Classic font or color table is truncated.");
        short Read(int at) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at));
        Height = Read(4); _letterSpacing = Read(6); _wordSpacing = Read(8); LineHeight = Height + Read(10);
        if (Height is < 1 or > 128 || _letterSpacing < 0 || _wordSpacing < 0 || LineHeight < Height)
            throw new InvalidDataException("Classic font spacing is invalid.");
        var color = palette[768 + colorTableIndex] * 3;
        for (var code = 0; code < 256; code++)
        {
            var at = 12 + code * 8; var width = Read(at); var height = Read(at + 2);
            var offset = checked(payload + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4)));
            if (width is < 0 or > 128 || height < 0 || height > Height || offset > data.Length - width * height)
                throw new InvalidDataException("Classic font glyph escapes its payload.");
            if (width == 0 || height == 0) { _glyphs[code] = (null, width, height); continue; }
            var rgba = new byte[width * height * 4];
            for (var pixel = 0; pixel < width * height; pixel++)
            {
                var intensity = data[offset + pixel];
                if (intensity > 7) throw new InvalidDataException("Classic font intensity is invalid.");
                for (var channel = 0; channel < 3; channel++) rgba[pixel * 4 + channel] = (byte)Math.Min(255, palette[color + channel] * 4);
                rgba[pixel * 4 + 3] = (byte)Math.Round(intensity * 255.0 / 7);
            }
            using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
            _glyphs[code] = (ImageTexture.CreateFromImage(image), width, height);
        }
    }

    private int Advance(char character) => character == ' ' ? _wordSpacing :
        _glyphs[character < 256 ? character : '?'].Width + _letterSpacing;

    internal IReadOnlyList<ClassicTextLine> Wrap(string text, int width) => ClassicTextLayout.Wrap(text, width, Advance, _letterSpacing);

    internal int Draw(Control canvas, string text, Vector2 top, int width, int lines,
        HorizontalAlignment alignment = HorizontalAlignment.Left) =>
        Draw(canvas, text, new Rect2(top, new(width, Math.Max(0, lines - 1) * LineHeight + Height)), alignment,
            VerticalAlignment.Top, lines);

    internal int Draw(Control canvas, string text, Rect2 bounds, HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Top, int maximumLines = int.MaxValue)
    {
        var capacity = Math.Min(maximumLines, (int)(bounds.Size.Y - Height) / LineHeight + 1);
        if (bounds.Size.X <= 0 || bounds.Size.Y < Height || capacity <= 0) return 0;
        var lines = Wrap(text, (int)bounds.Size.X).Take(capacity).ToArray();
        var height = (lines.Length - 1) * LineHeight + Height;
        var top = bounds.Position.Y + (vertical == VerticalAlignment.Center ? MathF.Floor((bounds.Size.Y - height) / 2) :
            vertical == VerticalAlignment.Bottom ? bounds.Size.Y - height : 0);
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            var x = bounds.Position.X + (horizontal == HorizontalAlignment.Center ? MathF.Floor((bounds.Size.X - line.Width) / 2) :
                horizontal == HorizontalAlignment.Right ? bounds.Size.X - line.Width : 0);
            foreach (var character in line.Text)
            {
                var glyph = _glyphs[character < 256 ? character : '?'];
                if (glyph.Texture is not null)
                {
                    var rect = new Rect2(new(x, top + index * LineHeight + Height - glyph.Height), new(glyph.Width, glyph.Height));
                    var clipped = rect.Intersection(bounds);
                    if (clipped.HasArea()) canvas.DrawTextureRectRegion(glyph.Texture, clipped, new Rect2(clipped.Position - rect.Position, clipped.Size));
                }
                x += Advance(character);
            }
        }
        return height;
    }
}
