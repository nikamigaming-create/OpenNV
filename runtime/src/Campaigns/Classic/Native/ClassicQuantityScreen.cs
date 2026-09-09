using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Original MOVEMULT panel inside the ordinary game viewport.</summary>
internal sealed partial class ClassicQuantityScreen : Control
{
    private Texture2D _background = null!;
    private Texture2D? _icon;
    private ClassicOwnedFont _font = null!;
    private ClassicInventoryEntry _item = null!;
    private LineEdit _amount = null!;
    private Action<int> _confirm = null!;
    private readonly List<(Control Control, Rect2 Bounds)> _controls = [];
    private Vector2 _origin;
    private float _scale;
    private string _error = "";
    private ClassicInventoryModels _models = null!;
    private ClassicWorldPreview _world = null!;
    private ClassicPlayerSession _player = null!;
    private ClassicArtCache _art = null!;
    private Button _mode = null!;

    internal void Configure(ClassicArtCache art, ClassicOwnedFont font, ClassicInventoryEntry item,
        ClassicWorldPreview world, ClassicPlayerSession player, Action<int> confirm)
    {
        Name = "ClassicItemQuantity"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        _background = art.Frame("art/intrface/movemult.frm").Texture; _font = font; _item = item; _confirm = confirm;
        _world = world; _player = player; _art = art;
        _icon = item.Definition.Icon is { } path ? art.Frame(path).Texture : null;
        _amount = new LineEdit
        {
            Name = "Quantity",
            Text = item.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Alignment = HorizontalAlignment.Right,
            MaxLength = 10,
            SelectAllOnFocus = true,
            CaretBlink = true
        };
        _amount.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = Colors.Black });
        _amount.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = Colors.Black });
        _amount.AddThemeColorOverride("font_color", new Color(0.2f, 1, 0.1f));
        _amount.AddThemeFontOverride("font", new SystemFont { FontNames = ["Consolas"] });
        _amount.AddThemeFontSizeOverride("font_size", 14);
        _amount.TextChanged += _ => { _error = ""; QueueRedraw(); };
        _amount.TextSubmitted += _ => Confirm();
        Add(_amount, new(149, 44, 49, 27));
        Button("QuantityConfirm", new(18, 124, 105, 25), Confirm);
        Button("QuantityCancel", new(137, 124, 105, 25), QueueFree);
        Button("QuantityAll", new(128, 81, 85, 32), () => { _amount.Text = item.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture); QueueRedraw(); });
        Button("QuantityIncrease", new(201, 45, 16, 13), () => Step(1));
        Button("QuantityDecrease", new(201, 59, 16, 13), () => Step(-1));
        _models = new ClassicInventoryModels(); AddChild(_models); _models.Configure(font);
        _mode = new Button { Name = "QuantityRepresentationMode", TooltipText = "Switch world, inventory and character · F4" };
        _mode.AddThemeFontSizeOverride("font_size", 10); _mode.Pressed += world.ToggleRepresentation;
        Add(_mode, new(0, -23, 259, 22));
        world.RepresentationChanged += RefreshModels;
        GetViewport().SizeChanged += Resize; Resize();
        _amount.GrabFocus(); _amount.SelectAll();
    }
    private void Add(Control control, Rect2 bounds) { AddChild(control); _controls.Add((control, bounds)); }
    private void Button(string name, Rect2 bounds, Action action)
    {
        var button = new Button { Name = name, Flat = true, MouseDefaultCursorShape = CursorShape.PointingHand };
        button.Pressed += action; Add(button, bounds);
    }
    private void Step(int direction)
    {
        var value = long.TryParse(_amount.Text, out var parsed) ? parsed : 1;
        _amount.Text = Math.Clamp(value + direction, 1, _item.Amount).ToString(System.Globalization.CultureInfo.InvariantCulture); QueueRedraw();
    }
    private void Confirm()
    {
        if (!int.TryParse(_amount.Text, out var value) || value < 1 || value > _item.Amount)
        { _error = "Choose 1 to " + _item.Amount; QueueRedraw(); return; }
        _confirm(value); QueueFree();
    }
    private void Resize()
    {
        Size = GetViewport().GetVisibleRect().Size;
        _scale = Math.Max(1, Math.Min(Size.X / 640, Size.Y / 480));
        _origin = (Size - _background.GetSize() * _scale) / 2;
        foreach (var (control, bounds) in _controls)
        { control.Scale = new(_scale, _scale); control.Position = _origin + bounds.Position * _scale; control.Size = bounds.Size; }
        _models.Position = _origin; _models.Scale = new(_scale, _scale); RefreshModels();
    }
    private void RefreshModels()
    {
        _models.Display([_world.InventorySlot(_item, new(19, 42, 86, 64), _art, _player)], _world.ShowModels, _scale);
        _mode.Text = _world.ShowModels ? "3D · World and inventory · F4" : "Original sprites · World and inventory · F4";
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_background is null) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.3f));
        DrawSetTransform(_origin, 0, new(_scale, _scale)); DrawTexture(_background, Vector2.Zero);
        if (!_world.ShowModels && _icon is not null)
        {
            var scale = Math.Min(86f / _icon.GetWidth(), 64f / _icon.GetHeight()); var size = _icon.GetSize() * scale;
            DrawTextureRect(_icon, new Rect2(new Vector2(62, 74) - size / 2, size), false);
        }
        _font.Draw(this, _item.Definition.Name, new Rect2(16, 9, 226, 25), HorizontalAlignment.Center, VerticalAlignment.Center, 2);
        _font.Draw(this, _error.Length == 0 ? "ALL (" + _item.Amount + ")" : _error, new Rect2(129, 82, 84, 29),
            HorizontalAlignment.Center, VerticalAlignment.Center, 2);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }) return;
        QueueFree(); GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree()
    {
        if (_background is not null) GetViewport().SizeChanged -= Resize;
        if (_world is not null) _world.RepresentationChanged -= RefreshModels;
    }
}
