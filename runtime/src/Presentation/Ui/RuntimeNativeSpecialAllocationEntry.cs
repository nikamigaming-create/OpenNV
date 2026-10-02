using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeSpecialAllocationContent(Control Menu, Func<object> Observe);

// Modal lease shared by 3D allocation menus. Its callbacks explicitly bind
// player modal input; source Enable/DisablePlayerControls masks stay untouched.
internal sealed partial class RuntimeNativeSpecialAllocationEntry : CanvasLayer
{
    private NativeSpecialAllocationContent? _content;
    private SceneTree? _pausedTree;
    private bool _configured, _previousPause, _previousModal;
    private Input.MouseModeEnum _previousMouseMode;
    private Action<bool>? _writeModal;
    internal string? Error { get; private set; }
    internal object State => new { menu = _content?.Observe(), error = Error, leased = _pausedTree is not null };
    internal event Action? Accepted;
    internal event Action<Exception>? Failed;
    internal event Action? Released;

    internal void Configure(Func<Action, Action<Exception>, NativeSpecialAllocationContent> factory, Func<bool> readModal, Action<bool> writeModal)
    {
        if (_configured) throw new InvalidOperationException("SPECIAL allocation entry already has a menu owner.");
        ArgumentNullException.ThrowIfNull(factory); ArgumentNullException.ThrowIfNull(readModal); ArgumentNullException.ThrowIfNull(writeModal);
        _configured = true; Name = "NativeSpecialAllocationEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        _previousModal = readModal(); _writeModal = writeModal;
        _previousMouseMode = Input.MouseMode; _pausedTree = GetTree(); _previousPause = _pausedTree.Paused;
        _pausedTree.Paused = true; Input.MouseMode = Input.MouseModeEnum.Visible;
        try
        {
            writeModal(true);
            _content = factory(() => { Accepted?.Invoke(); _content!.Menu.Visible = false; ReleasePause(); }, Fail);
            AddChild(_content.Menu);
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error) { Error ??= error.Message; Failed?.Invoke(error); }
    internal void ReleasePause()
    {
        if (_pausedTree is null) return;
        var tree = _pausedTree; _pausedTree = null;
        try { _writeModal!(_previousModal); }
        finally { tree.Paused = _previousPause; Input.MouseMode = _previousMouseMode; Released?.Invoke(); }
    }
    public override void _ExitTree() => ReleasePause();
}
