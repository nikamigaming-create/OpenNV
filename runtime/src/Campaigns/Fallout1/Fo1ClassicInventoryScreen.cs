using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed partial class Fo1ClassicInventoryScreen : Control
{
    private static readonly Color ClassicGreen = new(0.24f, 0.97f, 0.0f);
    private readonly List<TextureRect> _rowIcons = [];
    private readonly List<Label> _rowCounts = [];
    private readonly List<Button> _rowButtons = [];
    private Fo1ClassicInventoryAssets _assets = null!;
    private IReadOnlyDictionary<string, string> _displayNames = null!;
    private Func<IReadOnlyDictionary<string, int>> _inventory = null!;
    private Func<string> _equippedSymbol = null!;
    private Action _close = null!;
    private TextureRect _selectedIcon = null!;
    private Label _selectedText = null!;
    private TextureRect _item1 = null!;
    private TextureRect _item2 = null!;
    private IReadOnlyList<KeyValuePair<string, int>> _rows = [];
    private int _scrollOffset;
    private int _selectedIndex;

    internal bool IsOpen => Visible;
    internal Key PhysicalKey => _assets.PhysicalKey;
    internal int OpenedCount { get; private set; }
    internal int ClosedCount { get; private set; }
    internal int VisibleStackCount => _rows.Count;
    internal string SelectedSymbol => _rows.Count == 0
        ? ""
        : _rows[_selectedIndex].Key;

    internal void Configure(
        Fo1ClassicInventoryAssets assets,
        Fo1CharacterProfile profile,
        IReadOnlyList<Fo1PremadeCharacter> premades,
        IReadOnlyDictionary<string, string> displayNames,
        Func<IReadOnlyDictionary<string, int>> inventory,
        Func<string> equippedSymbol,
        Action close)
    {
        profile.Validate();
        _assets = assets;
        _displayNames = displayNames;
        _inventory = inventory;
        _equippedSymbol = equippedSymbol;
        _close = close;
        Name = "OwnedFallout1ClassicInventory";
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var dimmer = new ColorRect
        {
            Color = new Color(0.0f, 0.0f, 0.0f, 0.72f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dimmer);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var frame = new Control
        {
            CustomMinimumSize = new Vector2(assets.Layout.Width, assets.Layout.Height),
            MouseFilter = MouseFilterEnum.Stop,
        };
        center.AddChild(frame);
        var background = new TextureRect
        {
            Name = "OwnedInvboxFrm",
            Texture = assets.Background.Load(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(background);

        AddPortrait(frame, profile, premades);
        BuildRows(frame);
        BuildScrollButton(frame, assets.ScrollUp, assets.Layout.ScrollUp, -1);
        BuildScrollButton(frame, assets.ScrollDown, assets.Layout.ScrollDown, 1);

        _selectedIcon = AddItemSurface(frame, assets.Layout.SelectedItem);
        _selectedText = AddLabel(frame, assets.Layout.SelectedText, 13);
        _item1 = AddItemSurface(frame, assets.Layout.Item1);
        _item2 = AddItemSurface(frame, assets.Layout.Item2);

        var done = AddButton(frame, assets.Layout.Done, "Close inventory (Escape)");
        done.Name = "OwnedInventoryDoneButton";
        done.Pressed += close;
        Visible = false;
    }

    internal void Open()
    {
        _rows = _inventory()
            .Where(row => row.Value > 0)
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
        if (_rows.Count == 0)
            throw new InvalidOperationException(
                "Fallout classic inventory cannot open without an authoritative stack.");
        foreach (var row in _rows)
        {
            _ = _assets.ItemInventory(row.Key);
            if (!_displayNames.ContainsKey(row.Key))
                throw new InvalidOperationException(
                    $"Fallout classic inventory has no display name for {row.Key}.");
        }
        _scrollOffset = Math.Clamp(
            _scrollOffset,
            0,
            Math.Max(0, _rows.Count - _assets.Layout.VisibleRows));
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _rows.Count - 1);
        Refresh();
        Visible = true;
        OpenedCount++;
    }

    internal bool Close()
    {
        if (!Visible)
            return false;
        Visible = false;
        ClosedCount++;
        return true;
    }

    internal object Report() => new
    {
        source = _assets.Report(),
        open = Visible,
        openedCount = OpenedCount,
        closedCount = ClosedCount,
        visibleStackCount = VisibleStackCount,
        selectedSymbol = SelectedSymbol,
        input = PhysicalKey.ToString(),
        hudButton = true,
        escapeClose = true,
        gameplayMutation = false,
    };

    private void BuildRows(Control frame)
    {
        var layout = _assets.Layout.InventoryList;
        var rowHeight = layout.Height / _assets.Layout.VisibleRows;
        for (var index = 0; index < _assets.Layout.VisibleRows; index++)
        {
            var row = new Fo1HudRect(
                layout.X,
                layout.Y + index * rowHeight,
                layout.Width,
                rowHeight);
            var icon = AddItemSurface(frame, row);
            _rowIcons.Add(icon);
            var count = AddLabel(
                frame,
                new Fo1HudRect(row.X + row.Width - 24, row.Y + row.Height - 17, 23, 16),
                12);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            _rowCounts.Add(count);
            var button = AddButton(frame, row, "Select inventory item");
            var slot = index;
            button.Pressed += () => SelectVisibleRow(slot);
            _rowButtons.Add(button);
        }
    }

    private void BuildScrollButton(
        Control frame,
        Fo1OwnedUiTexture texture,
        Fo1HudPoint point,
        int direction)
    {
        var surface = new TextureRect
        {
            Position = new Vector2(point.X, point.Y),
            Size = new Vector2(texture.Width, texture.Height),
            Texture = texture.Load(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.AddChild(surface);
        var button = AddButton(
            frame,
            new Fo1HudRect(point.X, point.Y, texture.Width, texture.Height),
            direction < 0 ? "Scroll inventory up" : "Scroll inventory down");
        button.Pressed += () => Scroll(direction);
    }

    private void AddPortrait(
        Control frame,
        Fo1CharacterProfile profile,
        IReadOnlyList<Fo1PremadeCharacter> premades)
    {
        Texture2D? texture = null;
        if (profile.Appearance is not null)
        {
            var image = Image.LoadFromFile(profile.Appearance.PortraitPath);
            if (image is null || image.IsEmpty() ||
                image.GetWidth() != profile.Appearance.PortraitWidth ||
                image.GetHeight() != profile.Appearance.PortraitHeight)
                throw new InvalidOperationException(
                    "Fallout classic inventory custom portrait failed validation.");
            texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            texture = premades.FirstOrDefault(
                row => row.Profile.Name == profile.Name)?.Portrait.Load();
        }
        var portrait = AddItemSurface(frame, _assets.Layout.Portrait);
        portrait.Name = "OwnedInventoryCharacterPortrait";
        portrait.Texture = texture;
        if (texture is null)
        {
            var name = AddLabel(frame, _assets.Layout.Portrait, 12);
            name.Text = profile.Name.ToUpperInvariant();
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void SelectVisibleRow(int slot)
    {
        var index = _scrollOffset + slot;
        if (index >= _rows.Count)
            return;
        _selectedIndex = index;
        Refresh();
    }

    private void Scroll(int direction)
    {
        var maximum = Math.Max(0, _rows.Count - _assets.Layout.VisibleRows);
        _scrollOffset = Math.Clamp(_scrollOffset + direction, 0, maximum);
        if (_selectedIndex < _scrollOffset)
            _selectedIndex = _scrollOffset;
        if (_selectedIndex >= _scrollOffset + _assets.Layout.VisibleRows)
            _selectedIndex = _scrollOffset + _assets.Layout.VisibleRows - 1;
        Refresh();
    }

    private void Refresh()
    {
        for (var slot = 0; slot < _rowIcons.Count; slot++)
        {
            var index = _scrollOffset + slot;
            var occupied = index < _rows.Count;
            _rowIcons[slot].Visible = occupied;
            _rowCounts[slot].Visible = occupied;
            _rowButtons[slot].Disabled = !occupied;
            if (!occupied)
                continue;
            var row = _rows[index];
            _rowIcons[slot].Texture = _assets.ItemInventory(row.Key).Load();
            _rowCounts[slot].Text = row.Value > 1 ? row.Value.ToString() : "";
            _rowButtons[slot].TooltipText =
                $"{_displayNames[row.Key]} × {row.Value}";
        }

        var selected = _rows[_selectedIndex];
        _selectedIcon.Texture = _assets.ItemInventory(selected.Key).Load();
        _selectedText.Text =
            $"{_displayNames[selected.Key].ToUpperInvariant()}\n" +
            $"COUNT {selected.Value}";
        var ranged = _assets.ItemInventoryBySymbol.ContainsKey("PID_10MM_PISTOL")
            ? _assets.ItemInventory("PID_10MM_PISTOL").Load()
            : null;
        var melee = _assets.ItemInventoryBySymbol.ContainsKey("PID_KNIFE")
            ? _assets.ItemInventory("PID_KNIFE").Load()
            : null;
        _item1.Texture = ranged;
        _item2.Texture = melee;
        if (selected.Key == _equippedSymbol())
            _selectedText.Text += "\nACTIVE HAND";
    }

    private static TextureRect AddItemSurface(Control parent, Fo1HudRect rect)
    {
        var surface = new TextureRect
        {
            Position = new Vector2(rect.X, rect.Y),
            Size = new Vector2(rect.Width, rect.Height),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        parent.AddChild(surface);
        return surface;
    }

    private static Label AddLabel(Control parent, Fo1HudRect rect, int fontSize)
    {
        var label = new Label
        {
            Position = new Vector2(rect.X, rect.Y),
            Size = new Vector2(rect.Width, rect.Height),
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", ClassicGreen);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label);
        return label;
    }

    private static Button AddButton(Control parent, Fo1HudRect rect, string tooltip)
    {
        var button = new Button
        {
            Position = new Vector2(rect.X, rect.Y),
            Size = new Vector2(rect.Width, rect.Height),
            Flat = true,
            FocusMode = FocusModeEnum.None,
            TooltipText = tooltip,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        parent.AddChild(button);
        return button;
    }
}
