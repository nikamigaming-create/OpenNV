using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal static class OwnedUiTheme
{
    private const float ByteChannelMaximum = 255.0f;
    private const int FontCacheIndex = 0;
    private const int FontOutlineSizePixels = 0;
    private const int FontTextureIndex = 0;
    internal const float CenteringFactor = 0.5f;

    internal static float NormalizeByteChannel(float value)
    {
        if (!float.IsFinite(value) || value < 0.0f || value > ByteChannelMaximum)
            throw new InvalidOperationException("Owned UI byte channel is invalid.");
        return value / ByteChannelMaximum;
    }

    internal static FontFile BuildFont(OwnedBitmapFont authored)
    {
        var fontSize = Mathf.RoundToInt(authored.LineHeightPixels);
        var cacheSize = new Vector2I(fontSize, FontOutlineSizePixels);
        var atlas = Image.LoadFromFile(authored.Atlas.Path);
        if (atlas is null || atlas.IsEmpty())
            throw new InvalidOperationException("Owned UI font atlas could not be decoded.");
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
                    -glyph.VerticalBearingPixels));
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
            throw new InvalidOperationException($"Owned UI texture could not be decoded: {path}");
        return ImageTexture.CreateFromImage(image);
    }

    internal static void ApplyButton(
        Button button,
        FontFile font,
        Color systemColor,
        OwnedUiStyle style)
    {
        button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride("font_size", font.FixedSize);
        button.AddThemeColorOverride("font_color", Brightness(systemColor, style.TextBrightness));
        button.AddThemeColorOverride("font_hover_color", Brightness(systemColor, style.TextBrightness));
        button.AddThemeColorOverride("font_focus_color", Brightness(systemColor, style.TextBrightness));
        button.AddThemeColorOverride("font_pressed_color", Brightness(systemColor, style.TextBrightness));
        button.AddThemeColorOverride(
            "font_disabled_color",
            Brightness(systemColor, style.DisabledTextBrightness));
        var empty = new StyleBoxEmpty();
        var highlighted = HighlightedStyle(systemColor, style);
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("disabled", empty);
        button.AddThemeStyleboxOverride("hover", highlighted);
        button.AddThemeStyleboxOverride("pressed", highlighted);
        button.AddThemeStyleboxOverride("focus", highlighted);
    }

    internal static StyleBoxFlat HighlightedStyle(Color systemColor, OwnedUiStyle style)
    {
        var lineWidth = Mathf.RoundToInt(style.LineThicknessPixels);
        return new StyleBoxFlat
        {
            BgColor = Brightness(
                systemColor,
                style.BackgroundFillBrightness,
                style.BackgroundFillAlpha),
            BorderColor = Brightness(systemColor, style.LineBrightness),
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
