using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedSleepWaitMenu : Control
{
    private readonly NativeSleepWaitMenuSource _source;
    private readonly FalloutSleepWait _session;
    private readonly long _request;
    private readonly Action<Exception> _failed;
    private readonly Dictionary<Key, int> _keys = [];
    private readonly List<(XElement Tile, NativeOwnedTileTarget Button, int Id)> _actions = [];
    private NativeViewportLayout? _layout;
    private XElement _slider = null!, _sliderMarker = null!;
    private NativeOwnedTileTarget _sliderTarget = null!;
    private bool _faulted, _dragging;
    internal string? Error { get; private set; }
    internal object State => new { source = FalloutSleepWaitSource.MenuPath, identity = _source.Identity,
        sourceSha256 = _source.Source.MenuSha256, request = _request, session = _session.State, error = Error,
        unowned = "matched-retail-pixels,XR-surface,cue-cadence,source-input-joystick-binding" };
    private NativeOwnedSleepWaitMenu(NativeSleepWaitMenuSource source, FalloutSleepWait session, Action<Exception> failed)
    {
        _source = source; _session = session; _request = session.RequestOrdinal; _failed = failed;
        Name = "SleepWaitMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
    }
    internal static NativeOwnedSleepWaitMenu Create(NativeSleepWaitMenuSource source, FalloutSleepWait session, Action<Exception> failed)
    {
        if (source.Source != session.Source || !session.NeedsMenuPublication || session.Failure is not null)
            throw new InvalidOperationException("Native rest view has no genuine current healthy source request.");
        // Every actual menu constructor owns fresh source tiles. Retained
        // target values are projected from this request, not a previous view.
        var currentSource = source.CreateCurrentPublication();
        var result = new NativeOwnedSleepWaitMenu(currentSource, session, failed);
        try { result.Initialize(); return result; }
        catch { result.Free(); throw; }
    }
    private void Initialize()
    {
        var tiles = _source.Tiles;
        _slider = _source.Named("SWM_Scrollbar");
        _sliderMarker = _slider.Descendants().Single(tile =>
            (string?)tile.Attribute("name") == "scrollbar_horiz_marker");
        tiles.Bind(_sliderMarker, "dragx", 0); tiles.Bind(_sliderMarker, "dragoffsetx", 0);
        foreach (var (name, id) in new[] { ("SWM_WaitButton", 4), ("SWM_CancelButton", 5) })
        {
            var tile = _source.Named(name);
            var pad = tile.Element("_xbox_button")?.Value.Trim().ToLowerInvariant();
            if (pad != (id == 4 ? "entity_xbuttona" : "entity_xbuttonb"))
                throw new NotSupportedException("Rest source button requires another native pad-action consumer.");
            var button = new NativeOwnedTileTarget { Name = name, FocusMode = FocusModeEnum.All };
            button.SetMeta("opennv_source_choice_kind", "sleep-wait"); button.SetMeta("opennv_source_choice_id", id);
            button.Pressed += () => Try(() => DispatchAction(id));
            AddChild(button); _actions.Add((tile, button, id));
        }
        _sliderTarget = new NativeOwnedTileTarget { Name = "SourceRestHourSlider", FocusMode = FocusModeEnum.All };
        _sliderTarget.GuiInput += SliderInput; AddChild(_sliderTarget);
        foreach (var property in tiles.Root.Elements().Where(property => property.Name.LocalName.StartsWith("_PCButton_", StringComparison.Ordinal)))
        {
            var action = _actions.SingleOrDefault(action => (string?)action.Tile.Attribute("name") == property.Value.Trim());
            if (action.Tile is null || !Enum.TryParse<Key>(property.Name.LocalName[10..], out var key) || !_keys.TryAdd(key, action.Id))
                throw new NotSupportedException("Rest source keyboard shortcut has an unowned target.");
        }
    }
    public override void _EnterTree() => _layout = new(this, Refresh);
    public override void _ExitTree() => _layout?.Dispose();
    public override void _Ready() => Refresh();
    internal void Refresh()
    {
        if (_faulted) return;
        Try(() =>
        {
            if (_session.RequestOrdinal != _request) throw new InvalidOperationException("Rest native input belongs to a retired source request.");
            var tiles = _source.Tiles;
            RestoreCurrentSourceTargets();
            var kind = _session.Request!.Kind;
            tiles.Text[_source.Named("SWM_HowManyText")] = _source.Question(kind);
            tiles.Text[_source.Named("SWM_HoursChosen")] = _source.Hours(_session.Phase == FalloutRestPhase.Choosing ?
                _session.SelectedHours : Math.Max(0, _session.RemainingHours));
            tiles.Text[_source.Named("SWM_CurrentTime")] = _source.CalendarCaption();
            tiles.BindText(_source.Named("SWM_WaitButton"), "string", _source.Action(kind));
            tiles.Bind(_slider, "_current_value", Math.Clamp((_session.Phase == FalloutRestPhase.Choosing ?
                _session.SelectedHours : _session.RemainingHours) - 1, 0, _session.Source.MaximumMenuHours - 1));
            // The original consumer writes target, not visibility or the
            // template's private _enabled input. Source XML owns appearance.
            var scale = GetViewportRect().Size.Y / 960;
            if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Rest native viewport has no positive source layout.");
            tiles.ResolutionConverter = 1 / scale; Scale = Vector2.One * scale; Size = tiles.Screen = GetViewportRect().Size / scale;
            foreach (var action in _actions)
            {
                action.Button.Position = tiles.Position(action.Tile);
                action.Button.Size = new(tiles.Number(action.Tile, "width"), tiles.Number(action.Tile, "height"));
                action.Button.Visible = SourceVisible(action.Tile);
                action.Button.Disabled = !SourceTargetEnabled(action.Tile);
            }
            _sliderTarget.Position = tiles.Position(_slider);
            _sliderTarget.Size = new(tiles.Number(_slider, "width"), tiles.Number(_slider, "height"));
            _sliderTarget.Visible = SourceVisible(_slider);
            _sliderTarget.Disabled = !SourceTargetEnabled(_slider);
            tiles.ValidateDrawing(); QueueRedraw();
        });
    }
    private void DispatchAction(int id)
    {
        if (id == 4)
        {
            if (!SourceTargetEnabled(_source.Named("SWM_WaitButton"))) return;
            _session.Begin();
        }
        else if (id == 5)
        { if (!SourceTargetEnabled(_source.Named("SWM_CancelButton"))) return; _session.Cancel(); }
        else throw new NotSupportedException("Rest native button has no source action consumer.");
        if (_session.NeedsMenuPublication) Refresh();
    }
    private void SliderInput(InputEvent input) => Try(() =>
    {
        if (!SourceTargetEnabled(_slider)) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } left)
        { _dragging = left.Pressed; if (_dragging) SetSlider(left.Position.X); }
        if (input is InputEventMouseMotion motion && _dragging) SetSlider(motion.Position.X);
    });
    private void SetSlider(float x)
    {
        var width = _sliderTarget.Size.X;
        if (!float.IsFinite(x) || width <= 0) throw new InvalidDataException("Rest source slider has invalid native geometry.");
        var index = (int)MathF.Round(Math.Clamp(x / width, 0, 1) * (_session.Source.MaximumMenuHours - 1));
        _session.Select(index + 1); Refresh();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_faulted || !_session.NeedsMenuPublication) return;
        Try(() =>
        {
            if (input is InputEventJoypadButton { Pressed: true } pad && pad.ButtonIndex is JoyButton.A or JoyButton.B)
            { DispatchAction(pad.ButtonIndex == JoyButton.A ? 4 : 5); GetViewport().SetInputAsHandled(); return; }
            if (input is InputEventKey { Pressed: true, Echo: false } key && _keys.TryGetValue(key.Keycode, out var action))
            { DispatchAction(action); GetViewport().SetInputAsHandled(); return; }
            if (!SourceTargetEnabled(_slider)) return;
            var direction = input switch
            {
                InputEventKey { Pressed: true, Keycode: Key.Left } => -1,
                InputEventKey { Pressed: true, Keycode: Key.Right } => 1,
                InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } => -1,
                InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } => 1,
                _ => 0,
            };
            if (direction != 0)
            {
                _session.Select(Math.Clamp(_session.SelectedHours + direction, 1, _session.Source.MaximumMenuHours));
                Refresh(); GetViewport().SetInputAsHandled();
            }
        });
    }
    private void Try(Action action)
    {
        if (_faulted) return;
        try { action(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            _faulted = true; Error = error.Message;
            foreach (var target in _actions) target.Button.Disabled = true;
            _sliderTarget.Disabled = true;
            _session.ReportNativeFailure(FalloutRestStep.Publication, error); _failed(error);
        }
    }
    public override void _Draw()
    {
        if (_faulted) return;
        Try(() => _source.Tiles.Draw(this));
    }
}
