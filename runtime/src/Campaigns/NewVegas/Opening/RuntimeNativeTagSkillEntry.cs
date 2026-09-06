using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeTagSkillEntry : CanvasLayer
{
    private const int LayerIndex = 120;
    private const float PanelWidthPixels = 560.0f;
    private const float ShadeOpacity = 0.82f;
    private FalloutNativeTagSkillContract _contract = null!;
    private readonly List<FalloutNativeSkillIdentity> _selected = [];
    private readonly List<Button> _buttons = [];
    private Label _status = null!;
    private Button _confirm = null!;

    internal event Action<IReadOnlyList<FalloutNativeSkillIdentity>>? Accepted;

    internal void Configure(
        FalloutNativeTagSkillContract contract,
        IReadOnlyList<FalloutNativeSkillIdentity> current)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        if (current.Count != 0)
            FalloutNativeTagSkillResolver.Validate(contract, current);
        _selected.AddRange(current);
        Name = "NativeTagSkillEntry";
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
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidthPixels, 0.0f) };
        center.AddChild(column);
        column.AddChild(new Label
        {
            Text = "CHOOSE THREE TAG SKILLS",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach (var skill in contract.Skills)
        {
            var button = new Button { Text = skill.DisplayName, ToggleMode = true };
            button.Pressed += () => Toggle(skill);
            _buttons.Add(button);
            column.AddChild(button);
        }
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        column.AddChild(_status);
        _confirm = new Button { Text = "CONFIRM TAG SKILLS" };
        _confirm.Pressed += Submit;
        column.AddChild(_confirm);
        column.AddChild(new Label
        {
            Text = "Winning AVIF identities • functional OpenNV presentation",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    private void Toggle(FalloutNativeSkillIdentity skill)
    {
        if (_selected.Contains(skill))
            _selected.Remove(skill);
        else if (_selected.Count < _contract.RequiredCount)
            _selected.Add(skill);
        Refresh();
    }

    private void Refresh()
    {
        for (var index = 0; index < _contract.Skills.Count; ++index)
            _buttons[index].ButtonPressed = _selected.Contains(_contract.Skills[index]);
        _status.Text = $"Selected: {_selected.Count} / {_contract.RequiredCount}";
        _confirm.Disabled = _selected.Count != _contract.RequiredCount;
        if (!_confirm.Disabled)
            _confirm.GrabFocus();
    }

    private void Submit()
    {
        var selection = _selected.OrderBy(value => value.RuntimeFormId).ToArray();
        FalloutNativeTagSkillResolver.Validate(_contract, selection);
        Accepted?.Invoke(selection);
    }
}
