using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1PipBoy2000NumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float SourcePresentationFloat0Point09f = 0.09f;
    internal const float SourcePresentationFloat0Point22f = 0.22f;
    internal const float SourcePresentationFloat0Point25f = 0.25f;
    internal const float SourcePresentationFloat0Point34f = 0.34f;
    internal const float SourcePresentationFloat0Point54f = 0.54f;
    internal const float SourcePresentationFloat0Point5f = 0.5f;
    internal const float SourcePresentationFloat0Point76f = 0.76f;
    internal const float SourcePresentationFloat0Point91f = 0.91f;
    internal const float SourcePresentationFloat0Point94f = 0.94f;
    internal const float SourcePresentationFloat0Point95f = 0.95f;
    internal const int SourcePresentationInt10 = 10;
    internal const int SourcePresentationInt12 = 12;
    internal const float SourcePresentationFloat122Point0f = 122.0f;
    internal const int SourcePresentationInt140 = 140;
    internal const float SourcePresentationFloat22Point0f = 22.0f;
    internal const float SourcePresentationFloat24Point0f = 24.0f;
    internal const float SourcePresentationFloat269Point0f = 269.0f;
    internal const float SourcePresentationFloat270Point0f = 270.0f;
    internal const float SourcePresentationFloat28Point0f = 28.0f;
    internal const float SourcePresentationFloat29Point0f = 29.0f;
    internal const float SourcePresentationFloat319Point0f = 319.0f;
    internal const float SourcePresentationFloat320Point0f = 320.0f;
    internal const float SourcePresentationFloat322Point0f = 322.0f;
    internal const float SourcePresentationFloat326Point0f = 326.0f;
    internal const float SourcePresentationFloat33Point0f = 33.0f;
    internal const float SourcePresentationFloat331Point0f = 331.0f;
    internal const float SourcePresentationFloat336Point0f = 336.0f;
    internal const float SourcePresentationFloat361Point0f = 361.0f;
    internal const float SourcePresentationFloat37Point0f = 37.0f;
    internal const float SourcePresentationFloat375Point0f = 375.0f;
    internal const float SourcePresentationFloat391Point0f = 391.0f;
    internal const float SourcePresentationFloat400Point0f = 400.0f;
    internal const float SourcePresentationFloat423Point0f = 423.0f;
    internal const float SourcePresentationFloat424Point0f = 424.0f;
    internal const float SourcePresentationFloat480Point0f = 480.0f;
    internal const float SourcePresentationFloat50Point0f = 50.0f;
    internal const float SourcePresentationFloat52Point0f = 52.0f;
    internal const float SourcePresentationFloat61Point0f = 61.0f;
    internal const float SourcePresentationFloat640Point0f = 640.0f;
    internal const float SourcePresentationFloat82Point0f = 82.0f;
    internal const int SourcePresentationInt9 = 9;
    internal const float SourcePresentationFloat9Point0f = 9.0f;
}

internal partial class Fo1PipBoy2000 : CanvasLayer
{
    private static readonly Color Amber = new(Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point91f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point76f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point25f);
    private static readonly Color Green = new(Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point54f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point94f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point34f);
    private Fo1CharacterStartContract _contract = null!;
    private Fo1TacticalSession _session = null!;
    private Fo1CharacterProfile _profile = null!;
    private Control _canvas = null!;
    private Label _pageTitle = null!;
    private Label _pageText = null!;
    private Label _pageIndicator = null!;
    private ColorRect _radioIndicator = null!;
    private bool _hudWasVisible;
    private string _selectedPage = "STATUS";
    private int _openedCount;

    internal bool IsOpen => Visible;
    internal string SelectedPage => _selectedPage;
    internal int OpenedCount => _openedCount;

    internal void Configure(
        Fo1CharacterStartContract contract,
        Fo1TacticalSession session,
        Fo1CharacterProfile profile)
    {
        _contract = contract;
        _session = session;
        _profile = profile;
        Name = "OwnedFalloutPipBoy2000";
        Layer = Fo1PipBoy2000NumericContracts.SourcePresentationInt140;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        Build();
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!Visible || inputEvent is not InputEventKey key ||
            !key.Pressed || key.Echo)
            return;
        if (key.PhysicalKeycode is Key.P or Key.Escape)
        {
            SetOpen(false);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        if (GetTree().Paused)
            GetTree().Paused = false;
    }

    internal void Toggle() => SetOpen(!Visible);

    internal void SetOpen(bool open)
    {
        if (Visible == open)
            return;
        if (open)
        {
            _openedCount++;
            _hudWasVisible = _session.Hud.Visible;
            _session.Hud.Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Visible = true;
            ShowPage(_selectedPage);
            GetTree().Paused = true;
        }
        else
        {
            Visible = false;
            GetTree().Paused = false;
            _session.Hud.Visible = _hudWasVisible;
        }
    }

    internal void ShowPage(string page)
    {
        if (!_contract.PipBoy.Pages.Contains(page, StringComparer.Ordinal))
            throw new InvalidOperationException($"Unsupported Pip-Boy 2000 page: {page}");
        _selectedPage = page;
        _pageTitle.Text = $"PIP-BOY 2000  /  {page}";
        _pageIndicator.Text = $"{page}  •  P / ESC CLOSE";
        _radioIndicator.Position = new Vector2(Fo1PipBoy2000NumericContracts.SourcePresentationFloat50Point0f, page switch
        {
            "STATUS" => Fo1PipBoy2000NumericContracts.SourcePresentationFloat331Point0f,
            "AUTOMAPS" => Fo1PipBoy2000NumericContracts.SourcePresentationFloat375Point0f,
            _ => Fo1PipBoy2000NumericContracts.SourcePresentationFloat400Point0f,
        });
        _pageText.Text = page switch
        {
            "STATUS" => StatusText(),
            "AUTOMAPS" => AutomapText(),
            "ARCHIVES" => ArchivesText(),
            _ => throw new InvalidOperationException($"Unsupported Pip-Boy 2000 page: {page}"),
        };
    }

    internal object Report() => new
    {
        model = "Pip-Boy 2000",
        authenticOwnedChrome = true,
        pages = _contract.PipBoy.Pages,
        selectedPage = _selectedPage,
        isOpen = IsOpen,
        openedCount = _openedCount,
        source = _contract.PipBoy.Report(),
    };

    private void Build()
    {
        var black = new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(black);
        _canvas = new Control { Size = new Vector2(Fo1PipBoy2000NumericContracts.SourcePresentationFloat640Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat480Point0f) };
        AddChild(_canvas);
        LayoutCanvas();
        _canvas.AddChild(new TextureRect
        {
            Name = "OwnedPipBoy2000Chrome",
            Texture = _contract.PipBoy.Main.Load(),
            Size = new Vector2(Fo1PipBoy2000NumericContracts.SourcePresentationFloat640Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat480Point0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _pageTitle = AddText("", Fo1PipBoy2000NumericContracts.SourcePresentationFloat270Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat52Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat320Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat24Point0f, Amber, Fo1PipBoy2000NumericContracts.SourcePresentationInt12);
        _pageText = AddText("", Fo1PipBoy2000NumericContracts.SourcePresentationFloat269Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat82Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat326Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat336Point0f, Green, Fo1PipBoy2000NumericContracts.SourcePresentationInt10);
        _pageText.VerticalAlignment = VerticalAlignment.Top;
        _pageText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageIndicator = AddText("", Fo1PipBoy2000NumericContracts.SourcePresentationFloat270Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat424Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat322Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat22Point0f, Amber, Fo1PipBoy2000NumericContracts.SourcePresentationInt9);
        _pageIndicator.HorizontalAlignment = HorizontalAlignment.Center;
        _radioIndicator = new ColorRect
        {
            Name = "ActivePipBoyPageLamp",
            Position = new Vector2(Fo1PipBoy2000NumericContracts.SourcePresentationFloat50Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat331Point0f),
            Size = new Vector2(Fo1PipBoy2000NumericContracts.SourcePresentationFloat9Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat9Point0f),
            Color = new Color(1.0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point22f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point09f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point95f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_radioIndicator);

        AddHotspot("STATUS", Fo1PipBoy2000NumericContracts.SourcePresentationFloat61Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat319Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat122Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat37Point0f, () => ShowPage("STATUS"));
        AddHotspot("AUTOMAPS", Fo1PipBoy2000NumericContracts.SourcePresentationFloat61Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat361Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat122Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat29Point0f, () => ShowPage("AUTOMAPS"));
        AddHotspot("ARCHIVES", Fo1PipBoy2000NumericContracts.SourcePresentationFloat61Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat391Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat122Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat28Point0f, () => ShowPage("ARCHIVES"));
        AddHotspot("CLOSE", Fo1PipBoy2000NumericContracts.SourcePresentationFloat61Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat423Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat122Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat33Point0f, () => SetOpen(false));
    }

    private void LayoutCanvas()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var scale = MathF.Min(viewport.X / Fo1PipBoy2000NumericContracts.SourcePresentationFloat640Point0f, viewport.Y / Fo1PipBoy2000NumericContracts.SourcePresentationFloat480Point0f);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position = (viewport - new Vector2(Fo1PipBoy2000NumericContracts.SourcePresentationFloat640Point0f, Fo1PipBoy2000NumericContracts.SourcePresentationFloat480Point0f) * scale) * Fo1PipBoy2000NumericContracts.SourcePresentationFloat0Point5f;
    }

    private string StatusText()
    {
        var skills = _profile.Skills();
        return
            $"{_profile.Name.ToUpperInvariant()}  •  {_profile.Sex.ToUpperInvariant()}  •  AGE {_profile.Age}\n" +
            "05 DEC 2161  •  VAULT 13 ENTRANCE\n\n" +
            $"ST {_profile.EffectiveStrength:00}   PE {_profile.EffectivePerception:00}   " +
            $"EN {_profile.EffectiveEndurance:00}   CH {_profile.EffectiveCharisma:00}\n" +
            $"IN {_profile.EffectiveIntelligence:00}   AG {_profile.EffectiveAgility:00}   " +
            $"LK {_profile.EffectiveLuck:00}\n\n" +
            $"HIT POINTS       {_session.PlayerHitPoints:00}/{_profile.HitPoints:00}\n" +
            $"ARMOR CLASS      {_profile.ArmorClass:00}\n" +
            $"ACTION POINTS    {_session.ActionPoints:00}/{_profile.ActionPoints:00}\n" +
            $"SEQUENCE         {_profile.Sequence:00}\n" +
            $"CARRY WEIGHT     {_profile.CarryWeight:000}\n\n" +
            $"TAGGED  {string.Join(" • ", _profile.TaggedSkills)}\n" +
            $"TRAITS  {(_profile.Traits.Count == 0 ? "NONE" : string.Join(" • ", _profile.Traits))}\n\n" +
            $"SMALL GUNS {skills["Small Guns"],3}%    FIRST AID {skills["First Aid"],3}%\n" +
            $"SNEAK      {skills["Sneak"],3}%    LOCKPICK  {skills["Lockpick"],3}%\n" +
            $"SCIENCE    {skills["Science"],3}%    REPAIR    {skills["Repair"],3}%\n" +
            $"SPEECH     {skills["Speech"],3}%    BARTER    {skills["Barter"],3}%";
    }

    private string AutomapText()
    {
        var player = _session.PlayerTile;
        var living = _session.Mobs.Count(mob => mob.Alive);
        return
            "LOCAL MAP DATA\n\n" +
            "VAULT 13 ENTRANCE  /  V13ENT\n" +
            "SOURCE GRID 200 × 200 HEXES\n\n" +
            $"CURRENT HEX     {player:00000}\n" +
            $"COORDINATES     {player % 200:000}, {player / 200:000}\n" +
            $"VAULT DOOR HEX  {_session.DoorTile:00000}\n" +
            $"LIVING HOSTILES {living:00}\n\n" +
            "TACTICAL MODE uses the same authoritative hex centers shown by G. " +
            "FPS mode moves continuously over that walk mask and rejoins the nearest valid center when you return.";
    }

    private string ArchivesText() =>
        "DATA ARCHIVES\n\n" +
        "01  OVERSEER BRIEFING\n" +
        "    Owned original Fallout recording\n\n" +
        "02  VAULT 13 MISSION\n" +
        "    Locate a replacement water purification control chip.\n\n" +
        "03  SELECTED DWELLER\n" +
        $"    {_profile.Name} / TAGGED: {string.Join(", ", _profile.TaggedSkills)}\n\n" +
        "This bounded slice exposes live Status, V13ENT Automap data, and Archives. " +
        "The same surface is designed to become the wrist-mounted VR interface later.";

    private Label AddText(
        string text,
        float x,
        float y,
        float width,
        float height,
        Color color,
        int fontSize)
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
        _canvas.AddChild(label);
        return label;
    }

    private void AddHotspot(
        string label,
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
            Text = "",
            TooltipText = label,
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.Pressed += pressed;
        _canvas.AddChild(button);
    }
}
