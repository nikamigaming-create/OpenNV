using System.Text.Json;
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

internal enum OwnedGamebryoHorizontalJustification
{
    Left,
    Center,
    Right,
}

internal sealed record OwnedGamebryoAxisExpression(
    float ParentFactor,
    float SelfFactor,
    float Constant);

internal sealed record OwnedGamebryoTilePlacement(
    string Document,
    string DocumentSha256,
    string Tile,
    OwnedGamebryoAxisExpression X,
    OwnedGamebryoAxisExpression Y,
    OwnedGamebryoHorizontalJustification Justification);

internal sealed record OwnedGamebryoPositionedText(
    OwnedGamebryoTilePlacement Placement,
    OwnedGamebryoTextBinding Text);

internal sealed record OwnedGamebryoTextEditMenu(
    Vector2 CanvasSize,
    OwnedGamebryoTileLayout Panel,
    OwnedGamebryoPositionedText Prompt,
    OwnedGamebryoTilePlacement Input,
    float InputWrapWidth,
    OwnedGamebryoPositionedText Accept);

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

    internal static void BindText(Button button, OwnedGamebryoTextBinding source)
    {
        Validate(source);
        button.Name = source.Tile;
        button.Text = source.Text;
    }

    internal static void ApplyTraitPosition(
        Control control,
        OwnedGamebryoTilePlacement source,
        Vector2 parentSize,
        Vector2 selfSize)
    {
        var position = EvaluateTraitPosition(source, parentSize, selfSize);
        control.Name = source.Tile;
        control.Position = position;
        control.Size = selfSize;
    }

    internal static Vector2 EvaluateTraitPosition(
        OwnedGamebryoTilePlacement source,
        Vector2 parentSize,
        Vector2 selfSize)
    {
        Validate(source);
        if (!parentSize.IsFinite() || !selfSize.IsFinite() ||
            parentSize.X <= 0.0f || parentSize.Y <= 0.0f ||
            selfSize.X <= 0.0f || selfSize.Y <= 0.0f)
            throw new InvalidOperationException(
                "Owned Gamebryo UI trait dimensions are invalid.");
        var position = new Vector2(
            Evaluate(source.X, parentSize.X, selfSize.X),
            Evaluate(source.Y, parentSize.Y, selfSize.Y));
        position.X -= source.Justification switch
        {
            OwnedGamebryoHorizontalJustification.Left => 0.0f,
            OwnedGamebryoHorizontalJustification.Center => selfSize.X / 2.0f,
            OwnedGamebryoHorizontalJustification.Right => selfSize.X,
            _ => throw new InvalidOperationException(
                "Owned Gamebryo UI justification is unsupported."),
        };
        if (!position.IsFinite())
            throw new InvalidOperationException(
                "Owned Gamebryo UI trait position is invalid.");
        return position;
    }

    internal static OwnedGamebryoTextEditMenu ParseTextEditMenu(JsonElement source)
    {
        const string schema = "opennv-owned-textedit-menu-tiles/v1";
        if (source.GetProperty("schema").GetString() != schema ||
            source.GetProperty("menuName").GetString() != "TextEditMenu")
            throw new InvalidOperationException(
                "Owned TextEditMenu tile contract identity differs.");
        var document = source.GetProperty("document").GetString()!;
        var sha256 = source.GetProperty("documentSha256").GetString()!;
        var panel = source.GetProperty("panel");
        var prompt = source.GetProperty("prompt");
        var input = source.GetProperty("input");
        var accept = source.GetProperty("accept");
        var result = new OwnedGamebryoTextEditMenu(
            ReadVector(source.GetProperty("canvasSize")),
            new OwnedGamebryoTileLayout(
                document,
                sha256,
                panel.GetProperty("tile").GetString()!,
                ReadRect(panel.GetProperty("rect")),
                OwnedGamebryoTileVisibility.Inherited),
            PositionedText(prompt, document, sha256),
            Placement(input, document, sha256),
            input.GetProperty("wrapWidth").GetSingle(),
            PositionedText(accept, document, sha256));
        Validate(result.Panel);
        Validate(result.Prompt.Placement);
        Validate(result.Prompt.Text);
        Validate(result.Input);
        Validate(result.Accept.Placement);
        Validate(result.Accept.Text);
        if (!result.CanvasSize.IsFinite() ||
            result.CanvasSize.X <= 0.0f || result.CanvasSize.Y <= 0.0f ||
            !float.IsFinite(result.InputWrapWidth) || result.InputWrapWidth <= 0.0f ||
            result.Prompt.Text.SourceSha256s.Count != 1 ||
            result.Accept.Text.SourceSha256s.Count != 1 ||
            !result.Prompt.Text.SourceSha256s[0].Equals(
                sha256,
                StringComparison.OrdinalIgnoreCase) ||
            !result.Accept.Text.SourceSha256s[0].Equals(
                sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned TextEditMenu runtime contract is incomplete.");
        return result;
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

    internal static void Validate(OwnedGamebryoTilePlacement source)
    {
        if (string.IsNullOrWhiteSpace(source.Document) ||
            source.DocumentSha256.Length != Sha256HexCharacters ||
            source.DocumentSha256.Any(value => !Uri.IsHexDigit(value)) ||
            string.IsNullOrWhiteSpace(source.Tile) ||
            !Enum.IsDefined(source.Justification) ||
            !Finite(source.X) || !Finite(source.Y))
            throw new InvalidOperationException(
                "Owned Gamebryo UI tile placement is incomplete.");
    }

    private static OwnedGamebryoPositionedText PositionedText(
        JsonElement source,
        string document,
        string sha256) => new(
        Placement(source, document, sha256),
        new OwnedGamebryoTextBinding(
            source.GetProperty("tile").GetString()!,
            source.GetProperty("stringEntity").GetString()!,
            source.GetProperty("text").GetString()!,
            [source.GetProperty("sourceSha256").GetString()!]));

    private static OwnedGamebryoTilePlacement Placement(
        JsonElement source,
        string document,
        string sha256) => new(
        document,
        sha256,
        source.GetProperty("tile").GetString()!,
        Axis(source.GetProperty("x")),
        Axis(source.GetProperty("y")),
        source.GetProperty("justify").GetString() switch
        {
            "left" => OwnedGamebryoHorizontalJustification.Left,
            "center" => OwnedGamebryoHorizontalJustification.Center,
            "right" => OwnedGamebryoHorizontalJustification.Right,
            _ => throw new InvalidOperationException(
                "Owned Gamebryo UI justification is unsupported."),
        });

    private static OwnedGamebryoAxisExpression Axis(JsonElement source) => new(
        source.GetProperty("parentFactor").GetSingle(),
        source.GetProperty("selfFactor").GetSingle(),
        source.GetProperty("constant").GetSingle());

    private static Rect2 ReadRect(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException(
                "Owned Gamebryo UI rectangle component count differs.");
        return new Rect2(values[0], values[1], values[2], values[3]);
    }

    private static Vector2 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException(
                "Owned Gamebryo UI vector component count differs.");
        return new Vector2(values[0], values[1]);
    }

    private static bool Finite(OwnedGamebryoAxisExpression source) =>
        float.IsFinite(source.ParentFactor) &&
        float.IsFinite(source.SelfFactor) &&
        float.IsFinite(source.Constant);

    private static float Evaluate(
        OwnedGamebryoAxisExpression source,
        float parent,
        float self) =>
        source.ParentFactor * parent + source.SelfFactor * self + source.Constant;

    private static void ApplyVisibility(
        CanvasItem control,
        OwnedGamebryoTileVisibility visibility)
    {
        if (visibility != OwnedGamebryoTileVisibility.Inherited)
            control.Visible = visibility == OwnedGamebryoTileVisibility.Visible;
    }
}
