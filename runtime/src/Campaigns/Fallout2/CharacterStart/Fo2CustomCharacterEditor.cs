using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed partial class Fo2CustomCharacterEditor : Control
{
    private const float SourceWidth = 1600.0f;
    private const float SourceHeight = 1200.0f;
    private const float ControlDesignWidth = 300.0f;
    private const float ControlDesignHeight = 360.0f;
    private const float ControlX = 8.0f;
    private const float ControlWidth = 284.0f;
    private const float FaceControlY = 46.0f;
    private const float HairControlY = 78.0f;
    private const float SkinControlY = 110.0f;
    private const float HairColorControlY = 142.0f;
    private const float EyeColorControlY = 174.0f;
    private const float BrowControlY = 206.0f;
    private const float NoseControlY = 238.0f;
    private const float MouthControlY = 270.0f;
    private const float PreviewToggleX = 0.0f;
    private const float PreviewToggleY = 0.0f;
    private const float PreviewToggleWidth = 94.0f;
    private const float PreviewToggleHeight = 22.0f;
    private const float FaceButtonSize = 24.0f;
    private const float FaceLabelX = 34.0f;
    private const float FaceLabelWidth = 232.0f;
    private const int FaceLabelFontSize = 11;
    private static readonly string[] SpecialNames = ["ST", "PE", "EN", "CH", "IN", "AG", "LK"];
    private readonly Fo2CharacterStartCatalog _catalog;
    private readonly Fo2PremadeCharacter _source;
    private readonly Control _canvas;
    private readonly Control _controlRoot;
    private readonly OpeningRaceSexRenderedDeviceHost _reflectron;
    private readonly LineEdit _name;
    private readonly TextureRect _portrait;
    private readonly TextureRect _sourcePanel;
    private readonly Fo2HumanoidDonorContract _humanoidDonor;
    private readonly Fo2PremadeHumanoidPreview _livePreview;
    private readonly IReadOnlyList<Control> _appearanceControls;
    private readonly IReadOnlyList<Control> _faceControls;
    private readonly IReadOnlyList<Control> _hairControls;
    private readonly IReadOnlyList<Control> _raceControls;
    private readonly IReadOnlyList<Control> _statsControls;
    private readonly Button _previewToggle;
    private readonly Button _bodyToggle;
    private readonly Control _bodyPanel;
    private readonly Control _rulesPanel;
    private readonly Label[] _tagSkillValues = new Label[3];
    private readonly Label[] _traitValues = new Label[2];
    private readonly Label _faceShape;
    private readonly Label _hairStyle;
    private readonly Label _skinTone;
    private readonly Label _hairColor;
    private readonly Label _eyeColor;
    private readonly Label _browStyle;
    private readonly Label _noseStyle;
    private readonly Label _mouthStyle;
    private readonly Button _sex;
    private readonly Label _age;
    private readonly Label[] _specialValues = new Label[7];
    private readonly Label _allocation;
    private readonly Button _rules;
    private readonly Button _confirm;
    private readonly int[] _special;
    private readonly List<string> _taggedSkills;
    private readonly List<string> _traits;
    private int _ageValue;
    private int _faceShapeIndex;
    private int _hairStyleIndex;
    private int _skinToneIndex;
    private int _hairColorIndex;
    private int _eyeColorIndex;
    private int _browStyleIndex;
    private int _noseStyleIndex;
    private int _mouthStyleIndex;
    private CharacterBodyProportions _bodyProportions;
    private string _sexValue;
    private bool _classicProjectionEnabled;
    private bool _facePreviewEnabled;

    internal Fo2CustomCharacterEditor(
        Fo2CharacterStartCatalog catalog,
        Fo2PremadeCharacter source,
        Fo2HumanoidDonorContract humanoidDonor,
        OpeningManifest characterReflectron)
    {
        _catalog = catalog;
        _source = source;
        _humanoidDonor = humanoidDonor;
        _ = _humanoidDonor.ForSex(source.Profile.Sex);
        _ageValue = source.Profile.Age;
        _sexValue = source.Profile.Sex;
        _bodyProportions = Fo2CharacterBodyProfile.ForSex(_sexValue);
        _special = source.Profile.Special.ToArray();
        _taggedSkills = Fo2CharacterStartCatalog.SkillNames.Take(3).ToList();
        _traits = [];
        var appearance = Fo2ProceduralAppearanceCatalog.Load();
        _faceShapeIndex = Fo2ProceduralPortrait.ShapeIndex(appearance.DefaultFaceShapeId);
        _hairStyleIndex = Fo2ProceduralPortrait.HairStyleIndex(appearance.DefaultHairStyleId);
        _skinToneIndex = Fo2ProceduralPortrait.SkinToneIndex(appearance.DefaultSkinToneId);
        _hairColorIndex = Fo2ProceduralPortrait.HairColorIndex(appearance.DefaultHairColorId);
        _eyeColorIndex = Fo2ProceduralPortrait.EyeColorIndex(appearance.DefaultEyeColorId);
        _browStyleIndex = Fo2ProceduralPortrait.BrowStyleIndex(appearance.DefaultBrowStyleId);
        _noseStyleIndex = Fo2ProceduralPortrait.NoseStyleIndex(appearance.DefaultNoseStyleId);
        _mouthStyleIndex = Fo2ProceduralPortrait.MouthStyleIndex(appearance.DefaultMouthStyleId);
        Name = "FALLOUT_2_CREATE_CUSTOM_CHARACTER";
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
            Name = "OwnedSourceReflectron1280x720CustomCanvas",
            Size = new Vector2(SourceWidth, SourceHeight),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(_canvas);
        if (!characterReflectron.NewGameFlow.ReferenceCanvasSize.IsEqualApprox(
                new Vector2(SourceWidth, SourceHeight)))
            throw new InvalidOperationException(
                "Fallout 2 Reflectron workbench requires the source 1600x1200 device canvas.");
        var renderedDevice = characterReflectron.NewGameFlow.Menus.Values
            .Select(menu => menu.RenderedDevice)
            .SingleOrDefault(device => device is not null)
            ?? throw new InvalidOperationException(
                "The locally exported opening manifest has no owned Reflectron device.");
        var configuration = RuntimeConfiguration.Load();
        _reflectron = new OpeningRaceSexRenderedDeviceHost(
            renderedDevice,
            _canvas,
            characterReflectron.NewGameFlow.ReferenceCanvasSize,
            configuration,
            new CellContentLoader.LightingContract(
                "fo2-character-reflectron-2.0",
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
            new Rect2(0.0f, 0.0f, ControlDesignWidth, ControlDesignHeight));
        _sourcePanel = new TextureRect
        {
            Name = "OwnedPremadePanelReflectronPortraitBasis",
            Texture = source.Panel.Load(),
            Size = faceRoot.Size,
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        faceRoot.AddChild(_sourcePanel);
        _portrait = new TextureRect
        {
            Name = "OpenNvLocalClassicGreenPortraitPreview",
            Size = faceRoot.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        faceRoot.AddChild(_portrait);
        _livePreview = new Fo2PremadeHumanoidPreview(
            source,
            catalog,
            humanoidDonor,
            useCustomDonor: true)
        {
            Size = faceRoot.Size,
            Visible = false,
        };
        _livePreview.SetCompositionRightOffset(
            Fo2PremadeHumanoidPreview.EditorColumnCompositionRightOffset);
        faceRoot.AddChild(_livePreview);
        _previewToggle = AddButton(
            "LIVE 3D",
            PreviewToggleX,
            PreviewToggleY,
            PreviewToggleWidth,
            PreviewToggleHeight,
            TogglePreviewMode,
            9);
        _previewToggle.Visible = false;
        _bodyToggle = AddButton(
            "BODY",
            PreviewToggleX + PreviewToggleWidth + 4.0f,
            PreviewToggleY,
            62.0f,
            PreviewToggleHeight,
            ToggleBodyEditor,
            9);
        _bodyToggle.Visible = false;
        var appearanceControlStart = _controlRoot.GetChildCount();
        AddButton(
            "◀",
            ControlX,
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
            ControlX + ControlWidth - FaceButtonSize,
            FaceControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetFaceShapeIndex(_faceShapeIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
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
            ControlX + ControlWidth - FaceButtonSize,
            HairControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetHairStyleIndex(_hairStyleIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
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
            ControlX + ControlWidth - FaceButtonSize,
            SkinControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetSkinToneIndex(_skinToneIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
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
            ControlX + ControlWidth - FaceButtonSize,
            HairColorControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetHairColorIndex(_hairColorIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
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
            ControlX + ControlWidth - FaceButtonSize,
            EyeColorControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetEyeColorIndex(_eyeColorIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
            BrowControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetBrowStyleIndex(_browStyleIndex - 1),
            FaceLabelFontSize);
        _browStyle = AddText(
            "",
            FaceLabelX,
            BrowControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            ControlX + ControlWidth - FaceButtonSize,
            BrowControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetBrowStyleIndex(_browStyleIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
            NoseControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetNoseStyleIndex(_noseStyleIndex - 1),
            FaceLabelFontSize);
        _noseStyle = AddText(
            "",
            FaceLabelX,
            NoseControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            ControlX + ControlWidth - FaceButtonSize,
            NoseControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetNoseStyleIndex(_noseStyleIndex + 1),
            FaceLabelFontSize);
        AddButton(
            "◀",
            ControlX,
            MouthControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetMouthStyleIndex(_mouthStyleIndex - 1),
            FaceLabelFontSize);
        _mouthStyle = AddText(
            "",
            FaceLabelX,
            MouthControlY,
            FaceLabelWidth,
            FaceButtonSize,
            FaceLabelFontSize,
            HorizontalAlignment.Center);
        AddButton(
            "▶",
            ControlX + ControlWidth - FaceButtonSize,
            MouthControlY,
            FaceButtonSize,
            FaceButtonSize,
            () => SetMouthStyleIndex(_mouthStyleIndex + 1),
            FaceLabelFontSize);
        _appearanceControls = _controlRoot.GetChildren()
            .Skip(appearanceControlStart)
            .OfType<Control>()
            .ToArray();
        if (_appearanceControls.Count != 24)
            throw new InvalidOperationException(
                "Fallout 2 appearance editor requires exactly eight three-part controls.");
        _faceControls =
        [
            .. _appearanceControls.Take(3),
            .. _appearanceControls.Skip(12),
        ];
        _hairControls =
        [
            .. _appearanceControls.Skip(3).Take(3),
            .. _appearanceControls.Skip(9).Take(3),
        ];
        _raceControls = _appearanceControls.Skip(6).Take(3).ToArray();
        if (_faceControls.Count != 15 || _hairControls.Count != 6 ||
            _raceControls.Count != 3)
            throw new InvalidOperationException(
                "Fallout 2 Reflectron source sections do not cover every appearance control.");
        var statsControlStart = _controlRoot.GetChildCount();
        AddText(
            "CREATE CHOSEN ONE",
            10.0f,
            8.0f,
            280.0f,
            24.0f,
            13);
        AddText("NAME", 10.0f, 42.0f, 48.0f, 24.0f, 10);
        _name = new LineEdit
        {
            Name = "ChosenOneName",
            Position = new Vector2(60.0f, 39.0f),
            Size = new Vector2(230.0f, 28.0f),
            MaxLength = 11,
            Text = "",
            PlaceholderText = "1-11 characters",
            CaretBlink = true,
        };
        _name.AddThemeColorOverride("font_color", new Color("78e781"));
        _name.AddThemeColorOverride("font_placeholder_color", new Color("477e4b"));
        _name.AddThemeFontSizeOverride("font_size", 11);
        _name.TextChanged += _ => Refresh();
        _controlRoot.AddChild(_name);

        AddText("SEX", 10.0f, 77.0f, 48.0f, 24.0f, 10);
        _sex = AddButton("", 60.0f, 74.0f, 92.0f, 28.0f, ToggleSex, 11);
        AddText("AGE", 162.0f, 77.0f, 38.0f, 24.0f, 10);
        AddButton("−", 202.0f, 74.0f, 26.0f, 28.0f, () => SetAge(_ageValue - 1), 13);
        _age = AddText("", 230.0f, 77.0f, 30.0f, 24.0f, 11);
        AddButton("+", 262.0f, 74.0f, 28.0f, 28.0f, () => SetAge(_ageValue + 1), 13);

        AddText("SPECIAL", 10.0f, 112.0f, 80.0f, 24.0f, 11);
        for (var index = 0; index < SpecialNames.Length; index++)
        {
            var column = index < 4 ? 0 : 1;
            var row = column == 0 ? index : index - 4;
            var x = 10.0f + column * 145.0f;
            var y = 140.0f + row * 32.0f;
            AddText(SpecialNames[index], x, y + 3.0f, 24.0f, 20.0f, 10);
            var captured = index;
            AddButton("−", x + 25.0f, y, 24.0f, 23.0f, () => AdjustSpecial(captured, -1), 12);
            _specialValues[index] = AddText("", x + 51.0f, y + 3.0f, 24.0f, 20.0f, 11);
            AddButton("+", x + 76.0f, y, 24.0f, 23.0f, () => AdjustSpecial(captured, 1), 12);
        }
        _allocation = AddText("", 155.0f, 246.0f, 135.0f, 22.0f, 10);
        _rules = AddButton(
            "",
            10.0f,
            276.0f,
            280.0f,
            32.0f,
            ShowRulesEditor,
            9);
        _rules.TooltipText = "Choose exactly three tagged skills and up to two traits";
        _statsControls = _controlRoot.GetChildren()
            .Skip(statsControlStart)
            .OfType<Control>()
            .ToArray();

        _confirm = AddButton("SAVE CHARACTER", 8.0f, 320.0f, 138.0f, 30.0f, Confirm, 11);
        _confirm.TooltipText = "Take this custom Chosen One into Arroyo";
        AddButton("BACK", 154.0f, 320.0f, 138.0f, 30.0f, Cancel, 11)
            .TooltipText = "Cancel custom character editing";
        _bodyPanel = BuildBodyPanel();
        _controlRoot.AddChild(_bodyPanel);
        _rulesPanel = BuildRulesPanel();
        _controlRoot.AddChild(_rulesPanel);
        SetAppearanceControlsVisible(false);
        _reflectron.ConfigureCharacterControls(
            characterReflectron.Font,
            ShowSexEditor,
            ShowRaceEditor,
            ShowFaceEditor,
            ShowHairEditor,
            ShowClassicPortrait,
            ToggleBodyEditor,
            ToggleClassicProjection);
        ApplyPreviewState();
        _reflectron.SetActiveList("sex");
        SetMeta("custom_editor_panel", "identity-and-special");
        Refresh();
    }

    internal event Action<Fo2CharacterSelection>? Confirmed;
    internal event Action? Cancelled;
    internal string CharacterName => _name.Text.Trim();
    internal string Sex => _sexValue;
    internal int Age => _ageValue;
    internal string FaceShapeId => Fo2ProceduralPortrait.Shapes[_faceShapeIndex];
    internal string HairStyleId => Fo2ProceduralPortrait.HairStyles[_hairStyleIndex];
    internal string SkinToneId => Fo2ProceduralPortrait.SkinTones[_skinToneIndex];
    internal string HairColorId => Fo2ProceduralPortrait.HairColors[_hairColorIndex];
    internal string EyeColorId => Fo2ProceduralPortrait.EyeColors[_eyeColorIndex];
    internal string BrowStyleId => Fo2ProceduralPortrait.BrowStyles[_browStyleIndex];
    internal string NoseStyleId => Fo2ProceduralPortrait.NoseStyles[_noseStyleIndex];
    internal string MouthStyleId => Fo2ProceduralPortrait.MouthStyles[_mouthStyleIndex];
    internal bool Live3DVisible => _livePreview.Visible && !_classicProjectionEnabled;
    internal bool ClassicProjectionVisible => _classicProjectionEnabled;
    internal bool FacePreviewVisible => _facePreviewEnabled;
    internal string ProjectionStyle => _livePreview.ProjectionStyle;
    internal bool BodyControlsVisible => _bodyPanel.Visible;
    internal bool AppearanceControlsVisible => _appearanceControls.Any(row => row.Visible);
    internal bool RulesControlsVisible => _rulesPanel.Visible;
    internal string ActiveEditorPanel => GetMeta("custom_editor_panel").AsString();
    internal Fo2PremadeHumanoidPreview LivePreview => _livePreview;
    internal CharacterBodyProportions BodyProportions => _bodyProportions;
    internal IReadOnlyList<int> Special => _special;
    internal IReadOnlyList<string> TaggedSkills => _taggedSkills;
    internal IReadOnlyList<string> Traits => _traits;
    internal int AllocatedSpecial => _special.Sum();
    internal bool CanConfirm =>
        CharacterName.Length is >= 1 and <= 11 && AllocatedSpecial == 40 &&
        _taggedSkills.Count == 3 &&
        _taggedSkills.Distinct(StringComparer.Ordinal).Count() == 3 &&
        _traits.Count <= 2 &&
        _traits.Distinct(StringComparer.Ordinal).Count() == _traits.Count;

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
        _ = _humanoidDonor.ForSex(value);
        _sexValue = value;
        _livePreview.SetSex(value);
        _livePreview.SetProportions(_bodyProportions);
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

    internal void SetBrowStyle(string value)
    {
        var index = Fo2ProceduralPortrait.BrowStyleIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetBrowStyleIndex(index);
    }

    internal void SetNoseStyle(string value)
    {
        var index = Fo2ProceduralPortrait.NoseStyleIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetNoseStyleIndex(index);
    }

    internal void SetMouthStyle(string value)
    {
        var index = Fo2ProceduralPortrait.MouthStyleIndex(value);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetMouthStyleIndex(index);
    }

    internal void TogglePreviewMode() =>
        SelectPreviewView(!_facePreviewEnabled);

    internal void ToggleClassicProjection()
    {
        _classicProjectionEnabled = !_classicProjectionEnabled;
        ApplyPreviewState();
    }

    internal void ToggleBodyControls() => ToggleBodyEditor();

    internal void ActivateReflectronFaceControl() =>
        _reflectron.ActivateCreatorModeControl("FACE");

    internal void ActivateReflectronBodyControl() =>
        _reflectron.ActivateCreatorModeControl("BODY");

    internal void ActivateReflectronSourceControl(string label) =>
        _reflectron.ActivateSourceSectionControl(label);

    internal void ActivateRulesControl() =>
        _rules.EmitSignal(BaseButton.SignalName.Pressed);

    internal void SetBodyProportion(string role, float value)
    {
        _bodyProportions = _bodyProportions.With(role, value);
        _bodyProportions.Validate("fallout2-custom-character-editor");
        _livePreview.SetProportions(_bodyProportions);
        var slider = _bodyPanel.GetNodeOrNull<HSlider>($"Body_{role}");
        if (slider is not null && !Mathf.IsEqualApprox((float)slider.Value, value))
            slider.SetValueNoSignal(value);
        var label = _bodyPanel.GetNodeOrNull<Label>($"BodyValue_{role}");
        if (label is not null)
            label.Text = $"{Mathf.RoundToInt(value * 100.0f)}%";
        SetMeta("custom_body_proportions", BodyProportionText());
    }

    internal void SetAge(int value)
    {
        _ageValue = Math.Clamp(value, 16, 35);
        Refresh();
    }

    internal void SetTaggedSkills(IReadOnlyList<string> values)
    {
        if (values.Count != 3 || values.Distinct(StringComparer.Ordinal).Count() != 3 ||
            values.Any(value => !Fo2CharacterStartCatalog.SkillNames.Contains(
                value,
                StringComparer.Ordinal)))
            throw new ArgumentException(
                "Fallout 2 custom tags require exactly three distinct source skills.",
                nameof(values));
        _taggedSkills.Clear();
        _taggedSkills.AddRange(values);
        Refresh();
    }

    internal void SetTraits(IReadOnlyList<string> values)
    {
        if (values.Count > 2 || values.Distinct(StringComparer.Ordinal).Count() != values.Count ||
            values.Any(value => !Fo2CharacterStartCatalog.TraitNames.Contains(
                value,
                StringComparer.Ordinal)))
            throw new ArgumentException(
                "Fallout 2 custom traits require up to two distinct source traits.",
                nameof(values));
        _traits.Clear();
        _traits.AddRange(values);
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
            _taggedSkills.ToArray(),
            _traits.ToArray());
        var provisional = new Fo2CharacterSelection(
            Fo2CharacterSelection.CreateMode,
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
                EyeColorId,
                BrowStyleId,
                NoseStyleId,
                MouthStyleId,
                _bodyProportions),
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

    private void SetBrowStyleIndex(int index)
    {
        var count = Fo2ProceduralPortrait.BrowStyles.Count;
        _browStyleIndex = (index % count + count) % count;
        Refresh();
    }

    private void SetNoseStyleIndex(int index)
    {
        var count = Fo2ProceduralPortrait.NoseStyles.Count;
        _noseStyleIndex = (index % count + count) % count;
        Refresh();
    }

    private void SetMouthStyleIndex(int index)
    {
        var count = Fo2ProceduralPortrait.MouthStyles.Count;
        _mouthStyleIndex = (index % count + count) % count;
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
        _browStyle.Text = $"BROW: {BrowStyleId.ToUpperInvariant()}";
        _noseStyle.Text = $"NOSE: {NoseStyleId.ToUpperInvariant()}";
        _mouthStyle.Text = $"MOUTH: {MouthStyleId.ToUpperInvariant()}";
        _livePreview.SetAppearance(new Fo2HumanoidAppearance(
            FaceShapeId,
            HairStyleId,
            SkinToneId,
            HairColorId,
            EyeColorId,
            BrowStyleId,
            NoseStyleId,
            MouthStyleId));
        _portrait.Texture = ImageTexture.CreateFromImage(
            Fo2ProceduralPortrait.Render(
                _sexValue,
                FaceShapeId,
                HairStyleId,
                SkinToneId,
                HairColorId,
                EyeColorId,
                BrowStyleId,
                NoseStyleId,
                MouthStyleId));
        _age.Text = _ageValue.ToString();
        for (var index = 0; index < _special.Length; index++)
            _specialValues[index].Text = _special[index].ToString("00");
        _allocation.Text = $"ALLOCATED {AllocatedSpecial}/40";
        _allocation.AddThemeColorOverride(
            "font_color",
            AllocatedSpecial == 40 ? new Color("78e781") : new Color("e6c34c"));
        _rules.Text =
            $"RULES  •  TAGS {_taggedSkills.Count}/3  •  TRAITS {_traits.Count}/2";
        RefreshRulesPanel();
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
        SetMeta("custom_brow_style", BrowStyleId);
        SetMeta("custom_nose_style", NoseStyleId);
        SetMeta("custom_mouth_style", MouthStyleId);
        SetMeta("custom_body_proportions", BodyProportionText());
        SetMeta("custom_appearance_recipe_sha256", Fo2ProceduralAppearanceCatalog.Load().Sha256);
        SetMeta("custom_portrait_generator", Fo2ProceduralPortrait.GeneratorId);
        SetMeta("custom_special", string.Join(",", _special));
        SetMeta("custom_special_total", AllocatedSpecial);
        SetMeta("custom_tagged_skills", string.Join(",", _taggedSkills));
        SetMeta("custom_traits", string.Join(",", _traits));
        SetMeta("custom_tags_traits_policy", "player-selected-source-rules");
    }

    private void ToggleBodyEditor()
    {
        SelectPreviewView(false);
        if (_bodyPanel.Visible)
        {
            ShowStatsEditor();
            return;
        }
        SetStatsControlsVisible(false);
        SetAppearanceControlsVisible(false);
        _rulesPanel.Visible = false;
        _bodyPanel.Visible = true;
        _bodyToggle.Text = "STATS";
        SetMeta(
            "custom_editor_panel",
            "body-proportions");
    }

    private void ShowSexEditor() => ShowStatsEditor();

    private void ShowRaceEditor() => ShowAppearanceEditor(
        _raceControls,
        "race",
        "human-race-and-complexion");

    private void ShowFaceEditor() => ShowAppearanceEditor(
        _faceControls,
        "face",
        "face-appearance");

    private void ShowHairEditor() => ShowAppearanceEditor(
        _hairControls,
        "hair",
        "hair-appearance");

    private void ShowClassicPortrait()
    {
        ShowAppearanceEditor(_faceControls, "face", "face-preview");
        SetMeta("custom_editor_panel", "face-preview");
        SetMeta("custom_editor_view", "face-framing-of-current-live-3d-character");
    }

    private void ShowAppearanceEditor(
        IReadOnlyList<Control> controls,
        string sourceSection,
        string editorPanel)
    {
        _bodyPanel.Visible = false;
        _rulesPanel.Visible = false;
        SetStatsControlsVisible(false);
        SetAppearanceControlsVisible(false);
        foreach (var control in controls)
            control.Visible = true;
        _bodyToggle.Text = "BODY";
        SelectPreviewView(true);
        _reflectron.SetActiveList(sourceSection);
        SetMeta("custom_editor_panel", editorPanel);
    }

    private void ShowStatsEditor()
    {
        _bodyPanel.Visible = false;
        _rulesPanel.Visible = false;
        SetAppearanceControlsVisible(false);
        SetStatsControlsVisible(true);
        _bodyToggle.Text = "BODY";
        SelectPreviewView(false);
        _reflectron.SetActiveList("sex");
        SetMeta("custom_editor_panel", "identity-and-special");
    }

    private void ShowRulesEditor()
    {
        _bodyPanel.Visible = false;
        SetAppearanceControlsVisible(false);
        SetStatsControlsVisible(false);
        _rulesPanel.Visible = true;
        _bodyToggle.Text = "BODY";
        SelectPreviewView(false);
        SetMeta("custom_editor_panel", "tagged-skills-and-traits");
    }

    private void SetAppearanceControlsVisible(bool visible)
    {
        foreach (var control in _appearanceControls)
            control.Visible = visible;
    }

    private void SetStatsControlsVisible(bool visible)
    {
        foreach (var control in _statsControls)
            control.Visible = visible;
    }

    private string BodyProportionText() => string.Join(
        ",",
        new[]
        {
            "height", "chest", "shoulders", "waist", "arms", "thighs", "calves",
        }.Select(role => $"{role}:{_bodyProportions.Value(role):F2}"));

    private void SelectPreviewView(bool face)
    {
        _facePreviewEnabled = face;
        ApplyPreviewState();
    }

    private void ApplyPreviewState()
    {
        _livePreview.Visible = true;
        _livePreview.SetPreviewState(_facePreviewEnabled, _classicProjectionEnabled);
        _sourcePanel.Visible = false;
        _portrait.Visible = false;
        _previewToggle.Text = _classicProjectionEnabled ? "SOURCE" : "PROJECT";
        if (!_facePreviewEnabled)
            _livePreview.SetProportions(_bodyProportions);
        _reflectron.SetCreatorModeState(
            _facePreviewEnabled ? "FACE" : "BODY",
            _bodyPanel.Visible,
            projectionEnabled: _classicProjectionEnabled,
            faceEnabled: AppearanceControlsVisible);
        SetMeta(
            "custom_preview_mode",
            _classicProjectionEnabled
                ? _facePreviewEnabled
                    ? "green-face-wireframe-closeup-projection-of-current-3d-character"
                    : "green-wireframe-body-projection-of-current-3d-character"
                : _facePreviewEnabled
                    ? "source-material-live-3d-head-and-shoulders"
                    : _livePreview.PresentationMode);
        SetMeta(
            "custom_live_3d_boundary",
            "verified owned full-body donor drives both face and body views; green projected views are presentation-only");
    }

    private void UpdateReflectronState() => _reflectron.SetCreatorModeState(
        _facePreviewEnabled ? "FACE" : "BODY",
        _bodyPanel.Visible,
        projectionEnabled: _classicProjectionEnabled,
        faceEnabled: AppearanceControlsVisible);

    private Control BuildRulesPanel()
    {
        var panel = new Control
        {
            Name = "FO2_SOURCE_RULE_CONTROLS",
            Size = new Vector2(ControlDesignWidth, 310.0f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        panel.AddChild(new ColorRect
        {
            Position = Vector2.Zero,
            Size = panel.Size,
            Color = new Color(0.0f, 0.035f, 0.018f, 0.96f),
            MouseFilter = MouseFilterEnum.Stop,
        });
        var title = AddPanelText(panel, "TAGGED SKILLS", 10.0f, 8.0f, 280.0f, 26.0f, 12);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        for (var index = 0; index < _tagSkillValues.Length; index++)
        {
            var captured = index;
            var y = 42.0f + index * 38.0f;
            AddPanelButton(panel, "◀", 10.0f, y, 28.0f, 28.0f, () => CycleTag(captured, -1));
            _tagSkillValues[index] = AddPanelText(
                panel,
                "",
                42.0f,
                y,
                216.0f,
                28.0f,
                9);
            _tagSkillValues[index].HorizontalAlignment = HorizontalAlignment.Center;
            AddPanelButton(panel, "▶", 262.0f, y, 28.0f, 28.0f, () => CycleTag(captured, 1));
        }
        var traitsTitle = AddPanelText(panel, "OPTIONAL TRAITS", 10.0f, 160.0f, 280.0f, 26.0f, 12);
        traitsTitle.HorizontalAlignment = HorizontalAlignment.Center;
        for (var index = 0; index < _traitValues.Length; index++)
        {
            var captured = index;
            var y = 194.0f + index * 38.0f;
            AddPanelButton(panel, "◀", 10.0f, y, 28.0f, 28.0f, () => CycleTrait(captured, -1));
            _traitValues[index] = AddPanelText(
                panel,
                "",
                42.0f,
                y,
                216.0f,
                28.0f,
                9);
            _traitValues[index].HorizontalAlignment = HorizontalAlignment.Center;
            AddPanelButton(panel, "▶", 262.0f, y, 28.0f, 28.0f, () => CycleTrait(captured, 1));
        }
        AddPanelButton(panel, "BACK TO SPECIAL", 76.0f, 272.0f, 148.0f, 28.0f, ShowStatsEditor);
        RefreshRulesPanel();
        return panel;
    }

    private void CycleTag(int slot, int delta)
    {
        if (slot is < 0 or >= 3 || delta is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(slot));
        var choices = Fo2CharacterStartCatalog.SkillNames;
        var current = Array.IndexOf(choices, _taggedSkills[slot]);
        for (var step = 1; step <= choices.Length; step++)
        {
            var candidate = choices[(current + delta * step % choices.Length + choices.Length) %
                choices.Length];
            if (_taggedSkills.Where((_, index) => index != slot).Contains(
                    candidate,
                    StringComparer.Ordinal))
                continue;
            _taggedSkills[slot] = candidate;
            Refresh();
            return;
        }
    }

    private void CycleTrait(int slot, int delta)
    {
        if (slot is < 0 or >= 2 || delta is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(slot));
        var choices = new string?[] { null }.Concat(
            Fo2CharacterStartCatalog.TraitNames.Cast<string?>()).ToArray();
        var currentValue = slot < _traits.Count ? _traits[slot] : null;
        var current = Array.IndexOf(choices, currentValue);
        for (var step = 1; step <= choices.Length; step++)
        {
            var candidate = choices[(current + delta * step % choices.Length + choices.Length) %
                choices.Length];
            if (candidate is not null &&
                _traits.Where((_, index) => index != slot).Contains(
                    candidate,
                    StringComparer.Ordinal))
                continue;
            if (slot < _traits.Count)
            {
                if (candidate is null)
                    _traits.RemoveAt(slot);
                else
                    _traits[slot] = candidate;
            }
            else if (candidate is not null)
                _traits.Add(candidate);
            Refresh();
            return;
        }
    }

    private void RefreshRulesPanel()
    {
        for (var index = 0; index < _tagSkillValues.Length; index++)
            if (_tagSkillValues[index] is not null)
                _tagSkillValues[index].Text =
                    $"TAG {index + 1}: {_taggedSkills[index].ToUpperInvariant()}";
        for (var index = 0; index < _traitValues.Length; index++)
            if (_traitValues[index] is not null)
                _traitValues[index].Text = index < _traits.Count
                    ? $"TRAIT {index + 1}: {_traits[index].ToUpperInvariant()}"
                    : $"TRAIT {index + 1}: NONE";
    }

    private static Label AddPanelText(
        Control parent,
        string text,
        float x,
        float y,
        float width,
        float height,
        int fontSize)
    {
        var label = new Label
        {
            Text = text,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", new Color("78e781"));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label);
        return label;
    }

    private static Button AddPanelButton(
        Control parent,
        string text,
        float x,
        float y,
        float width,
        float height,
        Action pressed)
    {
        var button = new Button
        {
            Text = text,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Flat = true,
            FocusMode = FocusModeEnum.None,
        };
        button.AddThemeColorOverride("font_color", new Color("78e781"));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", 9);
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private Control BuildBodyPanel()
    {
        var panel = new Control
        {
            Name = "FO2_LIVE_BODY_PROPORTION_CONTROLS",
            Size = new Vector2(ControlDesignWidth, ControlDesignHeight),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        panel.AddChild(new ColorRect
        {
            Position = Vector2.Zero,
            Size = new Vector2(ControlDesignWidth, ControlDesignHeight),
            Color = new Color(0.0f, 0.035f, 0.018f, 0.90f),
            MouseFilter = MouseFilterEnum.Stop,
        });
        var roles = new[]
        {
            (Key: "height", Label: "HEIGHT"),
            (Key: "chest", Label: "CHEST"),
            (Key: "shoulders", Label: "SHOULDERS"),
            (Key: "waist", Label: "WAIST"),
            (Key: "arms", Label: "ARMS"),
            (Key: "thighs", Label: "THIGHS"),
            (Key: "calves", Label: "CALVES"),
        };
        for (var index = 0; index < roles.Length; index++)
        {
            var row = roles[index];
            var y = 44.0f + index * 38.0f;
            var label = new Label
            {
                Position = new Vector2(12.0f, y),
                Size = new Vector2(72.0f, 28.0f),
                Text = row.Label,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeColorOverride("font_color", new Color("78e781"));
            label.AddThemeFontSizeOverride("font_size", 9);
            panel.AddChild(label);
            var value = new Label
            {
                Name = $"BodyValue_{row.Key}",
                Position = new Vector2(236.0f, y),
                Size = new Vector2(54.0f, 28.0f),
                Text = $"{Mathf.RoundToInt(_bodyProportions.Value(row.Key) * 100.0f)}%",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            value.AddThemeColorOverride("font_color", new Color("78e781"));
            value.AddThemeFontSizeOverride("font_size", 9);
            panel.AddChild(value);
            var slider = new HSlider
            {
                Name = $"Body_{row.Key}",
                Position = new Vector2(86.0f, y),
                Size = new Vector2(145.0f, 28.0f),
                MinValue = CharacterBodyProportions.Minimum,
                MaxValue = CharacterBodyProportions.Maximum,
                Step = CharacterBodyProportions.Step,
                Value = _bodyProportions.Value(row.Key),
            };
            var selected = row.Key;
            slider.ValueChanged += next =>
            {
                SetBodyProportion(selected, (float)next);
            };
            panel.AddChild(slider);
        }
        return panel;
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
        _controlRoot.AddChild(label);
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
        _controlRoot.AddChild(button);
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
