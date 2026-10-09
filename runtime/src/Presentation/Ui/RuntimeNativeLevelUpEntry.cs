using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeLevelUpEntry : CanvasLayer
{
    private NativeOwnedLevelUpMenu _menu = null!;
    private Func<Action> _acquireInput = null!;
    private Action? _releaseInput;
    private SceneTree? _pausedTree;
    private bool _previousPause, _accepted, _submitted, _released;
    private Input.MouseModeEnum _previousMouse;
    private readonly FalloutLevelUpMenuSession _session;
    private readonly Action _submit;
    private readonly Action<Exception> _fail;
    private readonly Action<bool> _release;
    private Exception? _attachmentError;
    internal object State => _menu.State;
    internal uint MenuId => _menu.MenuId;
    internal string? Error => _menu.Error;

    private RuntimeNativeLevelUpEntry(FalloutLevelUpMenuSession session, Action submit, Action<Exception> fail, Action<bool> release)
    {
        _session = session; _submit = submit; _fail = fail; _release = release;
        Name = "NativeLevelUpEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
    }
    internal static RuntimeNativeLevelUpEntry Attach(Node parent, NativeLevelUpMenuSource source,
        FalloutLevelUpMenuSession session, Func<bool> ownsRequest, Func<Action> acquireInput,
        Action submit, Action<Exception> fail, Action<bool> release)
    {
        if (!parent.IsInsideTree() || session.Completed || session.Error is not null || !ownsRequest())
            throw new InvalidOperationException("Native level-up cannot attach outside its living source request and world.");
        var entry = new RuntimeNativeLevelUpEntry(session, submit, fail, release) { _acquireInput = acquireInput };
        try
        {
            entry._menu = NativeOwnedLevelUpMenu.Create(source, session, ownsRequest, entry.Accept, fail);
            entry.AddChild(entry._menu);
            if (entry._menu.GetParent() != entry) throw new InvalidOperationException("Native level-up control was not attached to its owner.");
            parent.AddChild(entry);
            if (entry.GetParent() != parent || !entry.IsInsideTree()) throw new InvalidOperationException("Native level-up did not attach to its world.");
            if (entry._attachmentError is { } attachment)
                throw new InvalidOperationException("Native level-up input/pause attachment failed.", attachment);
            if (entry.Error is { } error) throw new InvalidOperationException("Native level-up source publication failed: " + error);
            return entry;
        }
        catch
        {
            // Rejected source/attachment/Ready publication cannot leak an
            // unattached native Node or leave its modal lease acquired.
            entry.Free(); throw;
        }
    }
    public override void _EnterTree()
    {
        try
        {
            if (_released || _pausedTree is not null) throw new InvalidOperationException("Native level-up owner cannot be attached twice.");
            _pausedTree = GetTree(); _previousPause = _pausedTree.Paused; _previousMouse = Input.MouseMode;
            _releaseInput = _acquireInput() ?? throw new InvalidOperationException("Native level-up input owner returned no release lease.");
            _pausedTree.Paused = true;
            if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        catch (Exception error)
        {
            // Godot reports managed override exceptions at its bridge. Retain
            // this attempt so Attach cannot mistake a swallowed exception for
            // successful input/pause ownership after AddChild returns.
            _attachmentError ??= error; _fail(error);
        }
    }
    private void Accept()
    {
        if (_accepted || !_session.Completed) throw new InvalidOperationException("Native level-up has no new completed source receipt.");
        _accepted = true;
        _submit();
        _submitted = true;
        ReleasePause(); QueueFree();
    }
    internal void ReleasePause()
    {
        if (_released) return;
        _released = true;
        try
        {
            _releaseInput?.Invoke(); _releaseInput = null;
        }
        finally
        {
            if (_pausedTree is { } tree && GodotObject.IsInstanceValid(tree)) tree.Paused = _previousPause;
            _pausedTree = null;
            if (DisplayServer.GetName() != "headless") Input.MouseMode = _previousMouse;
            _release(_submitted && _session.Completed);
        }
    }
    public override void _ExitTree()
    {
        try { ReleasePause(); }
        catch (Exception error) { _fail(error); }
    }
}
