using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Func<bool>? _pendingSelectionReady;
    private Func<FalloutActorPendingPackageSelection>? _pendingSelectionCapture;

    private bool CanCapturePendingSelection() => _aiReferenceState is { } state &&
        state.ProcedureCaptureBlocker == _selectionCaptureBlocker &&
        _selectionCaptureBlocker == FalloutActorPendingPackageSelection.CaptureBlocker &&
        !_bindingInitialBase && _requestedSelection is { } selected && _sourceSelectionKnown &&
        _selectedSourcePackage == selected.Record?.FormKey && _failedSelectionCondition is null &&
        _pendingPackage is null && _aiPackage is null && _packageEvents is { Active: null, Done: false, Error: null } &&
        (_packageIdleSource is null ? _packageIdles is null && _idleReplays.Remaining.Count == 0 : _packageIdles is not null) &&
        _findFurniture is null && _seat is null && _sitting == 0 && !_furnitureApproaching && !_travelActive &&
        _travelProgress?.ArrivalPending != true && _escortPackage is null && _editorTravel is null &&
        _nativeMarkerTravel is null && _dialoguePackage is null && _guardPackage is null && _patrol is null &&
        _animation is null && !_responseIdleActive && AnimationError is null && _animationSounds?.CanCaptureSilent != false && _conversationTarget is null &&
        CanCaptureStoppedAiPose() && !_baseLocomotionMoving && _baseClock.Resource.Length != 0;

    private FalloutActorPendingPackageSelection CapturePendingSelection()
    {
        if (!CanCapturePendingSelection())
            throw new NotSupportedException("Pending selection still requires an independent procedure or pose continuation.");
        var records = _aiStack!;
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(Appearance.Npc), 32, _templates);
        string Hash(FalloutFormKey key) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(key));
        var package = _requestedSelection!.Record?.FormKey;
        var saved = new FalloutActorPendingPackageSelection(owner.FormKey, Hash(owner.FormKey), package,
            package is { } selected ? Hash(selected) : null, WriteFurniturePose(Transform), _aiRandom.State,
            Math.Max(0, _aiPollRemaining), _aiScheduleTime, _aiQuestRevision, _aiActivityRevision, Activity.Capture(),
            FalloutPackageRetirement.Capture(records, _packageEvents!), _blink?.Capture(), _aiError, _failedPackage,
            _failedPackage is { } failed ? Hash(failed) : null,
            _packageIdleSource is null ? null : CapturePackageIdleState(), _packageIdleError)
        { OverrideRevision = _requestedSelection.OverrideRevision, ScriptOverride = _requestedSelection.ScriptOverride };
        saved.Validate(records, _aiReferenceState!);
        return saved;
    }

    private void BindPendingSelectionCapture()
    {
        if (_aiReferenceState is not { } state) return;
        if (state.ProcedureCaptureBlocker == FalloutActorPendingPackageSelection.CaptureBlocker &&
            state.PendingPackageSelection is null && state.CapturePendingPackageSelection is null)
            throw new NotSupportedException("Pending source selection lost its native retirement receipt.");
        state.CanCapturePendingPackageSelection = _pendingSelectionReady = CanCapturePendingSelection;
        state.CapturePendingPackageSelection = _pendingSelectionCapture = CapturePendingSelection;
    }

    private void RestorePendingSelection(FalloutActorPendingPackageSelection saved)
    {
        saved.Validate(_aiStack!, _aiReferenceState!);
        Activity.Restore(saved.Activity);
        _packageEvents!.RestoreRetirement(saved.Retirement);
        _aiRandom.Restore(saved.RandomState);
        if (saved.Blink is { } blink)
            (_blink ?? throw new InvalidDataException("Pending selection has no source face owner.")).Restore(blink);
        else if (_blink is not null) throw new InvalidDataException("Pending selection is missing its source blink queue.");
        if (saved.IdleState is { } idles)
        {
            _packageIdleSource = FalloutScriptPackage.Read(_aiStack!.GetEffective(idles.Package));
            _packageIdles = new(_packageIdleSource, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
            RestorePackageIdleState(idles);
        }
        var source = saved.Package is { } package ? _aiStack!.GetEffective(package) : null;
        _requestedSelection = new(source, source is null ? null : FalloutScriptPackage.Read(source),
            saved.OverrideRevision, saved.ScriptOverride);
        _selectedSourcePackage = saved.Package; _sourceSelectionKnown = true;
        _aiError = saved.Error; _failedPackage = saved.FailedPackage; _packageIdleError = saved.IdleError;
        _aiPollRemaining = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime;
        _aiQuestRevision = saved.QuestRevision; _aiActivityRevision = saved.ActivityRevision;
        _selectionCaptureBlocker = FalloutActorPendingPackageSelection.CaptureBlocker;
        Transform = ReadFurniturePose(saved.Pose);
    }

    private void RetainPendingSelection()
    {
        if (_aiReferenceState is not { } state) return;
        if (CanCapturePendingSelection()) state.PendingPackageSelection = CapturePendingSelection().Copy();
        if (ReferenceEquals(state.CanCapturePendingPackageSelection, _pendingSelectionReady)) state.CanCapturePendingPackageSelection = null;
        if (ReferenceEquals(state.CapturePendingPackageSelection, _pendingSelectionCapture)) state.CapturePendingPackageSelection = null;
    }
}
