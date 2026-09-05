using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedNameMenu : Control
{
    private readonly XElement _panel;
    private readonly XElement _prompt;
    private readonly XElement _input;
    private readonly XElement _accept;
    private readonly XElement _textBox;
    private readonly XElement _globals;
    private readonly List<(XElement Owner, XElement Image, NativeOwnedUiArt Art)> _images = [];
    private readonly NativeBitmapFontAsset _font;
    private readonly Color _color;
    private readonly LineEdit _entry;
    private readonly NativeBitmapMenuButton _button;
    private readonly string _promptText;
    private Vector2 _canvasSize;
    private Vector2 _panelSize;
    private Vector2 _panelOrigin;
    private float _scale;

    internal NativeOwnedNameMenu(string currentName, FalloutPluginStack records, Action<string> accepted)
    {
        Name = "TextEditMenu";
        var document = FalloutMenuXml.Read("menus/dialog/texteditmenu.xml");
        XElement Id(int id) => document.Descendants().Single(element => element.Element("id")?.Value.Trim() == id.ToString());
        _prompt = Id(2); _input = Id(0); _accept = Id(1);
        _panel = _prompt.Parent ?? throw new InvalidDataException("TextEditMenu prompt has no panel.");
        _textBox = FalloutMenuXml.Read("menus/prefabs/" + _accept.Element("include")!.Attribute("src")!.Value);
        _globals = FalloutMenuXml.Read("menus/globals.xml").Elements().Single();
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        var backgroundOpacity = settings.Number("Interface", "fMenuBackgroundOpacity");
        if (!float.IsFinite(backgroundOpacity) || backgroundOpacity is < 0 or > 1)
            throw new InvalidDataException("Owned menu background opacity is outside its unit interval.");
        _globals.SetElementValue("_background_fill_alpha", backgroundOpacity * 255);
        var buttonText = _textBox.Elements("text").Single(element => (string?)element.Attribute("name") == "button_text");
        var fontId = checked((int)FalloutMenuXml.Number(buttonText.Element("font")!, (_, trait) => trait switch
        {
            "font" => 0,
            "_glow" => 0,
            _ => throw new NotSupportedException($"TextEdit font trait is unbound: {trait}"),
        }));
        _font = NativeBitmapFontAsset.Read(settings, fontId);
        var packed = settings.Unsigned("Interface", "uHUDColor");
        _color = new Color((packed >> 24) / 255.0f, ((packed >> 16) & 255) / 255.0f, ((packed >> 8) & 255) / 255.0f);
        _promptText = FalloutMenuXml.String(_prompt.Element("string")!, records);
        _entry = new LineEdit
        {
            Name = "textedit_text",
            Text = currentName,
            Alignment = HorizontalAlignment.Center,
            SelectAllOnFocus = false,
            Flat = true,
            CaretBlink = true
        };
        var nativeFont = _font.CreateFontFile();
        _entry.AddThemeFontOverride("font", nativeFont);
        _entry.AddThemeFontSizeOverride("font_size", nativeFont.FixedSize);
        _entry.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _entry.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _entry.AddThemeColorOverride("font_color", _color);
        _entry.AddThemeColorOverride("caret_color", _color);
        AddChild(_entry);
        _button = new NativeBitmapMenuButton(_font.Font, _font.Atlas, _color)
        { Name = "textedit_button_ok", Text = FalloutMenuXml.String(_accept.Element("string")!, records), Baseline = _font.Font.TileBaseline };
        void Submit()
        {
            var value = _entry.Text.Trim();
            if (value.Length == 0 || value.Any(char.IsControl)) { _entry.GrabFocus(); return; }
            accepted(value);
        }
        _button.Pressed += Submit;
        _entry.TextSubmitted += _ => Submit();
        AddChild(_button);
        foreach (var element in _panel.Elements())
        {
            if (element.Name == "image") _images.Add((_panel, element, NativeOwnedUiArt.Read(element)));
            if (element.Name != "rect" || element.Element("include") is not { } include) continue;
            var prefab = FalloutMenuXml.Read("menus/prefabs/" + include.Attribute("src")!.Value);
            var owner = new XElement("rect", prefab.Elements().Select(child => new XElement(child)));
            foreach (var property in element.Elements().Where(child => child.Name != "include"))
            {
                owner.Elements(property.Name).Remove();
                owner.Add(new XElement(property));
            }
            foreach (var image in owner.Elements("image")) _images.Add((owner, image, NativeOwnedUiArt.Read(image)));
        }
        SetMeta("opennv_ui_source", "menus/dialog/texteditmenu.xml");
        SetMeta("opennv_ui_font", _font.Font.TextureName);
        SetMeta("opennv_ui_parity", "unverified-font-default-caret-focus-timing");
    }

    private float Number(XElement tile, string trait, Vector2 parent, Vector2 self)
    {
        var property = tile.Element(trait);
        if (property is null) return trait is "x" or "y" ? 0 : throw new InvalidDataException($"Source menu trait is missing: {trait}");
        return FalloutMenuXml.Number(property, (source, key) => source switch
        {
            "screen()" when key == "width" => _canvasSize.X,
            "screen()" when key == "height" => _canvasSize.Y,
            "screen()" when key == "resolutionconverter" => 1 / _scale,
            "parent()" when key == "width" => parent.X,
            "parent()" when key == "height" => parent.Y,
            "parent()" when key == "alpha" => 255,
            "me()" when key == "width" => self.X,
            "me()" when key == "height" => self.Y,
            "globals()" => Number(_globals, key, parent, self),
            _ => throw new NotSupportedException($"Menu layout reference is unbound: {source}/{key}"),
        });
    }

    public override void _Ready()
    {
        GetViewport().SizeChanged += Layout;
        Layout();
        _entry.GrabFocus();
        _entry.CaretColumn = _entry.Text.Length;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private void Layout()
    {
        // The live tile screen owner exposes a 960-unit-high reference canvas.
        _scale = GetViewportRect().Size.Y / 960;
        _canvasSize = GetViewportRect().Size / _scale;
        Size = _canvasSize;
        Scale = Vector2.One * _scale;
        _panelSize = new Vector2(Number(_panel, "width", _canvasSize, Vector2.Zero), Number(_panel, "height", _canvasSize, Vector2.Zero));
        _panelOrigin = new Vector2(Number(_panel, "x", _canvasSize, _panelSize), Number(_panel, "y", _canvasSize, _panelSize));
        var inputSize = new Vector2(Number(_input, "wrapwidth", _panelSize, Vector2.Zero), _font.Font.Height);
        _entry.Size = inputSize;
        _entry.Position = _panelOrigin + new Vector2(Number(_input, "x", _panelSize, inputSize) - inputSize.X / 2,
            Number(_input, "y", _panelSize, inputSize));
        var buttonSize = new Vector2(_font.Font.Measure(_button.Text) + Number(_textBox, "_horbuf", _panelSize, Vector2.Zero),
            _font.Font.Height + Number(_textBox, "_verbuf", _panelSize, Vector2.Zero));
        _button.Size = new Vector2(_font.Font.Measure(_button.Text), _font.Font.Height);
        _button.Position = _panelOrigin + new Vector2(Number(_accept, "_x", _panelSize, buttonSize) - buttonSize.X,
            Number(_accept, "_y", _panelSize, buttonSize)) + (buttonSize - _button.Size) / 2;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (owner, tile, art) in _images)
        {
            var ownerSize = owner == _panel ? _panelSize : new Vector2(Number(owner, "width", _panelSize, Vector2.Zero),
                Number(owner, "height", _panelSize, Vector2.Zero));
            var ownerOrigin = owner == _panel ? _panelOrigin : _panelOrigin +
                new Vector2(Number(owner, "x", _panelSize, ownerSize), Number(owner, "y", _panelSize, ownerSize));
            var size = new Vector2(Number(tile, "width", ownerSize, Vector2.Zero), Number(tile, "height", ownerSize, Vector2.Zero));
            var origin = ownerOrigin + new Vector2(Number(tile, "x", ownerSize, size), Number(tile, "y", ownerSize, size));
            var alpha = tile.Element("alpha") is null ? 1 : Number(tile, "alpha", ownerSize, size) / 255;
            var brightness = tile.Element("brightness") is null ? 1 : Number(tile, "brightness", ownerSize, size) / 255;
            DrawTextureRectRegion(art.Texture, new Rect2(origin, size), art.Region,
                new Color(_color.R * brightness, _color.G * brightness, _color.B * brightness, alpha));
        }
        var promptSize = new Vector2(_font.Font.Measure(_promptText), _font.Font.Height);
        _font.Draw(this, _panelOrigin + new Vector2(Number(_prompt, "x", _panelSize, promptSize) - promptSize.X / 2,
            Number(_prompt, "y", _panelSize, promptSize)), _promptText, _color, _font.Font.TileBaseline);
    }
}
