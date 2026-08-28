using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record OwnedUiTexture(string Path, Vector2I Size);

internal sealed record OwnedBitmapFont(
    string LogicalPath,
    float LineHeightPixels,
    float AscentPixels,
    float DescentPixels,
    OwnedUiTexture Atlas,
    IReadOnlyList<OwnedUiGlyph> Glyphs);

internal sealed record OwnedUiGlyph(
    int Codepoint,
    Rect2 UvRect,
    Vector2 Size,
    float HorizontalOffsetPixels,
    float VerticalBearingPixels,
    float AdvancePixels);

internal sealed record OwnedUiStyle(
    float HorizontalPaddingPixels,
    float VerticalPaddingPixels,
    float TextOffsetYPixels,
    float LineThicknessPixels,
    float LineBrightness,
    float DisabledLineBrightness,
    float TextBrightness,
    float DisabledTextBrightness,
    float BackgroundFillAlpha,
    float BackgroundFillBrightness);

internal sealed record OwnedGameplayUiPresentation(
    Vector2 CanvasSize,
    OwnedUiTexture Background,
    Color SystemColor,
    OwnedUiStyle Style,
    IReadOnlyDictionary<string, OwnedGameplayUiRole> Roles,
    IReadOnlyDictionary<int, OwnedBitmapFont> Fonts)
{
    internal OwnedGameplayUiRole Role(string id) =>
        Roles.TryGetValue(id, out var role)
            ? role
            : throw new InvalidOperationException($"Owned gameplay UI role is absent: {id}");

    internal OwnedBitmapFont Font(int id) =>
        Fonts.TryGetValue(id, out var font)
            ? font
            : throw new InvalidOperationException($"Owned gameplay UI font is absent: {id}");
}

internal sealed record OwnedGameplayUiRole(
    string Role,
    string Document,
    string MenuName,
    int BodyFontId,
    int TitleFontId,
    IReadOnlyDictionary<string, Rect2> Layout);
