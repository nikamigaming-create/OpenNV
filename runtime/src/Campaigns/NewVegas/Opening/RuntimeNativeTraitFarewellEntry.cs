using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeTraitEntry : CanvasLayer
{
    private const int LayerIndex = 120;
    private const float PanelWidthPixels = 580.0f;
    private const float ShadeOpacity = 0.82f;
    private FalloutNativeTraitFarewellContract _contract = null!;
    private readonly List<FalloutNativeTraitIdentity> _selected = [];
    private readonly List<Button> _buttons = [];
    private Label _status = null!;

    internal event Action<IReadOnlyList<FalloutNativeTraitIdentity>>? Accepted;

    internal void Configure(
        FalloutNativeTraitFarewellContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> current)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        FalloutNativeTraitFarewellResolver.ValidateTraits(contract, current);
        _selected.AddRange(current);
        Name = "NativeTraitEntry";
        Layer = LayerIndex;
        var shade = FullShade();
        AddChild(shade);
        var center = FullCenter();
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidthPixels, 0.0f) };
        center.AddChild(column);
        column.AddChild(new Label
        {
            Text = "CHOOSE UP TO TWO TRAITS",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach (var trait in contract.Traits)
        {
            var button = new Button { Text = trait.DisplayName, ToggleMode = true };
            button.Pressed += () => Toggle(trait);
            _buttons.Add(button);
            column.AddChild(button);
        }
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        column.AddChild(_status);
        var confirm = new Button { Text = "CONFIRM TRAITS" };
        confirm.Pressed += Submit;
        column.AddChild(confirm);
        column.AddChild(new Label
        {
            Text = "Winning playable PERK identities • functional OpenNV presentation",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
        confirm.GrabFocus();
    }

    private void Toggle(FalloutNativeTraitIdentity trait)
    {
        if (_selected.Contains(trait))
            _selected.Remove(trait);
        else if (_selected.Count < _contract.MaximumTraits)
            _selected.Add(trait);
        Refresh();
    }

    private void Refresh()
    {
        for (var index = 0; index < _contract.Traits.Count; ++index)
            _buttons[index].ButtonPressed = _selected.Contains(_contract.Traits[index]);
        _status.Text = $"Selected: {_selected.Count} / {_contract.MaximumTraits} maximum";
    }

    private void Submit()
    {
        var selection = _selected.OrderBy(value => value.RuntimeFormId).ToArray();
        FalloutNativeTraitFarewellResolver.ValidateTraits(_contract, selection);
        Accepted?.Invoke(selection);
    }

    private static ColorRect FullShade() => new()
    {
        Color = new Color(0.0f, 0.0f, 0.0f, ShadeOpacity),
        LayoutMode = 1,
        AnchorsPreset = (int)Control.LayoutPreset.FullRect,
    };

    private static CenterContainer FullCenter() => new()
    {
        LayoutMode = 1,
        AnchorsPreset = (int)Control.LayoutPreset.FullRect,
    };
}
