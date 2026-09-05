using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeBitmapFontAsset(FalloutBitmapFont Font, Texture2D Atlas)
{
    internal FontFile CreateFontFile()
    {
        var size = Mathf.RoundToInt(Font.SourceSize);
        var cacheSize = new Vector2I(size, 0);
        var result = new FontFile
        {
            FontName = Font.TextureName,
            FixedSize = size,
            AllowSystemFallback = false,
            GenerateMipmaps = false
        };
        using var pixels = Atlas.GetImage();
        result.SetTextureImage(0, cacheSize, 0, pixels);
        result.SetCacheAscent(0, size, Font.Ascent);
        result.SetCacheDescent(0, size, Font.Height - Font.Ascent);
        result.SetCacheScale(0, size, 1);
        for (var index = 0; index < Font.Glyphs.Count; index++)
        {
            var glyph = Font.Glyphs[index];
            result.SetGlyphAdvance(0, size, index, new Vector2(glyph.Advance, 0));
            result.SetGlyphOffset(0, cacheSize, index, new Vector2(glyph.LeftBearing, -glyph.Ascent));
            result.SetGlyphSize(0, cacheSize, index, new Vector2(glyph.Width, glyph.Height));
            result.SetGlyphUVRect(0, cacheSize, index, new Rect2(glyph.Left * Atlas.GetWidth(), glyph.Top * Atlas.GetHeight(),
                (glyph.Right - glyph.Left) * Atlas.GetWidth(), (glyph.Bottom - glyph.Top) * Atlas.GetHeight()));
            result.SetGlyphTextureIdx(0, cacheSize, index, 0);
        }
        return result;
    }

    internal static NativeBitmapFontAsset Read(FalloutInstallationSettings settings, int id)
    {
        static byte[] Owned(string path) => RuntimeLiveContentSource.Current!.TryRead(path, null, out var bytes, out _)
            ? bytes : throw new FileNotFoundException("Missing owned bitmap font resource.", path);
        var font = FalloutBitmapFont.Read(Owned(settings.Require("Fonts", $"sFontFile_{id}")));
        var tex = Owned("textures\\fonts\\" + font.TextureName + ".tex");
        if (tex.Length < 8) throw new InvalidDataException("Font TEX header is truncated.");
        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tex));
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tex.AsSpan(4)));
        if (width <= 0 || height <= 0 || tex.Length != checked(8 + width * height * 4))
            throw new InvalidDataException("Font TEX dimensions do not match its original RGBA bytes.");
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, tex.AsSpan(8).ToArray());
        return new NativeBitmapFontAsset(font, ImageTexture.CreateFromImage(image));
    }

    internal void Draw(CanvasItem canvas, Vector2 origin, string text, Color color, float? baseline = null)
    {
        var cursor = origin.X;
        foreach (var character in text)
        {
            var glyph = Font.Glyph(character);
            if (glyph.Width > 0 && glyph.Height > 0)
                canvas.DrawTextureRectRegion(Atlas,
                    new Rect2(cursor + glyph.LeftBearing, origin.Y + (baseline ?? Font.Ascent) - glyph.Ascent, glyph.Width, glyph.Height),
                    new Rect2(glyph.Left * Atlas.GetWidth(), glyph.Top * Atlas.GetHeight(),
                        (glyph.Right - glyph.Left) * Atlas.GetWidth(), (glyph.Bottom - glyph.Top) * Atlas.GetHeight()), color);
            cursor += glyph.Advance;
        }
    }
}
