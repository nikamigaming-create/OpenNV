using System.Xml.Linq;
using System.Globalization;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedMessageMenu : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _panel, _title, _body, _list;
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _buttons = [];
    private bool _submitted;
    private readonly Action<Exception> _failed;
    private bool _faulted;

    internal NativeOwnedMessageMenu(FalloutSourceMessage message, FalloutPluginStack records, Action<int> selected, Action<Exception> failed)
    {
        Name = "MessageMenu";
        ProcessMode = ProcessModeEnum.Always;
        _failed = failed;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/message_menu.xml")).Elements("menu").Single();
        _tiles = new(menu);
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _panel = Named("MM_MainRect"); _title = Named("MM_Title"); _body = Named("MM_MessageText"); _list = Named("MM_ButtonList");
        _tiles.Text[_title] = message.Title; _tiles.Text[_body] = message.Text;
        _tiles.Bind(_title, "justify", 1); _tiles.Bind(_body, "justify", 1);
        _tiles.Bind(_title, "visible", message.Title.Length == 0 ? 0 : 1);
        var labels = message.Buttons.Count == 0 ? new[] { FalloutGameSettingStrings.Read(records, "sOk") } : message.Buttons;
        var template = Named("MM_ButtonTemplate").Elements().Single();
        foreach (var (label, index) in labels.Select((label, index) => (label, index)))
        {
            var tile = new XElement(template);
            tile.SetAttributeValue("name", $"MM_Button_{index}");
            _list.Add(tile);
            var text = tile.Descendants().Single(child => child.Name == "text");
            _tiles.Text[text] = label;
            _tiles.Bind(tile, "listindex", index);
            _tiles.Bind(tile, "_enabled", 1);
            var font = _tiles.Font(text);
            var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
            { Name = $"MM_Button_{index}", Text = label, FocusMode = FocusModeEnum.All, DrawText = false };
            button.Pressed += () => { if (_submitted) return; _submitted = true; selected(index); };
            button.MouseEntered += () => { Select(tile); QueueRedraw(); };
            button.FocusEntered += () => { Select(tile); QueueRedraw(); };
            AddChild(button);
            _buttons.Add((tile, button));
        }
        menu.Elements("template").Remove();
        _tiles.Bind(_list, "_enabled", 1);
        var scrollbar = _list.Elements().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar");
        _tiles.Bind(scrollbar, "_current_value", 0);
        _tiles.Bind(scrollbar, "_number_of_items", _buttons.Count);
        SetMeta("opennv_ui_source", "menus/message_menu.xml");
        SetMeta("opennv_ui_message", message.Form.ToString());
        SetMeta("opennv_ui_unbound", "scrolling,focus-sound,exact-layout-timing");
    }

    public override void _Ready()
    {
        GetViewport().SizeChanged += Layout;
        Layout();
        if (!_faulted) Callable.From(_buttons[0].Button.GrabFocus).CallDeferred();
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;
    private void Select(XElement tile)
    {
        _tiles.Bind(_list, "_highlight_y", _tiles.Number(tile, "_y"));
        _tiles.Bind(_list, "_selected_height", _tiles.Number(tile, "height"));
    }
    private void Layout()
    {
        if (_faulted) return;
        try { ApplyLayout(); }
        catch (Exception error) { Fail(error); }
    }
    private void ApplyLayout()
    {
        var scale = GetViewportRect().Size.Y / 960;
        _tiles.ResolutionConverter = 1 / scale;
        Scale = Vector2.One * scale;
        Size = _tiles.Screen = GetViewportRect().Size / scale;
        var maxWidth = _tiles.Number(_tiles.Root, "_MaxMenuWidth");
        var padding = _tiles.Number(_tiles.Root, "_horbuf");
        var textWidth = Math.Max(_tiles.Number(_title, "width"), _tiles.Number(_body, "width"));
        var buttonWidth = _buttons.Max(value => _tiles.Number(value.Tile.Elements("text").Single(), "width")) + padding * 2;
        _tiles.Bind(_panel, "width", Math.Clamp(Math.Max(textWidth + padding * 2, buttonWidth), _tiles.Number(_tiles.Root, "_MinMenuWidth"), maxWidth));
        _tiles.Bind(_list, "width", Math.Min(buttonWidth, maxWidth - padding * 2));
        var height = 0.0f;
        foreach (var (tile, _) in _buttons)
        {
            var size = _tiles.Number(tile.Elements("text").Single(), "height") + _tiles.Number(tile, "_VerticalSpacing");
            _tiles.Bind(tile, "height", size);
            _tiles.Bind(tile, "_y", height);
            height += size;
        }
        // The source height expression clamps the engine-supplied list extent.
        var cap = _list.Element("height")!.Element("min")!;
        var maximum = FalloutMenuXml.Number(cap, (_, _) => throw new NotSupportedException("Message list height cap is unbound."));
        if (height > maximum) throw new NotSupportedException("Message button list requires scrolling.");
        _tiles.Bind(_list, "height", height);
        Select(_buttons[0].Tile);
        foreach (var (tile, button) in _buttons)
        {
            button.Size = new Vector2(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
            button.Position = _tiles.Position(tile);
        }
        _tiles.ValidateDrawing();
        QueueRedraw();
    }
    private void Fail(Exception error)
    {
        _faulted = true;
        foreach (var (_, button) in _buttons) button.Disabled = true;
        Hide();
        _failed(error);
    }
    public override void _Draw()
    {
        if (_faulted) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { Fail(error); }
    }
}

// Shared interpretation of owned tile layout, prefab art and bitmap glyphs.
internal sealed class NativeOwnedMenuTree
{
    internal XElement Root { get; }
    internal Vector2 Screen { get; set; }
    internal float ResolutionConverter { get; set; } = 1;
    internal Color Color { get; }
    internal readonly Dictionary<XElement, string> Text = [];
    private readonly Dictionary<(XElement, string), string> _textValues = [];
    private readonly Dictionary<(XElement, string), float> _values = [];
    private readonly HashSet<(XElement, string)> _evaluating = [];
    private readonly HashSet<(XElement, string)> _evaluatingText = [];
    private readonly Dictionary<XElement, NativeOwnedUiArt> _art = [];
    private readonly Dictionary<int, NativeBitmapFontAsset> _fonts = [];
    private readonly FalloutInstallationSettings _settings;
    private readonly Func<string, string>? _stringSetting;
    private readonly XElement _globals = FalloutMenuXml.Read("menus/globals.xml").Elements().Single();
    internal NativeOwnedMenuTree(XElement root, Func<string, string>? stringSetting = null)
    {
        Root = root;
        _stringSetting = stringSetting;
        _settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        var backgroundOpacity = _settings.Number("Interface", "fMenuBackgroundOpacity");
        if (!float.IsFinite(backgroundOpacity) || backgroundOpacity is < 0 or > 1)
            throw new InvalidDataException("Owned menu background opacity is outside its unit interval.");
        Bind(_globals, "_background_fill_alpha", backgroundOpacity * 255);
        var color = _settings.Unsigned("Interface", "uHUDColor");
        Color = new Color((color >> 24) / 255.0f, ((color >> 16) & 255) / 255.0f, ((color >> 8) & 255) / 255.0f);
    }
    internal void Bind(XElement tile, string trait, float value) => _values[(tile, trait)] = value;
    internal void BindText(XElement tile, string trait, string value) => _textValues[(tile, trait)] = value;
    internal string String(XElement tile, string trait = "string")
    {
        if (trait == "string" && Text.TryGetValue(tile, out var text)) return text;
        if (_textValues.TryGetValue((tile, trait), out var boundText)) return boundText;
        var property = tile.Element(trait);
        if (property is null) return "";
        if (!property.HasElements)
        {
            var literal = property.Value.Trim();
            return literal.StartsWith("entity_-", StringComparison.Ordinal)
                ? (_stringSetting ?? throw new NotSupportedException("Owned string setting has no record owner."))(literal[8..]) : literal;
        }
        var copy = property.Elements().SingleOrDefault();
        if (copy?.Name != "copy" || copy.Attribute("src") is not { } source || copy.Attribute("trait") is not { } key)
            throw new NotSupportedException("Owned text expression has an unbound operation.");
        if (!_evaluatingText.Add((tile, trait))) throw new InvalidDataException("Owned text expression contains a cycle.");
        try { return String(Owner(tile, source.Value), key.Value); }
        finally { _evaluatingText.Remove((tile, trait)); }
    }

    private XElement Owner(XElement tile, string source) => source switch
    {
        "me()" => tile,
        "parent()" => tile.Parent!,
        "io()" => Root,
        "globals()" => _globals,
        _ when source == (string?)Root.Attribute("name") => Root,
        _ when source.StartsWith("sibling(", StringComparison.Ordinal) => tile.Parent!.Elements().Single(value => (string?)value.Attribute("name") == source[8..^1]),
        _ when source.StartsWith("child(", StringComparison.Ordinal) => tile.Elements().Single(value => (string?)value.Attribute("name") == source[6..^1]),
        _ => throw new NotSupportedException($"Owned tile source {source} is unbound."),
    };
    internal Color TileColor(XElement tile)
    {
        var value = tile.Element("systemcolor")?.Value.Trim();
        if (value is null) return tile.Parent is { } parent ? TileColor(parent) : Color;
        if (value == "entity_terminal")
        {
            float Component(string channel)
            {
                var component = _settings.Number("Interface", "iSystemColorTerminal" + channel);
                if (!float.IsFinite(component) || component is < 0 or > 255)
                    throw new InvalidDataException("Owned terminal system color is outside its byte interval.");
                return component / 255;
            }
            return new Color(Component("Red"), Component("Green"), Component("Blue"));
        }
        if (value == "entity_hudmain") return Color;
        throw new NotSupportedException($"Owned menu system color is unbound: {value}.");
    }
    internal NativeBitmapFontAsset Font(XElement tile)
    {
        // An absent font trait is native slot zero, which selects the first
        // configured bitmap font. It is not a system-font fallback.
        var declared = checked((int)Number(tile, "font"));
        var id = declared == 0 ? 1 : declared;
        if (!_fonts.TryGetValue(id, out var font)) _fonts[id] = font = NativeBitmapFontAsset.Read(_settings, id);
        return font;
    }
    private string[] Lines(XElement tile)
    {
        var text = String(tile);
        var width = tile.Element("wrapwidth") is null ? float.PositiveInfinity : Number(tile, "wrapwidth");
        if (width <= 0) width = float.PositiveInfinity;
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length != 0 && Font(tile).Font.Measure(next) > width) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        return lines.ToArray();
    }
    internal float Number(XElement tile, string trait, bool bindings = true)
    {
        if (bindings && _values.TryGetValue((tile, trait), out var bound)) return bound;
        if (trait == "string") return String(tile).Length == 0 ? 0 : 1;
        if (trait is "filewidth" or "fileheight")
        {
            if (!_art.TryGetValue(tile, out var art)) _art[tile] = art = NativeOwnedUiArt.Read(tile, Filename(tile));
            return trait == "filewidth" ? art.Region.Size.X : art.Region.Size.Y;
        }
        if (tile.Name == "text" && trait is "width" or "height")
            return trait == "width" ? Lines(tile).Max(line => Font(tile).Font.Measure(line)) : Lines(tile).Length * Font(tile).Font.Height;
        var property = tile.Element(trait);
        if (property is null || string.IsNullOrWhiteSpace(property.Value) && !property.HasElements)
            return trait switch
            {
                "alpha" or "brightness" => 255,
                "visible" => 1,
                "justify" or "mouseover" or "target" => 0,
                "font" => tile.Parent is { } parent ? Number(parent, trait) : 0,
                "x" or "y" or "width" or "height" or "locus" or "_glow" => 0,
                _ => throw new NotSupportedException($"Owned tile {(string?)tile.Attribute("name")}/{trait} needs a runtime binding.")
            };
        if (!_evaluating.Add((tile, trait))) throw new InvalidDataException($"Owned tile expression cycle: {(string?)tile.Attribute("name")}/{trait}.");
        try
        {
            return FalloutMenuXml.Number(property, (source, key) =>
            {
                if (source == "screen()") return key switch { "width" => Screen.X, "height" => Screen.Y, "resolutionconverter" => ResolutionConverter, _ => throw new NotSupportedException($"Screen trait {key} is unbound.") };
                return Number(Owner(tile, source), key);
            });
        }
        finally { _evaluating.Remove((tile, trait)); }
    }
    internal Vector2 Position(XElement tile)
    {
        var position = new Vector2(Number(tile, "x"), Number(tile, "y"));
        for (var parent = tile.Parent; parent is not null; parent = parent.Parent)
            if (Number(parent, "locus") != 0) position += new Vector2(Number(parent, "x"), Number(parent, "y"));
        return position;
    }
    private string Filename(XElement tile, string trait = "filename")
    {
        var property = tile.Element(trait) ?? throw new NotSupportedException($"Owned texture trait {trait} is unbound.");
        if (!property.HasElements) return property.Value.Trim();
        var index = 0f;
        string? value = null;
        foreach (var operation in property.Elements())
        {
            if (operation.Name != "copy" || (string?)operation.Attribute("src") != "me()")
                throw new NotSupportedException("Owned texture expression operator is unbound.");
            var key = (string?)operation.Attribute("trait") ?? throw new InvalidDataException("Texture expression has no trait.");
            if (key.EndsWith('_'))
            {
                if (index != MathF.Truncate(index)) throw new InvalidDataException("Texture trait index is not integral.");
                value = Filename(tile, key + index.ToString(CultureInfo.InvariantCulture));
            }
            else index = Number(tile, key);
        }
        return value ?? throw new NotSupportedException("Texture expression did not resolve a source filename.");
    }
    internal void ValidateDrawing()
    {
        var errors = new List<string>();
        void Validate(XElement tile)
        {
            try { if (Number(tile, "visible") == 0) return; }
            catch (Exception error) { errors.Add(error.Message); }
            try
            {
                _ = Position(tile);
                if (tile.Element("filename") is not null)
                {
                    if (!_art.ContainsKey(tile)) _art[tile] = NativeOwnedUiArt.Read(tile, Filename(tile));
                    _ = Number(tile, "width"); _ = Number(tile, "height");
                    _ = Number(tile, "brightness"); _ = Number(tile, "alpha");
                    _ = TileColor(tile);
                }
                if (tile.Name == "text") { _ = Font(tile); _ = Lines(tile); _ = Number(tile, "justify"); }
            }
            catch (Exception error) { errors.Add(error.Message); }
            foreach (var child in tile.Elements().Where(child => child.Attribute("name") is not null && child.Name != "template")) Validate(child);
        }
        Validate(Root);
        if (errors.Count != 0) throw new NotSupportedException("Owned menu drawing is unbound: " + string.Join(" | ", errors.Distinct()));
    }
    internal void Draw(CanvasItem canvas)
    {
        void Render(XElement tile, bool visible)
        {
            visible &= Number(tile, "visible") != 0;
            if (!visible) return;
            var color = TileColor(tile);
            if (tile.Element("filename") is not null)
            {
                if (!_art.TryGetValue(tile, out var art)) _art[tile] = art = NativeOwnedUiArt.Read(tile, Filename(tile));
                var size = new Vector2(Number(tile, "width"), Number(tile, "height"));
                if (size.X > 0 && size.Y > 0)
                {
                    var brightness = Number(tile, "brightness") / 255;
                    canvas.DrawTextureRectRegion(art.Texture, new Rect2(Position(tile), size), art.Region,
                        new Color(color.R * brightness, color.G * brightness, color.B * brightness, Number(tile, "alpha") / 255));
                }
            }
            if (tile.Name == "text")
            {
                var font = Font(tile);
                var origin = Position(tile);
                var justify = Number(tile, "justify");
                var brightness = Number(tile, "brightness") / 255;
                var textColor = new Color(color.R * brightness, color.G * brightness, color.B * brightness, Number(tile, "alpha") / 255);
                foreach (var line in Lines(tile))
                {
                    font.Draw(canvas, origin - new Vector2(MathF.Truncate(font.Font.Measure(line) * justify / 2), 0),
                        line, textColor, font.Font.TileBaseline);
                    origin.Y += font.Font.Height;
                }
            }
            foreach (var child in tile.Elements().Where(child => child.Attribute("name") is not null && child.Name != "template")) Render(child, visible);
        }
        Render(Root, true);
    }
}
