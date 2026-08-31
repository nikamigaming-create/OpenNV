using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;


using OpenNV.Runtime.World.Cells;

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
    private readonly Fo1CustomPortraitPreview _portrait;
    private readonly Control _controlRoot;
    private readonly OpeningRaceSexRenderedDeviceHost _reflectron;
    private readonly Label[] _labels =
        new Label[Fo1CustomAppearanceEditorNumericContracts.FeatureCount];
    private int _face;
    private int _hair;
    private int _skin;
    private int _hairColor;
    private int _eyeColor;
    private bool _faceFraming = true;
    private bool _greenProjection;

    internal Fo1CustomAppearanceEditor(
        string sex,
        IReadOnlyDictionary<string, Fo1HexSceneLoader.PlayerPresentationSource> donors,
        OpeningManifest characterReflectron,
        Fo1CustomAppearanceSelection? current = null)
    {
        _sex = sex;
        if (!donors.TryGetValue(sex, out var portraitDonor))
            throw new InvalidOperationException(
                $"Fallout 1 custom portrait requires its {sex} owned donor.");
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
        var deviceCanvas = new Control
        {
            Name = "FO1_SHARED_REFLECTRON_1600X1200",
            Size = characterReflectron.NewGameFlow.ReferenceCanvasSize,
            Scale = new Vector2(
                Size.X / characterReflectron.NewGameFlow.ReferenceCanvasSize.X,
                Size.Y / characterReflectron.NewGameFlow.ReferenceCanvasSize.Y),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(deviceCanvas);
        var renderedDevice = characterReflectron.NewGameFlow.Menus.Values
            .Select(menu => menu.RenderedDevice)
            .SingleOrDefault(device => device is not null)
            ?? throw new InvalidOperationException(
                "The shared owned opening manifest has no Reflectron device.");
        var configuration = RuntimeConfiguration.Load();
        _reflectron = new OpeningRaceSexRenderedDeviceHost(
            renderedDevice,
            deviceCanvas,
            characterReflectron.NewGameFlow.ReferenceCanvasSize,
            configuration,
            new CellContentLoader.LightingContract(
                "fo1-character-reflectron-2.0",
                new Color("74806f"),
                new Color("c6d1bb"),
                new Color("07100b"),
                0.0f,
                100000.0f,
                1.0f,
                new Vector2(-28.0f, -32.0f),
                1.0f,
                []),
            configuration.World.GameUnitsToMeters);
        var faceRoot = _reflectron.CreateFacePresentationHost();
        _controlRoot = _reflectron.CreateMenuPresentationHost(
            new Rect2(0.0f, 0.0f, 340.0f, 500.0f));
        var title = Text(
            "CUSTOM PORTRAIT",
            8.0f,
            4.0f,
            324.0f,
            30.0f,
            14,
            Amber);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _portrait = new Fo1CustomPortraitPreview(sex, portraitDonor)
        {
            Name = "FO1_HEX_CUSTOM_PORTRAIT",
            Size = faceRoot.Size,
        };
        faceRoot.AddChild(_portrait);

        var names = new[] { "FACE", "HAIR", "SKIN", "HAIR COLOR", "EYES" };
        for (var row = 0; row < names.Length; row++)
        {
            var captured = row;
            var y = 46.0f + row * 55.0f;
            Text(
                names[row],
                8.0f,
                y,
                92.0f,
                26.0f,
                10,
                Amber);
            Button(
                "◀",
                104.0f,
                y - 2,
                30.0f,
                30.0f,
                () => Change(captured, -1));
            _labels[row] = Text(
                "",
                138.0f,
                y,
                158.0f,
                26.0f,
                10,
                Green);
            _labels[row].HorizontalAlignment = HorizontalAlignment.Center;
            Button(
                "▶",
                300.0f,
                y - 2,
                30.0f,
                30.0f,
                () => Change(captured, 1));
        }
        Text(
            "OWNED DONOR • SHARED REFLECTRON",
            8.0f,
            326.0f,
            324.0f,
            24.0f,
            9,
            Green).HorizontalAlignment = HorizontalAlignment.Center;
        Button(
            "USE FACE",
            32.0f,
            366.0f,
            124.0f,
            34.0f,
            Commit);
        Button(
            "BACK",
            184.0f,
            366.0f,
            124.0f,
            34.0f,
            () => Cancelled?.Invoke());
        void RefreshProjection()
        {
            _portrait.SetPreviewState(_faceFraming, _greenProjection);
            _reflectron.SetCreatorModeState(
                _faceFraming ? "FACE" : "BODY",
                bodyEnabled: !_faceFraming,
                projectionEnabled: _greenProjection,
                faceEnabled: _faceFraming);
        }
        _reflectron.ConfigureCharacterControls(
            characterReflectron.Font,
            () => { },
            () => { },
            () => { },
            () => { },
            () =>
            {
                _faceFraming = true;
                RefreshProjection();
            },
            () =>
            {
                _faceFraming = false;
                RefreshProjection();
            },
            () =>
            {
                _greenProjection = !_greenProjection;
                RefreshProjection();
            });
        RefreshProjection();
        Refresh();
    }

    internal event Action<Fo1CustomAppearanceSelection>? Confirmed;
    internal event Action? Cancelled;
    internal bool Live3DVisible => !_greenProjection;
    internal bool GreenPortraitReady => _portrait.ReadyForCapture;
    internal string PortraitSourceActorFormId => _portrait.SourceActorFormId;
    internal Image CapturePortrait() => _portrait.CapturePortrait();

    internal void SetSelection(Fo1CustomAppearanceSelection selection)
    {
        _face = Index(Fo1ProceduralPortrait.FaceShapes, selection.FaceShapeId);
        _hair = Index(Fo1ProceduralPortrait.HairStyles, selection.HairStyleId);
        _skin = Index(Fo1ProceduralPortrait.SkinTones, selection.SkinToneId);
        _hairColor = Index(Fo1ProceduralPortrait.HairColors, selection.HairColorId);
        _eyeColor = Index(Fo1ProceduralPortrait.EyeColors, selection.EyeColorId);
        Refresh();
    }

    internal void ActivateReflectronFaceControl() =>
        _reflectron.ActivateCreatorModeControl("FACE");
    internal void ActivateReflectronBodyControl() =>
        _reflectron.ActivateCreatorModeControl("BODY");
    internal void TogglePreviewMode() =>
        _reflectron.ActivateCreatorModeControl("PROJECTION");
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

    private void Commit() => Confirmed?.Invoke(Selection);

    private void Refresh()
    {
        var selection = Selection;
        _portrait.SetSelection(selection);
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
        _controlRoot.AddChild(label);
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
        _controlRoot.AddChild(button);
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
