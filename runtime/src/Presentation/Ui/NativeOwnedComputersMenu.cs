using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedComputersMenu : Control
{
    private readonly FalloutTerminalMenu _session;
    private readonly FalloutPluginStack _records;
    private readonly Action _close;
    private readonly Action<Exception> _failed;
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _depth, _list, _template, _welcome, _separator, _display, _displayText, _back;
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _rows = [];
    private readonly NativeBitmapMenuButton _backButton;
    private int _offset;
    private bool _closed, _faulted;
    internal string? Error { get; private set; }
    internal object Observation => new
    {
        reference = _session.Reference.ToString(),
        page = _session.CurrentPage.Record.FormKey.ToString(),
        _session.Generation,
        _session.Active,
        _session.HasResult,
        _session.CanBack,
        entries = _session.VisibleEntries.Select(value => new
        { ordinal = value.Entry.Index, value.Entry.Text, value.Selectable, value.Error }),
        receipt = _session.LastReceipt is { } receipt ? new
        {
            caller = receipt.Selection.Reference.ToString(),
            page = receipt.Selection.Page.Record.FormKey.ToString(),
            ordinal = receipt.Selection.Entry.Index,
            receipt.Selection.Generation,
            receipt.State,
            receipt.Error,
        } : null,
        sessionError = _session.Error,
        presentationError = Error,
    };

    internal NativeOwnedComputersMenu(FalloutPluginStack records, FalloutTerminalMenu session,
        Action close, Action<Exception> failed)
    {
        Name = "OwnedComputersMenu"; ProcessMode = ProcessModeEnum.Always;
        _records = records; _session = session; _close = close; _failed = failed;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/computers_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _depth = Named("computers_depth_rect"); _list = Named("computers_file_directory");
        _template = new(Named("computers_file_template").Elements().Single());
        _welcome = Named("computers_welcome"); _separator = Named("computers_separator");
        _display = Named("computers_display_zone"); _displayText = Named("computers_display_zone_text");
        _back = Named("computers_result_prompt");
        menu.Elements("template").Remove();
        _tiles.Bind(menu, "alpha", 255);
        foreach (var name in new[] { "computers_intro_welcome", "computers_intro_logon",
            "computers_intro_enterpw", "computers_intro_pwdisplay", "computers_cursor", "computers_result_text" })
            _tiles.Bind(Named(name), "visible", 0);
        _tiles.Text[Named("computers_header1")] = FalloutGameSettingStrings.Read(records, "sComputersHeader1");
        _tiles.Text[Named("computers_header2")] = FalloutGameSettingStrings.Read(records, "sComputersHeader2");
        _tiles.Text[_back] = FalloutGameSettingStrings.Read(records, "sComputersBack");
        var font = _tiles.Font(_back);
        _backButton = new(font.Font, font.Atlas, _tiles.TileColor(_back))
        { Text = _tiles.Text[_back], DrawText = false, FocusMode = FocusModeEnum.All };
        _backButton.Pressed += Back; AddChild(_backButton);
        SetMeta("opennv_ui_source", "menus/computers_menu.xml; source-fonts-and-atlas; ordered-TERM-entries");
        SetMeta("opennv_ui_unverified", "terminal-camera,boot-and-typewriter-timing,scanlines,hacking,matched-pixels");
    }

    public override void _Ready() => Refresh();
    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();

    private void Refresh()
    {
        if (_closed || _faulted) return;
        try
        {
            foreach (var (tile, button) in _rows)
            { _tiles.Forget(tile); tile.Remove(); RemoveChild(button); button.QueueFree(); }
            _rows.Clear();
            var page = _session.RootPage;
            if ((page.Flags & 4) != 0)
                throw new NotSupportedException("Terminal alternate-color presentation has no source owner.");
            var server = _tiles.Root.Descendants().Single(tile => (string?)tile.Attribute("name") == "computers_server");
            _tiles.Text[server] = FalloutGameSettingStrings.Read(_records, "sTerminalServerText" + (page.ServerType + 1));
            _tiles.Text[_welcome] = page.Welcome;
            _tiles.Bind(_welcome, "visible", _session.HasResult || page.Welcome.Length == 0 ? 0 : 1);
            _tiles.Bind(_separator, "visible", _session.HasResult ? 0 : 1);
            _tiles.Bind(_list, "visible", _session.HasResult ? 0 : 1);
            _tiles.Bind(_display, "visible", _session.HasResult ? 1 : 0);
            _tiles.Bind(_displayText, "visible", _session.HasResult ? 1 : 0);
            _tiles.Text[_displayText] = _session.DisplayNote is { } note
                ? FalloutNote.Read(_records, note).RequireText() : _session.DisplayText;
            _tiles.Bind(_back, "visible", 1);
            _backButton.Visible = true;
            var available = _session.VisibleEntries;
            var rowsPerPage = checked((int)_tiles.Number(_list, "_number_of_visible_items"));
            if (rowsPerPage <= 0) throw new InvalidDataException("Source terminal list has no visible row extent.");
            _offset = Math.Clamp(_offset, 0, Math.Max(0, available.Count - rowsPerPage));
            if (!_session.HasResult)
                foreach (var (choice, index) in available.Skip(_offset).Take(rowsPerPage).Select((value, index) => (value, index)))
                {
                    var tile = new XElement(_template); tile.SetAttributeValue("name", $"TerminalEntry_{choice.Entry.Index}");
                    _list.Add(tile);
                    var text = tile.Descendants("text").Single(value => (string?)value.Attribute("name") == "computers_file_template_text");
                    _tiles.BindText(tile, "user0", choice.Entry.Text);
                    _tiles.Bind(tile, "listindex", index);
                    _tiles.Bind(tile, "_enabled", choice.Selectable ? 1 : 0);
                    var font = _tiles.Font(text);
                    var height = _tiles.Number(text, "height") + 2 * _tiles.Number(text, "y");
                    _tiles.Bind(tile, "height", height); _tiles.Bind(tile, "_y", index * height);
                    var generation = _session.Generation;
                    var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.TileColor(text))
                    { Text = choice.Entry.Text, DrawText = false, Disabled = !choice.Selectable, FocusMode = FocusModeEnum.All };
                    button.Pressed += () => Select(choice.Entry.Index, generation);
                    void Focus()
                    {
                        _tiles.Bind(_list, "_highlight_y", _tiles.Number(tile, "_y"));
                        _tiles.Bind(_list, "_selected_height", height); QueueRedraw();
                    }
                    button.FocusEntered += Focus; button.MouseEntered += Focus;
                    AddChild(button); _rows.Add((tile, button));
                }
            var scrollbar = _list.Elements().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar");
            _tiles.Bind(_list, "_scrollbar_vis", 0);
            _tiles.Bind(scrollbar, "_current_value", 0);
            _tiles.Bind(scrollbar, "_number_of_items", available.Count);
            _tiles.Bind(scrollbar, "_number_of_visible_items", rowsPerPage);
            var rowHeight = _rows.Count == 0 ? 0 : _tiles.Number(_rows[0].Tile, "height");
            _tiles.Bind(_list, "height", rowsPerPage * rowHeight);
            _tiles.Bind(_list, "_highlight_y", 0); _tiles.Bind(_list, "_selected_height", rowHeight);
            Layout();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Select(int ordinal, long generation)
    {
        if (_closed || _faulted) return;
        try { _session.Select(ordinal, generation); _offset = 0; Refresh(); }
        catch (Exception error) { Fail(error); }
    }
    private void Back()
    {
        if (_closed) return;
        try
        {
            if (!_faulted && _session.Back()) { _offset = 0; Refresh(); return; }
        }
        catch (Exception error) { Fail(error); }
        _closed = true; _close();
    }
    private void Layout()
    {
        if (!IsInsideTree() || _faulted) return;
        try
        {
            var scale = GetViewportRect().Size.Y / 960;
            Scale = Vector2.One * scale; Size = _tiles.Screen = GetViewportRect().Size / scale;
            _tiles.ResolutionConverter = 1 / scale;
            _tiles.Bind(_depth, "x", (Size.X - _tiles.Number(_depth, "width")) / 2);
            _tiles.Bind(_depth, "y", (Size.Y - _tiles.Number(_depth, "height")) / 2);
            foreach (var (tile, button) in _rows.Append((_back, _backButton)))
            { button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height")); }
            _tiles.ValidateDrawing(); QueueRedraw();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error)
    {
        if (_faulted) return;
        _faulted = true; Error = error.Message;
        foreach (var (_, button) in _rows) button.Disabled = true;
        _failed(error);
    }
    public override void _Input(InputEvent inputEvent)
    {
        if (_closed) return;
        if (inputEvent is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode == Key.Escape) { Back(); GetViewport().SetInputAsHandled(); }
            else if (!_faulted && key.PhysicalKeycode is Key.Pageup or Key.Pagedown)
            { _offset += key.PhysicalKeycode == Key.Pageup ? -1 : 1; Refresh(); GetViewport().SetInputAsHandled(); }
        }
        if (!_faulted && inputEvent is InputEventMouseButton { Pressed: true } mouse &&
            mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { _offset += mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1; Refresh(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Draw()
    {
        if (_closed) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { Fail(error); }
    }
}
