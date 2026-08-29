using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed partial class Fo2CustomCharacterEditor : Control
{
    private const float SourceWidth = 640.0f;
    private const float SourceHeight = 480.0f;
    private const float PortraitX = 78.0f;
    private const float PortraitY = 34.0f;
    private const float PortraitSize = 150.0f;
    private const float FaceControlY = 186.0f;
    private const float HairControlY = 208.0f;
    private const float SkinControlY = 230.0f;
    private const float HairColorControlY = 252.0f;
    private const float EyeColorControlY = 274.0f;
    private const float PreviewToggleX = 106.0f;
    private const float PreviewToggleY = 7.0f;
    private const float PreviewToggleWidth = 94.0f;
    private const float PreviewToggleHeight = 22.0f;
    private const float FaceButtonSize = 20.0f;
    private const float FaceLabelX = 99.0f;
    private const float FaceLabelWidth = 108.0f;
    private const int FaceLabelFontSize = 8;
    private static readonly string[] SpecialNames = ["ST", "PE", "EN", "CH", "IN", "AG", "LK"];
    private readonly Fo2CharacterStartCatalog _catalog;
    private readonly Fo2PremadeCharacter _source;
    private readonly bool _modify;
    private readonly Control _canvas;
    private readonly LineEdit _name;
    private readonly TextureRect _portrait;
    private readonly Fo2ProceduralHeadPreview _headPreview;
    private readonly Button _previewToggle;
    private readonly Label _faceShape;
    private readonly Label _hairStyle;
    private readonly Label _skinTone;
    private readonly Label _hairColor;
    private readonly Label _eyeColor;
    private readonly Button _sex;
    private readonly Label _age;
    private readonly Label[] _specialValues = new Label[7];
    private readonly Label _allocation;
    private readonly Label _policy;
    private readonly Button _confirm;
    private readonly int[] _special;
    private int _ageValue;
    private int _faceShapeIndex;
    private int _hairStyleIndex;
    private int _skinToneIndex;
    private int _hairColorIndex;
    private int _eyeColorIndex;
    private string _sexValue;

    internal Fo2CustomCharacterEditor(
        Fo2CharacterStartCatalog catalog,
        Fo2PremadeCharacter source,
        bool modify)
    {
        _catalog = catalog;
        _source = source;
        _modify = modify;
        _ageValue = source.Profile.Age;
        _sexValue = source.Profile.Sex;
        _special = source.Profile.Special.ToArray();
        var appearance = Fo2ProceduralAppearanceCatalog.Load();
        _faceShapeIndex = Fo2ProceduralPortrait.ShapeIndex(appearance.DefaultFaceShapeId);
        _hairStyleIndex = Fo2ProceduralPortrait.HairStyleIndex(appearance.DefaultHairStyleId);
        _skinToneIndex = Fo2ProceduralPortrait.SkinToneIndex(appearance.DefaultSkinToneId);
        _hairColorIndex = Fo2ProceduralPortrait.HairColorIndex(appearance.DefaultHairColorId);
        _eyeColorIndex = Fo2ProceduralPortrait.EyeColorIndex(appearance.DefaultEyeColorId);
        Name = modify
            ? "FALLOUT_2_MODIFY_OWNED_CHARACTER"
            : "FALLOUT_2_CREATE_CUSTOM_CHARACTER";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = MouseFilterEnum.Ignore,
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.FullRect,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
        });
        _canvas = new Control
        {
            Name = "OwnedSource640x480CustomCanvas",
            Size = new Vector2(SourceWidth, SourceHeight),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(_canvas);
        _canvas.AddChild(new TextureRect
        {
            Name = "OwnedPickcharCustomBackground",
            Texture = catalog.Picker.Load(),
            Size = new Vector2(SourceWidth, SourceHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _canvas.AddChild(new TextureRect
        {
            Name = "OwnedCustomSourcePanel",
            Texture = source.Panel.Load(),
            Position = new Vector2(24.0f, 20.0f),
            Size = new Vector2(592.0f, 260.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _canvas.AddChild(new ColorRect
        {
            Name = "CustomRulesReadabilityBacking",
            Position = new Vector2(292.0f, 27.0f),
            Size = new Vector2(309.0f, 240.0f),
            Color = new Color(0.0f, 0.0f, 0.0f, 0.78f),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _portrait = new TextureRect
        {
            Name = "OpenNvLocalClassicGreenPortraitPreview",
            Position = new Vector2(PortraitX, PortraitY),
            Size = new Vector2(PortraitSize, PortraitSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_portrait);
        _headPreview = new Fo2ProceduralHeadPreview
        {
            Position = _portrait.Position,
            Size = _portrait.Size,
        };
        _canvas.AddChild(_headPreview);
        _previewToggle = AddButton(
            "LIVE 3D",
            PreviewToggleX,
            PreviewToggleY,
            PreviewToggleWidth,
            PreviewToggleHeight,
            TogglePreviewMode,
            FaceLabelFontSize);
        _previewToggle.TooltipText =
            "Toggle the local procedural portrait and matching live 3D head";
        AddButton(
            "◀",
            PortraitX,
            FaceControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetFaceShapeIndex(_faceShapeIndex - 1),
            FaceLabelFontSize);
        _faceShape = AddText(
            "",
            FaceLabelX,
            FaceControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            PortraitX + PortraitSize - FaceButtonSize,
            FaceControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetFaceShapeIndex(_faceShapeIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            PortraitX,
            HairControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetHairStyleIndex(_hairStyleIndex - 1),
            FaceLabelFontSize);
        _hairStyle = AddText(
            "",
            FaceLabelX,
            HairControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            PortraitX + PortraitSize - FaceButtonSize,
            HairControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetHairStyleIndex(_hairStyleIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            PortraitX,
            SkinControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetSkinToneIndex(_skinToneIndex - 1),
            FaceLabelFontSize);
        _skinTone = AddText(
            "",
            FaceLabelX,
            SkinControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            PortraitX + PortraitSize - FaceButtonSize,
            SkinControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetSkinToneIndex(_skinToneIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            PortraitX,
            HairColorControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetHairColorIndex(_hairColorIndex - 1),
            FaceLabelFontSize);
        _hairColor = AddText(
            "",
            FaceLabelX,
            HairColorControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            PortraitX + PortraitSize - FaceButtonSize,
            HairColorControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetHairColorIndex(_hairColorIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            PortraitX,
            EyeColorControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetEyeColorIndex(_eyeColorIndex - 1),
            FaceLabelFontSize);
        _eyeColor = AddText(
            "",
            FaceLabelX,
            EyeColorControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            PortraitX + PortraitSize - FaceButtonSize,
            EyeColorControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetEyeColorIndex(_eyeColorIndex + 1),
            FaceLabelFontSize);
        AddText(
            modify ? "MODIFY CHOSEN ONE" : "CREATE CHOSEN ONE",
            304.0f,
            32.0f,
            285.0f,
            20.0f,
            13);
        AddText("NAME", 304.0f, 57.0f, 50.0f, 20.0f, 10);
        _name = new LineEdit
        {
            Name = "ChosenOneName",
            Position = new Vector2(354.0f, 52.0f),
            Size = new Vector2(218.0f, 25.0f),
            MaxLength = 11,
            Text = modify ? source.Profile.Name : "",
            PlaceholderText = "1-11 characters",
            CaretBlink = true,
        };
        _name.AddThemeColorOverride("font_color", new Color("78e781"));
        _name.AddThemeColorOverride("font_placeholder_color", new Color("477e4b"));
        _name.AddThemeFontSizeOverride("font_size", 11);
        _name.TextChanged += _ => Refresh();
        _canvas.AddChild(_name);

        AddText("SEX", 304.0f, 83.0f, 50.0f, 20.0f, 10);
        _sex = AddButton("", 354.0f, 79.0f, 90.0f, 24.0f, ToggleSex, 11);
        AddText("AGE", 454.0f, 83.0f, 40.0f, 20.0f, 10);
        AddButton("−", 493.0f, 79.0f, 24.0f, 24.0f, () => SetAge(_ageValue - 1), 13);
        _age = AddText("", 518.0f, 82.0f, 28.0f, 20.0f, 11);
        AddButton("+", 548.0f, 79.0f, 24.0f, 24.0f, () => SetAge(_ageValue + 1), 13);

        AddText("SPECIAL", 304.0f, 110.0f, 80.0f, 20.0f, 11);
        for (var index = 0; index < SpecialNames.Length; index++)
        {
            var column = index < 4 ? 0 : 1;
            var row = column == 0 ? index : index - 4;
            var x = 304.0f + column * 142.0f;
            var y = 134.0f + row * 25.0f;
            AddText(SpecialNames[index], x, y + 3.0f, 24.0f, 20.0f, 10);
            var captured = index;
            AddButton("−", x + 25.0f, y, 24.0f, 23.0f, () => AdjustSpecial(captured, -1), 12);
            _specialValues[index] = AddText("", x + 51.0f, y + 3.0f, 24.0f, 20.0f, 11);
            AddButton("+", x + 76.0f, y, 24.0f, 23.0f, () => AdjustSpecial(captured, 1), 12);
        }
        _allocation = AddText("", 446.0f, 211.0f, 135.0f, 20.0f, 10);
        _policy = AddText("", 304.0f, 238.0f, 285.0f, 24.0f, 8);
        _policy.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _confirm = AddButton("", 65.0f, 301.0f, 181.0f, 79.0f, Confirm, 12);
        _confirm.TooltipText = "Take this custom Chosen One into Arroyo";
        AddButton("", 443.0f, 397.0f, 153.0f, 63.0f, Cancel, 12)
            .TooltipText = "Cancel custom character editing";
        AddText(
            "TAKE = CONFIRM   •   BACK = CANCEL",
            190.0f,
            279.0f,
            260.0f,
            22.0f,
            10,
            HorizontalAlignment.Center);
        Refresh();
    }

    internal event Action<Fo2CharacterSelection>? Confirmed;
    internal event Action? Cancelled;
    internal bool IsModify => _modify;
    internal string CharacterName => _name.Text.Trim();
    internal string Sex => _sexValue;
    internal int Age => _ageValue;
    internal string FaceShapeId => Fo2ProceduralPortrait.Shapes[_faceShapeIndex];
    internal string HairStyleId => Fo2ProceduralPortrait.HairStyles[_hairStyleIndex];
    internal string SkinToneId => Fo2ProceduralPortrait.SkinTones[_skinToneIndex];
    internal string HairColorId => Fo2ProceduralPortrait.HairColors[_hairColorIndex];
    internal string EyeColorId => Fo2ProceduralPortrait.EyeColors[_eyeColorIndex];
    internal bool Live3DVisible => _headPreview.Visible;
    internal Fo2ProceduralHeadPreview HeadPreview => _headPreview;
    internal IReadOnlyList<int> Special => _special;
    internal int AllocatedSpecial => _special.Sum();
    internal bool CanConfirm =>
        CharacterName.Length is >= 1 and <= 11 && AllocatedSpecial == 40;

    public override void _Ready()
    {
        FitCanvas();
        _name.GrabFocus();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && IsInsideTree())
            FitCanvas();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    internal void SetCharacterName(string value)
    {
        _name.Text = value.Length <= _name.MaxLength
            ? value
            : value[.._name.MaxLength];
        Refresh();
    }

    internal void SetSex(string value)
    {
        if (value is not "Male" and not "Female")
            throw new ArgumentOutOfRangeException(nameof(value));
        _sexValue = value;
        Refresh();
    }

    internal void SetFaceShape(string value)
    {
        var index = Fo2ProceduralPortrait.ShapeIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetFaceShapeIndex(index);
    }

    internal void SetHairStyle(string value)
    {
        var index = Fo2ProceduralPortrait.HairStyleIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetHairStyleIndex(index);
    }

    internal void SetSkinTone(string value)
    {
        var index = Fo2ProceduralPortrait.SkinToneIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetSkinToneIndex(index);
    }

    internal void SetHairColor(string value)
    {
        var index = Fo2ProceduralPortrait.HairColorIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetHairColorIndex(index);
    }

    internal void SetEyeColor(string value)
    {
        var index = Fo2ProceduralPortrait.EyeColorIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetEyeColorIndex(index);
    }

    internal void TogglePreviewMode()
    {
        _headPreview.Visible = !_headPreview.Visible;
        _portrait.Visible = !_headPreview.Visible;
        _previewToggle.Text = _headPreview.Visible ? "PORTRAIT" : "LIVE 3D";
        SetMeta("custom_preview_mode", _headPreview.Visible
            ? "local-procedural-live-3d-head"
            : Fo2CharacterAppearanceContract.GeneratedPortraitPreview);
    }

    internal void SetAge(int value)
    {
        _ageValue = Math.Clamp(value, 16, 35);
        Refresh();
    }

    internal void SetSpecial(IReadOnlyList<int> values)
    {
        if (values.Count != 7 || values.Any(value => value is < 1 or > 10) ||
            values.Sum() != 40)
            throw new ArgumentException(
                "Fallout 2 custom SPECIAL must contain seven values from 1-10 totaling 40.",
                nameof(values));
        for (var index = 0; index < _special.Length; index++)
            _special[index] = values[index];
        Refresh();
    }

    internal Fo2CharacterSelection BuildSelection()
    {
        var profile = new Fo2CharacterProfile(
            CharacterName,
            _ageValue,
            _sexValue,
            _special.ToArray(),
            _modify ? _source.Profile.TaggedSkills.ToArray() : Array.Empty<string>(),
            _modify ? _source.Profile.Traits.ToArray() : Array.Empty<string>());
        var provisional = new Fo2CharacterSelection(
            _modify ? Fo2CharacterSelection.ModifyMode : Fo2CharacterSelection.CreateMode,
            _source,
            profile);
        var selection = provisional with
        {
            AppearanceState = Fo2ProceduralPortrait.Commit(
                _source,
                profile.Sex,
                FaceShapeId,
                HairStyleId,
                SkinToneId,
                HairColorId,
                EyeColorId),
        };
        selection.Validate(_catalog);
        return selection;
    }

    internal void Confirm()
    {
        if (!CanConfirm)
        {
            Refresh();
            return;
        }
        Confirmed?.Invoke(BuildSelection());
    }

    internal void Cancel() => Cancelled?.Invoke();

    private void ToggleSex() => SetSex(_sexValue == "Male" ? "Female" : "Male");

    private void SetFaceShapeIndex(int index)
    {
        var count = Fo2ProceduralPortrait.Shapes.Count;
        _faceShapeIndex = (index % count + count) % count;
        Refresh();
    }

    private void SetHairStyleIndex(int index)
    {
        var count = Fo2ProceduralPortrait.HairStyles.Count;
        _hairStyleIndex = (index % count + count) % count;
        Refresh();
    }

    private void SetSkinToneIndex(int index)
    {
        var count = Fo2ProceduralPortrait.SkinTones.Count;
        _skinToneIndex = (index % count + count) % count;
        Refresh();
    }

    private void SetHairColorIndex(int index)
    {
        var count = Fo2ProceduralPortrait.HairColors.Count;
        _hairColorIndex = (index % count + count) % count;
        Refresh();
    }

    private void SetEyeColorIndex(int index)
    {
        var count = Fo2ProceduralPortrait.EyeColors.Count;
        _eyeColorIndex = (index % count + count) % count;
        Refresh();
    }

    private void AdjustSpecial(int index, int delta)
    {
        if (index is < 0 or >= 7 || delta is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(index));
        var next = _special[index] + delta;
        if (next is < 1 or > 10 || delta > 0 && AllocatedSpecial >= 40)
            return;
        _special[index] = next;
        Refresh();
    }

    private void Refresh()
    {
        _sex.Text = _sexValue.ToUpperInvariant();
        _faceShape.Text = $"FACE: {FaceShapeId.ToUpperInvariant()}";
        _hairStyle.Text = $"HAIR: {HairStyleId.ToUpperInvariant()}";
        _skinTone.Text = $"SKIN: {SkinToneId.ToUpperInvariant()}";
        _hairColor.Text = $"HAIR COLOR: {HairColorId.ToUpperInvariant()}";
        _eyeColor.Text = $"EYE COLOR: {EyeColorId.ToUpperInvariant()}";
        _portrait.Texture = ImageTexture.CreateFromImage(
            Fo2ProceduralPortrait.Render(
                _sexValue,
                FaceShapeId,
                HairStyleId,
                SkinToneId,
                HairColorId,
                EyeColorId));
        _headPreview.SetIdentity(
            _sexValue,
            FaceShapeId,
            HairStyleId,
            SkinToneId,
            HairColorId,
            EyeColorId);
        _age.Text = _ageValue.ToString();
        for (var index = 0; index < _special.Length; index++)
            _specialValues[index].Text = _special[index].ToString("00");
        _allocation.Text = $"ALLOCATED {AllocatedSpecial}/40";
        _allocation.AddThemeColorOverride(
            "font_color",
            AllocatedSpecial == 40 ? new Color("78e781") : new Color("e6c34c"));
        _policy.Text = _modify
            ? "TAG SKILLS / TRAITS: SOURCE VALUES UNCHANGED"
            : "TAG SKILLS / TRAITS: UNSELECTED IN THIS BOUNDED FLOW";
        _confirm.Disabled = !CanConfirm;
        _confirm.MouseDefaultCursorShape = CanConfirm
            ? CursorShape.PointingHand
            : CursorShape.Forbidden;
        SetMeta("custom_name", CharacterName);
        SetMeta("custom_sex", _sexValue);
        SetMeta("custom_age", _ageValue);
        SetMeta("custom_face_shape", FaceShapeId);
        SetMeta("custom_hair_style", HairStyleId);
        SetMeta("custom_skin_tone", SkinToneId);
        SetMeta("custom_hair_color", HairColorId);
        SetMeta("custom_eye_color", EyeColorId);
        SetMeta("custom_appearance_recipe_sha256", Fo2ProceduralAppearanceCatalog.Load().Sha256);
        SetMeta("custom_portrait_generator", Fo2ProceduralPortrait.GeneratorId);
        SetMeta("custom_special", string.Join(",", _special));
        SetMeta("custom_special_total", AllocatedSpecial);
        SetMeta("custom_tags_traits_policy", _modify ? "source-unchanged" : "unselected");
    }

    private Label AddText(
        string text,
        float x,
        float y,
        float width,
        float height,
        int fontSize,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", new Color("78e781"));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _canvas.AddChild(label);
        return label;
    }

    private Button AddButton(
        string text,
        float x,
        float y,
        float width,
        float height,
        Action pressed,
        int fontSize)
    {
        var button = new Button
        {
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Text = text,
            Flat = true,
            FocusMode = FocusModeEnum.None,
        };
        button.AddThemeColorOverride("font_color", new Color("78e781"));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.Pressed += pressed;
        _canvas.AddChild(button);
        return button;
    }

    private void FitCanvas()
    {
        var size = GetViewportRect().Size;
        var scale = MathF.Min(size.X / SourceWidth, size.Y / SourceHeight);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position = (size - new Vector2(SourceWidth, SourceHeight) * scale) / 2.0f;
    }
}
