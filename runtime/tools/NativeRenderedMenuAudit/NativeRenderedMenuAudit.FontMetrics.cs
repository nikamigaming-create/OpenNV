using System.Globalization;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private static void BitmapFontMetrics(string root, string[] options)
    {
        try
        {
            var selectionOptions = options.Where(option => !option.StartsWith("--font=", StringComparison.Ordinal)).ToArray();
            using var selected = OpenUiColorSource(root, selectionOptions);
            RuntimeLiveContentSource.Configure(root, selected.Game, selected.ContentRoots.Skip(1).ToArray(),
                selected.PluginSources.Select(plugin => plugin.Name).ToArray(), selected.Settings);
            var source = RuntimeLiveContentSource.Current!;
            var settings = FalloutInstallationSettings.Read(source);
            var fontIds = options.Where(option => option.StartsWith("--font=", StringComparison.Ordinal))
                .Select(option => int.Parse(option[7..], CultureInfo.InvariantCulture)).ToArray();
            if (fontIds.Length == 0) throw new ArgumentException("Owned font audit requires actual configured --font=id selections.");
            var rows = new List<object>();
            foreach (var id in fontIds)
            {
                var path = settings.Require("Fonts", $"sFontFile_{id}");
                if (!source.TryRead(path, null, out var before, out var identity)) throw new FileNotFoundException(path);
                var digest = SHA256.HashData(before);
                var asset = NativeBitmapFontAsset.Read(settings, id);
                using var atlas = asset.Atlas;
                using var native = asset.CreateFontFile();
                var size = Mathf.RoundToInt(asset.Font.SourceSize);
                var cache = new Vector2I(size, 0);
                for (var slot = 0; slot < asset.Font.Glyphs.Count; ++slot)
                {
                    var character = FalloutBitmapFont.Character(slot);
                    var glyph = asset.Font.Glyph(character);
                    var uv = new Rect2(glyph.Left * atlas.GetWidth(), glyph.Top * atlas.GetHeight(),
                        (glyph.Right - glyph.Left) * atlas.GetWidth(), (glyph.Bottom - glyph.Top) * atlas.GetHeight());
                    // Check the actual registered cache entry. Control characters
                    // need no invented shaping or non-rendering classification.
                    if (native.GetGlyphAdvance(0, size, character) != new Vector2(glyph.Advance, 0) ||
                        native.GetGlyphOffset(0, cache, character) != new Vector2(glyph.LeftBearing, -glyph.Ascent) ||
                        native.GetGlyphSize(0, cache, character) != new Vector2(glyph.Width, glyph.Height) ||
                        native.GetGlyphUVRect(0, cache, character) != uv || native.GetGlyphTextureIdx(0, cache, character) != 0)
                        throw new InvalidDataException($"Native font changed source metrics at slot{slot}, configured font{id}.");
                }
                if (native.AllowSystemFallback || !source.TryRead(path, null, out var after, out var afterIdentity) ||
                    identity != afterIdentity || !SHA256.HashData(after).AsSpan().SequenceEqual(digest))
                    throw new InvalidDataException("Native font lost its source identity or invented fallback.");
                rows.Add(new
                {
                    font = id,
                    identity,
                    sha256 = Convert.ToHexString(digest),
                    slots = asset.Font.Glyphs.Count,
                    signedAdvances = asset.Font.Glyphs.Count(glyph => glyph.Advance < 0)
                });
            }
            GD.Print("OPENNV_OWNED_BITMAP_FONT_METRICS_PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                source.Game,
                source.StackId,
                rows,
                sourceReadonly = true,
                recording = false,
                retainedFrames = 0,
                ordinaryMenus = "unverified",
                retailPixels = "unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
