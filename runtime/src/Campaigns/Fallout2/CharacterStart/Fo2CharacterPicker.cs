using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed partial class Fo2CharacterPicker : Control
{
    private const float SourceWidth = 640.0f;
    private const float SourceHeight = 480.0f;
    private readonly Fo2CharacterStartCatalog _catalog;
    private readonly Control _canvas;
    private readonly TextureRect _panel;
    private readonly Label _details;
    private readonly Label _selection;
    private int _index;

    internal Fo2CharacterPicker(Fo2CharacterStartCatalog catalog)
    {
        _catalog = catalog;
        Name = "FALLOUT_2_OWNED_PREMADE_CHARACTER_START";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect
        {
            Name = "CharacterStartBlackBackground",
            Color = Colors.Black,
            MouseFilter = MouseFilterEnum.Ignore,
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.FullRect,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
        });
        _canvas = new Control
        {
            Name = "OwnedSource640x480Canvas",
            Size = new Vector2(SourceWidth, SourceHeight),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(_canvas);
        _canvas.AddChild(new TextureRect
        {
            Name = "OwnedPickcharBackground",
            Texture = catalog.Picker.Load(),
            Size = new Vector2(SourceWidth, SourceHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _panel = new TextureRect
        {
            Name = "OwnedPremadePanel",
            Position = new Vector2(24.0f, 20.0f),
            Size = new Vector2(592.0f, 260.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_panel);
        _details = new Label
        {
            Name = "OwnedPremadeGcdState",
            Position = new Vector2(305.0f, 35.0f),
            Size = new Vector2(300.0f, 225.0f),
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _details.AddThemeColorOverride("font_color", new Color("78e781"));
        _details.AddThemeColorOverride("font_outline_color", Colors.Black);
        _details.AddThemeConstantOverride("outline_size", 2);
        _details.AddThemeConstantOverride("line_spacing", -1);
        _details.AddThemeFontSizeOverride("font_size", 9);
        _canvas.AddChild(_details);
        _selection = new Label
        {
            Name = "SelectedPremadeState",
            Position = new Vector2(250.0f, 279.0f),
            Size = new Vector2(140.0f, 22.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _selection.AddThemeColorOverride("font_color", new Color("78e781"));
        _selection.AddThemeColorOverride("font_outline_color", Colors.Black);
        _selection.AddThemeConstantOverride("outline_size", 3);
        _selection.AddThemeFontSizeOverride("font_size", 12);
        _canvas.AddChild(_selection);
        AddButton("◀", 270.0f, 303.0f, 35.0f, 35.0f, () => Select(_index - 1));
        AddButton("▶", 335.0f, 303.0f, 35.0f, 35.0f, () => Select(_index + 1));
        AddButton("", 65.0f, 301.0f, 181.0f, 79.0f, ChooseCurrent)
            .TooltipText = "Take this owned Fallout 2 premade";
        AddButton("", 443.0f, 301.0f, 153.0f, 79.0f, () => OpenCustom(true))
            .TooltipText = "Modify this owned Fallout 2 premade";
        AddButton("", 65.0f, 397.0f, 181.0f, 63.0f, () => OpenCustom(false))
            .TooltipText = "Create a custom Chosen One from the owned rules";
        AddButton("", 443.0f, 397.0f, 153.0f, 63.0f, () => BackRequested?.Invoke())
            .TooltipText = "Back";
        Select(0);
    }

    internal event Action<Fo2CharacterSelection>? CharacterChosen;
    internal event Action? BackRequested;
    internal int SelectedIndex => _index;
    internal Fo2PremadeCharacter Selected => _catalog.Characters[_index];
    internal Fo2CustomCharacterEditor? CustomEditor { get; private set; }

    public override void _Ready() => FitCanvas();

    public override void _Notification(int what)
    {
        if (what == NotificationResized && IsInsideTree())
            FitCanvas();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        switch (code)
        {
            case Key.Left:
            case Key.A:
                Select(_index - 1);
                break;
            case Key.Right:
            case Key.D:
                Select(_index + 1);
                break;
            case Key.Enter:
            case Key.KpEnter:
            case Key.Space:
                ChooseCurrent();
                break;
            case Key.Escape:
                BackRequested?.Invoke();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    internal void Select(int index)
    {
        _index = (index % _catalog.Characters.Count + _catalog.Characters.Count) %
            _catalog.Characters.Count;
        var character = Selected;
        var profile = character.Profile;
        _panel.Texture = character.Panel.Load();
        _selection.Text =
            $"{profile.Name.ToUpperInvariant()}  {_index + 1}/{_catalog.Characters.Count}";
        var biography = string.Join(
            " ",
            character.Biography.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        _details.Text =
            $"{profile.Name.ToUpperInvariant()}  •  {character.Role.ToUpperInvariant()}\n" +
            $"{profile.Sex.ToUpperInvariant()}  •  AGE {profile.Age}\n" +
            $"ST {profile.Special[0]:00}  PE {profile.Special[1]:00}  " +
            $"EN {profile.Special[2]:00}  CH {profile.Special[3]:00}\n" +
            $"IN {profile.Special[4]:00}  AG {profile.Special[5]:00}  " +
            $"LK {profile.Special[6]:00}\n" +
            $"TAGGED  {string.Join(" • ", profile.TaggedSkills)}\n" +
            $"TRAITS  {string.Join(" • ", profile.Traits)}\n\n" +
            biography;
        SetMeta("selected_character", profile.Name);
        SetMeta("selected_sex", profile.Sex);
        SetMeta("selected_gcd_sha256", character.GcdSha256);
    }

    internal void ChooseCurrent()
    {
        Selected.Profile.Validate();
        CharacterChosen?.Invoke(Fo2CharacterSelection.FromPremade(Selected));
    }

    internal Fo2CustomCharacterEditor OpenCustom(bool modify)
    {
        if (CustomEditor is not null)
            throw new InvalidOperationException(
                "Fallout 2 custom character editor is already open.");
        SetProcessInput(false);
        _canvas.Visible = false;
        var editor = new Fo2CustomCharacterEditor(_catalog, Selected, modify);
        editor.Confirmed += selection =>
        {
            selection.Validate(_catalog);
            CharacterChosen?.Invoke(selection);
        };
        editor.Cancelled += CloseCustom;
        CustomEditor = editor;
        AddChild(editor);
        return editor;
    }

    private void CloseCustom()
    {
        if (CustomEditor is null)
            return;
        CustomEditor.QueueFree();
        CustomEditor = null;
        _canvas.Visible = true;
        SetProcessInput(true);
    }

    private Button AddButton(
        string text,
        float x,
        float y,
        float width,
        float height,
        Action pressed)
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
        button.AddThemeFontSizeOverride("font_size", 18);
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
