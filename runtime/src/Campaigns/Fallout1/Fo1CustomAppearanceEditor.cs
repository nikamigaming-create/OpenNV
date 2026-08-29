using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed record Fo1CustomAppearanceSelection(
    string FaceShapeId,
    string HairStyleId,
    string SkinToneId,
    string HairColorId,
    string EyeColorId);

internal static class Fo1CustomAppearanceEditorNumericContracts
{
    internal const int FeatureCount = 5;
    internal const float EditorWidth = 640.0f;
    internal const float EditorHeight = 480.0f;
    internal const float TitleX = 32.0f;
    internal const float TitleY = 18.0f;
    internal const float TitleWidth = 576.0f;
    internal const float TitleHeight = 28.0f;
    internal const int TitleFontSize = 16;
    internal const float PortraitX = 48.0f;
    internal const float PortraitY = 74.0f;
    internal const float PortraitSize = 224.0f;
    internal const float PreviewButtonX = 106.0f;
    internal const float PreviewButtonY = 312.0f;
    internal const float PreviewButtonWidth = 108.0f;
    internal const float RowStartY = 82.0f;
    internal const float RowSpacing = 48.0f;
    internal const float FieldNameX = 318.0f;
    internal const float FieldNameWidth = 116.0f;
    internal const float FieldHeight = 22.0f;
    internal const int FieldFontSize = 10;
    internal const float PreviousButtonX = 440.0f;
    internal const float RowButtonWidth = 28.0f;
    internal const float RowButtonHeight = 26.0f;
    internal const float ValueX = 470.0f;
    internal const float ValueWidth = 104.0f;
    internal const float NextButtonX = 576.0f;
    internal const float BoundaryX = 48.0f;
    internal const float BoundaryY = 360.0f;
    internal const float BoundaryWidth = 544.0f;
    internal const float BoundaryHeight = 26.0f;
    internal const int BoundaryFontSize = 9;
    internal const float CommitButtonX = 170.0f;
    internal const float CancelButtonX = 338.0f;
    internal const float FooterButtonY = 410.0f;
    internal const float FooterButtonWidth = 132.0f;
    internal const float FooterButtonHeight = 34.0f;
    internal const int ButtonFontSize = 11;
}

internal sealed partial class Fo1CustomAppearanceEditor : Control
{
    private static readonly Color Green = new("78e781");
    private static readonly Color Amber = new("e6c34c");
    private readonly string _sex;
    private readonly TextureRect _portrait;
    private readonly Fo1ProceduralHeadPreview _head;
    private readonly Button _previewMode;
    private readonly Label[] _labels =
        new Label[Fo1CustomAppearanceEditorNumericContracts.FeatureCount];
    private int _face;
    private int _hair;
    private int _skin;
    private int _hairColor;
    private int _eyeColor;

    internal Fo1CustomAppearanceEditor(
        string sex,
        Fo1CustomAppearanceSelection? current = null)
    {
        _sex = sex;
        Name = "FO1_HEX_CUSTOM_APPEARANCE_EDITOR";
        Size = new Vector2(
            Fo1CustomAppearanceEditorNumericContracts.EditorWidth,
            Fo1CustomAppearanceEditorNumericContracts.EditorHeight);
        MouseFilter = MouseFilterEnum.Stop;
        var catalog = Fo1ProceduralAppearanceCatalog.Load();
        var selection = current ?? new Fo1CustomAppearanceSelection(
            catalog.DefaultFaceShapeId,
            catalog.DefaultHairStyleId,
            catalog.DefaultSkinToneId,
            catalog.DefaultHairColorId,
            catalog.DefaultEyeColorId);
        _face = Index(Fo1ProceduralPortrait.FaceShapes, selection.FaceShapeId);
        _hair = Index(Fo1ProceduralPortrait.HairStyles, selection.HairStyleId);
        _skin = Index(Fo1ProceduralPortrait.SkinTones, selection.SkinToneId);
        _hairColor = Index(Fo1ProceduralPortrait.HairColors, selection.HairColorId);
        _eyeColor = Index(Fo1ProceduralPortrait.EyeColors, selection.EyeColorId);
        AddChild(new ColorRect
        {
            Size = Size,
            Color = Colors.Black,
            MouseFilter = MouseFilterEnum.Stop,
        });
        var title = Text(
            "CUSTOM PORTRAIT + LIVE 3D HEAD",
            Fo1CustomAppearanceEditorNumericContracts.TitleX,
            Fo1CustomAppearanceEditorNumericContracts.TitleY,
            Fo1CustomAppearanceEditorNumericContracts.TitleWidth,
            Fo1CustomAppearanceEditorNumericContracts.TitleHeight,
            Fo1CustomAppearanceEditorNumericContracts.TitleFontSize,
            Amber);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _portrait = new TextureRect
        {
            Name = "FO1_HEX_CUSTOM_PORTRAIT",
            Position = new Vector2(
                Fo1CustomAppearanceEditorNumericContracts.PortraitX,
                Fo1CustomAppearanceEditorNumericContracts.PortraitY),
            Size = new Vector2(
                Fo1CustomAppearanceEditorNumericContracts.PortraitSize,
                Fo1CustomAppearanceEditorNumericContracts.PortraitSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_portrait);
        _head = new Fo1ProceduralHeadPreview
        {
            Position = _portrait.Position,
            Size = _portrait.Size,
        };
        AddChild(_head);
        _previewMode = Button(
            "LIVE 3D",
            Fo1CustomAppearanceEditorNumericContracts.PreviewButtonX,
            Fo1CustomAppearanceEditorNumericContracts.PreviewButtonY,
            Fo1CustomAppearanceEditorNumericContracts.PreviewButtonWidth,
            Fo1CustomAppearanceEditorNumericContracts.RowButtonWidth,
            TogglePreview);
        _previewMode.TooltipText = "Toggle the deterministic green portrait and matching live head";

        var names = new[] { "FACE", "HAIR", "SKIN", "HAIR COLOR", "EYES" };
        for (var row = 0; row < names.Length; row++)
        {
            var captured = row;
            var y = Fo1CustomAppearanceEditorNumericContracts.RowStartY +
                row * Fo1CustomAppearanceEditorNumericContracts.RowSpacing;
            Text(
                names[row],
                Fo1CustomAppearanceEditorNumericContracts.FieldNameX,
                y,
                Fo1CustomAppearanceEditorNumericContracts.FieldNameWidth,
                Fo1CustomAppearanceEditorNumericContracts.FieldHeight,
                Fo1CustomAppearanceEditorNumericContracts.FieldFontSize,
                Amber);
            Button(
                "◀",
                Fo1CustomAppearanceEditorNumericContracts.PreviousButtonX,
                y - 2,
                Fo1CustomAppearanceEditorNumericContracts.RowButtonWidth,
                Fo1CustomAppearanceEditorNumericContracts.RowButtonHeight,
                () => Change(captured, -1));
            _labels[row] = Text(
                "",
                Fo1CustomAppearanceEditorNumericContracts.ValueX,
                y,
                Fo1CustomAppearanceEditorNumericContracts.ValueWidth,
                Fo1CustomAppearanceEditorNumericContracts.FieldHeight,
                Fo1CustomAppearanceEditorNumericContracts.FieldFontSize,
                Green);
            _labels[row].HorizontalAlignment = HorizontalAlignment.Center;
            Button(
                "▶",
                Fo1CustomAppearanceEditorNumericContracts.NextButtonX,
                y - 2,
                Fo1CustomAppearanceEditorNumericContracts.RowButtonWidth,
                Fo1CustomAppearanceEditorNumericContracts.RowButtonHeight,
                () => Change(captured, 1));
        }
        Text(
            "HEX EXTENSION • LOCAL GENERATED PORTRAIT • NO RETAIL HEAD GEOMETRY",
            Fo1CustomAppearanceEditorNumericContracts.BoundaryX,
            Fo1CustomAppearanceEditorNumericContracts.BoundaryY,
            Fo1CustomAppearanceEditorNumericContracts.BoundaryWidth,
            Fo1CustomAppearanceEditorNumericContracts.BoundaryHeight,
            Fo1CustomAppearanceEditorNumericContracts.BoundaryFontSize,
            Green).HorizontalAlignment = HorizontalAlignment.Center;
        Button(
            "USE FACE",
            Fo1CustomAppearanceEditorNumericContracts.CommitButtonX,
            Fo1CustomAppearanceEditorNumericContracts.FooterButtonY,
            Fo1CustomAppearanceEditorNumericContracts.FooterButtonWidth,
            Fo1CustomAppearanceEditorNumericContracts.FooterButtonHeight,
            Commit);
        Button(
            "BACK",
            Fo1CustomAppearanceEditorNumericContracts.CancelButtonX,
            Fo1CustomAppearanceEditorNumericContracts.FooterButtonY,
            Fo1CustomAppearanceEditorNumericContracts.FooterButtonWidth,
            Fo1CustomAppearanceEditorNumericContracts.FooterButtonHeight,
            () => Cancelled?.Invoke());
        Refresh();
    }

    internal event Action<Fo1CustomAppearanceSelection>? Confirmed;
    internal event Action? Cancelled;
    internal bool Live3DVisible => _head.Visible;
    internal Fo1ProceduralHeadPreview Head => _head;

    internal void SetSelection(Fo1CustomAppearanceSelection selection)
    {
        _face = Index(Fo1ProceduralPortrait.FaceShapes, selection.FaceShapeId);
        _hair = Index(Fo1ProceduralPortrait.HairStyles, selection.HairStyleId);
        _skin = Index(Fo1ProceduralPortrait.SkinTones, selection.SkinToneId);
        _hairColor = Index(Fo1ProceduralPortrait.HairColors, selection.HairColorId);
        _eyeColor = Index(Fo1ProceduralPortrait.EyeColors, selection.EyeColorId);
        Refresh();
    }

    internal void TogglePreviewMode() => TogglePreview();
    internal void Confirm() => Commit();

    private Fo1CustomAppearanceSelection Selection => new(
        Fo1ProceduralPortrait.FaceShapes[_face],
        Fo1ProceduralPortrait.HairStyles[_hair],
        Fo1ProceduralPortrait.SkinTones[_skin],
        Fo1ProceduralPortrait.HairColors[_hairColor],
        Fo1ProceduralPortrait.EyeColors[_eyeColor]);

    private void Change(int row, int delta)
    {
        switch (row)
        {
            case 0:
                _face = Wrap(_face + delta, Fo1ProceduralPortrait.FaceShapes.Count);
                break;
            case 1:
                _hair = Wrap(_hair + delta, Fo1ProceduralPortrait.HairStyles.Count);
                break;
            case 2:
                _skin = Wrap(_skin + delta, Fo1ProceduralPortrait.SkinTones.Count);
                break;
            case 3:
                _hairColor = Wrap(
                    _hairColor + delta, Fo1ProceduralPortrait.HairColors.Count);
                break;
            case 4:
                _eyeColor = Wrap(_eyeColor + delta, Fo1ProceduralPortrait.EyeColors.Count);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row));
        }
        Refresh();
    }

    private void TogglePreview()
    {
        _head.Visible = !_head.Visible;
        _portrait.Visible = !_head.Visible;
        _previewMode.Text = _head.Visible ? "PORTRAIT" : "LIVE 3D";
    }

    private void Commit() => Confirmed?.Invoke(Selection);

    private void Refresh()
    {
        var selection = Selection;
        _portrait.Texture = ImageTexture.CreateFromImage(Fo1ProceduralPortrait.Render(
            _sex,
            selection.FaceShapeId,
            selection.HairStyleId,
            selection.SkinToneId,
            selection.HairColorId,
            selection.EyeColorId));
        _head.SetIdentity(
            _sex,
            selection.FaceShapeId,
            selection.HairStyleId,
            selection.SkinToneId,
            selection.HairColorId,
            selection.EyeColorId);
        _labels[0].Text = selection.FaceShapeId.ToUpperInvariant();
        _labels[1].Text = selection.HairStyleId.ToUpperInvariant();
        _labels[2].Text = selection.SkinToneId.ToUpperInvariant();
        _labels[3].Text = selection.HairColorId.ToUpperInvariant();
        _labels[4].Text = selection.EyeColorId.ToUpperInvariant();
        SetMeta("face_shape_id", selection.FaceShapeId);
        SetMeta("hair_style_id", selection.HairStyleId);
        SetMeta("skin_tone_id", selection.SkinToneId);
        SetMeta("hair_color_id", selection.HairColorId);
        SetMeta("eye_color_id", selection.EyeColorId);
        SetMeta("recipe_sha256", Fo1ProceduralAppearanceCatalog.Load().Sha256);
    }

    private Label Text(
        string value,
        float x,
        float y,
        float width,
        float height,
        int size,
        Color color)
    {
        var label = new Label
        {
            Text = value,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeFontSizeOverride("font_size", size);
        AddChild(label);
        return label;
    }

    private Button Button(
        string value,
        float x,
        float y,
        float width,
        float height,
        Action pressed)
    {
        var button = new Button
        {
            Text = value,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Flat = true,
            FocusMode = FocusModeEnum.None,
        };
        button.AddThemeColorOverride("font_color", Green);
        button.AddThemeColorOverride("font_hover_color", Amber);
        button.AddThemeFontSizeOverride(
            "font_size",
            Fo1CustomAppearanceEditorNumericContracts.ButtonFontSize);
        button.Pressed += pressed;
        AddChild(button);
        return button;
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;

    private static int Index(IReadOnlyList<string> rows, string id)
    {
        var index = rows.ToList().IndexOf(id);
        return index >= 0
            ? index
            : throw new InvalidOperationException(
                $"Fallout 1 custom appearance selection is unsupported: {id}");
    }
}
