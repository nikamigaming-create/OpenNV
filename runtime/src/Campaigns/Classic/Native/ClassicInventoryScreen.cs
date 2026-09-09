using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Original INVBOX/LOOT art and item icons, backed by the live classic inventory owner.</summary>
internal sealed partial class ClassicInventoryScreen : Control
{
    private ClassicArtCache _art = null!;
    private ClassicPlayerSession _player = null!;
    private ClassicWorldPreview _world = null!;
    private ClassicInventoryModels _models = null!;
    private Button _mode = null!;
    private string _idlePath = "";
    private Texture2D? _hostPortrait;
    private ClassicOwnedFont _font = null!;
    private int? _host;
    private string? _containerId;
    private bool Looting => _host.HasValue || _containerId is not null;
    private IReadOnlyList<ClassicInventoryEntry> LootItems => _containerId is { } id ? _player.Inventory.ContainerContents(id) : _player.Inventory.Contents(_host!.Value);
    private string LootName => _containerId is { } id ? _player.Inventory.CheckContainerAccess(id).Definition.Name : _player.Inventory.HostName(_host!.Value);
    private void Transfer(ClassicInventoryEntry item, int amount, bool take)
    {
        if (_containerId is { } id)
        {
            if (take) _player.Inventory.TakeFromContainer(id, item.Id, amount);
            else _player.Inventory.DepositIntoContainer(id, item.Id, amount);
        }
        else if (take) _player.Inventory.Take(_host!.Value, item.Id, amount);
        else _player.Inventory.Deposit(_host!.Value, item.Id, amount);
    }
    private Texture2D _background = null!;
    private Texture2D _portrait = null!;
    private string _message = "";
    private int _leftOffset, _rightOffset;
    private ClassicInventoryEntry? _selected;
    private bool _previousPause;
    private readonly List<(Rect2 Bounds, string Label)> _actions = [];

    internal void Configure(ClassicArtCache art, ClassicPlayerSession player, ClassicWorldPreview world, int? host = null, string? containerId = null)
    {
        _art = art; _player = player; _world = world; _host = host; _containerId = containerId; ProcessMode = ProcessModeEnum.Always;
        Name = Looting ? "ClassicLootScreen" : "ClassicInventoryScreen";
        _background = art.Frame("art/intrface/" + (Looting ? "loot.frm" : "invbox.frm")).Texture;
        _font = new(art.Read("font1.aaf"), art.Read("color.pal"), 992);
        var animation = new ClassicPlayerAnimation(path => art.Read(path), player.Choice.Character.Female, player.Choice.Campaign, player.Inventory);
        _idlePath = animation.IdlePath; _portrait = art.Frame(_idlePath, 2).Texture;
        if (Looting)
        {
            var placed = _containerId is { } id ? player.Inventory.CheckContainerAccess(id).Object : player.Inventory.Host(_host!.Value);
            var path = placed.Prototype.ObjectType == 1 ? art.Critter(placed.Fid, placed.Frame) :
                (Content.Fallout1NativePrototypeReader.ResolveArt(player.Catalog, placed.Fid), placed.Frame);
            _hostPortrait = art.Frame(path.Item1, placed.Rotation, path.Item2).Texture;
        }
        Size = _background.GetSize(); TextureFilter = TextureFilterEnum.Nearest; MouseFilter = MouseFilterEnum.Stop;
        var done = new Button
        {
            Name = "CloseClassicInventory",
            Flat = true,
            Position = Looting ? new(206, 320) : new(354, 320),
            Size = new(113, 31),
            TooltipText = "Done · Escape",
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        done.Pressed += QueueFree; AddChild(done);
        if (!Looting)
        {
            Slot("EquipArmor", new(154, 185, 90, 60), "armor");
            Slot("EquipLeftHand", new(154, 285, 90, 60), "left");
            Slot("EquipRightHand", new(249, 285, 90, 60), "right");
            Action("ReloadInventoryWeapon", "RELOAD", new(299, 200, 67, 18), () =>
            {
                var item = RequireSelection(); var loaded = _player.Inventory.Reload(item.Id);
                _message = loaded == 0 ? "Magazine already full." : $"Loaded {loaded} rounds.";
            });
            Action("DropInventoryItem", "DROP", new(369, 200, 65, 18), () =>
            { var item = RequireSelection(); Quantity(item, count => _player.Inventory.Drop(item.Id, count), "Drop"); });
            Action("UnloadInventoryWeapon", "UNLOAD", new(299, 220, 67, 17), () =>
            { var rounds = _player.Inventory.Unload(RequireSelection().Id); _message = $"Unloaded {rounds} rounds."; });
            Action("OpenInventoryContainer", "OPEN", new(369, 220, 65, 17), () => OpenContainer(RequireSelection().Id));
        }
        else Action("TakeAllInventoryItems", "TAKE ALL", new(261, 282, 117, 23), () =>
        {
            var count = 0;
            foreach (var item in LootItems.ToArray()) { Transfer(item, item.Amount, true); count++; }
            _message = $"Taken {count} stacks.";
        });
        _models = new ClassicInventoryModels(); AddChild(_models); _models.Configure(_font);
        _mode = new Button
        {
            Name = "InventoryRepresentationMode",
            Position = new(0, -23),
            Size = new(Size.X, 22),
            TooltipText = "Switch the world, inventory items and equipped character together · F4"
        };
        _mode.AddThemeFontSizeOverride("font_size", 10); _mode.Pressed += world.ToggleRepresentation; AddChild(_mode);
        _previousPause = GetTree().Paused; GetTree().Paused = true;
        _message = Looting ? "Click loot to take it.\nClick carried items to deposit." : "Choose an item.\nClick a slot to equip.";
        player.Inventory.Changed += Refresh;
        world.RepresentationChanged += RefreshModels;
        GetViewport().SizeChanged += Resize; Resize();
        GD.Print($"OPENNV_CLASSIC_INVENTORY_OPEN campaign={player.Choice.Campaign} host={host} carried={player.Inventory.Carried.Count}");
    }

    private void OpenContainer(string id)
    {
        _player.Inventory.CheckContainerAccess(id);
        var layer = new CanvasLayer { Layer = 9, ProcessMode = ProcessModeEnum.Always }; AddChild(layer);
        var screen = new ClassicInventoryScreen(); layer.AddChild(screen);
        try { screen.Configure(_art, _player, _world, containerId: id); screen.TreeExiting += layer.QueueFree; }
        catch { layer.QueueFree(); throw; }
    }

    private void Resize()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var scale = Math.Max(1, Math.Min(size.X / 640, size.Y / 480));
        Scale = new(scale, scale); Position = (size - Size * scale) / 2;
        RefreshModels();
    }
    private void Refresh()
    {
        try
        {
            var animation = new ClassicPlayerAnimation(path => _art.Read(path), _player.Choice.Character.Female, _player.Choice.Campaign, _player.Inventory);
            _idlePath = animation.IdlePath; _portrait = _art.Frame(_idlePath, 2).Texture;
        }
        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException) { _message = error.Message; }
        _leftOffset = Math.Clamp(_leftOffset, 0, Math.Max(0, _player.Inventory.Carried.Count - 6));
        if (Looting) _rightOffset = Math.Clamp(_rightOffset, 0, Math.Max(0, LootItems.Count - 6));
        RefreshModels();
    }

    private void RefreshModels()
    {
        if (_models is null) return;
        var inventory = _player.Inventory;
        var slots = new List<ClassicInventoryModelSlot>();
        void Rows(IReadOnlyList<ClassicInventoryEntry> items, Vector2 at, int offset)
        {
            foreach (var (item, index) in items.Skip(offset).Take(6).Select((item, index) => (item, index)))
                slots.Add(_world.InventorySlot(item, new(at + new Vector2(1, index * 50 + 1), new(68, item.Amount > 1 ? 32 : 42)), _art, _player));
        }
        Rows(inventory.Carried, new(42, 39), _leftOffset);
        if (Looting)
        {
            Rows(LootItems, new(421, 39), _rightOffset);
            var placed = _containerId is { } id ? inventory.CheckContainerAccess(id).Object : inventory.Host(_host!.Value);
            slots.Add(new($"loot:{placed.Pid}:{placed.Fid}:{placed.Frame}", LootName, new(288, 39, 75, 69),
                () => _world.InventoryAnalog(placed, _art, _player)));
        }
        else
        {
            void Equipment(string? id, Rect2 bounds)
            {
                if (inventory.Carried.SingleOrDefault(row => row.Id == id) is { } item)
                    slots.Add(_world.InventorySlot(item, bounds, _art, _player));
            }
            Equipment(inventory.Armor, new(157, 188, 80, 50));
            Equipment(inventory.LeftHand, new(157, 286, 80, 54));
            Equipment(inventory.RightHand, new(253, 286, 80, 54));
        }
        slots.Add(new($"character:{_idlePath}:{inventory.Held?.Id}:{inventory.Armor}", _player.Choice.Character.Name,
            new(172, 39, 61, 94), () => _world.PlayerAnalog(_idlePath, _player.Choice, inventory.Held)));
        _models.Display(slots, _world.ShowModels, Scale.X);
        _mode.Text = _world.ShowModels ? "3D · World, inventory and equipped character · F4" : "Original sprites · World, inventory and character · F4";
        SetMeta("inventory_representation", _world.ShowModels ? "3D" : "original-sprites");
        QueueRedraw();
    }
    private ClassicInventoryEntry RequireSelection() => _selected is { } selected
        ? _player.Inventory.Carried.Single(row => row.Id == selected.Id) : throw new InvalidOperationException("Choose a carried item first.");
    private void Attempt(System.Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { _message = error.Message; }
        QueueRedraw();
    }
    private void Slot(string name, Rect2 bounds, string slot)
    {
        var button = new Button
        {
            Name = name,
            Flat = true,
            Position = bounds.Position,
            Size = bounds.Size,
            TooltipText = "Click selected item to equip; click again to unequip",
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.Pressed += () => Attempt(() =>
        {
            var inventory = _player.Inventory;
            var current = slot == "left" ? inventory.LeftHand : slot == "right" ? inventory.RightHand : inventory.Armor;
            var id = _selected?.Id == current ? null : RequireSelection().Id;
            inventory.Equip(id, slot); _message = id is null ? "Unequipped." : "Equipped: " + RequireSelection().Definition.Name;
        }); AddChild(button);
    }
    private void Action(string name, string label, Rect2 bounds, System.Action action)
    {
        var button = new Button
        {
            Name = name,
            Flat = true,
            Position = bounds.Position,
            Size = bounds.Size,
            TooltipText = label,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.Pressed += () => Attempt(action); AddChild(button); _actions.Add((bounds, label));
    }
    private void Quantity(ClassicInventoryEntry item, System.Action<int> action, string verb)
    {
        if (item.Amount == 1) { action(1); _message = verb + ": " + item.Definition.Name; return; }
        var layer = new CanvasLayer { Layer = 20, ProcessMode = ProcessModeEnum.Always }; AddChild(layer);
        var quantity = new ClassicQuantityScreen(); layer.AddChild(quantity);
        quantity.Configure(_art, _font, item, _world, _player, count => Attempt(() => { action(count); _message = verb + ": " + item.Definition.Name; }));
        quantity.TreeExiting += layer.QueueFree;
    }
    public override void _Draw()
    {
        if (_art is null) return;
        DrawTexture(_background, Vector2.Zero);
        var inventory = _player.Inventory;
        DrawRows(inventory.Carried, new(42, 39), _leftOffset);
        if (Looting)
        {
            DrawRows(LootItems, new(421, 39), _rightOffset);
            if (!_world.ShowModels && _hostPortrait is not null) Fit(_hostPortrait, new(288, 39, 75, 69));
            _font.Draw(this, LootName, new Rect2(288, 110, 75, 23),
                HorizontalAlignment.Center, VerticalAlignment.Center, 2);
            _font.Draw(this, _message, new Rect2(141, 178, 248, 74), HorizontalAlignment.Center);
            _font.Draw(this, $"{inventory.Weight}/{inventory.Capacity} lbs", new Rect2(141, 284, 96, 24),
                HorizontalAlignment.Center, VerticalAlignment.Center, 1);
        }
        else
        {
            var headingHeight = _font.Draw(this, _selected?.Definition.Name ?? "INVENTORY", new(295, 53), 144, 3, HorizontalAlignment.Center);
            var descriptionTop = 53 + headingHeight + _font.LineHeight;
            _font.Draw(this, _selected?.Definition.Description ?? _message, new Rect2(299, descriptionTop, 136, Math.Max(0, 157 - descriptionTop)),
                _selected is null ? HorizontalAlignment.Center : HorizontalAlignment.Left);
            if (_selected is not null) _font.Draw(this, _message, new Rect2(299, 162, 136, 34), HorizontalAlignment.Center);
            _font.Draw(this, $"{inventory.Weight}/{inventory.Capacity} lbs", new Rect2(290, 244, 153, 23),
                HorizontalAlignment.Center, VerticalAlignment.Center, 1);
            void Equipment(string? id, Rect2 bounds, string empty)
            {
                var item = inventory.Carried.SingleOrDefault(row => row.Id == id);
                if (item is null) _font.Draw(this, empty, bounds, HorizontalAlignment.Center, VerticalAlignment.Center, 4);
                else if (!_world.ShowModels && item.Definition.Icon is { } icon) Fit(_art.Frame(icon).Texture, bounds);
            }
            Equipment(inventory.Armor, new(157, 188, 80, 50), $"Armor class\n{_player.ArmorClass}");
            Equipment(inventory.LeftHand, new(157, 286, 80, 54), "Left hand");
            Equipment(inventory.RightHand, new(253, 286, 80, 54), "Right hand");
        }
        foreach (var (bounds, label) in _actions) _font.Draw(this, label, bounds, HorizontalAlignment.Center, VerticalAlignment.Center, 1);
        if (!_world.ShowModels) Fit(_portrait, new Rect2(172, 39, 61, 94));
    }

    private void DrawRows(IReadOnlyList<ClassicInventoryEntry> items, Vector2 at, int offset)
    {
        for (var index = offset; index < Math.Min(items.Count, offset + 6); index++)
        {
            var item = items[index]; var row = at + new Vector2(0, (index - offset) * 50);
            if (_selected?.Id == item.Id) DrawRect(new Rect2(row, new(70, 48)), new Color(0.1f, 0.3f, 0.08f, 0.5f));
            if (!_world.ShowModels)
            {
                if (item.Definition.Icon is { } path) Fit(_art.Frame(path).Texture, new Rect2(row + new Vector2(1, 1), new(68, 42)));
                else _font.Draw(this, item.Definition.Name, row + new Vector2(1, 5), 68, 3, HorizontalAlignment.Center);
            }
            if (item.Amount > 1) _font.Draw(this, item.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), row + new Vector2(2, 35), 68, 1, HorizontalAlignment.Right);
        }
    }

    private void Fit(Texture2D texture, Rect2 rect)
    {
        var scale = Math.Min(rect.Size.X / texture.GetWidth(), rect.Size.Y / texture.GetHeight());
        var size = texture.GetSize() * scale;
        DrawTextureRect(texture, new Rect2(rect.Position + (rect.Size - size) / 2, size), false);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse) return;
        var right = Looting && mouse.Position.X is >= 420 and <= 494;
        var left = mouse.Position.X is >= 42 and <= 114;
        if (!left && !right || mouse.Position.Y is < 38 or > 340) return;
        var rows = right ? LootItems : _player.Inventory.Carried;
        ref var offset = ref (right ? ref _rightOffset : ref _leftOffset);
        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { offset = Math.Clamp(offset + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 0, Math.Max(0, rows.Count - 6)); RefreshModels(); return; }
        var index = offset + (int)((mouse.Position.Y - 39) / 50);
        if (mouse.ButtonIndex != MouseButton.Left || index < 0 || index >= rows.Count) return;
        _selected = rows[index];
        try
        {
            if (Looting)
            {
                var selected = _selected;
                Quantity(selected, count =>
                {
                    Transfer(selected, count, right);
                    GD.Print($"OPENNV_CLASSIC_ITEM_TRANSFER campaign={_player.Choice.Campaign} map={_player.MapPath} host={_containerId ?? _host?.ToString()} serial={selected.Object.Serial} pid={selected.Object.Pid} amount={count} toPlayer={right}");
                }, right ? "Taken" : "Deposited");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { _message = error.Message; }
        QueueRedraw(); AcceptEvent();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape or Key.I })
        { QueueFree(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree()
    {
        if (_player is null) return;
        _player.Inventory.Changed -= Refresh; GetViewport().SizeChanged -= Resize; GetTree().Paused = _previousPause;
        _world.RepresentationChanged -= RefreshModels;
    }
}
