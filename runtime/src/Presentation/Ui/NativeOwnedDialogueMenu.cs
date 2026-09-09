using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedDialogueMenu : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _speaker, _response, _list, _scrollbar, _template;
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _choices = [];
    private readonly Button _skip;
    private readonly Action<Exception> _failed;
    private bool _faulted, _submitted;

    internal NativeOwnedDialogueMenu(Action skip, Action<Exception> failed)
    {
        Name = "OwnedDialogue"; _failed = failed;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/dialog/dialog_menu.xml")).Elements("menu").Single();
        _tiles = new(menu);
        XElement Named(string name) => menu.DescendantsAndSelf().Single(element => (string?)element.Attribute("name") == name);
        _speaker = Named("DM_SpeakerNameLabel"); _response = Named("DM_SpeakerText"); _list = Named("DM_TopicList");
        _scrollbar = _list.Elements().Single(element => (string?)element.Attribute("name") == "lb_scrollbar");
        _template = new(Named("DM_TopicTemplate").Elements().Single());
        menu.Elements("template").Remove();
        _tiles.Bind(menu, "_DialogVisible", 1); _tiles.Bind(menu, "_ShowSubtitles", 1);
        _tiles.Bind(_scrollbar, "_current_value", 0);
        _tiles.Bind(_list, "_scrollbar_vis", 0);
        _skip = new Button { Flat = true, MouseFilter = MouseFilterEnum.Stop, FocusMode = FocusModeEnum.None };
        _skip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _skip.Pressed += () => { if (!_submitted) { _submitted = true; skip(); } };
        AddChild(_skip);
        SetMeta("opennv_ui_source", "menus/dialog/dialog_menu.xml");
    }

    internal void Show(string speaker, FalloutConversation conversation, Action<FalloutFormKey> choose)
    {
        foreach (var (tile, button) in _choices) { _tiles.Forget(tile); tile.Remove(); RemoveChild(button); button.QueueFree(); }
        _choices.Clear(); _submitted = false;
        _tiles.Text[_speaker] = speaker;
        var speaking = conversation.Phase == "speaking";
        _tiles.Bind(_tiles.Root, "_ShowingText", speaking ? 1 : 0);
        _tiles.Text[_response] = conversation.Response?.Text ?? "";
        _skip.Visible = speaking;
        foreach (var (choice, index) in conversation.Choices.Select((choice, index) => (choice, index)))
        {
            var tile = new XElement(_template); tile.SetAttributeValue("name", $"Topic_{index}"); _list.Add(tile);
            _tiles.Bind(tile, "listindex", index); _tiles.Bind(tile, "_line_alpha", 255);
            var text = tile.Elements("text").Single(); _tiles.Text[text] = choice.Text;
            var font = _tiles.Font(text);
            var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
            { Text = choice.Text, DrawText = false, FocusMode = FocusModeEnum.All };
            button.Pressed += () => { if (_submitted) return; _submitted = true; choose(choice.Topic); };
            button.MouseEntered += () => Select(tile); button.FocusEntered += () => Select(tile);
            AddChild(button); _choices.Add((tile, button));
        }
        Visible = true;
        Layout();
        if (_choices.Count > 0) Callable.From(_choices[0].Button.GrabFocus).CallDeferred();
    }

    public override void _Ready() { Layout(); }
    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    private void Select(XElement tile)
    {
        _tiles.Bind(_list, "_highlight_y", _tiles.Number(tile, "_y"));
        _tiles.Bind(_list, "_selected_height", _tiles.Number(tile, "height"));
        QueueRedraw();
    }

    private void Layout()
    {
        if (!IsInsideTree() || _faulted) return;
        try
        {
            var scale = GetViewportRect().Size.Y / 960;
            _tiles.ResolutionConverter = 1 / scale;
            Scale = Vector2.One * scale;
            Size = _tiles.Screen = GetViewportRect().Size / scale;
            var height = 0f;
            foreach (var (tile, _) in _choices)
            {
                var rowHeight = _tiles.Number(tile.Elements("text").Single(), "height") + _tiles.Number(tile, "_VerticalSpacing");
                _tiles.Bind(tile, "height", rowHeight); _tiles.Bind(tile, "_y", height); height += rowHeight;
            }
            height = Math.Max(height, _tiles.Number(_tiles.Root, "_MinListHeight"));
            if (height > Size.Y - _tiles.Number(_speaker, "y")) throw new NotSupportedException("Dialogue choices require scrolling.");
            _tiles.Bind(_list, "height", height);
            _tiles.Bind(_list, "_number_of_visible_items", _choices.Count);
            _tiles.Bind(_scrollbar, "_number_of_items", height / _tiles.Number(_list, "_scroll_delta"));
            _tiles.Bind(_scrollbar, "_number_of_visible_items", height / _tiles.Number(_list, "_scroll_delta"));
            foreach (var (tile, button) in _choices)
            {
                button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
            }
            if (_choices.Count > 0) Select(_choices[0].Tile);
            QueueRedraw();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Fail(Exception error) { _faulted = true; _submitted = true; _failed(error); }
    public override void _Draw()
    {
        if (_faulted) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { Fail(error); }
    }
}
