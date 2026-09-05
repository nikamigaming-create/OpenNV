using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGamebryoStartMenu : Control
{
    private readonly List<NativeBitmapMenuButton> _buttons = [];
    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _actions = ["sContinue", "sNew", "sLoad", "sSettings", "sCrew", "sDownloads", "sQuit"];
    private readonly Action<string> _activate;
    private readonly FalloutInstallationSettings _settings;
    private readonly XElement _menu;
    private readonly XElement _itemText;
    private readonly XElement _itemTemplate;
    private readonly FalloutBitmapFont _font;
    private readonly Texture2D _atlas;
    private readonly NativeGamebryoLoadingBackground _background;
    private readonly TextureRect _logo;
    private readonly Control _canvas;
    private readonly float _itemHeight;
    private readonly float _rightPadding;
    private readonly float _textTop;
    private readonly Color _color;
    private AudioStreamPlayer? _music;
    private bool _canContinue;
    private NativeStartMenuConfirmation? _confirmation;
    private FalloutPluginStack? _records;

    internal NativeGamebryoStartMenu(Action<string> activate)
    {
        _activate = activate;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("StartMenu requires owned files.");
        _settings = FalloutInstallationSettings.Read(source);
        _menu = ReadXml("menus\\options\\start_menu.xml");
        _itemTemplate = Named(_menu, "lb_item_hotrect");
        _itemText = Named(_itemTemplate, "ListItemText");
        _itemHeight = Literal(_menu, "_item_height");
        _textTop = Literal(_itemText, "y");
        _rightPadding = float.Parse(_itemText.Element("x")!.Element("sub")!.Value.Trim(), CultureInfo.InvariantCulture);
        var fontId = checked((int)Literal(_itemText, "font"));
        var fontPath = _settings.Require("Fonts", $"sFontFile_{fontId}");
        _font = FalloutBitmapFont.Read(Read(fontPath));
        var tex = Read("textures\\fonts\\" + _font.TextureName + ".tex");
        if (tex.Length < 8) throw new InvalidDataException("Font TEX header is truncated.");
        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tex));
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tex.AsSpan(4)));
        if (width <= 0 || height <= 0 || tex.Length != checked(8 + width * height * 4))
            throw new InvalidDataException("Font TEX dimensions do not match its original RGBA bytes.");
        using var atlasImage = Image.CreateFromData(width, height, false, Image.Format.Rgba8, tex.AsSpan(8).ToArray());
        _atlas = ImageTexture.CreateFromImage(atlasImage);
        if (_settings.Contains("Interface", "uHUDColor"))
        {
            var packed = _settings.Unsigned("Interface", "uHUDColor");
            _color = new Color((packed >> 24) / 255.0f, ((packed >> 16) & 255) / 255.0f, ((packed >> 8) & 255) / 255.0f, 1);
        }
        else
            _color = new Color(_settings.Number("Interface", "iSystemColorMainMenuRed") / 255,
                _settings.Number("Interface", "iSystemColorMainMenuGreen") / 255,
                _settings.Number("Interface", "iSystemColorMainMenuBlue") / 255);
        MouseFilter = MouseFilterEnum.Ignore;
        Name = "StartMenu";
        _background = new NativeGamebryoLoadingBackground(_settings, NativeOwnedMediaLoader.LoadTexture(
            "textures\\interface\\main\\" + _settings.Require("Loading", "sMainMenuBackground") + ".dds"));
        AddChild(_background);
        _canvas = new Control { Name = "SourceCanvas", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_canvas);
        var title = Named(_menu, "main_title");
        _logo = new TextureRect
        {
            Name = "main_title",
            Texture = NativeOwnedMediaLoader.LoadTexture("textures\\" + title.Element("filename")!.Value.Trim()),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_logo);
        foreach (var action in _actions)
        {
            var button = new NativeBitmapMenuButton(_font, _atlas, _color) { Name = action, Disabled = true };
            button.Pressed += () => Activate(action);
            _canvas.AddChild(button);
            _buttons.Add(button);
        }
        SetMeta("opennv_ui_source", "menus/options/start_menu.xml");
        SetMeta("opennv_ui_font", _font.TextureName);
    }

    private void Activate(string action)
    {
        if (_confirmation is not null) return;
        if (action == "sNew")
        {
            _confirmation = new NativeStartMenuConfirmation(_settings, _menu, _records!, _color, "sConfirmNew", accepted =>
            {
                var previous = _confirmation;
                _confirmation = null;
                previous?.QueueFree();
                RefreshEnabled();
                if (accepted) _activate(action);
            });
            _canvas.AddChild(_confirmation);
            RefreshEnabled();
            Layout();
        }
        else if (action == "sCrew")
        {
            var credits = new NativeGamebryoCredits(_settings, () => _canvas.Visible = true);
            _canvas.Visible = false;
            AddChild(credits);
        }
        else _activate(action);
    }

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetViewport().SizeChanged += Layout;
        Layout();
        _music = new AudioStreamPlayer { Name = "OwnedMainTitleMusic", Stream = NativeOwnedMediaLoader.LoadAudio("music\\" + _settings.Require("General", "SMainMenuMusicTrack")) };
        _music.AddToGroup("opennv_music");
        AddChild(_music);
        _music.Finished += () => _music.Play();
        _music.Play();
    }

    internal void SetReady(FalloutPluginStack stack, bool canContinue)
    {
        _records = stack;
        _background.SetCatalog(FalloutLoadingScreenCatalog.MainMenu(stack));
        foreach (var action in _actions) _strings[action] = FalloutGameSettingStrings.Read(stack, action);
        _canContinue = canContinue;
        for (var index = 0; index < _buttons.Count; ++index)
        {
            var action = _actions[index];
            _buttons[index].Text = _strings[action];
            _buttons[index].QueueRedraw();
        }
        RefreshEnabled();
        _canvas.Visible = true;
        Layout();
    }

    private void RefreshEnabled()
    {
        for (var index = 0; index < _buttons.Count; ++index)
            _buttons[index].Disabled = _confirmation is not null || _actions[index] is "sContinue" or "sLoad" && !_canContinue;
    }

    private void Layout()
    {
        var size = GetViewportRect().Size;
        Size = size;
        _background.Size = size;
        // The retail tile engine exposes a 960-unit-high canvas; the live 1280×720
        // StartMenu reports 1706.6666×960. Presentation scales this canvas uniformly.
        const float canvasHeight = 960;
        var scale = size.Y / canvasHeight;
        var canvasWidth = size.X / scale;
        _canvas.Scale = Vector2.One * scale;
        _canvas.Size = new Vector2(canvasWidth, canvasHeight);
        var wide = size.X / size.Y >= 16.0f / 9.0f;
        var crop = _settings.Number("Interface", wide ? "iSafeZoneXWide" : "iSafeZoneX");
        var title = Named(_menu, "main_title");
        var zoom = float.Parse(title.Element("zoom")!.Element("copy")!.Value.Trim(), CultureInfo.InvariantCulture);
        if (wide) zoom += float.Parse(title.Element("zoom")!.Element("add")!.Element("copy")!.Value.Trim(), CultureInfo.InvariantCulture);
        _logo.Size = _logo.Texture.GetSize() * (zoom / 100);
        _logo.Position = new Vector2(crop * 2, (canvasHeight - _logo.Size.Y) / 2);
        var totalHeight = _buttons.Count * _itemHeight;
        // list_box.xml retains a five-unit gap between the list extent and its items.
        var itemGap = 5.0f;
        var right = canvasWidth - crop * 2 - itemGap - _rightPadding;
        for (var index = 0; index < _buttons.Count; ++index)
        {
            var button = _buttons[index];
            var width = _font.Measure(button.Text);
            button.Position = new Vector2(right - width, (canvasHeight - totalHeight) / 2 + index * _itemHeight + _textTop);
            button.Size = new Vector2(width, _font.Height);
        }
        if (_confirmation is not null)
        {
            var main = Named(_menu, "main_container");
            var mainWidth = FalloutMenuXml.Number(main.Element("width")!, (_, _) => 0);
            _confirmation.PositionBeside(new Vector2(canvasWidth - crop * 2 - mainWidth, (canvasHeight - totalHeight) / 2));
        }
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private static XElement Named(XElement parent, string name) => parent.DescendantsAndSelf().First(element => (string?)element.Attribute("name") == name);
    private static float Literal(XElement element, string trait) => float.Parse(element.Element(trait)?.Value.Trim() ?? throw new InvalidDataException($"Missing owned menu trait {trait}."), CultureInfo.InvariantCulture);
    private static byte[] Read(string path) => RuntimeLiveContentSource.Current!.TryRead(path, null, out var data, out _) ? data : throw new FileNotFoundException("Missing owned menu resource.", path);
    private static XElement ReadXml(string path)
    {
        // Preserve engine entity tokens as strings. No filesystem/XML entity resolution.
        var text = Encoding.UTF8.GetString(Read(path));
        text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"&(-?[A-Za-z_][A-Za-z0-9_]*);", match => "entity_" + match.Groups[1].Value);
        return XElement.Parse(text);
    }
}

internal sealed partial class NativeStartMenuConfirmation : Control
{
    private readonly NativeBitmapFontAsset _font;
    private readonly Color _color;
    private readonly XElement _question;
    private readonly XElement _itemText;
    private readonly float _itemHeight;
    private readonly string[] _lines;
    private readonly NativeBitmapMenuButton[] _choices;
    private readonly Action<bool> _complete;
    private bool _submitted;
    private readonly float _questionX;
    private readonly float _questionY;

    internal NativeStartMenuConfirmation(FalloutInstallationSettings settings, XElement menu,
        FalloutPluginStack records, Color color, string questionSetting, Action<bool> complete)
    {
        Name = "confirm_container";
        _color = color; _complete = complete;
        _question = menu.Descendants().Single(element => (string?)element.Attribute("name") == "confirm_question");
        var item = menu.Descendants().Single(element => (string?)element.Attribute("name") == "lb_item_hotrect");
        _itemText = item.Descendants("text").Single(element => (string?)element.Attribute("name") == "ListItemText");
        _itemHeight = FalloutMenuXml.Number(menu.Element("_item_height")!, Unbound);
        _font = NativeBitmapFontAsset.Read(settings, checked((int)FalloutMenuXml.Number(_question.Element("font")!, Unbound)));
        _questionX = FalloutMenuXml.Number(_question.Element("x")!, Unbound);
        _questionY = FalloutMenuXml.Number(_question.Element("y")!, (_, trait) => trait == "user5" ? 0 : throw new NotSupportedException(trait));
        var wrapWidth = FalloutMenuXml.Number(_question.Element("wrapwidth")!, Unbound);
        _lines = Wrap(FalloutGameSettingStrings.Read(records, questionSetting), wrapWidth).ToArray();
        _choices = [Choice("sYes", true), Choice("sNo", false)];
        SetMeta("opennv_ui_source", "menus/options/start_menu.xml#confirm_container");
        SetMeta("opennv_ui_font", _font.Font.TextureName);
        SetMeta("opennv_ui_unbound", "engine-confirmation-anchor,fade,focus-sound");
        return;

        NativeBitmapMenuButton Choice(string setting, bool accepted)
        {
            var button = new NativeBitmapMenuButton(_font.Font, _font.Atlas, color)
            { Name = setting, Text = FalloutGameSettingStrings.Read(records, setting) };
            button.Pressed += () => Submit(accepted);
            AddChild(button);
            return button;
        }
    }

    private static float Unbound(string source, string trait) => throw new NotSupportedException($"Confirmation trait is unbound: {source}/{trait}");

    private IEnumerable<string> Wrap(string text, float width)
    {
        foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length != 0 && _font.Font.Measure(next) > width) { yield return line; line = word; }
                else line = next;
            }
            yield return line;
        }
    }

    internal void PositionBeside(Vector2 mainOrigin)
    {
        var padding = float.Parse(_itemText.Element("x")!.Element("sub")!.Value.Trim(), CultureInfo.InvariantCulture);
        var width = _choices.Max(button => _font.Font.Measure(button.Text)) + padding * 2;
        // Relative menu flow while the native engine-owned confirmation anchor
        // is still unbound. Keep that divergence in telemetry; never fit a
        // screenshot or turn these coordinates into a source/parity claim.
        Position = mainOrigin - new Vector2(width + padding, 0);
        Size = new Vector2(width, _itemHeight * _choices.Length);
        for (var index = 0; index < _choices.Length; ++index)
        {
            var button = _choices[index];
            button.Size = new Vector2(_font.Font.Measure(button.Text), _font.Font.Height);
            button.Position = new Vector2(width - padding - button.Size.X,
                index * _itemHeight + FalloutMenuXml.Number(_itemText.Element("y")!, Unbound));
        }
        QueueRedraw();
    }

    private void Submit(bool accepted)
    {
        if (_submitted) return;
        _submitted = true;
        foreach (var choice in _choices) choice.Disabled = true;
        _complete(accepted);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Submit(false);
        }
    }

    public override void _Draw()
    {
        for (var index = 0; index < _lines.Length; ++index)
            _font.Draw(this, new Vector2(_questionX - _font.Font.Measure(_lines[index]), _questionY + index * _font.Font.SourceSize), _lines[index], _color);
    }
}

internal sealed partial class NativeBitmapMenuButton : BaseButton
{
    private readonly FalloutBitmapFont _font;
    private readonly Texture2D _atlas;
    private readonly Color _color;
    internal string Text { get; set; } = "";
    internal bool DrawText { get; set; } = true;
    internal float? Baseline { get; set; }

    internal NativeBitmapMenuButton(FalloutBitmapFont font, Texture2D atlas, Color color)
    {
        _font = font; _atlas = atlas; _color = color;
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        TextureFilter = TextureFilterEnum.Linear;
    }

    public override void _Draw()
    {
        if (!DrawText) return;
        var cursor = 0.0f;
        var tint = Disabled ? new Color(_color.R, _color.G, _color.B, 127.0f / 255) : _color;
        foreach (var character in Text)
        {
            var glyph = _font.Glyph(character);
            if (glyph.Width > 0 && glyph.Height > 0)
                DrawTextureRectRegion(_atlas,
                    new Rect2(cursor + glyph.LeftBearing, (Baseline ?? _font.Ascent) - glyph.Ascent, glyph.Width, glyph.Height),
                    new Rect2(glyph.Left * _atlas.GetWidth(), glyph.Top * _atlas.GetHeight(),
                        (glyph.Right - glyph.Left) * _atlas.GetWidth(), (glyph.Bottom - glyph.Top) * _atlas.GetHeight()), tint);
            cursor += glyph.Advance;
        }
    }
}
