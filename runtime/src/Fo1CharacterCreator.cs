using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1CharacterCreator : CanvasLayer
{
    private static readonly Color Amber = new(0.92f, 0.75f, 0.20f);
    private static readonly Color Green = new(0.55f, 0.95f, 0.36f);
    private static readonly string[] StatNames =
        ["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];

    private readonly int[] _stats = [5, 5, 5, 5, 5, 5, 5];
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _traits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _skillButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _traitButtons = new(StringComparer.Ordinal);
    private Fo1CharacterStartContract _contract = null!;
    private Control _canvas = null!;
    private LineEdit _name = null!;
    private Label _ageLabel = null!;
    private Button _sexButton = null!;
    private Label _derived = null!;
    private Label _info = null!;
    private Label _tagCountLabel = null!;
    private Image _creatorChromeImage = null!;
    private Image _creatorNumberAtlas = null!;
    private ImageTexture _creatorChromeTexture = null!;
    private Control _pickerOverlay = null!;
    private TextureRect _pickerPortrait = null!;
    private Label _pickerDetails = null!;
    private Label _pickerCounter = null!;
    private int _pickerIndex;
    private int _age = 25;
    private string _sex = "Male";

    internal event Action<Fo1CharacterProfile>? CharacterReady;

    internal void Configure(Fo1CharacterStartContract contract)
    {
        _contract = contract;
        Name = "OriginalFalloutCharacterCreator";
        Layer = 110;
    }

    public override void _Ready()
    {
        Build();
        UpdateAll();
        BuildPickerOverlay();
        ShowPremade(0);
    }

    internal async Task<Fo1CharacterProfile> RunAutomatedDemo(Node host)
    {
        await WaitFrames(host, 24);
        ShowPremade(0);
        await WaitFrames(host, 28);
        ShowPremade(1);
        await WaitFrames(host, 28);
        ShowPremade(2);
        await WaitFrames(host, 28);
        OpenCustomEditor();
        _name.Text = "NIKAMI";
        _info.Text = "NAME\nNIKAMI\n\nYour name is preserved in the live tactical session and save contract.";
        await WaitFrames(host, 28);

        foreach (var (index, count) in new[] { (0, 1), (1, 2), (5, 2) })
        {
            for (var step = 0; step < count; step++)
            {
                ChangeStat(index, 1);
                _info.Text = $"{StatNames[index].ToUpperInvariant()}  {_stats[index]:00}\n\n" +
                    $"Spend all five Fallout SPECIAL points.  CHARACTER POINTS: {PointsRemaining}";
                await WaitFrames(host, 20);
            }
        }
        foreach (var skill in new[] { "Small Guns", "First Aid", "Speech" })
        {
            ToggleSkill(skill);
            _info.Text = $"TAG SKILL  {_tags.Count}/3\n\n{skill} receives the original +20 starting bonus.";
            await WaitFrames(host, 24);
        }
        foreach (var trait in new[] { "Fast Shot", "Bloody Mess" })
        {
            ToggleTrait(trait);
            _info.Text = trait == "Fast Shot"
                ? "FAST SHOT\n\nFirearms cost one fewer AP; aimed attacks are unavailable."
                : "BLOODY MESS\n\nThe classic Fallout death-presentation trait is selected.";
            await WaitFrames(host, 28);
        }
        _info.Text =
            "READY\n\nNIKAMI  •  MALE  •  AGE 25\n" +
            "ST 06  PE 07  EN 05  CH 05  IN 05  AG 07  LK 05\n\n" +
            "DONE now creates the authoritative gameplay profile.";
        await WaitFrames(host, 55);
        var profile = BuildProfile();
        CharacterReady?.Invoke(profile);
        return profile;
    }

    private void Build()
    {
        var black = new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(black);

        _canvas = new Control { Size = new Vector2(640.0f, 480.0f) };
        AddChild(_canvas);
        LayoutCanvas();
        _creatorChromeImage = Image.LoadFromFile(_contract.ChromePath);
        if (_creatorChromeImage is null || _creatorChromeImage.IsEmpty() ||
            _creatorChromeImage.GetWidth() != 640 || _creatorChromeImage.GetHeight() != 480)
            throw new InvalidOperationException(
                "Prepared Fallout creator chrome failed image validation.");
        _creatorChromeImage.Convert(Image.Format.Rgba8);
        _creatorNumberAtlas = _contract.CreatorNumbers.Atlas.LoadImage();
        _creatorChromeTexture = ImageTexture.CreateFromImage(_creatorChromeImage);
        _canvas.AddChild(new TextureRect
        {
            Texture = _creatorChromeTexture,
            Size = new Vector2(640.0f, 480.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _name = new LineEdit
        {
            Position = new Vector2(22.0f, 5.0f),
            Size = new Vector2(130.0f, 24.0f),
            Text = "None",
            MaxLength = 11,
            PlaceholderText = "NAME",
        };
        StyleLineEdit(_name);
        _name.TextChanged += value =>
        {
            _info.Text = $"NAME\n{value}\n\nEnter up to eleven characters.";
        };
        _canvas.AddChild(_name);

        AddText("AGE", 162, 5, 28, 20, Amber, 11);
        AddSmallButton("−", 190, 5, () => ChangeAge(-1));
        _ageLabel = AddText("25", 207, 5, 25, 20, Green, 12, HorizontalAlignment.Center);
        AddSmallButton("+", 232, 5, () => ChangeAge(1));
        AddText("SEX", 247, 5, 28, 20, Amber, 11);
        _sexButton = AddFlatButton("MALE", 275, 4, 40, 22, () =>
        {
            _sex = _sex == "Male" ? "Female" : "Male";
            UpdateAll();
            _info.Text = $"SEX\n{_sex.ToUpperInvariant()}";
        }, 10);

        for (var index = 0; index < _stats.Length; index++)
        {
            var captured = index;
            AddSourceButton(
                _contract.CreatorNumbers.Layout.SpecialIncrease[index],
                $"Increase {StatNames[index]}",
                () => ChangeStat(captured, 1));
            AddSourceButton(
                _contract.CreatorNumbers.Layout.SpecialDecrease[index],
                $"Decrease {StatNames[index]}",
                () => ChangeStat(captured, -1));
        }

        _derived = AddText("", 188, 40, 133, 113, Green, 10);
        AddText("DERIVED STATS", 190, 174, 128, 18, Amber, 11, HorizontalAlignment.Center);
        AddText(
            "HP, AP, AC, Sequence and skills are calculated from the selected character and used after the opening.",
            191,
            196,
            126,
            106,
            Green,
            9,
            HorizontalAlignment.Left,
            true);

        AddText("OPTIONAL TRAITS  •  PICK UP TO TWO", 20, 329, 296, 18, Amber, 10);
        for (var index = 0; index < Fo1CharacterProfile.TraitNames.Length; index++)
        {
            var trait = Fo1CharacterProfile.TraitNames[index];
            var column = index / 8;
            var row = index % 8;
            var button = AddFlatButton(
                $"□ {trait}",
                18 + column * 150,
                348 + row * 14,
                148,
                15,
                () => ToggleTrait(trait),
                8);
            button.Alignment = HorizontalAlignment.Left;
            _traitButtons.Add(trait, button);
        }

        AddText("TAG SKILLS  •  PICK EXACTLY THREE", 342, 12, 280, 19, Amber, 10);
        for (var index = 0; index < Fo1CharacterProfile.SkillNames.Length; index++)
        {
            var skill = Fo1CharacterProfile.SkillNames[index];
            var column = index / 9;
            var row = index % 9;
            var button = AddFlatButton(
                "",
                341 + column * 143,
                36 + row * 22,
                140,
                21,
                () => ToggleSkill(skill),
                9);
            button.Alignment = HorizontalAlignment.Left;
            _skillButtons.Add(skill, button);
        }
        AddMask(520, 235, 46, 20);
        AddText("TAGS", 484, 236, 38, 18, Amber, 8, HorizontalAlignment.Right);
        _tagCountLabel = AddText("0", 521, 235, 44, 20, Green, 11, HorizontalAlignment.Center);

        _info = AddText(
            "CHARACTER CREATION\n\nSpend five SPECIAL points, tag exactly three skills, and optionally select up to two traits.",
            346,
            274,
            276,
            164,
            Green,
            10,
            HorizontalAlignment.Left,
            true);
        AddFlatButton("OPTIONS", 337, 452, 103, 24, () =>
        {
            _info.Text = "OPTIONS\n\nThis proof keeps the owned original UI art at 640×480 and scales it cleanly.";
        }, 11);
        AddFlatButton("DONE", 446, 452, 98, 24, CompleteInteractive, 11);
        AddFlatButton("CANCEL", 551, 452, 77, 24, () =>
        {
            _info.Text = "CANCEL\n\nFinish the character to begin the Vault 13 opening.";
        }, 10);

    }

    private void BuildPickerOverlay()
    {
        _pickerOverlay = new Control
        {
            Name = "OwnedFalloutCharacterPicker",
            Size = new Vector2(640.0f, 480.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _canvas.AddChild(_pickerOverlay);
        _pickerOverlay.AddChild(new TextureRect
        {
            Name = "OwnedPickcharChrome",
            Texture = _contract.CharacterPicker.Load(),
            Size = new Vector2(640.0f, 480.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _pickerPortrait = new TextureRect
        {
            Name = "OwnedPremadePortrait",
            Position = new Vector2(48.0f, 47.0f),
            Size = new Vector2(212.0f, 187.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _pickerOverlay.AddChild(_pickerPortrait);
        _pickerDetails = PickerLabel(
            "",
            275.0f,
            42.0f,
            306.0f,
            218.0f,
            9,
            Green);
        _pickerDetails.VerticalAlignment = VerticalAlignment.Top;
        _pickerDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pickerCounter = PickerLabel(
            "",
            263.0f,
            262.0f,
            115.0f,
            24.0f,
            10,
            Amber);
        _pickerCounter.HorizontalAlignment = HorizontalAlignment.Center;

        AddPickerButton("◀", 286.0f, 307.0f, 28.0f, 31.0f, () => ShowPremade(_pickerIndex - 1), 13);
        AddPickerButton("▶", 316.0f, 307.0f, 28.0f, 31.0f, () => ShowPremade(_pickerIndex + 1), 13);
        AddPickerButton("", 65.0f, 301.0f, 181.0f, 79.0f, TakePremade, 12)
            .TooltipText = "Take this original Fallout premade";
        AddPickerButton("", 416.0f, 301.0f, 180.0f, 79.0f, ModifyPremade, 12)
            .TooltipText = "Modify this original Fallout premade in the full editor";
        AddPickerButton("", 66.0f, 397.0f, 244.0f, 63.0f, OpenCustomEditor, 12)
            .TooltipText = "Create a new custom character";
        AddPickerButton("", 443.0f, 397.0f, 153.0f, 63.0f, () =>
        {
            _pickerDetails.Text = "BACK\n\nThis vertical slice starts here. Choose Max Stone, Natalia, Albert, or Create Character.";
        }, 12).TooltipText = "Back";
    }

    private void ShowPremade(int index)
    {
        if (_contract.PremadeCharacters.Count != 3)
            throw new InvalidOperationException("Fallout picker requires exactly three premades.");
        _pickerIndex = (index % _contract.PremadeCharacters.Count +
            _contract.PremadeCharacters.Count) % _contract.PremadeCharacters.Count;
        var premade = _contract.PremadeCharacters[_pickerIndex];
        var profile = premade.Profile;
        _pickerPortrait.Texture = premade.Portrait.Load();
        _pickerCounter.Text = $"{profile.Name.ToUpperInvariant()}  {_pickerIndex + 1}/3";
        var biography = string.Join(
            " ",
            premade.Biography.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        _pickerDetails.Text =
            $"{profile.Name.ToUpperInvariant()}  •  {premade.Role.ToUpperInvariant()}\n" +
            $"{profile.Sex.ToUpperInvariant()}  •  AGE {profile.Age}\n" +
            $"ST {profile.Strength:00}  PE {profile.Perception:00}  EN {profile.Endurance:00}  " +
            $"CH {profile.Charisma:00}  IN {profile.Intelligence:00}  " +
            $"AG {profile.Agility:00}  LK {profile.Luck:00}\n" +
            $"HP {profile.HitPoints:00}  AP {profile.ActionPoints:00}  AC {profile.ArmorClass:00}  " +
            $"SEQ {profile.Sequence:00}  CARRY {profile.CarryWeight:000}\n" +
            $"TAGGED  {string.Join(" • ", profile.TaggedSkills)}\n" +
            $"TRAITS  {string.Join(" • ", profile.Traits)}\n\n" +
            biography;
    }

    private void TakePremade()
    {
        var profile = _contract.PremadeCharacters[_pickerIndex].Profile;
        profile.Validate();
        _pickerDetails.Text = $"{profile.Name.ToUpperInvariant()} SELECTED\n\nBeginning the Vault Overseer briefing.";
        CharacterReady?.Invoke(profile);
    }

    private void ModifyPremade()
    {
        LoadProfile(_contract.PremadeCharacters[_pickerIndex].Profile);
        _pickerOverlay.Visible = false;
        _info.Text = "MODIFY CHARACTER\n\nThe selected owned premade is loaded into the complete SPECIAL, skills, and traits editor.";
    }

    private void OpenCustomEditor()
    {
        LoadProfile(new Fo1CharacterProfile(
            "None",
            25,
            "Male",
            5,
            5,
            5,
            5,
            5,
            5,
            5,
            [],
            []));
        _pickerOverlay.Visible = false;
        _info.Text = "CUSTOM CHARACTER\n\nSpend five SPECIAL points, tag exactly three skills, and optionally select up to two traits.";
    }

    private void LoadProfile(Fo1CharacterProfile profile)
    {
        _name.Text = profile.Name;
        _age = profile.Age;
        _sex = profile.Sex;
        var values = new[]
        {
            profile.Strength,
            profile.Perception,
            profile.Endurance,
            profile.Charisma,
            profile.Intelligence,
            profile.Agility,
            profile.Luck,
        };
        Array.Copy(values, _stats, values.Length);
        _tags.Clear();
        foreach (var skill in profile.TaggedSkills)
            _tags.Add(skill);
        _traits.Clear();
        foreach (var trait in profile.Traits)
            _traits.Add(trait);
        UpdateAll();
    }

    private Label PickerLabel(
        string text,
        float x,
        float y,
        float width,
        float height,
        int fontSize,
        Color color)
    {
        var label = new Label
        {
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _pickerOverlay.AddChild(label);
        return label;
    }

    private Button AddPickerButton(
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
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeColorOverride("font_color", Amber);
        button.AddThemeColorOverride("font_hover_color", Green);
        button.AddThemeColorOverride("font_pressed_color", Amber);
        button.AddThemeColorOverride("font_outline_color", Colors.Black);
        button.AddThemeConstantOverride("outline_size", 3);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.Pressed += pressed;
        _pickerOverlay.AddChild(button);
        return button;
    }

    private void LayoutCanvas()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var scale = MathF.Min(viewport.X / 640.0f, viewport.Y / 480.0f);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position = (viewport - new Vector2(640.0f, 480.0f) * scale) * 0.5f;
    }

    private void ChangeStat(int index, int delta)
    {
        if (delta > 0 && (PointsRemaining <= 0 || _stats[index] >= 10) ||
            delta < 0 && (_stats[index] <= 1 || PointsRemaining >= 5))
            return;
        _stats[index] += delta;
        UpdateAll();
    }

    private void ChangeAge(int delta)
    {
        _age = Math.Clamp(_age + delta, 16, 35);
        UpdateAll();
    }

    private void ToggleSkill(string skill)
    {
        if (!_tags.Remove(skill))
        {
            if (_tags.Count >= 3)
            {
                _info.Text = "TAG SKILLS\n\nFallout requires exactly three. Unselect one before choosing another.";
                return;
            }
            _tags.Add(skill);
        }
        UpdateAll();
    }

    private void ToggleTrait(string trait)
    {
        if (!_traits.Remove(trait))
        {
            if (_traits.Count >= 2)
            {
                _info.Text = "TRAITS\n\nFallout allows no more than two optional traits.";
                return;
            }
            _traits.Add(trait);
        }
        UpdateAll();
    }

    private void UpdateAll()
    {
        UpdateCreatorNumbers();
        _ageLabel.Text = _age.ToString("00");
        _sexButton.Text = _sex.ToUpperInvariant();
        _tagCountLabel.Text = _tags.Count.ToString();
        var preview = PreviewProfile();
        _derived.Text =
            $"HIT POINTS       {preview.HitPoints:00}\n" +
            $"ARMOR CLASS      {preview.ArmorClass:00}\n" +
            $"ACTION POINTS    {preview.ActionPoints:00}\n" +
            $"SEQUENCE         {preview.Sequence:00}\n" +
            $"CARRY WEIGHT    {preview.CarryWeight:000}";
        var skills = preview.Skills();
        foreach (var (skill, button) in _skillButtons)
        {
            var selected = _tags.Contains(skill);
            button.Text = $"{(selected ? "■" : "□")} {skill,-15} {skills[skill],3}%";
            button.Modulate = selected ? Amber : Green;
        }
        foreach (var (trait, button) in _traitButtons)
        {
            var selected = _traits.Contains(trait);
            button.Text = $"{(selected ? "■" : "□")} {trait}";
            button.Modulate = selected ? Amber : Green;
        }
    }

    private int PointsRemaining => 40 - _stats.Sum();

    private Fo1CharacterProfile PreviewProfile() => new(
        string.IsNullOrWhiteSpace(_name?.Text) ? "None" : _name.Text,
        _age,
        _sex,
        _stats[0],
        _stats[1],
        _stats[2],
        _stats[3],
        _stats[4],
        _stats[5],
        _stats[6],
        _tags.OrderBy(value => Array.IndexOf(Fo1CharacterProfile.SkillNames, value)).ToArray(),
        _traits.OrderBy(value => Array.IndexOf(Fo1CharacterProfile.TraitNames, value)).ToArray());

    private Fo1CharacterProfile BuildProfile()
    {
        var profile = PreviewProfile();
        profile.Validate();
        return profile;
    }

    private void CompleteInteractive()
    {
        try
        {
            var profile = BuildProfile();
            _info.Text = "DONE\n\nCharacter accepted. Beginning the Vault Overseer briefing.";
            CharacterReady?.Invoke(profile);
        }
        catch (Exception exception)
        {
            _info.Text = $"CHARACTER INCOMPLETE\n\n{exception.Message}";
        }
    }

    private void UpdateCreatorNumbers()
    {
        var canvas = Image.CreateEmpty(640, 480, false, Image.Format.Rgba8);
        canvas.BlitRect(
            _creatorChromeImage,
            new Rect2I(0, 0, 640, 480),
            Vector2I.Zero);
        for (var index = 0; index < _stats.Length; index++)
            DrawCreatorNumber(
                canvas,
                _stats[index],
                _contract.CreatorNumbers.Layout.Special[index],
                _contract.CreatorNumbers.SpecialDigitStride);

        var points = Math.Clamp(PointsRemaining, 0, 99);
        var pointDigits = new[] { points / 10, points % 10 };
        for (var index = 0; index < pointDigits.Length; index++)
            DrawCreatorDigit(
                canvas,
                pointDigits[index],
                _contract.CreatorNumbers.Layout.CharacterPoints[index]);
        _creatorChromeTexture.Update(canvas);
    }

    private void DrawCreatorNumber(
        Image canvas,
        int value,
        Fo1HudPoint destination,
        int digitStride)
    {
        var normalized = Math.Clamp(value, 0, 99);
        DrawCreatorDigit(canvas, normalized / 10, destination);
        DrawCreatorDigit(
            canvas,
            normalized % 10,
            new Fo1HudPoint(
                destination.X + digitStride,
                destination.Y));
    }

    private void DrawCreatorDigit(Image canvas, int digit, Fo1HudPoint destination)
    {
        var numbers = _contract.CreatorNumbers;
        canvas.BlitRect(
            _creatorNumberAtlas,
            new Rect2I(
                numbers.WhiteOffsetX + Math.Clamp(digit, 0, 9) * numbers.DigitWidth,
                0,
                numbers.DigitWidth,
                numbers.Atlas.Height),
            destination.Pixels);
    }

    private Label AddText(
        string text,
        float x,
        float y,
        float width,
        float height,
        Color color,
        int fontSize,
        HorizontalAlignment alignment = HorizontalAlignment.Left,
        bool wrap = false)
    {
        var label = new Label
        {
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _canvas.AddChild(label);
        return label;
    }

    private void AddMask(float x, float y, float width, float height)
    {
        _canvas.AddChild(new ColorRect
        {
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = new Color(0.008f, 0.012f, 0.009f, 0.94f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }

    private Button AddFlatButton(
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
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeColorOverride("font_color", Green);
        button.AddThemeColorOverride("font_hover_color", Amber);
        button.AddThemeColorOverride("font_pressed_color", Amber);
        button.AddThemeColorOverride("font_outline_color", Colors.Black);
        button.AddThemeConstantOverride("outline_size", 3);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.Pressed += pressed;
        _canvas.AddChild(button);
        return button;
    }

    private void AddSourceButton(Fo1HudRect bounds, string tooltip, Action pressed)
    {
        var button = new Button
        {
            Position = new Vector2(bounds.X, bounds.Y),
            Size = new Vector2(bounds.Width, bounds.Height),
            Text = "",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = tooltip,
        };
        button.Pressed += pressed;
        _canvas.AddChild(button);
    }

    private void AddSmallButton(string text, float x, float y, Action pressed) =>
        AddFlatButton(text, x, y, 18, 20, pressed, 11);

    private static void StyleLineEdit(LineEdit lineEdit)
    {
        lineEdit.AddThemeColorOverride("font_color", Green);
        lineEdit.AddThemeColorOverride("font_selected_color", Colors.Black);
        lineEdit.AddThemeColorOverride("selection_color", Amber);
        lineEdit.AddThemeColorOverride("caret_color", Amber);
        lineEdit.AddThemeFontSizeOverride("font_size", 11);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.01f, 0.015f, 0.01f, 0.82f),
            BorderColor = new Color(0.24f, 0.32f, 0.18f),
        };
        style.SetBorderWidthAll(1);
        lineEdit.AddThemeStyleboxOverride("normal", style);
        lineEdit.AddThemeStyleboxOverride("focus", style);
    }

    private static async Task WaitFrames(Node host, int count)
    {
        for (var frame = 0; frame < count; frame++)
        {
            if (DisplayServer.GetName() == "headless")
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            else
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
}
