using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeVigorEntry : CanvasLayer
{
    internal event Action<FalloutNativeSpecialState>? Accepted;
    private IDisposable? _background;
    private SceneTree? _pausedTree;
    private bool _previousPause;

    internal void Configure(FalloutNativeVigorContract contract, FalloutNativeSpecialState initial, FalloutPluginStack records,
        RuntimeNativeImageSpace? imageSpace = null)
    {
        ArgumentNullException.ThrowIfNull(contract); ArgumentNullException.ThrowIfNull(initial);
        if (initial.Values.Any(value => value < contract.MinimumAttribute || value > contract.MaximumAttribute) ||
            initial.Values.Sum() > contract.RequiredTotal)
            throw new InvalidDataException("Native initial SPECIAL allocation is invalid.");
        Name = "NativeVigorEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        var menu = new NativeOwnedLoveTesterMenu(contract, initial, records)
        {
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
        };
        menu.Accepted += state =>
        {
            ReleaseBackground();
            Accepted?.Invoke(state);
        };
        AddChild(menu);
        _pausedTree = GetTree();
        _previousPause = _pausedTree.Paused;
        _pausedTree.Paused = true;
        _background = imageSpace?.BeginMenuBackground(records, FalloutMenuBackgroundKind.Popup);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void ReleaseBackground()
    {
        _background?.Dispose();
        _background = null;
        if (_pausedTree is not { } tree) return;
        _pausedTree = null;
        tree.Paused = _previousPause;
    }

    public override void _ExitTree() => ReleaseBackground();
}
