using System.Globalization;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedTagSkillMenu : Control
{
    private sealed record Row(XElement Tile, NativeBitmapMenuButton Button, FalloutTagSkillMenuChoice Choice);
    private readonly NativeOwnedMenuTree _tiles;
    private readonly FalloutTagSkillMenuSelection _selection;
    private readonly FalloutPluginStack _records;
    private readonly FalloutTagMenuDeclarations _declarations;
    private readonly XElement _list, _scrollbar, _title, _description, _icon, _counter;
    private readonly List<Row> _rows = [];
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button, Action Action)> _actions = [];
    private readonly List<(XElement Tile, NativeOwnedTileTarget Button)> _scrollTargets = [];
    private readonly Dictionary<Key, Action> _shortcuts = [];
    private readonly Action<IReadOnlyList<FalloutNativeSkillIdentity>> _accepted;
    private readonly Action<Exception> _failed;
    private Row? _focused;
    private float _scroll;
    private bool _submitted;
    private NativeViewportLayout? _viewportLayout;
    private int[] _lastValues = [];
    internal string? Error { get; private set; }
    internal object State => new
    {
        source = "menus/chargen/char_gen_menu.xml",
        selected = _selection.Selected,
        required = _selection.Required,
        focused = _focused?.Choice.Skill.EditorId,
        values = _rows.Select((row, index) => new { row.Choice.Skill.RuntimeFormId, value = index < _lastValues.Length ? (int?)_lastValues[index] : null }),
        scroll = _scroll,
        error = Error,
        unbound = "permanent-versus-current-actor-values,native-list-sort-ties,scrollbar-drag,focus-and-click-sounds,exact-layout-timing,retail-and-XR-pixels"
    };

    internal NativeOwnedTagSkillMenu(FalloutPluginStack records, FalloutNativeTagSkillContract contract,
        IReadOnlyList<FalloutNativeSkillIdentity> current, Func<FalloutNativeSkillIdentity, float> liveValue,
        Action<IReadOnlyList<FalloutNativeSkillIdentity>> accepted, Action<Exception> failed, bool showInitialTaggedSkills = true)
    {
        Name = "CharGenMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
        _records = records; _accepted = accepted; _failed = failed;
        _selection = new(records, contract, current, liveValue, showInitialTaggedSkills); _declarations = FalloutTagMenuDefaults.Read();
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/chargen/char_gen_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _list = Named("CGM_ItemList"); _scrollbar = Named("lb_scrollbar"); _title = Named("CGM_Headline_Title");
        _description = Named("CGM_SelectionText"); _icon = Named("CGM_SelectionIcon"); _counter = Named("CGM_PointCounter");
        var template = new XElement(Named("CGM_SelectItemTemplate").Elements().Single());
        foreach (var choice in _selection.Choices)
        {
            var name = "Skill_" + choice.Skill.RuntimeFormId;
            var tile = new XElement(template); tile.SetAttributeValue("name", name);
            _list.Add(tile); _tiles.BindText(tile, "string", choice.Skill.DisplayName);
            _tiles.Bind(tile, "listindex", _rows.Count); _tiles.Bind(tile, "_enabled", 1);
            var font = _tiles.Font(tile.Descendants("text").First());
            var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
            { Name = name, Text = choice.Skill.DisplayName, DrawText = false, FocusMode = FocusModeEnum.All };
            var row = new Row(tile, button, choice); _rows.Add(row); AddChild(button);
            button.Pressed += () => Try(() => { if (_submitted) return; _selection.Toggle(choice.Skill); Focus(row); Refresh(); });
            button.MouseEntered += () => Try(() => { Focus(row); QueueRedraw(); });
            button.FocusEntered += () => Try(() => { Focus(row); EnsureVisible(row); Refresh(); });
        }
        foreach (var (tile, action) in new[]
        {
            (Named("CGM_ResetButton"), (Action)(() => { if (_submitted) return; _selection.Reset(); Refresh(); })),
            (Named("CGM_DoneButton"), (Action)Submit),
        })
        {
            var font = _tiles.Font(tile.Descendants("text").First());
            var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
            { Name = (string)tile.Attribute("name")!, DrawText = false, FocusMode = FocusModeEnum.All };
            button.Pressed += () => Try(action);
            button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
            button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
            button.FocusEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
            button.FocusExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
            _actions.Add((tile, button, action)); AddChild(button);
        }
        foreach (var shortcut in menu.Elements().Where(value => value.Name.LocalName.StartsWith("_PCButton_", StringComparison.Ordinal)))
        {
            var action = _actions.SingleOrDefault(value => (string?)value.Tile.Attribute("name") == shortcut.Value.Trim());
            if (action.Tile is null) throw new NotSupportedException("Tag menu shortcut targets an unbound source action.");
            if (!Enum.TryParse<Key>(shortcut.Name.LocalName[10..], out var key) || !_shortcuts.TryAdd(key, action.Action))
                throw new NotSupportedException("Tag menu shortcut key is unbound or ambiguous.");
        }
        menu.Elements("template").Remove();
        _tiles.Bind(_scrollbar, "_current_value", 0); _tiles.Bind(_scrollbar, "_SetInCode", 1);
        foreach (var marker in menu.Descendants().Where(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker"))
        { _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0); }
        foreach (var (name, direction, page) in new[]
        {
            ("scrollbar_vert_up", -1, false), ("scrollbar_vert_down", 1, false),
            ("scrollbar_vert_page_up", -1, true), ("scrollbar_vert_page_down", 1, true)
        })
        {
            var tile = _scrollbar.Elements().Single(tile => (string?)tile.Attribute("name") == name);
            var button = new NativeOwnedTileTarget { Name = name, Text = name, FocusMode = FocusModeEnum.None };
            button.Pressed += () => Try(() => Scroll(direction * _tiles.Number(_scrollbar, page ? "_jump_size" : "_step_size")));
            button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
            button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
            AddChild(button); _scrollTargets.Add((tile, button));
        }
        SetMeta("opennv_ui_source", "menus/chargen/char_gen_menu.xml;winning-AVIF;owned-font-and-atlas");
    }

    public override void _Ready()
    {
        Refresh();
        if (Error is null && _rows.Count != 0) { Focus(_rows[0]); Refresh(); }
        if (Error is null && _rows.Count != 0) Callable.From(_rows[0].Button.GrabFocus).CallDeferred();
    }
    public override void _EnterTree() => _viewportLayout = new(this, Refresh);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    public override void _Process(double delta) => Try(() =>
    {
        if (_submitted) return;
        var values = _rows.Select(row => _selection.Value(row.Choice.Skill)).ToArray();
        if (_lastValues.AsSpan().SequenceEqual(values)) return;
        Refresh();
    });
    private void Focus(Row row)
    {
        _focused = row; _tiles.Text[_description] = row.Choice.Description;
        if (row.Choice.Icon is { Length: > 0 } icon) { _tiles.SetFilename(_icon, icon); _tiles.Bind(_icon, "visible", 1); }
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
        _tiles.Bind(_tiles.Root, "_CurrPoints", _selection.Selected.Count); _tiles.Bind(_tiles.Root, "_MaxPoints", _selection.Required);
        _tiles.Bind(_tiles.Root, "_OnlyAdd", 0);
        _tiles.Text[_title] = FalloutTagSkillMenuSelection.FormatCounts(FalloutGameSettingStrings.Read(_records, "sSkillsTitle"),
            _selection.Selected.Count, _selection.Required);
        _tiles.Text[_counter] = FalloutTagSkillMenuSelection.FormatCounts(FalloutGameSettingStrings.Read(_records, "sSkillsCount"),
            _selection.Remaining) + (_selection.Remaining == 1 ? "" : _declarations.PluralSuffix);
        _tiles.Bind(_counter, "visible", _selection.Complete ? 0 : 1);
        _tiles.Bind(_list, "y", _declarations.ListY);
        var values = _rows.Select(row => _selection.Value(row.Choice.Skill)).ToArray();
        var y = 0f;
        var index = 0;
        foreach (var row in _rows)
        {
            if (_tiles.Number(row.Tile, "height", false) == 0)
                _tiles.Bind(row.Tile, "height", _tiles.Number(row.Tile.Descendants("text").First(), "height") + _tiles.Number(row.Tile, "_VerticalSpacing"));
            _tiles.Bind(row.Tile, "_y", y);
            _tiles.Bind(row.Tile, "_selected", _selection.Selected.Contains(row.Choice.Skill) ? 1 : 0);
            _tiles.BindText(row.Tile, "_ValueString", values[index++].ToString(CultureInfo.InvariantCulture));
            y += _tiles.Number(row.Tile, "height");
        }
        if (string.IsNullOrWhiteSpace(_list.Element("height")?.Value))
            _tiles.Bind(_list, "height", _tiles.Number(_rows[0].Tile, "height") * _tiles.Number(_list, "_number_of_visible_items"));
        var viewport = _tiles.Number(_list, "height");
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, y - viewport));
        var steps = y > viewport && y > 0 ? MathF.Ceiling(_rows.Count * (y - viewport) / y) + 1 : 1;
        if (steps > 1) _tiles.Bind(_list, "_scroll_delta", (y - viewport) / (steps - 1));
        _tiles.Bind(_scrollbar, "_number_of_items", steps);
        _tiles.Bind(_scrollbar, "_current_value", _scroll / _tiles.Number(_list, "_scroll_delta"));
        var bounds = new Rect2(_tiles.Position(_list), new(_tiles.Number(_list, "width"), viewport));
        foreach (var row in _rows)
        {
            var rect = new Rect2(_tiles.Position(row.Tile), new(_tiles.Number(row.Tile, "width"), _tiles.Number(row.Tile, "height"))).Intersection(bounds);
            row.Button.Visible = rect.HasArea(); row.Button.Position = rect.Position; row.Button.Size = rect.Size;
        }
        if (_focused is not null) Focus(_focused);
        foreach (var (tile, button, _) in _actions)
        {
            button.Text = _tiles.String(tile); button.Position = _tiles.Position(tile);
            button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
            button.Disabled = (string?)tile.Attribute("name") == "CGM_DoneButton" && !_selection.Complete;
        }
        foreach (var (tile, button) in _scrollTargets)
        {
            button.Visible = _tiles.Number(_scrollbar, "visible") != 0 && _tiles.Number(tile, "visible") != 0;
            if (!button.Visible) continue;
            button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
        _tiles.ValidateDrawing(); _lastValues = values; QueueRedraw();
    }
    private void Scroll(float step) { _scroll += step * _tiles.Number(_list, "_scroll_delta"); Refresh(); }
    private void Submit()
    {
        if (_submitted || !_selection.Complete) return;
        var selected = _selection.Submit(); _submitted = true; _accepted(selected);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Error is not null || _submitted) return;
        Try(() =>
        {
            if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { Scroll(mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1); GetViewport().SetInputAsHandled(); }
            if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
            if (_shortcuts.TryGetValue(key.Keycode, out var action)) { action(); GetViewport().SetInputAsHandled(); }
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
            foreach (var (_, button, _) in _actions) button.Disabled = true;
            foreach (var (_, button) in _scrollTargets) button.Disabled = true;
            _failed(error);
        }
    }
    public override void _Draw() => Try(() => _tiles.Draw(this));
}
