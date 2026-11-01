using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeVatsMenu : Control
{
    internal const uint MenuId = 1056;
    private readonly FalloutVatsSession _session;
    private readonly Func<IReadOnlyList<FalloutVatsSession.Target>> _targets;
    private readonly Func<bool> _preparing;
    private readonly Func<FalloutVatsSession.Target, Vector2?> _project;
    private readonly Action<FalloutVatsSession.Target> _queue;
    private readonly Action _execute, _cancel;
    private readonly Action<Exception> _failed;
    private readonly NativeOwnedMenuTree _tiles;
    private readonly FalloutInstallationSettings _settings;
    private readonly XElement _glow, _partTemplate, _queueTemplate, _queued, _notice, _accept;
    private readonly FalloutMenuPcButtonBindings<Key> _shortcuts;
    private readonly List<(FalloutVatsSession.Target Target, XElement Tile, XElement Text)> _parts = [];
    private readonly List<XElement> _rows = [];
    private FalloutFormKey? _selectedActor;
    private byte? _selectedPart;
    private NativeViewportLayout? _layout;
    private bool _closed, _faulted;

    internal NativeVatsMenu(FalloutVatsSession session, Func<IReadOnlyList<FalloutVatsSession.Target>> targets,
        Func<bool> preparing, Action<FalloutVatsSession.Target> queue, Action execute, Action cancel,
        Func<FalloutVatsSession.Target, Vector2?> project, FalloutPluginStack records,
        Func<GameplayVitals> vitals, Func<NativeHudAmmo?> ammunition, Func<FalloutPlayerControlState> controls,
        Action<Exception> failed)
    {
        _session = session; _targets = targets; _preparing = preparing; _queue = queue;
        _execute = execute; _cancel = cancel; _project = project;
        _failed = failed;
        Name = "VatsMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var source = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/vats_menu.xml")).Elements("menu").Single();
        _settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        _tiles = new(source, setting => FalloutGameSettingStrings.Read(records, setting));
        XElement Named(string name) => source.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _glow = Named("GlowBranch"); _partTemplate = new(Named("body_part_percent").Elements().Single());
        _queueTemplate = new(Named("queued_action").Elements().Single()); _queued = Named("queued_actions");
        _accept = Named("accept_button");
        _notice = new(Named("template_aiming_text").Elements().Single()); _glow.Add(_notice);
        _tiles.Bind(source, "alpha", 255); _tiles.Bind(source, "visible", 1);
        _tiles.Bind(_queued, "alpha", 255);
        foreach (var name in new[] { "special_attack_button", "special_attack_2_button" }) _tiles.Bind(Named(name), "visible", 0);
        _shortcuts = new(source, key => Enum.TryParse<Key>(key, true, out var parsed) ? parsed :
            throw new NotSupportedException("VATS source PC key is unowned: " + key), declaration => declaration.TargetName switch
            {
                "accept_button" => () => Run(_execute),
                "return_button" => Return,
                _ => () => Run(() => throw new NotSupportedException("VATS source special action has no gameplay producer: " + declaration.TargetName))
            });
        source.Descendants("template").Remove();
        SetMeta("opennv_menu_id", MenuId);
        SetMeta("opennv_ui_source", "menus/vats_menu.xml;body_part_percent;queued_action;source-PC-shortcuts");
        SetMeta("opennv_ui_unverified", "matched-percent-layout;native-body-boxes;weapon-part-node;limb-meter;focus-fades;camera-phase;CPTH;gamepad-queue;native-stick-selection;physical-XR");
        // The ordinary HUD is hidden while modal/paused. Reuse its actual
        // source HP/AP/ammo owner rather than inventing replacement meters.
        var hudLayer = new CanvasLayer { Name = "VatsSharedHud", Layer = 98, ProcessMode = ProcessModeEnum.Always };
        AddChild(hudLayer);
        hudLayer.AddChild(new NativeOwnedGameplayHud(records, "", () => null,
            () => _session.CurrentPhase == FalloutVatsSession.Phase.Selecting,
            vitals, ammunition, controls: controls, showReticle: false));
        Refresh();
    }

    private FalloutVatsSession.Target? Selected => _parts.Select(value => value.Target)
        .FirstOrDefault(target => target.Reference == _selectedActor && target.Part == _selectedPart);

    internal void Refresh()
    {
        // Queue/text refreshes do not retire the current target's native part
        // placement. Hidden parts retain it until their next admitted point.
        var previousActor = _parts.FirstOrDefault().Target?.Reference;
        var previous = _parts.ToDictionary(value => value.Target.Part,
            value => new Vector2(_tiles.Number(value.Tile, "x"), _tiles.Number(value.Tile, "y")));
        _tiles.Forget(_parts.Select(value => value.Tile).Concat(_rows));
        foreach (var value in _parts) value.Tile.Remove();
        foreach (var row in _rows) row.Remove();
        _parts.Clear(); _rows.Clear();
        var choices = _targets();
        if (_selectedActor is null || !choices.Any(value => value.Reference == _selectedActor))
            _selectedActor = choices.FirstOrDefault()?.Reference;
        if (_session.CurrentPhase == FalloutVatsSession.Phase.Selecting)
        {
            // Native construction walks ascending source type slots and prepends.
            foreach (var target in choices.Where(value => value.Reference == _selectedActor).OrderByDescending(value => value.Part))
            {
                var tile = new XElement(_partTemplate);
                tile.SetAttributeValue("name", $"part_{target.Reference.ObjectId:x6}_{target.Part}");
                _glow.Add(tile);
                if (previousActor == target.Reference && previous.TryGetValue(target.Part, out var placement))
                { _tiles.Bind(tile, "x", placement.X); _tiles.Bind(tile, "y", placement.Y); }
                _tiles.Bind(tile, "alpha", 255); _tiles.Bind(tile, "visible", 1);
                _tiles.BindText(tile, "_LimbName", target.PartName);
                _tiles.Bind(tile, "_LimbNameVisible", 1);
                _tiles.Text[tile] = $"{target.Chance:0}%";
                // Bracket assets/geometry come from the owned template. Limb
                // health and body-box highlight require separate live producers.
                var meter = tile.Elements().Single(value => (string?)value.Attribute("name") == "meter");
                _tiles.Bind(meter, "visible", 0);
                var text = tile.Descendants("text").Single(value => (string?)value.Attribute("name") == "chance_to_hit");
                _parts.Add((target, tile, text));
            }
        }
        if (!_parts.Any(value => value.Target.Part == _selectedPart)) _selectedPart = _parts.FirstOrDefault().Target?.Part;
        var queue = _session.Queue;
        foreach (var attack in queue)
        {
            var row = new XElement(_queueTemplate); row.SetAttributeValue("name", "queued_" + attack.Ordinal);
            _queued.Add(row); _rows.Add(row);
            _tiles.Bind(row, "listindex", _rows.Count - 1);
            _tiles.BindText(row, "string", attack.Target.ActorName + " — " + attack.Target.PartName);
            var text = row.Descendants("text").Single(value => (string?)value.Attribute("name") == "ListItemText");
            _tiles.Bind(row, "height", _tiles.Number(text, "height"));
        }
        _tiles.Bind(_queued, "height", _rows.Sum(row => _tiles.Number(row, "height")));
        _tiles.Bind(_queued, "childcount", _rows.Count + 1);
        _tiles.Bind(_accept, "target", queue.Count > 0 ? 1 : 0);
        Notice(_preparing() ? "Scanning targets…" : choices.Count == 0 ? "No targets in range." : SelectionText());
        PositionParts(); QueueRedraw();
    }

    private void PositionParts()
    {
        if (!IsInsideTree()) return;
        var physical = GetViewportRect().Size;
        var wide = (double)physical.Y / physical.X != .75;
        var crop = new System.Numerics.Vector2(_settings.Number("Interface", wide ? "iSafeZoneXWide" : "iSafeZoneX"),
            _settings.Number("Interface", wide ? "iSafeZoneYWide" : "iSafeZoneY"));
        var samples = _parts.Select(value =>
        {
            var point = _project(value.Target);
            _tiles.Bind(value.Tile, "visible", point is null ? 0 : 1);
            var local = point is { } pixel ? (Vector2?)(GetGlobalTransformWithCanvas().AffineInverse() * pixel) : null;
            return new FalloutVatsLabelSample(value.Target.Part,
                local is { } projected ? new System.Numerics.Vector2(projected.X, projected.Y) : null,
                new(_tiles.Number(value.Tile, "x"), _tiles.Number(value.Tile, "y")));
        }).ToArray();
        var actionPoints = _glow.Elements().Single(tile => (string?)tile.Attribute("name") == "ActionPoints");
        var layout = FalloutVatsLabelLayout.Calculate(new(_tiles.Screen.X, _tiles.Screen.Y), crop,
            _tiles.Number(actionPoints, "height"), samples);
        for (var i = 0; i < _parts.Count; i++)
        {
            var (target, tile, text) = _parts[i];
            _tiles.Bind(tile, "x", layout[i].Position.X); _tiles.Bind(tile, "y", layout[i].Position.Y);
            // Native rect justification values become the renderer's semantic
            // entity values; existing text justification remains unchanged.
            var justify = layout[i].Justify switch { 1 => 2, 4 => 0, 5 => 5, 6 => 6, _ => throw new InvalidDataException("VATS label justification is invalid.") };
            _tiles.Bind(tile, "justify", justify);
            _tiles.Bind(tile, "_chanceToHitType", layout[i].Justify switch { 1 => 1, 4 => 2, 5 => 4, 6 => 3, _ => 0 });
            _tiles.Bind(text, "mouseover", target.Part == _selectedPart ? 1 : 0);
        }
    }

    private void Notice(string text)
    {
        _tiles.Text[_notice] = text; _tiles.Bind(_notice, "visible", text.Length == 0 ? 0 : 1);
    }
    private string SelectionText() => Selected is not { } target ? "" :
        $"{target.ActorName} — {target.PartName}  {target.Chance:0}%  {_session.AvailableActionPoints - _session.ReservedActionPoints:0.##} / {_session.AvailableActionPoints:0.##} AP";

    private void Run(Action action)
    {
        if (_closed || _faulted) return;
        try { action(); Refresh(); }
        catch (Exception error) { Notice(error.Message); QueueRedraw(); }
    }

    private void Return()
    {
        if (_session.CurrentPhase == FalloutVatsSession.Phase.Selecting && _session.Queue.Count != 0) Run(_session.RemoveLast);
        else Cancel();
    }
    private void Cancel() { if (_closed) return; _closed = true; _cancel(); }
    private void MoveTarget(int delta)
    {
        var actors = _targets().Select(value => value.Reference).Distinct().ToArray();
        if (actors.Length == 0) return;
        var index = Array.FindIndex(actors, value => value == _selectedActor);
        _selectedActor = actors[(Math.Max(index, 0) + delta + actors.Length) % actors.Length];
        _selectedPart = null; Refresh();
    }
    private void MovePart(int delta)
    {
        if (_parts.Count == 0) return;
        var index = _parts.FindIndex(value => value.Target.Part == _selectedPart);
        _selectedPart = _parts[(Math.Max(index, 0) + delta + _parts.Count) % _parts.Count].Target.Part;
        Notice(SelectionText()); PositionParts(); QueueRedraw();
    }
    private void QueueSelected() { if (Selected is { } target) Run(() => _queue(target)); }
    private XElement? CommandAt(Vector2 position) => _glow.Elements().FirstOrDefault(tile =>
        (string?)tile.Attribute("name") is "accept_button" or "return_button" or "left_arrow" or "right_arrow" &&
        _tiles.Number(tile, "visible") != 0 &&
        new Rect2(_tiles.Position(tile), new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"))).HasPoint(position));
    private FalloutVatsSession.Target? PartAt(Vector2 position) => _parts.FirstOrDefault(value =>
        _tiles.Number(value.Tile, "visible") != 0 && new Rect2(_tiles.Position(value.Text),
            new(_tiles.Number(value.Text, "width"), _tiles.Number(value.Text, "height"))).HasPoint(position)).Target;

    public override void _Input(InputEvent input)
    {
        if (_closed) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Cancel(); GetViewport().SetInputAsHandled(); return; }
        if (_faulted) return;
        var handled = false;
        if (_session.CurrentPhase == FalloutVatsSession.Phase.Selecting && input is InputEventMouseMotion &&
            PartAt(GetLocalMousePosition()) is { } hovered)
        { _selectedPart = hovered.Part; Notice(SelectionText()); PositionParts(); QueueRedraw(); }
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            handled = true;
            if (_session.CurrentPhase != FalloutVatsSession.Phase.Selecting) handled = false;
            else if (_shortcuts.TryDispatch(key.Keycode, key.Pressed, key.Echo)) { }
            else switch (key.Keycode)
                {
                    case Key.Left: MoveTarget(-1); break;
                    case Key.Right: MoveTarget(1); break;
                    case Key.Up: MovePart(-1); break;
                    case Key.Down: MovePart(1); break;
                    case Key.Enter: QueueSelected(); break;
                    default: handled = false; break;
                }
        }
        if (_session.CurrentPhase == FalloutVatsSession.Phase.Selecting && input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right) { Return(); handled = true; }
            else if (mouse.ButtonIndex == MouseButton.WheelUp) { MovePart(-1); handled = true; }
            else if (mouse.ButtonIndex == MouseButton.WheelDown) { MovePart(1); handled = true; }
            else if (mouse.ButtonIndex == MouseButton.Left)
            {
                var position = GetLocalMousePosition();
                var hit = PartAt(position);
                if (hit is not null) { _selectedPart = hit.Part; QueueSelected(); handled = true; }
                else if (CommandAt(position) is { } command)
                {
                    handled = true;
                    switch ((string?)command.Attribute("name"))
                    {
                        case "accept_button": Run(_execute); break;
                        case "return_button": Return(); break;
                        case "left_arrow": MoveTarget(-1); break;
                        case "right_arrow": MoveTarget(1); break;
                        default: handled = false; break;
                    }
                }
            }
        }
        if (input is InputEventJoypadButton { Pressed: true } pad)
        {
            handled = true;
            if (pad.ButtonIndex == JoyButton.B) Return();
            else if (_session.CurrentPhase != FalloutVatsSession.Phase.Selecting) handled = false;
            else switch (pad.ButtonIndex)
                {
                    case JoyButton.A: Run(_execute); break;
                    case JoyButton.DpadLeft: MoveTarget(-1); break;
                    case JoyButton.DpadRight: MoveTarget(1); break;
                    case JoyButton.DpadUp: MovePart(-1); break;
                    case JoyButton.DpadDown: MovePart(1); break;
                    default: handled = false; break;
                }
        }
        if (handled) GetViewport().SetInputAsHandled();
    }
    public override void _Process(double delta)
    {
        if (_closed || _faulted) return;
        try { PositionParts(); QueueRedraw(); }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error)
    {
        if (_closed || _faulted) return;
        _faulted = true; _closed = true; _failed(error);
    }
    public override void _Draw()
    {
        if (_faulted) return;
        try { _tiles.Draw(this); } catch (Exception error) { Fail(error); }
    }
    public override void _EnterTree() => _layout = new(this, Layout);
    public override void _Ready() => Layout();
    public override void _ExitTree()
    {
        _layout?.Dispose();
        if (!_closed && _session.CurrentPhase is FalloutVatsSession.Phase.Selecting or FalloutVatsSession.Phase.Executing) Cancel();
    }
    private void Layout()
    {
        if (!IsInsideTree()) return;
        var physical = GetViewportRect().Size;
        var sourceSize = FalloutVatsLabelLayout.ReferenceSize(new(physical.X, physical.Y));
        var reference = new Vector2(sourceSize.X, sourceSize.Y);
        _tiles.Widescreen = (double)physical.Y / physical.X != .75;
        Scale = new(physical.X / reference.X, physical.Y / reference.Y); Size = _tiles.Screen = reference;
        PositionParts(); QueueRedraw();
    }
}
