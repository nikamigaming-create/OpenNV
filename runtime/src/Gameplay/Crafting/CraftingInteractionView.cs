using Godot;

namespace OpenNV.Runtime.Gameplay.Crafting;

internal static class CraftingInteractionNumericContracts
{
    // First-party presentation constants for the bounded crafting adapter.
    internal const float PanelBlue = 0.03f;
    internal const float PanelGreen = 0.035f;
    internal const float PanelRed = 0.02f;
    internal const float PanelAlpha = 0.98f;
    internal const float BorderBlue = 0.25f;
    internal const float BorderGreen = 0.58f;
    internal const float BorderRed = 0.75f;
    internal const int HorizontalContentMargin = 24;
    internal const int VerticalContentMargin = 20;
    internal const int TitleFontSize = 28;
}

internal partial class CraftingInteractionView : CanvasLayer
{
    private const int OverlayLayer = 91;
    private const int PanelWidthPixels = 760;
    private const int PanelHeightPixels = 520;

    private Panel _root = null!;
    private Label _title = null!;
    private VBoxContainer _recipes = null!;
    private Func<CraftingRecipe, bool>? _canCraft;
    private Action<CraftingRecipe>? _craft;
    private Action? _exit;
    private CraftingStationContract? _contract;
    private bool _useXr;
    private bool _previousPauseState;

    internal bool IsOpen => _root.Visible;

    internal void Configure(bool useXr)
    {
        _useXr = useXr;
        Name = "CraftingInteraction";
        Layer = OverlayLayer;
        ProcessMode = ProcessModeEnum.WhenPaused;
        BuildView();
    }

    internal void Open(
        CraftingStationContract contract,
        Func<CraftingRecipe, bool> canCraft,
        Action<CraftingRecipe> craft,
        Action exit)
    {
        if (IsOpen)
            throw new InvalidOperationException("Another crafting view is already open.");
        _contract = contract;
        _canCraft = canCraft;
        _craft = craft;
        _exit = exit;
        _previousPauseState = GetTree().Paused;
        Refresh();
        _root.Visible = true;
        GetTree().Paused = true;
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    internal void Refresh()
    {
        if (_contract is null || _canCraft is null)
            return;
        _title.Text = _contract.Category.DisplayName.ToUpperInvariant();
        foreach (var child in _recipes.GetChildren())
            child.Free();
        Button? first = null;
        foreach (var recipe in _contract.Recipes)
        {
            var button = new Button
            {
                Name = $"Recipe_{recipe.FormId}",
                Text = RecipeText(recipe),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.All,
                Disabled = !_canCraft(recipe),
            };
            button.Pressed += () => _craft?.Invoke(recipe);
            _recipes.AddChild(button);
            first ??= button;
        }
        first?.GrabFocus();
    }

    internal void Close()
    {
        if (!IsOpen)
            return;
        _root.Visible = false;
        _contract = null;
        _canCraft = null;
        _craft = null;
        _exit = null;
        GetTree().Paused = _previousPauseState;
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!IsOpen || !inputEvent.IsActionPressed("ui_cancel"))
            return;
        _exit?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (IsOpen)
            GetTree().Paused = _previousPauseState;
    }

    private void BuildView()
    {
        _root = new Panel
        {
            Name = "CraftingOverlay",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PanelWidthPixels, PanelHeightPixels),
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                CraftingInteractionNumericContracts.PanelRed,
                CraftingInteractionNumericContracts.PanelGreen,
                CraftingInteractionNumericContracts.PanelBlue,
                CraftingInteractionNumericContracts.PanelAlpha),
            BorderColor = new Color(
                CraftingInteractionNumericContracts.BorderRed,
                CraftingInteractionNumericContracts.BorderGreen,
                CraftingInteractionNumericContracts.BorderBlue),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = CraftingInteractionNumericContracts.HorizontalContentMargin,
            ContentMarginTop = CraftingInteractionNumericContracts.VerticalContentMargin,
            ContentMarginRight = CraftingInteractionNumericContracts.HorizontalContentMargin,
            ContentMarginBottom = CraftingInteractionNumericContracts.VerticalContentMargin,
        });
        center.AddChild(panel);
        var layout = new VBoxContainer();
        panel.AddChild(layout);
        _title = new Label { Text = "CRAFTING" };
        _title.AddThemeFontSizeOverride(
            "font_size",
            CraftingInteractionNumericContracts.TitleFontSize);
        layout.AddChild(_title);
        layout.AddChild(new HSeparator());
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddChild(scroll);
        _recipes = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_recipes);
        layout.AddChild(new HSeparator());
        var exit = new Button { Text = "EXIT" };
        exit.Pressed += () => _exit?.Invoke();
        layout.AddChild(exit);
    }

    private static string RecipeText(CraftingRecipe recipe) =>
        $"{recipe.DisplayName}\n  " +
        string.Join(" + ", recipe.Ingredients.Select(ItemText)) +
        "  →  " + string.Join(" + ", recipe.Outputs.Select(ItemText));

    private static string ItemText(CraftingItem item) =>
        item.Count == 1
            ? item.Definition.DisplayName!
            : $"{item.Definition.DisplayName} x{item.Count}";
}
