using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativePlayerNameEntry : CanvasLayer
{
    internal event Action<string>? Accepted;
    private SceneTree? _pausedTree;
    private bool _previousPause;

    internal void Configure(string currentName, FalloutPluginStack records)
    {
        Name = "NativePlayerNameEntry";
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        AddChild(new NativeOwnedNameMenu(currentName, records, value => Accepted?.Invoke(value)));
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
