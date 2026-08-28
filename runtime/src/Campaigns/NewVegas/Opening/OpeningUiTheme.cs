using Godot;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal static class OpeningUiTheme
{
    private const float ByteChannelMaximum = 255.0f;
    private const int FontCacheIndex = 0;
    private const int FontOutlineSizePixels = 0;
    private const int FontTextureIndex = 0;
    internal const float CenteringFactor = 0.5f;

    internal static FontFile BuildFont(OpeningBitmapFont authored)
    {
        var fontSize = Mathf.RoundToInt(authored.LineHeightPixels);
        var cacheSize = new Vector2I(fontSize, FontOutlineSizePixels);
        var atlas = Image.LoadFromFile(authored.Atlas.Path);
        if (atlas is null || atlas.IsEmpty())
            throw new InvalidOperationException("Owned opening font atlas could not be decoded.");
        var font = new FontFile
        {
            FontName = authored.LogicalPath,
            FixedSize = fontSize,
            AllowSystemFallback = false,
            GenerateMipmaps = false,
        };
        font.SetTextureImage(FontCacheIndex, cacheSize, FontTextureIndex, atlas);
        font.SetCacheAscent(FontCacheIndex, fontSize, authored.AscentPixels);
        font.SetCacheDescent(FontCacheIndex, fontSize, authored.DescentPixels);
        font.SetCacheScale(FontCacheIndex, fontSize, 1.0f);
        foreach (var glyph in authored.Glyphs)
        {
            font.SetGlyphAdvance(
                FontCacheIndex,
                fontSize,
                glyph.Codepoint,
                new Vector2(glyph.AdvancePixels, 0.0f));
            font.SetGlyphOffset(
                FontCacheIndex,
                cacheSize,
                glyph.Codepoint,
                new Vector2(
                    glyph.HorizontalOffsetPixels,
                    authored.AscentPixels - glyph.VerticalBearingPixels));
            font.SetGlyphSize(FontCacheIndex, cacheSize, glyph.Codepoint, glyph.Size);
            font.SetGlyphUVRect(FontCacheIndex, cacheSize, glyph.Codepoint, glyph.UvRect);
            font.SetGlyphTextureIdx(
                FontCacheIndex,
                cacheSize,
                glyph.Codepoint,
                FontTextureIndex);
        }
        return font;
    }

    internal static Texture2D LoadTexture(string path)
    {
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
            throw new InvalidOperationException($"Owned opening texture could not be decoded: {path}");
        return ImageTexture.CreateFromImage(image);
    }

    internal static void ApplyButton(
        Button button,
        FontFile font,
        OpeningManifest manifest)
    {
        button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride(
            "font_size",
            Mathf.RoundToInt(manifest.Font.LineHeightPixels));
        button.AddThemeColorOverride(
            "font_color",
            Brightness(manifest.MainMenuColor, manifest.Style.TextBrightness));
        button.AddThemeColorOverride(
            "font_hover_color",
            Brightness(manifest.MainMenuColor, manifest.Style.TextBrightness));
        button.AddThemeColorOverride(
            "font_focus_color",
            Brightness(manifest.MainMenuColor, manifest.Style.TextBrightness));
        button.AddThemeColorOverride(
            "font_pressed_color",
            Brightness(manifest.MainMenuColor, manifest.Style.TextBrightness));
        button.AddThemeColorOverride(
            "font_disabled_color",
            Brightness(manifest.MainMenuColor, manifest.Style.DisabledTextBrightness));
        var empty = new StyleBoxEmpty();
        var highlighted = HighlightedStyle(manifest);
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("disabled", empty);
        button.AddThemeStyleboxOverride("hover", highlighted);
        button.AddThemeStyleboxOverride("pressed", highlighted);
        button.AddThemeStyleboxOverride("focus", highlighted);
    }

    internal static StyleBoxFlat HighlightedStyle(OpeningManifest manifest)
    {
        var lineWidth = Mathf.RoundToInt(manifest.Style.LineThicknessPixels);
        return new StyleBoxFlat
        {
            BgColor = Brightness(
                manifest.MainMenuColor,
                manifest.Style.BackgroundFillBrightness,
                manifest.Style.BackgroundFillAlpha),
            BorderColor = Brightness(
                manifest.MainMenuColor,
                manifest.Style.LineBrightness),
            BorderWidthLeft = lineWidth,
            BorderWidthTop = lineWidth,
            BorderWidthRight = lineWidth,
            BorderWidthBottom = lineWidth,
            ContentMarginLeft = 0.0f,
            ContentMarginTop = 0.0f,
            ContentMarginRight = 0.0f,
            ContentMarginBottom = 0.0f,
        };
    }

    internal static Color Brightness(
        Color source,
        float brightness,
        float alpha = ByteChannelMaximum)
    {
        var scale = brightness / ByteChannelMaximum;
        return new Color(
            source.R * scale,
            source.G * scale,
            source.B * scale,
            alpha / ByteChannelMaximum);
    }
}
