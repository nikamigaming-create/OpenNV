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
    OwnedPhysicalDevice PhysicalDevice,
    OwnedPipBoyStatusPresentation StatusPresentation,
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

internal sealed record OwnedPhysicalDevice(
    string LogicalPath,
    string SourceSha256,
    string ModelPath,
    string ModelSha256,
    string SidecarPath,
    string SidecarSha256,
    string BufferPath,
    string BufferSha256,
    string MaterialManifestPath,
    string MaterialManifestSha256,
    string ScreenSurface,
    IReadOnlyDictionary<string, string> SurfaceRoles,
    int Surfaces,
    int Vertices,
    int Textures);

internal sealed record OwnedPipBoyStatusPresentation(
    Rect2 StatusContainerRect,
    IReadOnlyList<OwnedPipBoyRule> Rules,
    IReadOnlyList<OwnedPipBoyStringSource> Headline,
    IReadOnlyList<OwnedPipBoyStringSource> ConditionTabs,
    IReadOnlyList<OwnedPipBoyStringSource> Navigation,
    IReadOnlyList<OwnedPipBoyBodyImage> BodyImages);

internal sealed record OwnedPipBoyRule(string Tile, Rect2 Rect);

internal sealed record OwnedPipBoyStringSource(
    string Tile,
    int EngineId,
    string Entity,
    int FontId,
    string Text,
    string TextProvenance,
    Rect2 Rect,
    bool Selected);

internal sealed record OwnedPipBoyBodyImage(
    string Tile,
    string ParentTile,
    int EngineId,
    Rect2 Rect,
    OwnedUiTexture Texture);

internal sealed record OwnedGameplayUiRole(
    string Role,
    string Document,
    string MenuName,
    int BodyFontId,
    int TitleFontId,
    IReadOnlyDictionary<string, Rect2> Layout);
