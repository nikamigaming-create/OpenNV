using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedTraitMenu : Control
{
    private sealed record Row(XElement Tile, NativeBitmapMenuButton Button, FalloutTraitMenuChoice Choice);
    private readonly NativeOwnedMenuTree _tiles;
    private readonly FalloutTraitMenuSelection _selection;
    private readonly FalloutPluginStack _records;
    private readonly XElement _list, _scrollbar, _description, _descriptionScrollbar, _icon, _counter;
    private readonly List<Row> _rows = [];
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _actions = [];
    private readonly List<(XElement Tile, NativeOwnedTileTarget Button, XElement Bar)> _scrollTargets = [];
    private readonly Action<IReadOnlyList<FalloutNativeTraitIdentity>> _accepted;
    private readonly Action<Exception> _failed;
    private Row? _focused;
    private float _scroll;
    private float _descriptionScroll;
    private bool _submitted;
    internal string? Error { get; private set; }
    internal object State => new
    {
        source = "menus/trait_menu.xml",
        selected = _selection.Selected,
        maximum = _selection.Maximum,
        focused = _focused?.Choice.Trait.EditorId,
        scroll = _scroll,
        error = Error,
        unbound = "native-list-sort-ties,perk-eligibility-and-multirank,scrollbar-drag,focus-and-click-sounds,exact-layout-timing,retail-and-XR-pixels"
    };

    internal NativeOwnedTraitMenu(FalloutPluginStack records, FalloutNativeTraitFarewellContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> current, Action<IReadOnlyList<FalloutNativeTraitIdentity>> accepted, Action<Exception> failed)
    {
        Name = "TraitMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
        _records = records; _accepted = accepted; _failed = failed;
        _selection = new(records, contract, current);
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/trait_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _list = Named("LUM_PerkList"); _scrollbar = Named("lb_scrollbar");
        _description = Named("TM_DescriptionText"); _descriptionScrollbar = Named("TM_DescriptionScrollbar");
        _icon = Named("LUM_SelectionIcon"); _counter = Named("LUM_PointCounter");
        _tiles.Text[Named("LUM_Headline_Title")] = FalloutTraitMenuSelection.FormatCount(
            FalloutGameSettingStrings.Read(records, _selection.Maximum == 1 ? "sTraitMenuTitleTextSingular" : "sTraitMenuTitleText"), _selection.Maximum);
        var template = new XElement(Named("LUM_PerkTemplate").Elements().Single());
        foreach (var choice in _selection.Choices)
        {
            var tile = new XElement(template); tile.SetAttributeValue("name", "Trait_" + choice.Trait.RuntimeFormId);
            _list.Add(tile); _tiles.BindText(tile, "string", choice.Trait.DisplayName);
            _tiles.Bind(tile, "listindex", _rows.Count); _tiles.Bind(tile, "_enabled", 1);
            var font = _tiles.Font(tile.Descendants("text").Single());
            var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
            { Name = "Trait_" + choice.Trait.RuntimeFormId, Text = choice.Trait.DisplayName, DrawText = false, FocusMode = FocusModeEnum.All };
            var row = new Row(tile, button, choice); _rows.Add(row); AddChild(button);
            button.Pressed += () => Try(() => { _selection.Toggle(choice.Trait); Focus(row); Refresh(); });
            button.MouseEntered += () => Try(() => { Focus(row); QueueRedraw(); });
            button.FocusEntered += () => Try(() => { Focus(row); EnsureVisible(row); Refresh(); });
        }
        foreach (var (tile, action) in new[]
        {
            (Named("LUM_ResetButton"), (Action)(() => { _selection.Reset(); Refresh(); })),
            (Named("LUM_ContinueButton"), (Action)(() => { if (_submitted) return; _submitted = true; _accepted(_selection.Submit()); })),
        })
        {
            var font = _tiles.Font(tile.Descendants("text").First());
            var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color) { DrawText = false, FocusMode = FocusModeEnum.All };
            button.Pressed += () => Try(action);
            button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
            button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
            button.FocusEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
            button.FocusExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
            _actions.Add((tile, button)); AddChild(button);
        }
        menu.Elements("template").Remove();
        _tiles.Bind(_scrollbar, "_current_value", 0);
        _tiles.Bind(_scrollbar, "_SetInCode", 1);
        _tiles.Bind(_descriptionScrollbar, "_current_value", 0); _tiles.Bind(_descriptionScrollbar, "_SetInCode", 1);
        _tiles.Bind(_description.Parent!, "wheelmoved", 0);
        foreach (var marker in menu.Descendants().Where(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker"))
        { _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0); }
        foreach (var bar in new[] { _scrollbar, _descriptionScrollbar })
        {
            foreach (var (name, direction, page) in new[]
            {
                ("scrollbar_vert_up", -1, false), ("scrollbar_vert_down", 1, false),
                ("scrollbar_vert_page_up", -1, true), ("scrollbar_vert_page_down", 1, true)
            })
            {
                var tile = bar.Elements().Single(tile => (string?)tile.Attribute("name") == name);
                var button = new NativeOwnedTileTarget { Name = name, Text = name, FocusMode = FocusModeEnum.None };
                button.Pressed += () => Try(() =>
                {
                    var step = _tiles.Number(bar, page ? "_jump_size" : "_step_size");
                    Scroll(bar, direction * step);
                });
                button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
                button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
                AddChild(button); _scrollTargets.Add((tile, button, bar));
            }
        }
        SetMeta("opennv_ui_source", "menus/trait_menu.xml;winning-PERK;owned-font-and-atlas");
        SetMeta("opennv_ui_unbound", "native-list-sort-ties,perk-eligibility-and-multirank,scrollbar-drag,focus-and-click-sounds,exact-layout-timing,matched-retail-and-XR-pixels");
    }

    public override void _Ready()
    {
        Refresh();
        if (Error is null && _rows.Count != 0) { Focus(_rows[0]); Refresh(); }
        if (Error is null && _rows.Count != 0) Callable.From(_rows[0].Button.GrabFocus).CallDeferred();
    }
    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Refresh);
    public override void _ExitTree() => _viewportLayout?.Dispose();

    private void Focus(Row row)
    {
        if (_focused != row) { _descriptionScroll = 0; _tiles.Bind(_descriptionScrollbar, "_current_value", 0); }
        _focused = row;
        _tiles.Text[_description] = row.Choice.Description;
        if (row.Choice.Icon is { Length: > 0 } icon)
        { _tiles.SetFilename(_icon, icon); _tiles.Bind(_icon, "visible", 1); }
        else _tiles.Bind(_icon, "visible", 0);
        _tiles.Bind(_list, "_highlight_y", _tiles.Number(row.Tile, "y"));
        _tiles.Bind(_list, "_selected_height", _tiles.Number(row.Tile, "height"));
    }
    private void EnsureVisible(Row row)
    {
        var y = _tiles.Number(row.Tile, "_y"); var height = _tiles.Number(row.Tile, "height");
        var viewport = _tiles.Number(_list, "height");
        if (y < _scroll) _scroll = y;
        else if (y + height > _scroll + viewport) _scroll = y + height - viewport;
    }
    private void Refresh() => Try(ApplyLayout);
    private void ApplyLayout()
    {
        var scale = GetViewportRect().Size.Y / 960;
        if (scale <= 0) return;
        Scale = Vector2.One * scale; Size = _tiles.Screen = GetViewportRect().Size / scale;
        _tiles.ResolutionConverter = 1 / scale;
        _tiles.Bind(_tiles.Root, "_CurrPoints", _selection.Selected.Count); _tiles.Bind(_tiles.Root, "_MaxPoints", _selection.Maximum);
        _tiles.Text[_counter] = FalloutTraitMenuSelection.FormatCount(FalloutGameSettingStrings.Read(_records,
            _selection.Remaining == 1 ? "sTraitMenuCounterSingular" : "sTraitMenuCounter"), _selection.Remaining);
        var y = 0f;
        foreach (var row in _rows)
        {
            if (_tiles.Number(row.Tile, "height", false) == 0)
                _tiles.Bind(row.Tile, "height", _tiles.Number(row.Tile.Descendants("text").First(), "height") + _tiles.Number(row.Tile, "_VerticalSpacing"));
            _tiles.Bind(row.Tile, "_y", y);
            _tiles.Bind(row.Tile, "_selected", _selection.Selected.Contains(row.Choice.Trait) ? 1 : 0);
            y += _tiles.Number(row.Tile, "height");
        }
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, y - _tiles.Number(_list, "height")));
        var steps = y > _tiles.Number(_list, "height") && y > 0
            ? MathF.Ceiling(_rows.Count * (y - _tiles.Number(_list, "height")) / y) + 1 : 1;
        if (steps > 1) _tiles.Bind(_list, "_scroll_delta", (y - _tiles.Number(_list, "height")) / (steps - 1));
        _tiles.Bind(_scrollbar, "_number_of_items", steps);
        _tiles.Bind(_scrollbar, "_current_value", _scroll / _tiles.Number(_list, "_scroll_delta"));
        var bounds = new Rect2(_tiles.Position(_list), new(_tiles.Number(_list, "width"), _tiles.Number(_list, "height")));
        foreach (var row in _rows)
        {
            var rect = new Rect2(_tiles.Position(row.Tile), new(_tiles.Number(row.Tile, "width"), _tiles.Number(row.Tile, "height"))).Intersection(bounds);
            row.Button.Visible = rect.HasArea(); row.Button.Position = rect.Position; row.Button.Size = rect.Size;
        }
        if (_focused is not null) Focus(_focused);
        foreach (var (tile, button) in _actions)
        {
            button.Text = _tiles.String(tile); button.Position = _tiles.Position(tile);
            button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
        foreach (var (tile, button, bar) in _scrollTargets)
        {
            button.Visible = _tiles.Number(bar, "visible") != 0 && _tiles.Number(tile, "visible") != 0;
            button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
        _tiles.ValidateDrawing(); QueueRedraw();
    }
    private void Scroll(XElement bar, float step)
    {
        if (bar == _scrollbar) { _scroll += step * _tiles.Number(_list, "_scroll_delta"); Refresh(); }
        else
        {
            _descriptionScroll = Math.Clamp(_descriptionScroll + step, 0,
                Math.Max(0, _tiles.Number(bar, "_number_of_items") - _tiles.Number(bar, "_number_of_visible_items")));
            _tiles.Bind(bar, "_current_value", _descriptionScroll); QueueRedraw();
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Error is not null || _submitted) return;
        Try(() =>
        {
            if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var direction = mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
                var descriptionBounds = new Rect2(_tiles.Position(_description.Parent!), new(_tiles.Number(_description.Parent!, "width"), _tiles.Number(_description.Parent!, "height")));
                if (descriptionBounds.HasPoint(mouse.Position / Scale))
                {
                    Scroll(_descriptionScrollbar, direction);
                }
                else Scroll(_scrollbar, direction);
                GetViewport().SetInputAsHandled();
            }
            if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
            if (key.Keycode == Key.R) { _selection.Reset(); Refresh(); GetViewport().SetInputAsHandled(); }
            if (key.Keycode == Key.A) { _submitted = true; _accepted(_selection.Submit()); GetViewport().SetInputAsHandled(); }
            if (key.Keycode is Key.Up or Key.Down && _rows.Count != 0)
            {
                var index = _focused is null ? 0 : _rows.IndexOf(_focused);
                var next = _rows[Math.Clamp(index + (key.Keycode == Key.Up ? -1 : 1), 0, _rows.Count - 1)];
                Focus(next); EnsureVisible(next); Refresh(); next.Button.GrabFocus(); GetViewport().SetInputAsHandled();
            }
        });
    }
    private void Try(Action action)
    {
        if (Error is not null) return;
        try { action(); }
        catch (Exception error)
        {
            Error = error.Message;
            foreach (var row in _rows) row.Button.Disabled = true;
            foreach (var (_, button) in _actions) button.Disabled = true;
            foreach (var (_, button, _) in _scrollTargets) button.Disabled = true;
            _failed(error);
        }
    }
    public override void _Draw() => Try(() => _tiles.Draw(this));
}
