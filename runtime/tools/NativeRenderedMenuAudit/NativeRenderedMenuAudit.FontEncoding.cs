using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private static void BitmapFontEncoding(string baseRoot, string mod, string root, int fontId,
        string topicId, string[] dependencies)
    {
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            var topic = FalloutDialogueTopic.Read(records, topicId);
            var texts = topic.Infos.SelectMany(info => info.Responses).Select(response => response.Text).ToArray();
            if (!texts.Any(text => text.Contains('\u2019')))
                throw new InvalidDataException("Owned font fixture has no reached curly apostrophe source text.");
            var hashes = topic.Infos.Select(info => SHA256.HashData(info.Record.ReadData())).ToArray();
            var settings = FalloutInstallationSettings.Read(source);
            var path = settings.Require("Fonts", $"sFontFile_{fontId}");
            if (!source.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
            var hash = SHA256.HashData(bytes);
            var asset = NativeBitmapFontAsset.Read(settings, fontId);
            using var atlas = asset.Atlas;
            using var native = asset.CreateFontFile();
            var size = Mathf.RoundToInt(asset.Font.SourceSize);
            var cache = new Vector2I(size, 0);
            foreach (var text in texts) asset.Font.Measure(text);
            for (var slot = 0x20; slot < asset.Font.Glyphs.Count; ++slot)
            {
                var character = FalloutBitmapFont.Character(slot);
                var glyph = asset.Font.Glyph(character);
                var index = native.GetGlyphIndex(size, character, 0);
                var uv = new Rect2(glyph.Left * atlas.GetWidth(), glyph.Top * atlas.GetHeight(),
                    (glyph.Right - glyph.Left) * atlas.GetWidth(), (glyph.Bottom - glyph.Top) * atlas.GetHeight());
                if (!native.HasChar(character) || native.GetGlyphAdvance(0, size, index) != new Vector2(glyph.Advance, 0) ||
                    native.GetGlyphOffset(0, cache, index) != new Vector2(glyph.LeftBearing, -glyph.Ascent) ||
                    native.GetGlyphSize(0, cache, index) != new Vector2(glyph.Width, glyph.Height) ||
                    native.GetGlyphUVRect(0, cache, index) != uv || native.GetGlyphTextureIdx(0, cache, index) != 0)
                    throw new InvalidDataException($"Native font changed source glyph at U+{(int)character:X4}: " +
                        $"has={native.HasChar(character)} index={index} advance={native.GetGlyphAdvance(0, size, index)}/{glyph.Advance} " +
                        $"offset={native.GetGlyphOffset(0, cache, index)}/{new Vector2(glyph.LeftBearing, -glyph.Ascent)} " +
                        $"size={native.GetGlyphSize(0, cache, index)}/{new Vector2(glyph.Width, glyph.Height)} " +
                        $"uv={native.GetGlyphUVRect(0, cache, index)}/{uv} texture={native.GetGlyphTextureIdx(0, cache, index)}.");
            }
            const string quotes = "\u2018\u2019\u201c\u201d";
            if (native.GetStringSize(quotes, fontSize: size).X != asset.Font.Measure(quotes) ||
                native.GetStringSize(quotes, fontSize: size) != native.GetStringSize("''\"\"", fontSize: size))
                throw new InvalidDataException("Native shaping lost the source quote metrics.");
            if (native.AllowSystemFallback || native.HasChar(0x100) || native.HasChar(0x92) ||
                !source.TryRead(path, null, out var after, out _) || !hash.AsSpan().SequenceEqual(SHA256.HashData(after)) ||
                topic.Infos.Where((info, index) => !hashes[index].AsSpan().SequenceEqual(SHA256.HashData(info.Record.ReadData()))).Any())
                throw new InvalidDataException("Native font invented a fallback or changed owned source bytes.");
            GD.Print($"OPENNV_OWNED_BITMAP_FONT_ENCODING_PASS topic={topic.Topic.FormKey} font={fontId} responses={texts.Length} nativeSlots=224 sourceQuotes=true sourceReadonly=true recording=false campaign=unverified parity=unverified");
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
