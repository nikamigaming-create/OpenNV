using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeTraitEntry : CanvasLayer
{
    private NativeOwnedTraitMenu? _menu;
    private SceneTree? _pausedTree;
    private bool _previousPause;
    internal object? State => _menu?.State;
    internal event Action<IReadOnlyList<FalloutNativeTraitIdentity>>? Accepted;
    internal event Action<Exception>? Failed;

    internal void Configure(FalloutPluginStack records, FalloutTraitMenuContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> current, int playerLevel, Func<FalloutCondition, float> evaluate)
    {
        Name = "NativeTraitEntry"; Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        _menu = new(records, contract, current, playerLevel, evaluate, selection => Accepted?.Invoke(selection), error => Failed?.Invoke(error));
        AddChild(_menu);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _pausedTree = GetTree();
        _previousPause = _pausedTree.Paused;
        _pausedTree.Paused = true;
    }

    internal void ReleasePause()
    {
        if (_pausedTree is null) return;
        _pausedTree.Paused = _previousPause;
        _pausedTree = null;
    }

    public override void _ExitTree() => ReleasePause();
}
