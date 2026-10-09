using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeSleepWaitEntry : CanvasLayer
{
    private readonly FalloutSleepWait _session;
    private readonly long _request;
    private readonly Action<Exception> _failed;
    private readonly Action _retired;
    private NativeOwnedSleepWaitMenu? _menu;
    private Func<Action> _acquireInput = null!;
    private Action? _releaseInput;
    private SceneTree? _tree;
    private bool _priorPause, _mouseAcquired, _released;
    private Input.MouseModeEnum _priorMouse;
    private Exception? _attachmentError;
    private Func<FalloutRestObservation> _sourceCloseGate = null!;
    private bool _restControlReleased;
    internal uint MenuId => FalloutSleepWaitSource.MenuId;
    internal object State => new { request = _request, session = _session.State, native = _menu?.State, released = _released };

    private RuntimeNativeSleepWaitEntry(FalloutSleepWait session, Action<Exception> failed, Action retired)
    {
        _session = session; _request = session.RequestOrdinal; _failed = failed; _retired = retired;
        Name = "NativeSleepWaitEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
    }
    internal static RuntimeNativeSleepWaitEntry Attach(Node parent, NativeSleepWaitMenuSource source,
        FalloutSleepWait session, Func<Action> acquireInput, Func<FalloutRestObservation> sourceCloseGate,
        Action<Exception> failed, Action retired)
    {
        if (!parent.IsInsideTree() || !session.NeedsMenuPublication || session.Failure is not null || session.Published ||
            session.Request?.Origin == FalloutRestOrigin.ScriptHours)
            throw new InvalidOperationException("Native sleep/wait has no living unpublished menu request.");
        var entry = new RuntimeNativeSleepWaitEntry(session, failed, retired)
        { _acquireInput = acquireInput, _sourceCloseGate = sourceCloseGate };
        try
        {
            entry._menu = NativeOwnedSleepWaitMenu.Create(source, session, entry.Fail);
            entry.AddChild(entry._menu);
            if (entry._menu.GetParent() != entry) throw new InvalidOperationException("Rest control did not attach to its source owner.");
            parent.AddChild(entry);
            if (entry.GetParent() != parent || !entry.IsInsideTree()) throw new InvalidOperationException("Rest entry did not attach to its world.");
            if (entry._attachmentError is { } error) throw new InvalidOperationException("Rest native publication failed.", error);
            if (entry._menu.Error is { } failure) throw new InvalidOperationException("Rest source view failed: " + failure);
            return entry;
        }
        catch (Exception original)
        {
            entry._session.ReportNativeFailure(FalloutRestStep.Publication, original);
            var failures = new List<Exception> { original };
            try { entry.Release(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            try { entry.Free(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup))
            { entry._session.ReportNativeFailure(FalloutRestStep.Retirement, cleanup); failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Rest native attachment and retirement retained failures.", failures);
        }
    }
    public override void _EnterTree()
    {
        try
        {
            if (_released || _tree is not null) throw new InvalidOperationException("Rest entry cannot publish twice.");
            _tree = GetTree(); _priorPause = _tree.Paused;
            _releaseInput = _acquireInput() ?? throw new InvalidOperationException("Rest source input returned no release lease.");
            _tree.Paused = true;
            if (DisplayServer.GetName() != "headless")
            { _priorMouse = Input.MouseMode; _mouseAcquired = true; Input.MouseMode = Input.MouseModeEnum.Visible; }
            _session.Publish(_request);
            var rest = OpenNV.Runtime.InputSystem.RuntimeNativeInputControls.Action(FalloutSleepWaitSource.RestControl);
            _restControlReleased = InputMap.HasAction(rest) && !Input.IsActionPressed(rest);
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _attachmentError ??= error; Fail(error); }
    }
    public override void _Process(double delta)
    {
        if (_released || _session.Failure is not null || _session.RequestOrdinal != _request) return;
        try
        {
            var rest = OpenNV.Runtime.InputSystem.RuntimeNativeInputControls.Action(FalloutSleepWaitSource.RestControl);
            if (InputMap.HasAction(rest) && Input.IsActionJustReleased(rest)) _restControlReleased = true;
            var close = _restControlReleased && InputMap.HasAction(rest) && Input.IsActionPressed(rest) ||
                _session.Phase == FalloutRestPhase.Completed;
            if (close)
            {
                var fact = _sourceCloseGate(); fact.Validate();
                if (fact.State == FalloutRestFactState.Unowned)
                    throw new NotSupportedException("Rest source close gate has no actual UI predicate: " + fact.Reason);
                if (fact.State == FalloutRestFactState.Satisfied)
                {
                    if (_session.Phase == FalloutRestPhase.Completed) _session.RetireCompletedMenu();
                    else _session.Cancel();
                    return;
                }
            }
            _session.AdvanceCountdown((float)delta);
            if (_session.NeedsMenuPublication) _menu!.Refresh();
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { Fail(error); }
    }
    internal void Close()
    {
        if (_session.RequestOrdinal != _request || _session.Phase is not (FalloutRestPhase.Completed or FalloutRestPhase.Cancelled))
            throw new InvalidOperationException("Rest menu cannot retire without its actual completion/cancellation receipt.");
        Release(); QueueFree();
    }
    private void Fail(Exception error)
    { _session.ReportNativeFailure(FalloutRestStep.Publication, error); _failed(error); }
    internal void Release()
    {
        if (_released) return;
        _released = true;
        var failures = new List<Exception>();
        void Retire(Action action)
        {
            try { action(); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            { failures.Add(error); _session.ReportNativeFailure(FalloutRestStep.Retirement, error); }
        }
        Retire(() => _releaseInput?.Invoke()); _releaseInput = null;
        Retire(() => { if (_tree is { } tree && GodotObject.IsInstanceValid(tree)) tree.Paused = _priorPause; }); _tree = null;
        Retire(() => { if (_mouseAcquired) Input.MouseMode = _priorMouse; }); _mouseAcquired = false;
        Retire(_session.RetirePublication); Retire(_retired);
        if (failures.Count != 0) throw new AggregateException("Rest native retirement retained failed cleanup operations.", failures);
    }
    public override void _ExitTree()
    {
        try { Release(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { Fail(error); }
    }
}
