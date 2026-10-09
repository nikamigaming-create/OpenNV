using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private sealed record PackageSelection(FalloutPluginRecord? Record, FalloutScriptPackage? Declaration,
        long OverrideRevision = 0, bool ScriptOverride = false);
    private PackageSelection? _requestedSelection;
    private FalloutFormKey? _selectedSourcePackage;
    private bool _sourceSelectionKnown;
    private string? _selectionCaptureBlocker;
    private Func<FalloutFormKey, FalloutFormKey, double>? _aiItemCount;

    private PackageSelection SelectSourcePackage(bool? reevaluateScript = null)
    {
        if (_restoredFollowSelection is { } retained)
        {
            if (reevaluateScript is not null) throw new NotSupportedException("Cold Follow election must bind before a new explicit evaluation.");
            _restoredFollowSelection = null;
            return retained;
        }
        _failedSelectionCondition = null;
        var world = _aiWorld ?? throw new NotSupportedException("NPC package selection has no reference state owner.");
        var selected = _aiReferenceState?.PendingPackageChoice is { } choice ? choice.Bind(_aiStack!, _aiReferenceState) :
            world.SelectActorPackage(Appearance.Reference!.Value, EvaluateSelectionCondition,
                _templates, _aiClock, _packageEvents?.Active?.Form, _packageEvents?.Done == true,
                reevaluateScript ?? !_bindingInitialBase, PackageLocationReached(_packageEvents?.Active?.Form));
        if (_aiReferenceState is not null) _aiReferenceState.SelectionFailure = null;
        return new(selected, selected is null ? null : FalloutScriptPackage.Read(selected), ScriptPackageRevision,
            selected is not null && _aiReferenceState?.ScriptPackage?.Package == selected.FormKey);
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiStack is null || _questState is null)
            throw new NotSupportedException("Actor package commands require the live AI owner.");
        if (_packageEvents?.Error is { } eventError) throw new NotSupportedException(eventError);
        // EVP evaluates source conditions now. Procedure binding/movement is
        // performed by the ordinary actor frame, as it is for creatures. A
        // later native procedure fault is not the result of this void command.
        // Retain the selected result so random predicates are not drawn twice.
        var selected = SelectSourcePackage(reevaluateScript: true);
        if (_aiReferenceState is not null) ClearBindingFailure();
        if (reset) { _aiError = null; _packageIdleError = null; }
        _requestedSelection = selected;
        if (_aiReferenceState is { } state) state.PendingPackageSelection = null;
        _selectedSourcePackage = selected.Record?.FormKey; _sourceSelectionKnown = true;
        _aiQuestRevision = -1; _aiPollRemaining = 0;
        BlockSelectionCapture(OpenNV.Runtime.World.Cells.FalloutActorPendingPackageSelection.CaptureBlocker);
    }

    private void BlockSelectionCapture(string message)
    {
        if (_aiReferenceState is not { } state) return;
        // Cell replacement binds this controller before retiring the previous
        // presentation. Adopt the same source failure through that current
        // binding; a stale or unrelated owner cannot replace another blocker.
        if (state.ProcedureCaptureBlocker is not null && state.ProcedureCaptureBlocker != _selectionCaptureBlocker &&
            (state.ProcedureCaptureBlocker != message ||
                !ReferenceEquals(state.CanCapturePackageBindingFailure, _bindingFailureReady) ||
                !ReferenceEquals(state.CapturePackageBindingFailure, _bindingFailureCapture))) return;
        state.ProcedureCaptureBlocker = _selectionCaptureBlocker = message;
    }

    private void ClearSelectionCapture()
    {
        if (_aiReferenceState is { } state && state.ProcedureCaptureBlocker == _selectionCaptureBlocker)
            state.ProcedureCaptureBlocker = null;
        _selectionCaptureBlocker = null;
    }
}
