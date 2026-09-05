using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeRaceSexSlider(int Minimum, int Maximum, int Jump, Func<int> Value,
    Action<int> Changed, Func<string> Display);
internal sealed record NativeRaceSexChoice(string Label, bool Selected, Action Activate,
    NativeRaceSexSlider? Slider = null, bool Selectable = true);

/// <summary>Owned XML draws every control; transparent targets adapt ordinary input.</summary>
internal sealed partial class NativeOwnedRaceSexMenu : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly FalloutPluginStack _records;
    private readonly XElement _panel, _template, _sliderTemplate, _back, _next, _up, _down;
    private sealed record Target(XElement Tile, XElement Owner, NativeOwnedTileTarget Button);
    private readonly List<Target> _targets = [];
    private readonly List<XElement> _rows = [];
    private readonly Dictionary<XElement, NativeRaceSexSlider> _sliders = [];
    private readonly Action<Exception> _failed;
    private int _scroll, _page = -1;
    private XElement? _drag;
    private bool _faulted;
    internal event Action<int>? Navigate;
    internal event Action<int>? PageChanged;
    internal Rect2 Panel => new(_tiles.Position(_panel), new(_tiles.Number(_panel, "width"), _tiles.Number(_panel, "height")));

    internal NativeOwnedRaceSexMenu(FalloutPluginStack records, Action<Exception> failed)
    {
        Name = "RaceSexMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
        _records = records; _failed = failed;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/chargen/race_sex_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _panel = Named("RSM_Background");
        _template = new XElement(Named("RSM_list_item_template").Elements().Single());
        _sliderTemplate = new XElement(Named("RSM_slider_option_template").Elements().Single());
        _back = Named("RSM_back_button"); _next = Named("RSM_next_button");
        _up = Named("RSM_scroll_up_target"); _down = Named("RSM_scroll_down_target");
        menu.Elements("template").Remove();
        _tiles.Text[_back] = FalloutGameSettingStrings.Read(records, "sBack").ToUpperInvariant();
        _tiles.Text[_next] = FalloutGameSettingStrings.Read(records, "sNext").ToUpperInvariant();
        foreach (var button in new[] { _back, _next })
            _tiles.Bind(button, "_PCButtonText", _tiles.String(button, "_PCButtonText").Length == 0 ? 0 : 1);
        _tiles.Bind(menu, "user0", 0);
        _tiles.BindText(menu, "user1", "<"); _tiles.BindText(menu, "user2", ">");
        _tiles.Bind(_back, "visible", 0);
        AddTarget(_back, _back, () => Navigate?.Invoke(-1));
        AddTarget(_next, _next, () => Navigate?.Invoke(1));
        AddTarget(_up, _up, () => Scroll(-1), false);
        AddTarget(_down, _down, () => Scroll(1), false);
        SetMeta("opennv_ui_source", "menus/chargen/race_sex_menu.xml");
        SetMeta("opennv_ui_unbound", "focus-sounds,screen-effects,matched-render-target-transform");
    }

    internal void SetPage(int page, string title, IReadOnlyList<NativeRaceSexChoice> choices, bool last = false)
    {
        var oldFocus = _targets.SingleOrDefault(target => target.Button.HasFocus());
        var oldIndex = oldFocus is null ? -1 : _rows.IndexOf(oldFocus.Owner);
        var samePage = page == _page;
        foreach (var target in _targets.Where(target => _rows.Contains(target.Owner)).ToArray())
        { _targets.Remove(target); RemoveChild(target.Button); target.Button.QueueFree(); }
        foreach (var row in _rows) row.Remove();
        _rows.Clear(); _sliders.Clear(); _drag = null;
        if (!samePage) _scroll = 0;
        _page = page;
        _tiles.Bind(_tiles.Root, "user0", page);
        _tiles.Bind(_back, "visible", page == 0 ? 0 : 1);
        _tiles.Bind(_next, "visible", page < 4 ? 1 : 0);
        _tiles.Text[_next] = FalloutGameSettingStrings.Read(_records, last ? "sDone" : "sNext").ToUpperInvariant();
        var header = Row(page < 4 ? $"{page + 1}. {title}" : title, page, _template);
        _tiles.Bind(header, "_is_header", 1); _tiles.Bind(header, "target", 0);
        foreach (var choice in choices)
        {
            var row = Row(choice.Label, page, choice.Slider is null ? _template : _sliderTemplate);
            if (choice.Slider is { } slider)
            {
                if (slider.Minimum >= slider.Maximum || slider.Jump <= 0) throw new InvalidDataException("Creation slider has no valid source range.");
                _sliders.Add(row, slider); BindSlider(row, slider);
                AddTarget(row, row, () => { });
                foreach (var child in row.Elements("hotrect"))
                {
                    var id = (int)_tiles.Number(child, "id");
                    AddTarget(child, row, () =>
                    {
                        var delta = id switch
                        {
                            100 => -1,
                            104 => 1,
                            102 => -slider.Jump,
                            103 => slider.Jump,
                            105 => 0,
                            _ => throw new NotSupportedException("Owned slider target is unbound.")
                        };
                        if (delta != 0) Change(row, slider.Value() + delta);
                    }, false);
                    if (id == 105) _targets[^1].Button.ButtonDown += () => _drag = row;
                }
            }
            else
            {
                _tiles.Bind(row, "_selected", choice.Selectable ? choice.Selected ? 2 : 1 : 0);
                AddTarget(row, row, choice.Activate);
            }
        }
        Layout();
        Target? focus = null;
        if (samePage && oldIndex > 0 && oldIndex < _rows.Count)
            focus = _targets.Single(target => target.Tile == _rows[oldIndex]);
        else if (oldFocus?.Tile == _next && page < 4) focus = _targets.Single(target => target.Tile == _next);
        focus ??= _targets.FirstOrDefault(target => _rows.Contains(target.Tile));
        if (focus is not null) Focus(focus);
        PageChanged?.Invoke(page);
    }

    private XElement Row(string label, int page, XElement template)
    {
        var row = new XElement(template);
        row.SetAttributeValue("name", $"RSM_row_{_rows.Count}");
        _panel.Add(row); _rows.Add(row);
        _tiles.Bind(row, "user0", page); _tiles.Text[row] = label;
        return row;
    }
    private void BindSlider(XElement row, NativeRaceSexSlider slider)
    {
        _tiles.Bind(row, "user1", slider.Value()); _tiles.Bind(row, "user2", slider.Minimum);
        _tiles.Bind(row, "user3", slider.Maximum); _tiles.Bind(row, "user4", slider.Jump);
        _tiles.BindText(row, "user5", slider.Display());
    }
    private void Change(XElement row, int value)
    {
        var slider = _sliders[row]; value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        if (value == slider.Value()) return;
        slider.Changed(value);
        foreach (var (tile, binding) in _sliders) BindSlider(tile, binding);
        Layout();
    }
    private void AddTarget(XElement tile, XElement owner, Action action, bool focus = true)
    {
        var button = new NativeOwnedTileTarget { Name = (string)tile.Attribute("name")!, FocusMode = focus ? FocusModeEnum.All : FocusModeEnum.None };
        button.Pressed += () => { if (!_faulted) Try(action); };
        button.MouseEntered += () => Highlight(tile, 1);
        button.MouseExited += () => Highlight(tile, button.HasFocus() ? 1 : 0);
        button.FocusEntered += () => Highlight(tile, 1);
        button.FocusExited += () => Highlight(tile, button.IsHovered() ? 1 : 0);
        AddChild(button); _targets.Add(new(tile, owner, button));
    }
    private void Highlight(XElement tile, int value) { _tiles.Bind(tile, "mouseover", value); QueueRedraw(); }
    internal void SetCanvas(Vector2 size) { Size = _tiles.Screen = size; _tiles.ResolutionConverter = 1; Layout(); }
    private void Scroll(int direction) { _scroll = Math.Clamp(_scroll + direction, 0, Math.Max(0, _rows.Count - 2)); Layout(); }
    private void Focus(Target target)
    {
        var index = _rows.IndexOf(target.Owner);
        if (index > 0)
        {
            if (index - 1 < _scroll) _scroll = index - 1;
            while (_scroll < index - 1 && RowBottom(index) > _tiles.Number(_panel, "_bot_bound")) _scroll++;
        }
        Layout(); Highlight(target.Tile, 1);
        if (IsInsideTree()) target.Button.GrabFocus();
    }
    private float RowBottom(int index) => _tiles.Number(_panel, "_top_bound") +
        _rows.Skip(_scroll + 1).Take(index - _scroll).Sum(row => _tiles.Number(row, "height"));

    public override void _Input(InputEvent input)
    {
        if (_faulted) return;
        Try(() =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) _drag = null;
            if (input is InputEventMouseMotion mouse && _drag is { } row)
            {
                var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
                var bar = row.Elements().Single(child => (string?)child.Attribute("name") == "RSM_slider_bar");
                var slider = _sliders[row]; var t = (point.X - _tiles.Position(bar).X) / _tiles.Number(bar, "width");
                Change(row, (int)MathF.Round(slider.Minimum + Math.Clamp(t, 0, 1) * (slider.Maximum - slider.Minimum)));
                GetViewport().SetInputAsHandled();
            }
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
            { Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1); GetViewport().SetInputAsHandled(); }
            if (input is not InputEventKey { Pressed: true } key) return;
            var focused = _targets.SingleOrDefault(target => target.Button.HasFocus());
            if (key.Keycode is Key.Left or Key.Right && focused is not null && _sliders.TryGetValue(focused.Owner, out var binding))
            { Change(focused.Owner, binding.Value() + (key.Keycode == Key.Left ? -1 : 1)); GetViewport().SetInputAsHandled(); }
            if (key.Keycode is Key.Up or Key.Down)
            {
                var navigation = _targets.Where(target => target.Tile == target.Owner && _rows.Contains(target.Tile))
                    .Concat(_targets.Where(target => target.Tile == _back || target.Tile == _next).Where(target => target.Button.Visible)).ToArray();
                if (navigation.Length == 0) return;
                var index = Array.IndexOf(navigation, focused);
                Focus(navigation[(index + (key.Keycode == Key.Up ? -1 : 1) + navigation.Length) % navigation.Length]);
                GetViewport().SetInputAsHandled();
            }
            if (key.Keycode == Key.Escape && _page > 0) { Navigate?.Invoke(-1); GetViewport().SetInputAsHandled(); }
        });
    }
    private void Layout()
    {
        if (_faulted) return;
        Try(() =>
        {
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count - 2));
            var y = _tiles.Number(_panel, "_top_bound") - _rows.Skip(1).Take(_scroll).Sum(row => _tiles.Number(row, "height"));
            for (var index = 1; index < _rows.Count; index++) { _tiles.Bind(_rows[index], "y", y); y += _tiles.Number(_rows[index], "height"); }
            _tiles.Bind(_up, "visible", _scroll > 0 ? 1 : 0);
            _tiles.Bind(_down, "visible", y > _tiles.Number(_panel, "_bot_bound") ? 1 : 0);
            foreach (var target in _targets)
            {
                target.Button.Text = _tiles.String(target.Tile);
                target.Button.Visible = _tiles.Number(target.Tile, "visible") != 0 && _tiles.Number(target.Owner, "visible") != 0;
                target.Button.Position = _tiles.Position(target.Tile);
                target.Button.Size = new(Math.Max(0, _tiles.Number(target.Tile, "width")), Math.Max(0, _tiles.Number(target.Tile, "height")));
            }
            _tiles.ValidateDrawing(); QueueRedraw();
        });
    }
    private void Try(Action action) { try { action(); } catch (Exception error) { Fail(error); } }
    private void Fail(Exception error) { _faulted = true; foreach (var target in _targets) target.Button.Disabled = true; _failed(error); }
    public override void _Draw() { if (!_faulted) Try(() => _tiles.Draw(this)); }
}

internal sealed partial class NativeOwnedTileTarget : BaseButton
{
    internal string Text { get; set; } = "";
}
