using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeRaceSexEntry : CanvasLayer
{
    private const int LayerIndex = 120;
    private const float PanelWidthPixels = 560.0f;
    private const float PanelPaddingPixels = 24.0f;
    private const float ShadeOpacity = 0.82f;
    private FalloutNativeRaceSexContract _contract = null!;
    private FalloutNativeRaceSexSelection _selection = null!;
    private Label _details = null!;

    internal event Action<FalloutNativeRaceSexSelection>? Accepted;

    internal void Configure(
        FalloutNativeRaceSexContract contract,
        FalloutNativeRaceSexSelection current)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        FalloutNativeRaceSexResolver.Validate(contract, current);
        _selection = current;
        Name = "NativeRaceSexEntry";
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
            Text = "CHARACTER IDENTITY",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        column.AddChild(new Label
        {
            Text = $"Race: {contract.Male.RaceEditorId}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var sexes = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        var male = new Button { Text = "MALE" };
        male.Pressed += () => Select(contract.Male);
        sexes.AddChild(male);
        var female = new Button { Text = "FEMALE" };
        female.Pressed += () => Select(contract.Female);
        sexes.AddChild(female);
        column.AddChild(sexes);
        _details = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        column.AddChild(_details);
        var confirm = new Button { Text = "CONFIRM CHARACTER" };
        confirm.Pressed += () => Accepted?.Invoke(_selection);
        column.AddChild(confirm);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
        confirm.GrabFocus();
    }

    private void Select(FalloutNativeRaceSexSelection selection)
    {
        _selection = selection;
        Refresh();
    }

    private void Refresh()
    {
        _details.Text =
            $"Sex: {(_selection.Female ? "Female" : "Male")}\n" +
            $"Hair: {_selection.HairEditorId} • Eyes: {_selection.EyesEditorId}\n" +
            "Live winning records • functional OpenNV presentation";
    }
}
