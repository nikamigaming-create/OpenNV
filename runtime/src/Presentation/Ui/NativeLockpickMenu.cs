using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using System.Xml.Linq;

namespace OpenNV.Runtime.Presentation.Ui;

// Deliberately minimal first-party presentation of the shared picking owner.
// Retail 3D model, animation, haptics and complete audio remain separate.
internal sealed partial class NativeLockpickMenu : Control
{
    private static readonly string[] TurnActions =
    [RuntimeNativeInputControls.Action(2), RuntimeNativeInputControls.Action(3), RuntimeNativeInputControls.Action(0), RuntimeNativeInputControls.Action(1)];
    private readonly FalloutLockpickSession _session;
    private readonly Action<bool> _closed;
    private readonly Action<Exception> _failed;
    private bool _retired;
    internal float MouseScale { get; set; } = 1;
    private float _mouseDelta;
    private readonly Label _status;
    private Viewport? _viewport;

    internal static (float Width, float Position, float MouseScale) ReadGeometry(Vector2 display)
    {
        if (!float.IsFinite(display.X) || !float.IsFinite(display.Y) || display.X <= 0 || display.Y <= 0)
            throw new InvalidDataException("Lockpick display dimensions are invalid.");
        var root = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/lockpick_menu.xml")).Elements("menu").Single();
        XElement Tile(string name) => root.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        var uiWidth = display.X > display.Y ? display.X / display.Y * 960 : 1280;
        var scale = uiWidth / display.X;
        var tiles = new NativeOwnedMenuTree(root) { Screen = new(uiWidth, display.Y * scale) };
        var meter = Tile("LPM_DetectMeter"); var arrow = Tile("LPM_DetectArrow");
        if ((int?)meter.Element("id") != 14 || (int?)arrow.Element("id") != 15 ||
            (int?)Tile("LPM_DetectArea").Element("id") != 13)
            throw new NotSupportedException("The winning lockpick input tile identities differ from the admitted owner.");
        return (tiles.Number(meter, "width"), tiles.Number(arrow, "_x"), scale);
    }

    internal NativeLockpickMenu(FalloutLockpickSession session, Action<bool> closed, Action<Exception> failed)
    {
        _session = session; _closed = closed; _failed = failed;
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new(0, 0, 0, .95f), MouseFilter = MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var panel = new VBoxContainer { Position = new(80, 80), Size = new(640, 240) };
        AddChild(panel);
        panel.AddChild(new Label { Text = $"Pick lock — Force chance {session.Chance}%" });
        panel.AddChild(new Label
        {
            Text = session.ManualReady ?
            "Move the mouse to position the pick. Hold a movement control to turn.\nF forces the lock; E / Escape exits." :
            "Manual picking is not available in this session.\nA failed force attempt breaks the lock."
        });
        _status = new Label(); panel.AddChild(_status);
        var force = new Button { Text = "Force [F]" };
        var cancel = new Button { Text = "Exit [E / Esc]" };
        panel.AddChild(force); panel.AddChild(cancel);
        force.Pressed += Force; cancel.Pressed += Cancel;
    }

    private void Force()
    {
        if (_retired || _session.Turning || _session.ManualPhase is 3 or 4) return;
        try { if (!_session.Force()) Retire(false); }
        catch (Exception error)
        {
            try { Retire(false); }
            finally { _failed(error); }
        }
    }

    private void Cancel() { _session.Cancel(); Retire(false); }

    public override void _Ready()
    {
        _viewport = GetViewport();
        _viewport.SizeChanged += Cancel;
    }

    public override void _ExitTree()
    {
        try { if (_viewport is not null && GodotObject.IsInstanceValid(_viewport)) _viewport.SizeChanged -= Cancel; }
        finally { Cancel(); }
    }

    public override void _Process(double delta)
    {
        if (_retired) return;
        try
        {
            if (_session.ManualReady)
            {
                var pressure = TurnActions.Any(action => Input.IsActionPressed(action));
                _session.AdvanceManual(FalloutSourceMilliseconds.Read(), _mouseDelta * MouseScale, pressure);
                _mouseDelta = 0;
                _status.Text = $"Pick position: {_session.Position:F1} / {_session.Width:F1}\nTurn: {_session.CylinderAngle:F1}°    Pin health: {_session.PinHealth:F2}";
                if (_session.Finished) Retire(_session.Won);
            }
            else if (_session.Turning)
            {
                _session.Advance(FalloutSourceMilliseconds.Read());
                if (_session.Finished) Retire(true);
            }
        }
        catch (Exception error)
        {
            try { Retire(false); }
            finally { _failed(error); }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_retired && @event is InputEventMouseMotion motion && _session.ManualReady)
            _mouseDelta += motion.ScreenRelative.X;
    }

    private void Retire(bool success)
    {
        if (_retired) return;
        _retired = true;
        _closed(success);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.F) Force();
        else if (key.PhysicalKeycode is Key.E or Key.Escape) Cancel();
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) Cancel();
        else if (what == NotificationApplicationFocusOut) Cancel();
    }
}
