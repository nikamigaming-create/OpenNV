using System.Globalization;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedQuantityMenu : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _meter, _amount;
    private readonly List<(XElement Tile, Control Control)> _targets = [];
    private readonly Action<int?> _complete;
    private readonly int _maximum;
    private bool _closed;
    private NativeViewportLayout? _viewportLayout;
    internal int Quantity { get; private set; }
    internal string? Error { get; private set; }

    internal NativeOwnedQuantityMenu(FalloutPluginStack records, int maximum, Action<int?> complete)
    {
        if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
        Name = "OwnedQuantityMenu"; TopLevel = true; ZIndex = 100;
        ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        _maximum = maximum; _complete = complete; Quantity = maximum;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/quantity_menu.xml")).Elements("menu").Single();
        _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
        _meter = Named("QM_AmountMeter"); _amount = Named("QM_AmountChosen");
        AddButton(Named("QM_DecreaseArrow"), "Decrease", () => Select(Quantity - 1));
        AddButton(Named("QM_IncreaseArrow"), "Increase", () => Select(Quantity + 1));
        AddButton(Named("QM_OKButton"), FalloutGameSettingStrings.Read(records, "sOK"), () => Complete(Quantity));
        AddButton(Named("QM_CancelButton"), FalloutGameSettingStrings.Read(records, "sCancel"), () => Complete(null));
        var slider = new Control { Name = "QuantitySlider", MouseFilter = MouseFilterEnum.Stop };
        slider.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button) Slide(button.Position.X);
            else if (input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion) Slide(motion.Position.X);
        };
        void Slide(float x) => Select((int)Math.Round(1 + Math.Clamp(x / slider.Size.X, 0, 1) * (_maximum - 1)));
        AddChild(slider); _targets.Add((_meter, slider));
        SetMeta("opennv_ui_source", "menus/quantity_menu.xml; source-fonts-and-atlas");
        SetMeta("opennv_ui_unverified", "retail-input-timing,matched-pixels");
    }

    private void AddButton(XElement tile, string label, Action activate)
    {
        var font = _tiles.Font(tile.DescendantsAndSelf("text").FirstOrDefault() ?? tile);
        var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
        { Name = (string)tile.Attribute("name")!, Text = label, DrawText = false, FocusMode = FocusModeEnum.All };
        button.Pressed += activate; AddChild(button); _targets.Add((tile, button));
    }

    private void Select(int value)
    {
        if (_closed) return;
        Quantity = Math.Clamp(value, 1, _maximum);
        _tiles.Bind(_meter, "user0", _maximum); _tiles.Bind(_meter, "user2", Quantity);
        _tiles.Bind(_meter, "_Value", Quantity / (float)_maximum);
        _tiles.Text[_amount] = Quantity.ToString(CultureInfo.InvariantCulture);
        QueueRedraw();
    }

    private void Complete(int? quantity) { if (_closed || quantity is not null && Error is not null) return; _closed = true; _complete(quantity); }
    public override void _Ready() { Select(Quantity); Layout(); }
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();

    private void Layout()
    {
        if (!IsInsideTree()) return;
        try
        {
            var scale = GetViewportRect().Size.Y / 960;
            Scale = Vector2.One * scale; Position = Vector2.Zero;
            Size = _tiles.Screen = GetViewportRect().Size / scale; _tiles.ResolutionConverter = 1 / scale;
            foreach (var (tile, control) in _targets)
            {
                control.Position = _tiles.Position(tile);
                control.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
            }
            _tiles.ValidateDrawing(); QueueRedraw();
        }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_QUANTITY_UI_FAIL {error.Message}"); }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_closed) return;
        var viewport = GetViewport();
        if (inputEvent is InputEventKey { Pressed: true } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Left: Select(Quantity - 1); break;
                case Key.Right: Select(Quantity + 1); break;
                case Key.Home: Select(1); break;
                case Key.End: Select(_maximum); break;
                case Key.Enter or Key.KpEnter or Key.A when !key.Echo: Complete(Quantity); break;
                case Key.Escape or Key.Tab or Key.E when !key.Echo: Complete(null); break;
                default: break;
            }
            viewport.SetInputAsHandled();
        }
        else if (inputEvent is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Select(Quantity + (mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1)); viewport.SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        if (Error is not null) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_QUANTITY_UI_FAIL {error.Message}"); }
    }
}
