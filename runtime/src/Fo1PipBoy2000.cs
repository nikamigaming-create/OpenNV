using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1PipBoy2000 : CanvasLayer
{
    private static readonly Color Amber = new(0.91f, 0.76f, 0.25f);
    private static readonly Color Green = new(0.54f, 0.94f, 0.34f);
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
        Layer = 140;
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
        _radioIndicator.Position = new Vector2(50.0f, page switch
        {
            "STATUS" => 331.0f,
            "AUTOMAPS" => 375.0f,
            _ => 400.0f,
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
        _canvas = new Control { Size = new Vector2(640.0f, 480.0f) };
        AddChild(_canvas);
        LayoutCanvas();
        _canvas.AddChild(new TextureRect
        {
            Name = "OwnedPipBoy2000Chrome",
            Texture = _contract.PipBoy.Main.Load(),
            Size = new Vector2(640.0f, 480.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _pageTitle = AddText("", 270.0f, 52.0f, 320.0f, 24.0f, Amber, 12);
        _pageText = AddText("", 269.0f, 82.0f, 326.0f, 336.0f, Green, 10);
        _pageText.VerticalAlignment = VerticalAlignment.Top;
        _pageText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageIndicator = AddText("", 270.0f, 424.0f, 322.0f, 22.0f, Amber, 9);
        _pageIndicator.HorizontalAlignment = HorizontalAlignment.Center;
        _radioIndicator = new ColorRect
        {
            Name = "ActivePipBoyPageLamp",
            Position = new Vector2(50.0f, 331.0f),
            Size = new Vector2(9.0f, 9.0f),
            Color = new Color(1.0f, 0.22f, 0.09f, 0.95f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_radioIndicator);

        AddHotspot("STATUS", 61.0f, 319.0f, 122.0f, 37.0f, () => ShowPage("STATUS"));
        AddHotspot("AUTOMAPS", 61.0f, 361.0f, 122.0f, 29.0f, () => ShowPage("AUTOMAPS"));
        AddHotspot("ARCHIVES", 61.0f, 391.0f, 122.0f, 28.0f, () => ShowPage("ARCHIVES"));
        AddHotspot("CLOSE", 61.0f, 423.0f, 122.0f, 33.0f, () => SetOpen(false));
    }

    private void LayoutCanvas()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var scale = MathF.Min(viewport.X / 640.0f, viewport.Y / 480.0f);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position = (viewport - new Vector2(640.0f, 480.0f) * scale) * 0.5f;
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
