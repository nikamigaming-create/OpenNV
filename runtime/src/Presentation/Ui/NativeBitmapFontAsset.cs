using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeBitmapFontAsset(FalloutBitmapFont Font, Texture2D Atlas)
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Dictionary<string, (string Hash, NativeBitmapFontAsset Asset)>> Assets = new();
    internal FontFile CreateFontFile()
    {
        var size = Mathf.RoundToInt(Font.SourceSize);
        var cacheSize = new Vector2I(size, 0);
        var result = new FontFile
        {
            // Metadata setters can create the native size cache.
            FixedSize = size,
            FontName = Font.TextureName,
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
            var codepoint = FalloutBitmapFont.Character(index);
            var glyph = Font.Glyph(codepoint);
            result.SetGlyphAdvance(0, size, codepoint, new Vector2(glyph.Advance, 0));
            result.SetGlyphOffset(0, cacheSize, codepoint, new Vector2(glyph.LeftBearing, -glyph.Ascent));
            result.SetGlyphSize(0, cacheSize, codepoint, new Vector2(glyph.Width, glyph.Height));
            result.SetGlyphUVRect(0, cacheSize, codepoint, new Rect2(glyph.Left * Atlas.GetWidth(), glyph.Top * Atlas.GetHeight(),
                (glyph.Right - glyph.Left) * Atlas.GetWidth(), (glyph.Bottom - glyph.Top) * Atlas.GetHeight()));
            result.SetGlyphTextureIdx(0, cacheSize, codepoint, 0);
        }
        return result;
    }

    internal static NativeBitmapFontAsset Read(FalloutInstallationSettings settings, int id)
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned font source is absent.");
        byte[] Owned(string path) => source.TryRead(path, null, out var bytes, out _)
            ? bytes : throw new FileNotFoundException("Missing owned bitmap font resource.", path);
        var path = settings.Require("Fonts", $"sFontFile_{id}").Replace('\\', '/');
        var declaration = Owned(path);
        var font = FalloutBitmapFont.Read(declaration);
        var tex = Owned("textures\\fonts\\" + font.TextureName + ".tex");
        var cache = Assets.GetValue(source, owner =>
        {
            var entries = new Dictionary<string, (string Hash, NativeBitmapFontAsset Asset)>(StringComparer.OrdinalIgnoreCase);
            owner.RegisterRetirement(entries.Clear);
            return entries;
        });
        var hash = Convert.ToHexString(SHA256.HashData(declaration)) + Convert.ToHexString(SHA256.HashData(tex));
        if (cache.TryGetValue(path, out var cached) && cached.Hash == hash) return cached.Asset;
        if (tex.Length < 8) throw new InvalidDataException("Font TEX header is truncated.");
        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tex));
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tex.AsSpan(4)));
        if (width <= 0 || height <= 0 || tex.Length != checked(8 + width * height * 4))
            throw new InvalidDataException("Font TEX dimensions do not match its original RGBA bytes.");
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, tex.AsSpan(8).ToArray());
        var asset = new NativeBitmapFontAsset(font, ImageTexture.CreateFromImage(image));
        cache[path] = (hash, asset);
        return asset;
    }

    internal void Draw(CanvasItem canvas, Vector2 origin, string text, Color color, float? baseline = null, Rect2? clip = null)
    {
        var cursor = origin.X;
        foreach (var character in text)
        {
            var glyph = Font.Glyph(character);
            if (glyph.Width > 0 && glyph.Height > 0)
                NativeUiClip.Draw(canvas, Atlas,
                    new Rect2(cursor + glyph.LeftBearing, origin.Y + (baseline ?? Font.Ascent) - glyph.Ascent, glyph.Width, glyph.Height),
                    new Rect2(glyph.Left * Atlas.GetWidth(), glyph.Top * Atlas.GetHeight(),
                        (glyph.Right - glyph.Left) * Atlas.GetWidth(), (glyph.Bottom - glyph.Top) * Atlas.GetHeight()), color, clip);
            cursor += glyph.Advance;
        }
    }
}
