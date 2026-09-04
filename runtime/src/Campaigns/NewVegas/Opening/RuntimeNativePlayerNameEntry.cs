using Godot;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativePlayerNameEntry : CanvasLayer
{
    private const int LayerIndex = 120;
    private const float PanelWidthPixels = 520.0f;
    private const float PanelPaddingPixels = 24.0f;
    private const float ShadeOpacity = 0.82f;
    private LineEdit _entry = null!;
    private Label _validation = null!;

    internal event Action<string>? Accepted;

    internal void Configure(string currentName)
    {
        Name = "NativePlayerNameEntry";
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
            Text = "WHAT IS YOUR NAME?",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _entry = new LineEdit
        {
            Text = currentName,
            PlaceholderText = "Courier name",
            SelectAllOnFocus = true,
        };
        _entry.TextSubmitted += _ => Submit();
        column.AddChild(_entry);
        _validation = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        column.AddChild(_validation);
        var confirm = new Button { Text = "CONFIRM" };
        confirm.Pressed += Submit;
        column.AddChild(confirm);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _entry.GrabFocus();
    }

    private void Submit()
    {
        var value = _entry.Text.Trim();
        if (value.Length == 0 || value.Any(char.IsControl))
        {
            _validation.Text = "Enter a valid name.";
            _entry.GrabFocus();
            return;
        }
        Accepted?.Invoke(value);
    }
}
