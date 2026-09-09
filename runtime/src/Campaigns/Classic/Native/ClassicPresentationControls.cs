using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Two cameras consume the same live world and animation frame, with separate presentation layers.</summary>
internal sealed partial class ClassicPresentationControls : CanvasLayer
{
    private ClassicWorldPreview _world = null!;
    private HBoxContainer _split = null!;
    private Button _mode = null!;
    private HBoxContainer _bar = null!;
    private Label _gaps = null!;
    private readonly HashSet<Key> _heldKeys = [];
    private readonly List<Camera3D> _cameras = [];
    private readonly List<SubViewport> _views = [];

    internal void Configure(ClassicWorldPreview world)
    {
        Name = "ClassicPresentationControls"; Layer = 4; _world = world; ProcessMode = ProcessModeEnum.Always;
        _split = new HBoxContainer { Name = "LiveComparison", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _split.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _split.AddThemeConstantOverride("separation", 2); AddChild(_split);
        for (var index = 0; index < 2; index++)
        {
            var container = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Stop };
            _split.AddChild(container);
            var view = new SubViewport
            {
                OwnWorld3D = false,
                World3D = world.GetWorld3D(),
                HandleInputLocally = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                Msaa3D = Viewport.Msaa.Msaa2X
            };
            container.AddChild(view); _views.Add(view);
            var camera = new Camera3D
            {
                Current = true,
                CullMask = ClassicWorldPreview.SharedLayer |
                (index == 0 ? ClassicWorldPreview.SpriteLayer : ClassicWorldPreview.ModelLayer)
            };
            view.AddChild(camera); _cameras.Add(camera);
            container.AddChild(new Label
            {
                Text = index == 0 ? "ORIGINAL SPRITES" : "3D ANALOGS · IN DEVELOPMENT",
                Position = new(16, 52),
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }
        var bar = _bar = new HBoxContainer { Name = "PresentationToolbar", Position = new(20, 12) }; AddChild(bar);
        Button Button(string name, string text, Action action)
        {
            var button = new Button { Name = name, Text = text }; bar.AddChild(button); button.Pressed += action; return button;
        }
        _mode = Button("ToggleModels", "", ToggleModels); Refresh();
        Button("CompareModels", "Side by side", Compare);
        Button("SaveCameraView", "Mark view", world.Camera.SaveView);
        Button("RestoreCameraView", "Return to view", world.Camera.RestoreView);
        Button("CinematicPass", "Slow camera pass", world.Camera.CinematicPass);
        Button("StopCameraPass", "Stop camera", world.Camera.StopShot);
        Button("ToggleHexGrid", "Hex grid", world.Camera.SwitchGrid);
        Button("InspectNextActor", "Next actor", world.InspectNextActor);
        Button("InspectNextItem", "Next item", world.InspectNextItem);
        Button("Presentation1080p", "1080p", () => GetWindow().Size = new Vector2I(1920, 1080));
        Button("HidePresentationControls", "Hide controls · F2", ToggleChrome);
        _gaps = new Label
        {
            Name = "MissingClassicModels",
            Position = new(20, 126),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_gaps); Refresh();
        world.RepresentationChanged += Refresh;
    }

    private void Refresh()
    {
        _mode.Text = _world.ShowModels ? "View: 3D analogs" : "View: original sprites";
        if (_gaps is null) return;
        _gaps.Text = $"3D models still missing: {_world.MissingModels} · F4: world, inventory and character";
        _gaps.Visible = _world.MissingModels > 0 && _bar.Visible;
    }
    private void Compare()
    {
        _split.Visible = !_split.Visible;
        foreach (var view in _views) view.RenderTargetUpdateMode = _split.Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
    }
    private void ToggleModels()
    {
        if (_split.Visible) Compare();
        _world.ToggleRepresentation();
    }
    private void ToggleChrome()
    {
        _bar.Visible = !_bar.Visible;
        _world.ShowPresentationChrome(_bar.Visible);
        Refresh();
    }
    public override void _Process(double delta)
    {
        if (_world is null) return;
        foreach (var key in new[] { Key.F2, Key.F3, Key.F4 })
        {
            if (!Input.IsPhysicalKeyPressed(key)) { _heldKeys.Remove(key); continue; }
            if (!_heldKeys.Add(key)) continue;
            if (key == Key.F2) ToggleChrome();
            if (key == Key.F3) Compare();
            if (key == Key.F4) ToggleModels();
        }
        if (!_split.Visible) return;
        var size = GetViewport().GetVisibleRect().Size;
        _split.Size = size;
        foreach (var camera in _cameras)
        {
            camera.GlobalTransform = _world.Camera.GlobalTransform;
            camera.Fov = _world.Camera.Fov; camera.Near = _world.Camera.Near; camera.Far = _world.Camera.Far;
            camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        }
    }

    public override void _ExitTree() { if (_world is not null) _world.RepresentationChanged -= Refresh; }
}
