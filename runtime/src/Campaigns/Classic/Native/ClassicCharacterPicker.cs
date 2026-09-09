using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Shared original-data picker, with distinct FO1 illustration and FO2 live appearance.</summary>
internal sealed partial class ClassicCharacterPicker : CanvasLayer
{
    private string _campaign = "", _profile = "", _save = "";
    private string? _appearanceRoot;
    private ClassicArtCache _art = null!;
    private IReadOnlyList<ClassicPremade> _premades = [];
    private int _index;
    private ClassicCharacterProfile _character = null!;
    private ClassicAppearanceDraft? _appearance;
    private bool _custom, _previousPause, _ownsPause;
    private Control _canvas = null!, _editors = null!;
    private TextureRect _picture = null!;
    private RichTextLabel _details = null!;
    private Label _status = null!, _counter = null!, _heading = null!;
    private Button _editAppearance = null!, _envision = null!;
    private NativeOwnedActorPreview? _portrait;
    private FalloutPluginStack? _records;
    private ClassicPortraitMode _mode;
    internal event Action<ClassicCharacterDraft>? Selected;
    internal IReadOnlyList<ClassicPremade> Premades => _premades;
    internal ClassicCharacterProfile Character => _character;
    internal ClassicAppearanceDraft? Appearance => _appearance;
    internal NativeOwnedActorPreview? Portrait => _portrait;
    internal ClassicPortraitMode PortraitMode => _mode;

    internal void Configure(string campaign, string profile, Func<string, byte[]> read, string save, string? appearanceRoot)
    {
        Name = "ClassicCharacterPicker"; Layer = 80; ProcessMode = ProcessModeEnum.Always;
        _campaign = campaign; _profile = profile; _save = save; _appearanceRoot = appearanceRoot;
        _premades = ClassicPremadeReader.Load(campaign, read); _art = new(read);
        _previousPause = GetTree().Paused; _ownsPause = true; GetTree().Paused = true;
        var curtain = new ColorRect { Color = Colors.Black }; AddChild(curtain);
        curtain.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _canvas = new Control { Name = "ClassicCharacterSourceCanvas", Size = new(640, 480), Theme = new Theme { DefaultFontSize = 11 } }; AddChild(_canvas);
        _canvas.AddChild(new TextureRect
        {
            Texture = _art.Frame("art/intrface/pickchar.frm").Texture,
            Size = new(640, 480),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        });
        _picture = new TextureRect
        {
            Name = "ClassicCharacterPortrait",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear
        };
        _canvas.AddChild(_picture);
        _details = new RichTextLabel
        {
            Position = new(275, 39),
            Size = new(310, 216),
            FitContent = false,
            ScrollActive = true,
            Modulate = new Color("75e96b"),
            SelectionEnabled = true
        };
        _details.AddThemeFontSizeOverride("normal_font_size", 10); _canvas.AddChild(_details);
        _heading = LabelAt("", new(275, 39), new(310, 20), 12);
        _heading.HorizontalAlignment = HorizontalAlignment.Center; _heading.VerticalAlignment = VerticalAlignment.Center;
        var actions = new Panel { Position = new(38, 296), Size = new(564, 168), MouseFilter = Control.MouseFilterEnum.Ignore };
        actions.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("171e19"),
            BorderColor = new Color("626447"),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5
        });
        _canvas.AddChild(actions);
        _counter = LabelAt("", new(50, 384), new(540, 18), 12);
        _counter.HorizontalAlignment = HorizontalAlignment.Center; _counter.VerticalAlignment = VerticalAlignment.Center;
        _status = LabelAt("", new(50, 408), new(540, 42), 12); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        ButtonAt("PreviousCharacter", "◀", new(286, 307), new(29, 32), () => ShowPremade((_index + _premades.Count - 1) % _premades.Count));
        ButtonAt("NextCharacter", "▶", new(317, 307), new(29, 32), () => ShowPremade((_index + 1) % _premades.Count));
        ButtonAt("SaveCharacterChoice", "SAVE SELECTION", new(65, 307), new(181, 33), SaveChoice);
        ButtonAt("CustomCharacter", "CUSTOMIZE CHARACTER", new(379, 307), new(200, 33), BeginCustom);
        ButtonAt("BackToWorld", "BACK TO WORLD", new(65, 349), new(181, 33), Close);
        _editAppearance = ButtonAt("EditCharacterAppearance", "REFLECTRON 2.0", new(379, 349), new(200, 33), EditAppearance);
        _envision = ButtonAt("CharacterEnvision", "ENVISION · ILLUSTRATED", new(54, 244), new(201, 25), () => SetMode(
            _mode == ClassicPortraitMode.Illustrated ? ClassicPortraitMode.Live3D : ClassicPortraitMode.Illustrated));
        _editors = new Control { Position = new(274, 32), Size = new(316, 230), Visible = false }; _canvas.AddChild(_editors);
        GetViewport().SizeChanged += Resize; Resize();
        var saved = ClassicCharacterDraft.Read(save + ".character.json", campaign, profile, _premades);
        if (saved is { PremadeId: null })
        {
            _custom = true; _character = saved.Character; _appearance = saved.Appearance;
            ShowCustom();
        }
        else ShowPremade(saved is null ? 0 : _premades.ToList().FindIndex(row => row.Id == saved.PremadeId));
        GD.Print($"OPENNV_CLASSIC_PICKER_OPEN campaign={campaign} premades={_premades.Count} restored={saved is not null} source=live-gcd-bio-frm");
    }

    private void Resize()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var scale = Math.Min(size.X / 640, size.Y / 480);
        _canvas.Scale = new(scale, scale); _canvas.Position = (size - new Vector2(640, 480) * scale) / 2;
    }

    internal void ShowPremade(int index)
    {
        ReleasePortrait(); _index = index; _custom = false; _appearance = null;
        var source = _premades[index]; _character = source.Character;
        _editors.Visible = false; _envision.Visible = false; _editAppearance.Disabled = true;
        _picture.Material = null; _picture.Texture = _art.Frame(source.PortraitPath).Texture;
        _picture.Position = _campaign == "fallout-1" ? new(48, 47) : new(24, 20);
        _picture.Size = _campaign == "fallout-1" ? new(212, 187) : new(592, 260);
        _details.Visible = true; _heading.Visible = true;
        _details.Position = _campaign == "fallout-1" ? new(275, 63) : new(310, 63);
        _details.Size = _campaign == "fallout-1" ? new(310, 192) : new(274, 192);
        _heading.Position = new(_details.Position.X, 39); _heading.Size = new(_details.Size.X, 20);
        _heading.Text = $"{_character.Name.ToUpperInvariant()}   ·   AGE {_character.Age}";
        _details.Text = string.Join("  ", _character.Special.Select((value, at) => $"{ClassicCharacterProfile.SpecialNames[at][0]} {value}")) +
            "\n" + string.Join(" · ", _character.TaggedSkills.Select(value => ClassicCharacterProfile.SkillNames[value])) +
            "\n" + string.Join(" · ", _character.Traits.Select(value => ClassicCharacterProfile.TraitNames[value])) + "\n\n" +
            string.Join(" ", source.Biography.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        _counter.Text = $"{index + 1} / {_premades.Count}  ·  {_character.Name}";
        _status.Text = "Original character artwork. Choose a character or customize your own.";
    }

    private void BeginCustom()
    {
        if (_custom) return;
        _character = JsonSerializer.Deserialize<ClassicCharacterProfile>(JsonSerializer.Serialize(_character))!;
        _custom = true; _appearance = null; ShowCustom();
    }

    private void ShowCustom()
    {
        _details.Visible = false; _heading.Visible = false; _editors.Visible = true;
        _picture.Position = new(48, 47); _picture.Size = new(212, 187);
        _picture.Texture = null; _picture.Material = null;
        _counter.Text = "CUSTOM CHARACTER";
        _editAppearance.Disabled = string.IsNullOrWhiteSpace(_appearanceRoot);
        foreach (var child in _editors.GetChildren()) { _editors.RemoveChild(child); child.QueueFree(); }
        var tabs = new TabContainer { Size = _editors.Size }; tabs.AddThemeFontSizeOverride("font_size", 11); _editors.AddChild(tabs);
        VBoxContainer Page(string name)
        {
            var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; tabs.AddChild(scroll);
            var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(column); return column;
        }
        var identity = Page("Profile");
        identity.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Name = "ClassicCharacterName", Text = _character.Name, MaxLength = 11 }; identity.AddChild(name);
        name.TextChanged += value => _character = _character with { Name = value };
        identity.AddChild(new Label { Text = "Age" });
        var age = new SpinBox { MinValue = 16, MaxValue = 35, Step = 1, Value = _character.Age }; identity.AddChild(age);
        age.ValueChanged += value => _character = _character with { Age = (int)value };
        identity.AddChild(new Label { Text = $"{(_character.Female ? "Female" : "Male")} · set appearance in Reflectron", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var special = Page("SPECIAL"); var points = new Label(); special.AddChild(points);
        void Remaining() => points.Text = $"Points to allocate: {40 - _character.Special.Sum()}";
        Remaining();
        for (var at = 0; at < 7; at++)
        {
            var slot = at; var row = new HBoxContainer(); special.AddChild(row);
            row.AddChild(new Label { Text = ClassicCharacterProfile.SpecialNames[slot], CustomMinimumSize = new(175, 0) });
            var value = new SpinBox { MinValue = 1, MaxValue = 10, Step = 1, Value = _character.Special[slot] }; row.AddChild(value);
            value.ValueChanged += next => { _character.Special[slot] = (int)next; Remaining(); };
        }
        var tags = Page("Skills"); tags.AddChild(new Label { Text = "Choose three tag skills" });
        for (var at = 0; at < ClassicCharacterProfile.SkillNames.Length; at++)
        {
            var slot = at; var check = new CheckBox { Text = ClassicCharacterProfile.SkillNames[slot], ButtonPressed = _character.TaggedSkills.Contains(slot) }; tags.AddChild(check);
            check.Toggled += value => _character = _character with { TaggedSkills = value ? _character.TaggedSkills.Append(slot).ToArray() : _character.TaggedSkills.Where(item => item != slot).ToArray() };
        }
        var traits = Page("Traits"); traits.AddChild(new Label { Text = "Choose up to two traits" });
        for (var at = 0; at < ClassicCharacterProfile.TraitNames.Length; at++)
        {
            var slot = at; var check = new CheckBox { Text = ClassicCharacterProfile.TraitNames[slot], ButtonPressed = _character.Traits.Contains(slot) }; traits.AddChild(check);
            check.Toggled += value => _character = _character with { Traits = value ? _character.Traits.Append(slot).ToArray() : _character.Traits.Where(item => item != slot).ToArray() };
        }
        _status.Text = "Edit your character, approve the appearance, then save the selection.";
        _envision.Visible = false;
        if (_appearance is not null) Try(() =>
        {
            try { RefreshPortrait(); }
            catch { ReleasePortrait(); throw; }
        });
    }

    private void EditAppearance() => Try(() =>
    {
        if (!_custom || string.IsNullOrWhiteSpace(_appearanceRoot)) return;
        var studio = new ClassicCharacterStudio(); AddChild(studio);
        try
        {
            studio.AppearanceAccepted += appearance =>
            {
                _appearance = appearance; _character = _character with { Female = appearance.Character.Female };
                ShowCustom();
            };
            studio.Configure(_campaign, _profile, _appearanceRoot, _save, _appearance, _character.Female);
        }
        catch { RemoveChild(studio); studio.Free(); throw; }
    });

    private void RefreshPortrait()
    {
        ReleasePortrait();
        if (_appearance is null || string.IsNullOrWhiteSpace(_appearanceRoot)) return;
        RuntimeLiveContentSource.Configure(_appearanceRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        var source = RuntimeLiveContentSource.Current!;
        if (_appearance.AppearanceStackId != source.StackId) throw new InvalidDataException("Saved face belongs to a different appearance installation.");
        _records = FalloutPluginStack.Load(source.PluginSources);
        var contract = FalloutNativeRaceSexResolver.Resolve(_records); var settings = FalloutInstallationSettings.Read(source);
        var creation = new FalloutNativeCharacterCreation(_records, contract, _appearance.Character, settings);
        if (JsonSerializer.Serialize(creation.Selection) != JsonSerializer.Serialize(_appearance.Character))
            throw new InvalidDataException("Approved character changed during portrait restoration.");
        _portrait = new NativeOwnedActorPreview(_records, creation.Appearance(), settings, 848); AddChild(_portrait);
        var skeleton = creation.Appearance().SkeletonPath.Replace('\\', '/');
        var path = skeleton[..skeleton.LastIndexOf('/')] + "/locomotion/mtidle.kf";
        if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
        var file = FalloutNifFile.Read(bytes); _portrait.Actor.PlayBaseSequence(file, file.Roots.Select(file.ReadControllerSequence).Single(), identity);
        _portrait.UpdateProjection();
        // The classic screen shows a head-and-shoulders crop of the live source
        // render. It retains the same geometry, pose and facial coefficients.
        _picture.Texture = new AtlasTexture { Atlas = _portrait.View.GetTexture(), Region = new Rect2(0, 0, 848 * 0.70f, 848 * 0.6175f) };
        SetMode(_campaign == "fallout-1" ? ClassicPortraitMode.Illustrated : ClassicPortraitMode.Live3D);
        _envision.Visible = _campaign == "fallout-1";
        _status.Text = "Your approved character. Save the selection to use this appearance.";
    }

    private void SetMode(ClassicPortraitMode mode)
    {
        _mode = mode; _picture.Material = ClassicPortraitProjection.Create(mode);
        _portrait?.SetIllustratedBackdrop(mode == ClassicPortraitMode.Illustrated);
        _envision.Text = mode == ClassicPortraitMode.Illustrated ? "ENVISION · ILLUSTRATED" : "ENVISION · LIVE 3D";
    }

    private void SaveChoice() => Try(() =>
    {
        if (_custom && (_appearance is null || _portrait is null)) throw new InvalidOperationException("Approve your appearance in Reflectron before saving this custom character.");
        var source = _custom ? null : _premades[_index];
        var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, _campaign, _profile,
            source?.Id, source?.GcdSha256, source?.BiographySha256, source?.PortraitSha256, _character, _appearance);
        choice.Save(_save + ".character.json", _premades); Selected?.Invoke(choice);
        GD.Print($"OPENNV_CLASSIC_CHARACTER_SELECTED campaign={_campaign} identity={source?.Id ?? "custom"} appearance={_appearance is not null} campaignProgression=false");
        Close();
    });

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception error) { _status.Text = error.Message; GD.PushError($"OPENNV_CLASSIC_CHARACTER_UNBOUND {error}"); }
    }

    private Label LabelAt(string text, Vector2 position, Vector2 size, int font)
    {
        var label = new Label { Text = text, Position = position, Size = size, Modulate = new Color("75e96b"), MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", font); _canvas.AddChild(label); return label;
    }

    private Button ButtonAt(string name, string text, Vector2 position, Vector2 size, Action pressed)
    {
        var button = new Button { Name = name, Text = text, Position = position, Size = size };
        button.AddThemeFontSizeOverride("font_size", 12); _canvas.AddChild(button); button.Pressed += pressed; return button;
    }

    private void ReleasePortrait()
    {
        if (_picture is not null) _picture.Texture = null;
        if (_portrait is not null) { RemoveChild(_portrait); _portrait.Free(); _portrait = null; }
        _records?.Dispose(); _records = null;
    }

    private void Close()
    {
        if (_ownsPause) { GetTree().Paused = _previousPause; _ownsPause = false; }
        QueueFree();
    }

    public override void _ExitTree()
    {
        if (_ownsPause) { GetTree().Paused = _previousPause; _ownsPause = false; }
        GetViewport().SizeChanged -= Resize; ReleasePortrait();
    }
}
