using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeVigorEntry : CanvasLayer
{
    private const int LayerIndex = 120;
    private const float PanelWidthPixels = 620.0f;
    private const float PanelPaddingPixels = 24.0f;
    private const float AttributeLabelWidthPixels = 180.0f;
    private const float AttributeValueWidthPixels = 48.0f;
    private const float ShadeOpacity = 0.82f;
    private FalloutNativeVigorContract _contract = null!;
    private FalloutNativeSpecialState _state = null!;
    private readonly List<Label> _values = [];
    private readonly List<Button> _decrease = [];
    private readonly List<Button> _increase = [];
    private Label _remaining = null!;
    private Button _confirm = null!;

    internal event Action<FalloutNativeSpecialState>? Accepted;

    internal void Configure(
        FalloutNativeVigorContract contract,
        FalloutNativeSpecialState initial)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _state = initial ?? throw new ArgumentNullException(nameof(initial));
        if (_state.Values.Any(value =>
                value < contract.MinimumAttribute || value > contract.MaximumAttribute) ||
            _state.Values.Sum() > contract.RequiredTotal)
            throw new InvalidDataException("Native initial SPECIAL allocation is invalid.");
        Name = "NativeVigorEntry";
        Layer = LayerIndex;
        var shade = new ColorRect
        {
            Color = new Color(0.0f, 0.0f, 0.0f, ShadeOpacity),
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
        };
        AddChild(shade);
        var center = new CenterContainer
        {
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
        };
        AddChild(center);
        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(PanelWidthPixels, 0.0f),
        };
        margin.AddThemeConstantOverride("margin_left", (int)PanelPaddingPixels);
        margin.AddThemeConstantOverride("margin_top", (int)PanelPaddingPixels);
        margin.AddThemeConstantOverride("margin_right", (int)PanelPaddingPixels);
        margin.AddThemeConstantOverride("margin_bottom", (int)PanelPaddingPixels);
        center.AddChild(margin);
        var column = new VBoxContainer();
        margin.AddChild(column);
        column.AddChild(new Label
        {
            Text = "VIGOR TESTER — S.P.E.C.I.A.L.",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        for (var index = 0; index < FalloutNativeVigorResolver.AttributeNames.Count; ++index)
            AddAttributeRow(column, index);
        _remaining = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        column.AddChild(_remaining);
        _confirm = new Button { Text = "CONFIRM S.P.E.C.I.A.L." };
        _confirm.Pressed += Submit;
        column.AddChild(_confirm);
        column.AddChild(new Label
        {
            Text = "Live Player and Vigor script records • functional OpenNV presentation",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    private void AddAttributeRow(VBoxContainer column, int index)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(new Label
        {
            Text = FalloutNativeVigorResolver.AttributeNames[index],
            CustomMinimumSize = new Vector2(AttributeLabelWidthPixels, 0.0f),
        });
        var decrease = new Button { Text = "−" };
        decrease.Pressed += () => Change(index, -1);
        row.AddChild(decrease);
        var value = new Label
        {
            CustomMinimumSize = new Vector2(AttributeValueWidthPixels, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        row.AddChild(value);
        var increase = new Button { Text = "+" };
        increase.Pressed += () => Change(index, 1);
        row.AddChild(increase);
        _decrease.Add(decrease);
        _values.Add(value);
        _increase.Add(increase);
        column.AddChild(row);
    }

    private void Change(int index, int delta)
    {
        var next = _state.Values[index] + delta;
        var nextTotal = _state.Values.Sum() + delta;
        if (next < _contract.MinimumAttribute || next > _contract.MaximumAttribute ||
            nextTotal > _contract.RequiredTotal)
            return;
        _state = _state.WithValue(index, next);
        Refresh();
    }

    private void Refresh()
    {
        var total = _state.Values.Sum();
        for (var index = 0; index < _values.Count; ++index)
        {
            var value = _state.Values[index];
            _values[index].Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _decrease[index].Disabled = value <= _contract.MinimumAttribute;
            _increase[index].Disabled =
                value >= _contract.MaximumAttribute || total >= _contract.RequiredTotal;
        }
        var remaining = _contract.RequiredTotal - total;
        _remaining.Text = $"Points remaining: {remaining}";
        _confirm.Disabled = remaining != 0;
        if (!_confirm.Disabled)
            _confirm.GrabFocus();
    }

    private void Submit()
    {
        FalloutNativeVigorResolver.Validate(_contract, _state);
        Accepted?.Invoke(_state);
    }
}
