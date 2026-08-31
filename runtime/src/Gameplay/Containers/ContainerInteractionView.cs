using Godot;


namespace OpenNV.Runtime.Gameplay.Containers;

internal partial class ContainerInteractionView : CanvasLayer
{
    private const int OverlayLayer = 90;
    private const int PanelWidthPixels = 900;
    private const int PanelHeightPixels = 560;

    private Panel _root = null!;
    private Label _containerTitle = null!;
    private VBoxContainer _playerItems = null!;
    private VBoxContainer _containerItems = null!;
    private Button _takeAll = null!;
    private Action<string>? _takeOneAction;
    private Action? _takeAllAction;
    private Action? _exitAction;
    private string? _referenceFormId;
    private bool _useXr;
    private bool _previousPauseState;

    internal bool IsOpen => _root.Visible;

    internal void Configure(bool useXr)
    {
        _useXr = useXr;
        Name = "ContainerInteraction";
        Layer = OverlayLayer;
        ProcessMode = ProcessModeEnum.WhenPaused;
        BuildView();
    }

    internal void Open(
        ContainerInventorySnapshot snapshot,
        PlayerContainerInventorySnapshot playerInventory,
        Action<string> takeOneAction,
        Action takeAllAction,
        Action exitAction)
    {
        if (IsOpen)
            throw new InvalidOperationException("Another container view is already open.");
        _referenceFormId = snapshot.ReferenceFormId;
        _takeOneAction = takeOneAction;
        _takeAllAction = takeAllAction;
        _exitAction = exitAction;
        _previousPauseState = GetTree().Paused;
        Refresh(snapshot, playerInventory);
        _root.Visible = true;
        GetTree().Paused = true;
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    internal void Refresh(
        ContainerInventorySnapshot snapshot,
        PlayerContainerInventorySnapshot playerInventory)
    {
        if (_referenceFormId is not null &&
            !_referenceFormId.Equals(snapshot.ReferenceFormId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Container view refresh changed reference identity.");
        _containerTitle.Text = snapshot.DisplayName.ToUpperInvariant();
        foreach (var child in _playerItems.GetChildren())
            child.Free();
        foreach (var item in playerInventory.Items)
            _playerItems.AddChild(ItemLabel(item.DisplayName, item.Count));
        if (playerInventory.OtherItemCount > 0)
            _playerItems.AddChild(ItemLabel("OTHER ITEMS", playerInventory.OtherItemCount));
        if (playerInventory.Items.Count == 0 && playerInventory.OtherItemCount == 0)
            _playerItems.AddChild(EmptyLabel());

        foreach (var child in _containerItems.GetChildren())
            child.Free();
        Button? first = null;
        if (snapshot.Items.Count == 0)
        {
            _containerItems.AddChild(EmptyLabel());
        }
        else
        {
            foreach (var item in snapshot.Items)
            {
                var itemFormId = item.ItemFormId;
                var button = new Button
                {
                    Text = ItemText(item.DisplayName, item.RemainingCount),
                    Alignment = HorizontalAlignment.Left,
                    FocusMode = Control.FocusModeEnum.All,
                };
                button.Pressed += () => _takeOneAction?.Invoke(itemFormId);
                _containerItems.AddChild(button);
                first ??= button;
            }
        }
        _takeAll.Disabled = snapshot.IsEmpty;
        first?.GrabFocus();
    }

    internal void Close()
    {
        if (!IsOpen)
            return;
        _root.Visible = false;
        _referenceFormId = null;
        _takeOneAction = null;
        _takeAllAction = null;
        _exitAction = null;
        GetTree().Paused = _previousPauseState;
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!IsOpen || !inputEvent.IsActionPressed("ui_cancel"))
            return;
        _exitAction?.Invoke();
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
            Name = "ContainerOverlay",
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
            BgColor = new Color(0.02f, 0.035f, 0.03f, 0.98f),
            BorderColor = new Color(0.35f, 0.85f, 0.55f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 24,
            ContentMarginTop = 20,
            ContentMarginRight = 24,
            ContentMarginBottom = 20,
        });
        center.AddChild(panel);
        var layout = new VBoxContainer();
        panel.AddChild(layout);
        var columns = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", 24);
        layout.AddChild(columns);

        var playerColumn = InventoryColumn("ITEMS  •  VIEW ONLY", out _playerItems);
        playerColumn.SizeFlagsStretchRatio = 1.0f;
        columns.AddChild(playerColumn);
        var separator = new VSeparator();
        columns.AddChild(separator);
        var containerColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        containerColumn.SizeFlagsStretchRatio = 1.0f;
        columns.AddChild(containerColumn);
        _containerTitle = new Label
        {
            Text = "CONTAINER",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _containerTitle.AddThemeFontSizeOverride("font_size", 24);
        containerColumn.AddChild(_containerTitle);
        containerColumn.AddChild(new HSeparator());
        var containerScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        containerColumn.AddChild(containerScroll);
        _containerItems = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        containerScroll.AddChild(_containerItems);
        layout.AddChild(new HSeparator());
        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        layout.AddChild(actions);
        _takeAll = new Button { Text = "TAKE ALL" };
        _takeAll.Pressed += () => _takeAllAction?.Invoke();
        actions.AddChild(_takeAll);
        var exit = new Button { Text = "EXIT" };
        exit.Pressed += () => _exitAction?.Invoke();
        actions.AddChild(exit);
    }

    private static VBoxContainer InventoryColumn(
        string title,
        out VBoxContainer items)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var label = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        label.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(label);
        column.AddChild(new HSeparator());
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);
        items = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(items);
        return column;
    }

    private static Label ItemLabel(string displayName, int count) => new()
    {
        Text = ItemText(displayName, count),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Label EmptyLabel() => new()
    {
        Text = "EMPTY",
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static string ItemText(string displayName, int count) =>
        count == 1 ? displayName : $"{displayName}  ({count})";
}

internal sealed record PlayerContainerInventorySnapshot(
    IReadOnlyList<PlayerContainerInventoryItem> Items,
    int OtherItemCount);

internal sealed record PlayerContainerInventoryItem(string DisplayName, int Count);
