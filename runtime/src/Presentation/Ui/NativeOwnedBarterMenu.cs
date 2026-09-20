using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Shared barter offers against source-backed player and merchant inventories.</summary>
internal sealed partial class NativeOwnedBarterMenu : Control
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPlayerInventory _playerInventory, _merchantInventory;
    private readonly FalloutFormKey _caps;
    private readonly FalloutBarterPricing _pricing;
    private readonly Func<float> _barterSkill;
    private readonly Action<IReadOnlyList<FalloutTradeTransfer>> _exchange;
    private readonly Action _close;
    private readonly int _discount;
    private readonly VBoxContainer[] _rows = new VBoxContainer[2];
    private readonly Label _selectedLabel, _quantityLabel, _offerLabel, _totalsLabel, _message;
    private readonly Button _decrease, _increase, _offerButton, _offerAllButton, _closeButton, _acceptButton;
    private readonly Dictionary<(bool FromPlayer, FalloutFormKey Form), int> _offers = [];
    private FalloutCampaignItem? _selected;
    private int _selectedSide, _quantity = 1;
    private bool _closed;

    internal NativeOwnedBarterMenu(FalloutPluginStack records, FalloutPlayerInventory playerInventory,
        FalloutPlayerInventory merchantInventory, FalloutFormKey caps, string merchantName, int discount,
        FalloutBarterPricing pricing, Func<float> barterSkill,
        Action<IReadOnlyList<FalloutTradeTransfer>> exchange, Action close)
    {
        if (discount is < -100 or > 100) throw new ArgumentOutOfRangeException(nameof(discount));
        Name = "OwnedBarterMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        _records = records; _playerInventory = playerInventory; _merchantInventory = merchantInventory;
        _caps = caps; _discount = discount; _pricing = pricing; _barterSkill = barterSkill;
        _exchange = exchange; _close = close;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Name = "Backdrop", Color = new(0.015f, 0.02f, 0.03f, 0.88f), MouseFilter = MouseFilterEnum.Stop, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        var panel = new PanelContainer { Name = "BarterPanel", CustomMinimumSize = new(1120, 760) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new(0.035f, 0.045f, 0.055f, 0.98f), BorderColor = new(0.64f, 0.74f, 0.54f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 22, ContentMarginTop = 18, ContentMarginRight = 22, ContentMarginBottom = 18
        });
        center.AddChild(panel);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10); panel.AddChild(column);
        var title = new Label { Name = "BarterTitle", Text = $"Trade with {merchantName}", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26); column.AddChild(title);
        var inventories = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        inventories.AddThemeConstantOverride("separation", 18); column.AddChild(inventories);
        BuildInventoryColumn(inventories, 0, "Your Inventory");
        BuildInventoryColumn(inventories, 1, "Merchant Inventory");

        var details = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 8); column.AddChild(details);
        _selectedLabel = new Label { Text = "Select an item to offer.", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        details.AddChild(_selectedLabel);
        _decrease = new Button { Text = "−", CustomMinimumSize = new(46, 42), FocusMode = FocusModeEnum.All };
        _decrease.Pressed += () => ChangeQuantity(-1); details.AddChild(_decrease);
        _quantityLabel = new Label { Text = "0", CustomMinimumSize = new(64, 42), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        details.AddChild(_quantityLabel);
        _increase = new Button { Text = "+", CustomMinimumSize = new(46, 42), FocusMode = FocusModeEnum.All };
        _increase.Pressed += () => ChangeQuantity(1); details.AddChild(_increase);
        _offerButton = new Button { Text = "Update Offer", CustomMinimumSize = new(142, 42), FocusMode = FocusModeEnum.All };
        _offerButton.Pressed += SetOffer; details.AddChild(_offerButton);
        _offerAllButton = new Button { Text = "Offer All", CustomMinimumSize = new(110, 42), FocusMode = FocusModeEnum.All };
        _offerAllButton.Pressed += OfferAll; details.AddChild(_offerAllButton);

        var summary = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        summary.AddThemeConstantOverride("separation", 14); column.AddChild(summary);
        _offerLabel = new Label { Name = "BarterOffers", Text = "No items offered.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        summary.AddChild(_offerLabel);
        _totalsLabel = new Label { Name = "BarterTotals", Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(340, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        summary.AddChild(_totalsLabel);

        _message = new Label { Name = "BarterMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_message);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("separation", 10); column.AddChild(actions);
        var clear = new Button { Name = "ClearBarterOffers", Text = "Clear Offers", CustomMinimumSize = new(140, 44), FocusMode = FocusModeEnum.All };
        clear.Pressed += () => { _offers.Clear(); _message.Text = ""; Refresh(); }; actions.AddChild(clear);
        _closeButton = new Button { Name = "CloseBarterMenu", Text = "Cancel", CustomMinimumSize = new(130, 44), FocusMode = FocusModeEnum.All };
        _closeButton.Pressed += Close; actions.AddChild(_closeButton);
        _acceptButton = new Button { Name = "AcceptBarter", Text = "Accept", CustomMinimumSize = new(160, 44), FocusMode = FocusModeEnum.All };
        _acceptButton.Pressed += Accept; actions.AddChild(_acceptButton);
        SetMeta("opennv_ui_source", "ACHR.XMRC; MISC.Caps001; source barter/item-condition settings; bilateral shared-inventory transaction");
        SetMeta("opennv_ui_unverified", "price-perks-reputation,merchant-restock,matched-pixels,physical-headset-acceptance");
        Refresh();
    }

    private void BuildInventoryColumn(HBoxContainer parent, int side, string heading)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6); parent.AddChild(column);
        var label = new Label { Text = heading, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 19); column.AddChild(label);
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(panel);
        var scroll = new ScrollContainer { Name = side == 0 ? "PlayerBarterScroll" : "MerchantBarterScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);
        _rows[side] = new VBoxContainer { Name = side == 0 ? "PlayerBarterRows" : "MerchantBarterRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows[side].AddThemeConstantOverride("separation", 3); scroll.AddChild(_rows[side]);
    }

    private void Refresh()
    {
        RefreshRows(0, _playerInventory);
        RefreshRows(1, _merchantInventory);
        UpdateSelection();
        UpdateOffersAndTotals();
    }

    private void RefreshRows(int side, FalloutPlayerInventory inventory)
    {
        foreach (var child in _rows[side].GetChildren()) { _rows[side].RemoveChild(child); child.QueueFree(); }
        var fromPlayer = side == 0;
        foreach (var item in inventory.Items.Where(item => item.FormKey != _caps)
                     .OrderBy(item => NameOf(item.FormKey), StringComparer.CurrentCultureIgnoreCase))
        {
            var offered = _offers.GetValueOrDefault((fromPlayer, item.FormKey));
            var available = CanOffer(item, fromPlayer, out var price, out var reason);
            var text = $"{NameOf(item.FormKey)}   ×{item.Count}   {(fromPlayer ? "sell" : "buy")} {price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"} caps";
            if (offered != 0) text += $"   [offering {offered}]";
            if (!available) text += "   [" + reason + "]";
            var button = new Button
            {
                Name = $"BarterItem_{side}_{item.FormKey.ObjectId:x6}", Text = text,
                Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = !available
            };
            button.Pressed += () => Select(side, item);
            _rows[side].AddChild(button);
        }
        if (_rows[side].GetChildCount() == 0)
            _rows[side].AddChild(new Label { Text = "No tradeable items." });
    }

    private bool CanOffer(FalloutCampaignItem item, bool fromPlayer, out int? price, out string reason)
    {
        price = null; reason = "";
        if (!FalloutInventoryAccess.CanTransfer(_records.GetEffective(item.FormKey), fromPlayer))
        { reason = "source flags"; return false; }
        try
        {
            price = _pricing.Total(item, 1, vendorSells: !fromPlayer, _barterSkill(), _discount);
            return true;
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { reason = "price unbound"; return false; }
    }

    private void Select(int side, FalloutCampaignItem item)
    {
        _selectedSide = side; _selected = item;
        var fromPlayer = side == 0;
        var inventory = fromPlayer ? _playerInventory : _merchantInventory;
        _quantity = inventory.Equipped.Contains(item.RuntimeFormId) ? item.Count :
            _offers.GetValueOrDefault((fromPlayer, item.FormKey), 1);
        UpdateSelection();
    }

    private void ChangeQuantity(int delta)
    {
        if (_selected is not { } item) return;
        var inventory = _selectedSide == 0 ? _playerInventory : _merchantInventory;
        if (inventory.Equipped.Contains(item.RuntimeFormId)) return;
        _quantity = Math.Clamp(_quantity + delta, 0, item.Count);
        UpdateSelection();
    }

    private void SetOffer()
    {
        if (_selected is not { } item) return;
        var side = _selectedSide == 0;
        var inventory = side ? _playerInventory : _merchantInventory;
        if (_quantity != 0 && inventory.Equipped.Contains(item.RuntimeFormId) && _quantity != item.Count)
        {
            _message.Text = "Equipped items must be traded as a complete stack.";
            return;
        }
        var key = (side, item.FormKey);
        if (_quantity == 0) _offers.Remove(key); else _offers[key] = _quantity;
        _message.Text = ""; Refresh();
    }

    private void OfferAll()
    {
        if (_selected is not { } item) return;
        _quantity = item.Count; SetOffer();
    }

    private void UpdateSelection()
    {
        if (_selected is not { } item)
        {
            _selectedLabel.Text = "Select an item to offer."; _quantityLabel.Text = "0";
            _decrease.Disabled = true; _increase.Disabled = true; _offerButton.Disabled = true; _offerAllButton.Disabled = true;
            return;
        }
        _selectedLabel.Text = $"{(_selectedSide == 0 ? "You offer" : "You buy")}: {NameOf(item.FormKey)}";
        _quantityLabel.Text = $"{_quantity}/{item.Count}";
        var inventory = _selectedSide == 0 ? _playerInventory : _merchantInventory;
        var equipped = inventory.Equipped.Contains(item.RuntimeFormId);
        _decrease.Disabled = _quantity <= 0 || equipped; _increase.Disabled = _quantity >= item.Count || equipped;
        _offerButton.Disabled = false; _offerAllButton.Disabled = false;
    }

    private void UpdateOffersAndTotals()
    {
        try
        {
            var sell = 0; var buy = 0; var lines = new List<string>();
            foreach (var pair in _offers.OrderBy(pair => pair.Key.FromPlayer ? 0 : 1)
                         .ThenBy(pair => NameOf(pair.Key.Form), StringComparer.CurrentCultureIgnoreCase))
            {
                var fromPlayer = pair.Key.FromPlayer;
                var form = pair.Key.Form;
                var count = pair.Value;
                var inventory = fromPlayer ? _playerInventory : _merchantInventory;
                var item = inventory.Item(form) ?? throw new InvalidOperationException($"Offered item {form} left its inventory.");
                var price = _pricing.Total(item, count, vendorSells: !fromPlayer, _barterSkill(), _discount);
                if (fromPlayer) sell = checked(sell + price); else buy = checked(buy + price);
                lines.Add($"{(fromPlayer ? "You give" : "You take")}: {NameOf(form)} ×{count}  —  {price} caps");
            }
            _offerLabel.Text = lines.Count == 0 ? "No items offered." : string.Join('\n', lines);
            var difference = checked(buy - sell);
            var playerCaps = _playerInventory.Item(_caps)?.Count ?? 0;
            var merchantCaps = _merchantInventory.Item(_caps)?.Count ?? 0;
            var fundsAvailable = difference >= 0 ? playerCaps >= difference : merchantCaps >= -difference;
            var balance = difference > 0 ? $"You pay {difference} caps" : difference < 0 ? $"Merchant pays {-difference} caps" : "No caps change";
            _totalsLabel.Text = $"Goods you give: {sell} caps\nGoods you take: {buy} caps\n{balance}\nYour caps: {playerCaps}\nMerchant caps: {merchantCaps}";
            if (_message.Text.StartsWith("You do not have enough caps.", StringComparison.Ordinal) ||
                _message.Text.StartsWith("Merchant does not have enough caps.", StringComparison.Ordinal)) _message.Text = "";
            if (!fundsAvailable) _message.Text = difference >= 0 ? "You do not have enough caps." : "Merchant does not have enough caps.";
            _acceptButton.Disabled = _offers.Count == 0 || !fundsAvailable;
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            _totalsLabel.Text = "Trade total is unavailable."; _message.Text = error.Message; _acceptButton.Disabled = true;
        }
    }

    private void Accept()
    {
        if (_offers.Count == 0) return;
        try
        {
            var transfers = _offers.Select(pair => new FalloutTradeTransfer(pair.Key.Form, pair.Value, pair.Key.FromPlayer)).ToList();
            var sell = 0; var buy = 0;
            foreach (var (key, count) in _offers)
            {
                var inventory = key.FromPlayer ? _playerInventory : _merchantInventory;
                var item = inventory.Item(key.Form) ?? throw new InvalidOperationException($"Offered item {key.Form} is no longer available.");
                var value = _pricing.Total(item, count, vendorSells: !key.FromPlayer, _barterSkill(), _discount);
                if (key.FromPlayer) sell = checked(sell + value); else buy = checked(buy + value);
            }
            var difference = checked(buy - sell);
            if (difference > 0) transfers.Add(new(_caps, difference, FromThisInventory: true));
            else if (difference < 0) transfers.Add(new(_caps, -difference, FromThisInventory: false));
            _exchange(transfers);
            _offers.Clear(); _selected = null; _message.Text = "Trade complete."; Refresh();
            _message.Text = "Trade complete.";
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            _message.Text = "Trade failed: " + error.Message; Refresh(); _message.Text = "Trade failed: " + error.Message;
        }
    }

    private string NameOf(FalloutFormKey form)
    {
        var record = _records.GetEffective(form);
        var full = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
        return full.IsEmpty ? _playerInventory.Item(form)?.EditorId ?? _merchantInventory.Item(form)?.EditorId ?? form.ToString() : FalloutDialogueTopic.Text(full.Span);
    }

    private void Close()
    {
        if (_closed) return; _closed = true; _close();
    }

    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    public override void _Ready()
    {
        Layout();
        var firstAvailable = _rows.SelectMany(rows => rows.GetChildren()).OfType<Button>()
            .FirstOrDefault(button => !button.Disabled);
        (firstAvailable ?? _closeButton).GrabFocus();
    }

    private void Layout()
    {
        if (!IsInsideTree()) return;
        var scale = GetViewportRect().Size.Y / 960;
        Scale = Vector2.One * scale; Size = GetViewportRect().Size / scale;
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Close(); GetViewport().SetInputAsHandled(); }
    }
}
