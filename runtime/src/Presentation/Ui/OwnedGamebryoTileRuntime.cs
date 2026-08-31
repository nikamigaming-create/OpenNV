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

internal sealed record OwnedGamebryoRaceSexNavigation(
    string Tile,
    Vector2 Anchor,
    Vector2 Buffer,
    float Brightness,
    float TextYAdjust,
    float VerticalCenterDivisor,
    float BaseTextYOffset,
    OwnedGamebryoHorizontalJustification Justification,
    OwnedGamebryoTextBinding Text);

internal sealed record OwnedGamebryoRaceSexTemplate(
    string Tile,
    Rect2 Rect,
    string TextTile,
    Vector2 TextPosition);

internal sealed record OwnedGamebryoRaceSexControls(
    string Document,
    string DocumentSha256,
    Rect2 BackgroundRect,
    float TopBound,
    float BottomBound,
    Rect2 FaceGrabRect,
    OwnedGamebryoRaceSexNavigation Back,
    OwnedGamebryoRaceSexNavigation Next,
    OwnedGamebryoRaceSexTemplate List,
    OwnedGamebryoRaceSexTemplate Slider);

internal sealed record OwnedGamebryoDialogueMenu(
    Vector2 CanvasSize,
    string Document,
    string DocumentSha256,
    string BackgroundTile,
    string BackgroundTexture,
    float BackgroundWidth,
    float BackgroundBrightness,
    string ClickTile,
    string SpeakerNameTile,
    int SpeakerNameFont,
    float SpeakerNameRightInset,
    float SpeakerNameTopInset,
    string SpeakerTextTile,
    int SpeakerTextFont,
    float SpeakerWrapInset,
    float SpeakerLeftInset,
    float CenterHeightFactor,
    float SafeBottomInset,
    float BackgroundTopInset,
    float BackgroundVerticalInset,
    float BackgroundHeightPadding,
    string TopicListTile,
    float TopicMinimumHeight,
    float TopicWidthInset,
    float TopicLeftInset,
    float TopicBackgroundHeightPadding,
    string TopicTile,
    string TopicTextTile,
    int TopicFont,
    float TopicTextX,
    float TopicTextY,
    float TopicWrapInset,
    float TopicVerticalSpacing);

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

    internal static int RequireSourceSelection<T>(
        IReadOnlyList<T> options,
        Func<T, string> sourceIdentity,
        string selectedSourceIdentity)
    {
        var selected = -1;
        for (var index = 0; index < options.Count; index++)
        {
            if (sourceIdentity(options[index]) != selectedSourceIdentity)
                continue;
            if (selected >= 0)
                throw new InvalidOperationException(
                    $"Owned Gamebryo UI source selection is ambiguous: {selectedSourceIdentity}");
            selected = index;
        }
        if (selected < 0)
            throw new InvalidOperationException(
                $"Owned Gamebryo UI source selection is unavailable: {selectedSourceIdentity}");
        return selected;
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

    internal static OwnedGamebryoRaceSexControls ParseRaceSexControls(
        JsonElement source)
    {
        const string schema = "opennv-owned-racesex-menu-tiles/v1";
        if (source.GetProperty("schema").GetString() != schema ||
            source.GetProperty("menuName").GetString() != "RaceSexMenu")
            throw new InvalidOperationException(
                "Owned RaceSexMenu control contract identity differs.");
        var document = source.GetProperty("document").GetString()!;
        var sha256 = source.GetProperty("documentSha256").GetString()!;
        var background = source.GetProperty("background");
        var face = source.GetProperty("faceGrab");
        var navigation = source.GetProperty("navigation");
        var list = source.GetProperty("listItemTemplate");
        var listText = list.GetProperty("text");
        var slider = source.GetProperty("sliderTemplate");
        var sliderText = slider.GetProperty("label");
        var result = new OwnedGamebryoRaceSexControls(
            document,
            sha256,
            ReadRect(background.GetProperty("rect")),
            background.GetProperty("topBound").GetSingle(),
            background.GetProperty("bottomBound").GetSingle(),
            ReadRect(face.GetProperty("rect")),
            RaceSexNavigation(navigation.GetProperty("back"), document, sha256),
            RaceSexNavigation(navigation.GetProperty("next"), document, sha256),
            new OwnedGamebryoRaceSexTemplate(
                list.GetProperty("tile").GetString()!,
                ReadRect(list.GetProperty("rect")),
                listText.GetProperty("tile").GetString()!,
                new Vector2(
                    listText.GetProperty("notSelectableX").GetSingle(),
                    listText.GetProperty("y").GetSingle())),
            new OwnedGamebryoRaceSexTemplate(
                slider.GetProperty("tile").GetString()!,
                ReadRect(slider.GetProperty("rect")),
                sliderText.GetProperty("tile").GetString()!,
                new Vector2(
                    sliderText.GetProperty("x").GetSingle(),
                    sliderText.GetProperty("y").GetSingle())));
        ValidateRaceSexControls(result);
        return result;
    }

    internal static OwnedGamebryoDialogueMenu ParseDialogueMenu(JsonElement source)
    {
        const string schema = "opennv-owned-dialogue-menu-tiles/v1";
        if (source.GetProperty("schema").GetString() != schema ||
            source.GetProperty("menuName").GetString() != "DialogMenu")
            throw new InvalidOperationException(
                "Owned DialogueMenu tile contract identity differs.");
        var background = source.GetProperty("background");
        var speakerName = source.GetProperty("speakerName");
        var speakerText = source.GetProperty("speakerText");
        var topics = source.GetProperty("topics");
        var template = topics.GetProperty("template");
        var result = new OwnedGamebryoDialogueMenu(
            ReadVector(source.GetProperty("canvasSize")),
            source.GetProperty("document").GetString()!,
            source.GetProperty("documentSha256").GetString()!,
            background.GetProperty("tile").GetString()!,
            background.GetProperty("texture").GetString()!,
            background.GetProperty("width").GetSingle(),
            background.GetProperty("brightness").GetSingle(),
            source.GetProperty("clickTile").GetString()!,
            speakerName.GetProperty("tile").GetString()!,
            speakerName.GetProperty("font").GetInt32(),
            speakerName.GetProperty("rightInset").GetSingle(),
            speakerName.GetProperty("topInset").GetSingle(),
            speakerText.GetProperty("tile").GetString()!,
            speakerText.GetProperty("font").GetInt32(),
            speakerText.GetProperty("wrapInset").GetSingle(),
            speakerText.GetProperty("leftInset").GetSingle(),
            speakerText.GetProperty("centerHeightFactor").GetSingle(),
            speakerText.GetProperty("safeBottomInset").GetSingle(),
            background.GetProperty("topInset").GetSingle(),
            background.GetProperty("verticalInset").GetSingle(),
            background.GetProperty("heightPadding").GetSingle(),
            topics.GetProperty("tile").GetString()!,
            topics.GetProperty("minimumHeight").GetSingle(),
            topics.GetProperty("widthInset").GetSingle(),
            topics.GetProperty("leftInset").GetSingle(),
            topics.GetProperty("backgroundHeightPadding").GetSingle(),
            template.GetProperty("tile").GetString()!,
            template.GetProperty("textTile").GetString()!,
            template.GetProperty("font").GetInt32(),
            template.GetProperty("textX").GetSingle(),
            template.GetProperty("textY").GetSingle(),
            template.GetProperty("wrapInset").GetSingle(),
            template.GetProperty("verticalSpacing").GetSingle());
        var numbers = new[]
        {
            result.CanvasSize.X, result.CanvasSize.Y, result.BackgroundWidth,
            result.BackgroundBrightness, result.SpeakerNameRightInset,
            result.SpeakerNameTopInset, result.SpeakerWrapInset,
            result.SpeakerLeftInset, result.CenterHeightFactor,
            result.SafeBottomInset, result.BackgroundTopInset,
            result.BackgroundVerticalInset, result.BackgroundHeightPadding,
            result.TopicMinimumHeight, result.TopicWidthInset,
            result.TopicLeftInset, result.TopicBackgroundHeightPadding,
            result.TopicTextX, result.TopicTextY,
            result.TopicWrapInset, result.TopicVerticalSpacing,
        };
        var identities = new[]
        {
            result.Document, result.BackgroundTile, result.BackgroundTexture,
            result.ClickTile, result.SpeakerNameTile, result.SpeakerTextTile,
            result.TopicListTile, result.TopicTile, result.TopicTextTile,
        };
        if (result.DocumentSha256.Length != Sha256HexCharacters ||
            result.DocumentSha256.Any(value => !Uri.IsHexDigit(value)) ||
            identities.Any(string.IsNullOrWhiteSpace) ||
            numbers.Any(value => !float.IsFinite(value) || value <= 0.0f) ||
            result.CenterHeightFactor > 1.0f ||
            result.BackgroundBrightness > byte.MaxValue ||
            result.SpeakerNameFont <= 0 || result.SpeakerTextFont <= 0 ||
            result.TopicFont <= 0)
            throw new InvalidOperationException(
                "Owned DialogueMenu runtime contract is incomplete.");
        return result;
    }

    internal static Rect2 NavigationRect(
        OwnedGamebryoRaceSexNavigation source,
        Vector2 textSize)
    {
        Validate(source.Text);
        if (!textSize.IsFinite() || textSize.X <= 0.0f || textSize.Y <= 0.0f ||
            !source.Anchor.IsFinite() || !source.Buffer.IsFinite() ||
            source.Buffer.X < 0.0f || source.Buffer.Y < 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSexMenu navigation geometry is invalid.");
        var size = textSize + source.Buffer;
        var x = source.Justification == OwnedGamebryoHorizontalJustification.Right
            ? source.Anchor.X - size.X
            : source.Anchor.X;
        return new Rect2(x, source.Anchor.Y, size.X, size.Y);
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

    private static OwnedGamebryoRaceSexNavigation RaceSexNavigation(
        JsonElement source,
        string document,
        string documentSha256)
    {
        var sourceHashes = source.GetProperty("stringSourceDocuments")
            .EnumerateArray()
            .Select(value => value.GetProperty("sha256").GetString()!)
            .ToArray();
        return new OwnedGamebryoRaceSexNavigation(
            source.GetProperty("tile").GetString()!,
            new Vector2(
                source.GetProperty("x").GetSingle(),
                source.GetProperty("y").GetSingle()),
            new Vector2(
                source.GetProperty("horizontalBuffer").GetSingle(),
                source.GetProperty("verticalBuffer").GetSingle()),
            source.GetProperty("brightness").GetSingle(),
            source.GetProperty("textYAdjust").GetSingle(),
            source.GetProperty("verticalCenterDivisor").GetSingle(),
            source.GetProperty("baseTextYOffset").GetSingle(),
            source.GetProperty("justify").GetString() switch
            {
                "left" => OwnedGamebryoHorizontalJustification.Left,
                "right" => OwnedGamebryoHorizontalJustification.Right,
                _ => throw new InvalidOperationException(
                    "Owned RaceSexMenu navigation justification differs."),
            },
            new OwnedGamebryoTextBinding(
                source.GetProperty("tile").GetString()!,
                source.GetProperty("stringEntity").GetString()!,
                source.GetProperty("label").GetString()!,
                sourceHashes));
    }

    private static void ValidateRaceSexControls(OwnedGamebryoRaceSexControls source)
    {
        Validate(new OwnedGamebryoTileLayout(
            source.Document,
            source.DocumentSha256,
            "RaceSexMenu",
            source.BackgroundRect,
            OwnedGamebryoTileVisibility.Inherited));
        Validate(source.Back.Text);
        Validate(source.Next.Text);
        if (!source.FaceGrabRect.Position.IsFinite() ||
            !source.FaceGrabRect.Size.IsFinite() ||
            source.FaceGrabRect.Size.X <= 0.0f || source.FaceGrabRect.Size.Y <= 0.0f ||
            !float.IsFinite(source.TopBound) || !float.IsFinite(source.BottomBound) ||
            source.TopBound < 0.0f || source.BottomBound <= source.TopBound ||
            source.BottomBound > source.BackgroundRect.Size.Y ||
            source.List.Rect.Size.X <= 0.0f || source.List.Rect.Size.Y <= 0.0f ||
            source.Slider.Rect.Size.X <= 0.0f || source.Slider.Rect.Size.Y <= 0.0f ||
            !float.IsFinite(source.Back.Brightness) || source.Back.Brightness <= 0.0f ||
            !float.IsFinite(source.Next.Brightness) || source.Next.Brightness <= 0.0f ||
            source.Back.VerticalCenterDivisor <= 0.0f ||
            source.Next.VerticalCenterDivisor <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSexMenu shared control contract is incomplete.");
    }

    private static void ApplyVisibility(
        CanvasItem control,
        OwnedGamebryoTileVisibility visibility)
    {
        if (visibility != OwnedGamebryoTileVisibility.Inherited)
            control.Visible = visibility == OwnedGamebryoTileVisibility.Visible;
    }
}
