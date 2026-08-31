using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal enum OwnedGamebryoTileVisibility
{
    Inherited,
    Visible,
    Hidden,
}

internal sealed record OwnedGamebryoTileLayout(
    string Document,
    string DocumentSha256,
    string Tile,
    Rect2 Rect,
    OwnedGamebryoTileVisibility Visibility);

internal sealed record OwnedGamebryoTextBinding(
    string Tile,
    string StringEntity,
    string Text,
    IReadOnlyList<string> SourceSha256s);

internal static class OwnedGamebryoTileRuntime
{
    private const int Sha256HexCharacters = 64;

    internal static void ApplyAbsolute(Control control, OwnedGamebryoTileLayout source)
    {
        Validate(source);
        control.Name = source.Tile;
        control.Position = source.Rect.Position;
        control.Size = source.Rect.Size;
        ApplyVisibility(control, source.Visibility);
    }

    internal static void ApplyAnchored(
        Control control,
        OwnedGamebryoTileLayout source,
        Vector2 canvasSize)
    {
        Validate(source);
        if (!canvasSize.IsFinite() || canvasSize.X <= 0.0f || canvasSize.Y <= 0.0f)
            throw new InvalidOperationException(
                "Owned Gamebryo UI canvas dimensions are invalid.");
        control.Name = source.Tile;
        control.AnchorLeft = source.Rect.Position.X / canvasSize.X;
        control.AnchorTop = source.Rect.Position.Y / canvasSize.Y;
        control.AnchorRight = source.Rect.End.X / canvasSize.X;
        control.AnchorBottom = source.Rect.End.Y / canvasSize.Y;
        ApplyVisibility(control, source.Visibility);
    }

    internal static void BindText(Label label, OwnedGamebryoTextBinding source)
    {
        Validate(source);
        label.Name = source.Tile;
        label.Text = source.Text;
    }

    internal static void Validate(OwnedGamebryoTileLayout source)
    {
        if (string.IsNullOrWhiteSpace(source.Document) ||
            source.DocumentSha256.Length != Sha256HexCharacters ||
            source.DocumentSha256.Any(value => !Uri.IsHexDigit(value)) ||
            string.IsNullOrWhiteSpace(source.Tile) ||
            !source.Rect.Position.IsFinite() ||
            !source.Rect.Size.IsFinite() ||
            source.Rect.Size.X <= 0.0f ||
            source.Rect.Size.Y <= 0.0f ||
            !Enum.IsDefined(source.Visibility))
            throw new InvalidOperationException(
                "Owned Gamebryo UI tile layout is incomplete.");
    }

    internal static void Validate(OwnedGamebryoTextBinding source)
    {
        if (string.IsNullOrWhiteSpace(source.Tile) ||
            string.IsNullOrWhiteSpace(source.StringEntity) ||
            string.IsNullOrWhiteSpace(source.Text) ||
            source.SourceSha256s.Count == 0 ||
            source.SourceSha256s.Any(value =>
                value.Length != Sha256HexCharacters ||
                value.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidOperationException(
                "Owned Gamebryo UI text binding is incomplete.");
    }

    private static void ApplyVisibility(
        CanvasItem control,
        OwnedGamebryoTileVisibility visibility)
    {
        if (visibility != OwnedGamebryoTileVisibility.Inherited)
            control.Visible = visibility == OwnedGamebryoTileVisibility.Visible;
    }
}
