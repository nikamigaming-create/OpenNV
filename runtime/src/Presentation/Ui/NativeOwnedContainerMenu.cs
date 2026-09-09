using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedContainerMenu : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement[] _lists;
    private readonly XElement _template;
    private readonly FalloutPlayerInventory[] _inventories;
    private readonly FalloutPluginStack _records;
    private readonly Action _close, _changed;
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _targets = [];
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _rows = [];
    private readonly int[] _offsets = [0, 0];
    private int _side = 1;
    private bool _closed;
    internal string? Error { get; private set; }
    private const int RowsPerPage = 6;

    internal NativeOwnedContainerMenu(FalloutPluginStack records, FalloutPlayerInventory player, FalloutPlayerInventory container,
        string playerName, string containerName, Action close, Action changed)
    {
        Name = "OwnedContainerMenu"; ProcessMode = ProcessModeEnum.Always;
        _records = records; _inventories = [player, container]; _close = close; _changed = changed;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/container_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _lists = [Named("CM_Items_InventoryList"), Named("CM_Container_InventoryList")];
        _template = new(Named("CM_list_template").Elements().Single());
        menu.Elements("template").Remove();
        _tiles.Text[Named("CM_ItemsTitle")] = playerName;
        _tiles.Text[Named("CM_ContainerTitle")] = containerName;
        _tiles.Bind(Named("CM_ItemData"), "visible", 0);
        foreach (var side in new[] { "Items", "Container" })
            foreach (var direction in new[] { "Left", "Right" })
                _tiles.Bind(Named($"CM_{side}_{direction}FilterArrow"), "visible", 0);
        AddTarget(Named("CM_TakeAllButton"), FalloutGameSettingStrings.Read(records, "sTakeAll"), TakeAll);
        AddTarget(Named("CM_ExitButton"), FalloutGameSettingStrings.Read(records, "sExit"), Close);
        SetMeta("opennv_ui_source", "menus/container_menu.xml; source-fonts-and-atlas");
        SetMeta("opennv_ui_unverified", "quantity-dialog,item-preview,filters,matched-pixels");
    }
    public override void _Ready()
    {
        try { Refresh(); }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_CONTAINER_UI_FAIL {error.Message}"); }
    }
    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    private void AddTarget(XElement tile, string label, Action activate)
    {
        var font = _tiles.Font(tile.DescendantsAndSelf("text").FirstOrDefault() ?? tile);
        var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color) { Text = label, DrawText = false };
        button.Pressed += activate; AddChild(button); _targets.Add((tile, button));
    }
    private void Close() { if (_closed) return; _closed = true; _close(); }
    private void Move(int side, FalloutCampaignItem item)
    {
        if (!FalloutInventoryAccess.CanTransfer(_records.GetEffective(item.FormKey), side == 0)) return;
        _inventories[side].TransferTo(_inventories[1 - side], item.FormKey, item.Count);
        _inventories[0].Notifications.Publish([new(side == 1 ? FalloutHudEventKind.ItemAdded : FalloutHudEventKind.ItemRemoved, item.FormKey, item.Count)]);
        _changed(); Refresh();
    }
    private void TakeAll()
    {
        foreach (var item in TransferableItems(1))
        {
            _inventories[1].TransferTo(_inventories[0], item.FormKey, item.Count);
            _inventories[0].Notifications.Publish([new(FalloutHudEventKind.ItemAdded, item.FormKey, item.Count)]);
        }
        _changed(); Refresh();
    }
    private FalloutCampaignItem[] TransferableItems(int side) => _inventories[side].Items
        .Where(item => FalloutInventoryAccess.CanTransfer(_records.GetEffective(item.FormKey), side == 0)).ToArray();
    private void Refresh()
    {
        foreach (var (tile, button) in _rows) { _tiles.Forget(tile); tile.Remove(); RemoveChild(button); button.QueueFree(); }
        _rows.Clear();
        for (var side = 0; side < 2; side++)
        {
            var items = TransferableItems(side); var list = _lists[side];
            _offsets[side] = Math.Clamp(_offsets[side], 0, Math.Max(0, items.Length - RowsPerPage));
            _tiles.Bind(list, "_scrollbar_vis", 0);
            var scrollbar = list.Elements().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar");
            _tiles.Bind(scrollbar, "_current_value", 0);
            _tiles.Bind(scrollbar, "_number_of_items", items.Length);
            foreach (var (item, index) in items.Skip(_offsets[side]).Take(RowsPerPage).Select((item, index) => (item, index)))
            {
                var tile = new XElement(_template); tile.SetAttributeValue("name", $"Item_{side}_{index}"); list.Add(tile);
                _tiles.Bind(tile, "listindex", index); _tiles.Bind(tile, "height", 56); _tiles.Bind(tile, "_y", index * 56);
                var text = tile.Descendants("text").Single(value => (string?)value.Attribute("name") == "ListItemText");
                var record = _records.GetEffective(item.FormKey);
                var full = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
                var name = full.IsEmpty ? item.EditorId : FalloutDialogueTopic.Text(full.Span);
                var label = item.Count == 1 ? name : $"{name} ({item.Count})";
                _tiles.Text[text] = label;
                var font = _tiles.Font(text);
                var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color) { Text = label, DrawText = false };
                var selectedSide = side;
                button.Pressed += () => Move(selectedSide, item);
                void Select()
                {
                    _side = selectedSide;
                    _tiles.Bind(list, "_highlight_y", index * 56); _tiles.Bind(list, "_selected_height", 56);
                    QueueRedraw();
                }
                button.MouseEntered += Select; button.FocusEntered += Select;
                AddChild(button); _rows.Add((tile, button));
            }
        }
        Layout();
    }
    private void Layout()
    {
        if (!IsInsideTree()) return;
        var scale = GetViewportRect().Size.Y / 960;
        Scale = Vector2.One * scale; Size = _tiles.Screen = GetViewportRect().Size / scale; _tiles.ResolutionConverter = 1 / scale;
        foreach (var (tile, button) in _targets.Concat(_rows))
        { button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height")); }
        _tiles.ValidateDrawing();
        QueueRedraw();
    }
    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode is Key.E or Key.Escape) { Close(); GetViewport().SetInputAsHandled(); }
            else if (key.PhysicalKeycode == Key.A) { TakeAll(); GetViewport().SetInputAsHandled(); }
            else if (key.PhysicalKeycode is Key.Pageup or Key.Pagedown)
            { _offsets[_side] += key.PhysicalKeycode == Key.Pageup ? -RowsPerPage : RowsPerPage; Refresh(); GetViewport().SetInputAsHandled(); }
        }
        if (inputEvent is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { _offsets[_side] += mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1; Refresh(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Draw()
    {
        if (Error is not null) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_CONTAINER_UI_FAIL {error.Message}"); }
    }
}
