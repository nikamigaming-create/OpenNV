using System.Globalization;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

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
    private NativeOwnedQuantityMenu? _quantity;
    private readonly uint _askQuantityAt;
    private readonly Label _transferStatus;
    private string? _layoutError;
    internal string? TransferError { get; private set; }
    internal string? Error => _layoutError ?? TransferError;
    private readonly int[] _pageSizes = [1, 1];
    private readonly Func<(float Weight, float Capacity)>? _burden;
    private readonly Func<FalloutCampaignItem, IReadOnlyList<FalloutInventoryInstance>, ContainerItemCard?>? _itemCard;
    private (int Side, FalloutFormKey Form)? _selection;
    private readonly Dictionary<XElement, (int Side, FalloutFormKey Form, float Y, float Height)> _rowSelection = [];

    // Exact item-card formatting belongs to the qualified numeric owner.
    // No barter multiplier, stack rounding or variant policy is invented here.
    internal sealed record ContainerItemCard(string Weight, string Value);

    internal NativeOwnedContainerMenu(FalloutPluginStack records, FalloutPlayerInventory player, FalloutPlayerInventory container,
        string containerName, Action close, Action changed,
        Func<(float Weight, float Capacity)> burden,
        Func<FalloutCampaignItem, IReadOnlyList<FalloutInventoryInstance>, ContainerItemCard?>? itemCard = null)
        : this(records, player, container, containerName, close, changed,
            burden ?? throw new ArgumentNullException(nameof(burden)), itemCard, componentFixture: false)
    { }

    internal static NativeOwnedContainerMenu CreateComponentFixture(FalloutPluginStack records,
        FalloutPlayerInventory player, FalloutPlayerInventory container, string containerName, Action close, Action changed) =>
        new(records, player, container, containerName, close, changed, burden: null, itemCard: null, componentFixture: true);

    private NativeOwnedContainerMenu(FalloutPluginStack records, FalloutPlayerInventory player, FalloutPlayerInventory container,
        string containerName, Action close, Action changed, Func<(float Weight, float Capacity)>? burden,
        Func<FalloutCampaignItem, IReadOnlyList<FalloutInventoryInstance>, ContainerItemCard?>? itemCard, bool componentFixture)
    {
        Name = "OwnedContainerMenu"; ProcessMode = ProcessModeEnum.Always;
        _records = records; _inventories = [player, container]; _close = close; _changed = changed;
        _burden = burden; _itemCard = itemCard;
        if (componentFixture)
        {
            SetMeta("opennv_component_fixture", true);
            GD.Print("OPENNV_CONTAINER_COMPONENT_BURDEN_UNBOUND effective-player-value-owner-not-hosted;burden-display-not-proven");
        }
        _askQuantityAt = Math.Max(1, FalloutGameSettingIntegers.ReadRetained(records, "iInventoryAskQuantityAt", nameof(NativeOwnedContainerMenu)));
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/container_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _lists = [Named("CM_Items_InventoryList"), Named("CM_Container_InventoryList")];
        _template = new(Named("CM_list_template").Elements().Single());
        menu.Elements("template").Remove();
        _tiles.Text[Named("CM_ContainerTitle")] = containerName;
        _tiles.Bind(Named("CM_ItemData"), "visible", 0);
        _tiles.Bind(Named("CM_ItemIcon"), "visible", 0);
        _tiles.Bind(Named("CM_ArrowIcon"), "visible", 0);
        foreach (var side in new[] { "Items", "Container" })
            foreach (var direction in new[] { "Left", "Right" })
                _tiles.Bind(Named($"CM_{side}_{direction}FilterArrow"), "visible", 0);
        AddTarget(Named("CM_TakeAllButton"), FalloutGameSettingStrings.Read(records, "sTakeAll"), TakeAll);
        AddTarget(Named("CM_ExitButton"), FalloutGameSettingStrings.Read(records, "sExit"), Close);
        var statusFont = _tiles.Font(Named("CM_TakeAllButton").DescendantsAndSelf("text").FirstOrDefault() ?? Named("CM_TakeAllButton"));
        _transferStatus = new()
        {
            Name = "ContainerTransferRefusal",
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _transferStatus.AddThemeFontOverride("font", statusFont.CreateFontFile());
        _transferStatus.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(statusFont.Font.SourceSize));
        _transferStatus.AddThemeColorOverride("font_color", _tiles.Color);
        AddChild(_transferStatus);
        SetMeta("opennv_ui_source", "menus/container_menu.xml; source-fonts-and-atlas");
        SetMeta("opennv_ui_unverified", "item-preview,filters,theft,retail-transfer-policy,matched-pixels");
    }
    public override void _Ready()
    {
        try { Refresh(); }
        catch (Exception error) { _layoutError = error.Message; GD.PushError($"OPENNV_CONTAINER_UI_FAIL {error.Message}"); }
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
        if (_closed || _quantity is not null) return;
        if (!FalloutInventoryAccess.CanTransfer(_records, _records.GetEffective(item.FormKey), side == 0)) return;
        var available = _inventories[side].Item(item.FormKey)?.Count ?? 0;
        if (available == 0) { Refresh(); return; }
        if (available < _askQuantityAt || available == 1) { Transfer(side, item.FormKey, 1); return; }
        _quantity = new(_records, available, count =>
        {
            var menu = _quantity!; _quantity = null;
            RemoveChild(menu); menu.QueueFree();
            if (count is { } selected) Transfer(side, item.FormKey, selected);
        });
        AddChild(_quantity);
    }
    private void Transfer(int side, FalloutFormKey form, int count)
    {
        // Recheck after the modal choice; another authoritative action may
        // have changed this inventory while the quantity menu was open.
        if ((_inventories[side].Item(form)?.Count ?? 0) < count) { Refresh(); return; }
        ApplyTransfer(() =>
        {
            FalloutReferenceWorld.RequirePlayerItemAcquisition(_records, _records.GetEffective(form),
                side == 1 ? _records.RuntimeFormKey(0x14) : null);
            var notice = new FalloutHudEvent(side == 1 ? FalloutHudEventKind.ItemAdded : FalloutHudEventKind.ItemRemoved, form, count);
            _inventories[0].Notifications.RequirePublish([notice]);
            _inventories[side].TransferTo(_inventories[1 - side], form, count);
            _inventories[0].Notifications.Publish([notice]);
        });
    }
    private void TakeAll()
    {
        if (_closed || _quantity is not null) return;
        ApplyTransfer(() =>
        {
            var items = TransferableItems(1);
            foreach (var item in items) FalloutReferenceWorld.RequirePlayerItemAcquisition(_records, _records.GetEffective(item.FormKey), _records.RuntimeFormKey(0x14));
            var notices = items.Select(item => new FalloutHudEvent(FalloutHudEventKind.ItemAdded, item.FormKey, item.Count)).ToArray();
            _inventories[0].Notifications.RequirePublish(notices);
            _inventories[1].TransferItemsTo(_inventories[0], items.Select(item => new FalloutInventoryTransfer(item.FormKey, item.Count)).ToArray());
            _inventories[0].Notifications.Publish(notices);
        });
    }
    private void ApplyTransfer(Action transfer)
    {
        try
        {
            transfer();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or OverflowException or KeyNotFoundException)
        {
            TransferError = error.Message;
            _transferStatus.Text = "Transfer refused: " + error.Message;
            _transferStatus.Visible = true;
            GD.PushError($"OPENNV_CONTAINER_TRANSFER_REFUSED {error.Message}");
            return;
        }
        TransferError = null; _transferStatus.Visible = false;
        _changed(); Refresh();
    }
    private FalloutCampaignItem[] TransferableItems(int side) => _inventories[side].Items
        .Where(item => FalloutInventoryAccess.CanTransfer(_records, _records.GetEffective(item.FormKey), side == 0)).ToArray();
    private XElement Tile(string name) => _tiles.Root.DescendantsAndSelf()
        .Single(tile => (string?)tile.Attribute("name") == name);

    private void Refresh()
    {
        _tiles.Forget(_rows.Select(row => row.Tile));
        foreach (var (tile, button) in _rows) { tile.Remove(); RemoveChild(button); button.QueueFree(); }
        _rows.Clear(); _rowSelection.Clear();
        var discarded = new List<XElement>();
        if (_selection is { } selected && !TransferableItems(selected.Side).Any(item => item.FormKey == selected.Form))
            _selection = null;
        for (var side = 0; side < 2; side++)
        {
            var items = TransferableItems(side); var list = _lists[side];
            _offsets[side] = Math.Clamp(_offsets[side], 0, Math.Max(0, items.Length - 1));
            var viewport = _tiles.Number(list, "height");
            var unit = _tiles.Number(list, "_scroll_delta");
            if (!float.IsFinite(viewport) || viewport <= 0 || !float.IsFinite(unit) || unit <= 0)
                throw new InvalidDataException("Container source list has invalid viewport/scroll metrics.");
            var scrollbar = list.Elements().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar");
            var marker = scrollbar.Descendants().Single(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker");
            _tiles.Bind(scrollbar, "_SetInCode", 1);
            _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0);
            var extent = 0f;
            var entries = new List<(FalloutCampaignItem Item, int Index, XElement Tile, XElement Text, string Label, float Y, float Height)>();
            foreach (var (item, index) in items.Select((item, index) => (item, index)))
            {
                var tile = new XElement(_template); tile.SetAttributeValue("name", $"Item_{side}_{index}"); list.Add(tile);
                _tiles.Bind(tile, "listindex", index);
                var text = tile.Descendants("text").Single(value => (string?)value.Attribute("name") == "ListItemText");
                var record = _records.GetEffective(item.FormKey);
                var full = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
                var name = full.IsEmpty ? item.EditorId : FalloutDialogueTopic.Text(full.Span);
                var label = item.Count == 1 ? name : $"{name} ({item.Count})";
                _tiles.Text[text] = label;
                var height = _tiles.Number(text, "height") + _tiles.Number(tile, "_VerticalSpacing");
                if (!float.IsFinite(height) || height <= 0 || height > viewport)
                    throw new InvalidDataException("Container source row does not fit its source viewport.");
                entries.Add((item, index, tile, text, label, extent, height)); extent += height;
            }
            var maximumScroll = Math.Max(0, extent - viewport);
            var maximumIndex = entries.FindIndex(entry => entry.Y >= maximumScroll);
            if (maximumIndex < 0) maximumIndex = Math.Max(0, entries.Count - 1);
            _offsets[side] = Math.Min(_offsets[side], maximumIndex);
            var offset = entries.Count == 0 ? 0 : Math.Min(entries[_offsets[side]].Y, maximumScroll);
            var count = 0;
            foreach (var (item, index, tile, text, label, y, height) in entries)
            {
                if (y + height <= offset || y >= offset + viewport)
                { discarded.Add(tile); tile.Remove(); continue; }
                _tiles.Bind(tile, "height", height); _tiles.Bind(tile, "_y", y);
                var font = _tiles.Font(text);
                var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color) { Text = label, DrawText = false };
                var selectedSide = side;
                button.Pressed += () => { SelectRow(tile); Move(selectedSide, item); };
                button.MouseEntered += () => SelectRow(tile);
                button.FocusEntered += () => SelectRow(tile);
                _rowSelection.Add(tile, (side, item.FormKey, y - offset, height));
                AddChild(button); _rows.Add((tile, button));
                if (y >= offset && y + height <= offset + viewport) count++;
            }
            _pageSizes[side] = Math.Max(1, count);
            _tiles.Bind(list, "_scrollbar_vis", extent > viewport ? 1 : 0);
            _tiles.Bind(scrollbar, "visible", extent > viewport ? 1 : 0);
            _tiles.Bind(scrollbar, "_current_value", offset / unit);
            _tiles.Bind(scrollbar, "_number_of_items", extent / unit);
            _tiles.Bind(scrollbar, "_number_of_visible_items", viewport / unit);
        }
        _tiles.Forget(discarded);
        BindSelection();
        Layout();
    }

    private void SelectRow(XElement tile)
    {
        var row = _rowSelection[tile];
        if (_selection is { } selected && selected.Side == row.Side && selected.Form == row.Form) return;
        _side = row.Side; _selection = (row.Side, row.Form);
        BindSelection(); Layout();
    }

    private void BindSelection()
    {
        foreach (var list in _lists)
        { _tiles.Bind(list, "_highlight_y", -1); _tiles.Bind(list, "_selected_height", 0); }
        foreach (var (tile, row) in _rowSelection)
        {
            var chosen = _selection is { } selection && selection.Side == row.Side && selection.Form == row.Form;
            _tiles.Bind(tile, "_selected", chosen ? 1 : 0);
            if (chosen)
            {
                _tiles.Bind(_lists[row.Side], "_highlight_y", row.Y);
                _tiles.Bind(_lists[row.Side], "_selected_height", row.Height);
            }
        }
        SetMeta("opennv_container_burden_owner", _burden is null ? "unbound-component-fixture" : "effective-player-movement-owner");
        _tiles.Text[Tile("CM_Items_CapsLabel")] = "";
        if (_burden is not null)
        {
            var burden = _burden();
            if (!float.IsFinite(burden.Weight) || !float.IsFinite(burden.Capacity))
                throw new InvalidDataException("Container carried weight/capacity is not finite.");
            // Retain exact effective quantities; integer menu formatting still
            // requires native qualification (recorded explicitly in the draft).
            _tiles.Text[Tile("CM_Items_CapsLabel")] = string.Create(CultureInfo.CurrentCulture,
                $"{FalloutGameSettingStrings.Read(_records, "sInventoryWeightUpper")} {burden.Weight}/{burden.Capacity}");
        }
        var item = _selection is { } current ? _inventories[current.Side].Item(current.Form) : null;
        _tiles.Bind(Tile("CM_ArrowIcon"), "visible", item is null ? 0 : 1);
        _tiles.Bind(Tile("CM_ArrowIcon"), "user0", _selection?.Side == 0 ? 1 : 2);
        _tiles.Bind(Tile("CM_ItemIcon"), "visible", 0);
        _tiles.Bind(Tile("CM_ItemData"), "visible", 0);
        if (item is null) return;
        var icon = _records.GetEffective(item.FormKey).ReadSubrecords().SingleOrDefault(field => field.Signature == "ICON").Data;
        if (!icon.IsEmpty)
        { _tiles.SetFilename(Tile("CM_ItemIcon"), FalloutDialogueTopic.Text(icon.Span)); _tiles.Bind(Tile("CM_ItemIcon"), "visible", 1); }
        var card = _itemCard?.Invoke(item, _inventories[_selection!.Value.Side].Instances(item.FormKey));
        SetMeta("opennv_container_item_card_owner", card is null ? "unbound-native-numeric-policy" : "provided-numeric-variant-owner");
        if (card is null) return;
        _tiles.Bind(Tile("CM_ItemData"), "visible", 1);
        _tiles.Bind(Tile("WeightInfo"), "visible", 1);
        _tiles.Bind(Tile("ValueInfo"), "visible", 1);
        _tiles.BindText(Tile("WeightInfo"), "_Value", card.Weight);
        _tiles.BindText(Tile("ValueInfo"), "_Value", card.Value);
    }
    private void Layout()
    {
        if (!IsInsideTree()) return;
        var scale = GetViewportRect().Size.Y / 960;
        Scale = Vector2.One * scale; Size = _tiles.Screen = GetViewportRect().Size / scale; _tiles.ResolutionConverter = 1 / scale;
        foreach (var (tile, button) in _targets.Concat(_rows))
        { button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height")); }
        foreach (var (tile, button) in _rows)
        {
            var list = _lists[_rowSelection[tile].Side];
            var bounds = new Rect2(_tiles.Position(list), new(_tiles.Number(list, "width"), _tiles.Number(list, "height")));
            var clipped = new Rect2(button.Position, button.Size).Intersection(bounds);
            button.Position = clipped.Position; button.Size = clipped.Size;
        }
        _tiles.ValidateDrawing();
        _transferStatus.Position = new(24, Math.Max(24, Size.Y - 160));
        _transferStatus.Size = new(Math.Max(1, Size.X - 48), 120);
        QueueRedraw();
    }
    public override void _Input(InputEvent inputEvent)
    {
        if (_quantity is not null || _closed) return;
        if (inputEvent is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode is Key.E or Key.Escape) { Close(); GetViewport().SetInputAsHandled(); }
            else if (key.PhysicalKeycode == Key.A) { TakeAll(); GetViewport().SetInputAsHandled(); }
            else if (key.PhysicalKeycode is Key.Pageup or Key.Pagedown)
            { _offsets[_side] += key.PhysicalKeycode == Key.Pageup ? -_pageSizes[_side] : _pageSizes[_side]; Refresh(); GetViewport().SetInputAsHandled(); }
        }
        if (inputEvent is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { _offsets[_side] += mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1; Refresh(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Draw()
    {
        if (_layoutError is not null) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { _layoutError = error.Message; GD.PushError($"OPENNV_CONTAINER_UI_FAIL {error.Message}"); }
    }
}
