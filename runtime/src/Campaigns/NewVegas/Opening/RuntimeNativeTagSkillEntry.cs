using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeTagSkillEntry : CanvasLayer
{
    private NativeOwnedTagSkillMenu? _menu;
    private SceneTree? _pausedTree;
    private bool _previousPause;
    private Input.MouseModeEnum _previousMouseMode;
    internal object? State => _menu?.State;
    internal event Action<IReadOnlyList<FalloutNativeSkillIdentity>>? Accepted;
    internal event Action<Exception>? Failed;
    internal event Action? Released;

    internal void Configure(FalloutPluginStack records, FalloutNativeTagSkillContract contract,
        IReadOnlyList<FalloutNativeSkillIdentity> current, Func<FalloutNativeSkillIdentity, float> liveValue)
    {
        if (_menu is not null) throw new InvalidOperationException("Tag entry already has a menu owner.");
        Name = "NativeTagSkillEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        _menu = new(records, contract, current, liveValue, selection => Accepted?.Invoke(selection), error => Failed?.Invoke(error));
        _previousMouseMode = Input.MouseMode;
        _pausedTree = GetTree(); _previousPause = _pausedTree.Paused; _pausedTree.Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible; AddChild(_menu);
    }
    internal void ReleasePause()
    {
        if (_pausedTree is null) return;
        _pausedTree.Paused = _previousPause; _pausedTree = null;
        Input.MouseMode = _previousMouseMode;
        Released?.Invoke();
    }
    public override void _ExitTree() => ReleasePause();
}
