using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutCondition? _failedSelectionCondition;
    private Func<bool>? _selectionFailureReady;
    private Func<FalloutActorSelectionFailure>? _selectionFailureCapture;
    private Func<FalloutActorSelectionCaptureDiagnostic>? _selectionCaptureDiagnostic;
    private ulong _selectionCaptureNativeOwner;

    private FalloutActorSelectionCaptureDiagnostic ReadSelectionCaptureDiagnostic(bool retired = false)
    {
        var blockers = new List<FalloutActorCaptureBlocker>();
        void Refuse(bool condition, string predicate, string? detail = null)
        {
            if (condition) blockers.Add(new("npc-selection", predicate, detail));
        }
        Refuse(_failedSelectionCondition is null, "failed-condition-missing");
        Refuse(_aiError is null, "selection-error-missing");
        Refuse(_aiReferenceState is null, "reference-instance-missing");
        Refuse(_selectionCaptureBlocker is null, "selection-blocker-missing");
        Refuse(_aiReferenceState?.ProcedureCaptureBlocker != _selectionCaptureBlocker, "procedure-blocker-binding");
        Refuse(_bindingInitialBase, "binding-initial-base");
        Refuse(_requestedSelection is not null, "requested-selection");
        Refuse(_pendingPackage is not null, "pending-package");
        Refuse(_selectedSourcePackage is not null, "selected-source-package");
        Refuse(_failedPackage is not null, "failed-package");
        Refuse(_aiPackage is not null, "active-package");
        Refuse(_packageEvents is null, "package-events-missing");
        if (_packageEvents is { } events)
        {
            Refuse(events.Active is not null, "active-package-event");
            Refuse(events.Done, "completed-package-event");
            Refuse(events.Error is not null, "package-event-error", events.Error);
        }
        Refuse(_packageIdleSource is not null, "package-idle-source");
        Refuse(_findFurniture is not null, "furniture-search");
        Refuse(_seat is not null, "reserved-seat");
        Refuse(_sitting != 0, "sitting");
        Refuse(_furnitureApproaching, "furniture-approach");
        Refuse(_travelActive, "travel-active");
        Refuse(_escortPackage is not null, "escort-package");
        Refuse(_editorTravel is not null, "editor-travel");
        Refuse(_dialoguePackage is not null, "dialogue-package");
        Refuse(_guardPackage is not null, "guard-package");
        Refuse(_patrol is not null, "patrol");
        Refuse(_animation is not null, "active-animation");
        Refuse(_responseIdleActive, "response-idle");
        Refuse(_packageIdleError is not null, "package-idle-error", _packageIdleError);
        Refuse(AnimationError is not null, "animation-error", AnimationError);
        Refuse(_idleReplays.Remaining.Count != 0, "remaining-idle-replays");
        Refuse(_conversationTarget is not null, "conversation-target");
        var pose = ReadStoppedPoseCaptureDiagnostic();
        Refuse(!CanCaptureStoppedAiPose(), "independent-pose");
        Refuse(_baseLocomotionMoving, "base-locomotion-moving");
        Refuse(_baseClock.Resource.Length == 0, "base-clock-missing");
        return new(_aiReferenceState?.Reference ?? Appearance.Reference!.Value, _selectionCaptureNativeOwner,
            CanCaptureSelectionFailure(), retired, _failedSelectionCondition?.Owner.FormKey,
            _failedSelectionCondition?.Function, blockers.AsReadOnly(), pose);
    }

    private float EvaluateSelectionCondition(FalloutCondition condition)
    {
        try { return EvaluateAiCondition(condition); }
        catch
        {
            _failedSelectionCondition = condition;
            throw;
        }
    }

    private bool CanCaptureSelectionFailure() => _failedSelectionCondition is not null && _aiError is not null &&
        _aiReferenceState is { } state && state.ProcedureCaptureBlocker == _selectionCaptureBlocker &&
        _selectionCaptureBlocker is not null && !_bindingInitialBase && _requestedSelection is null && _pendingPackage is null &&
        _selectedSourcePackage is null && _failedPackage is null && _aiPackage is null &&
        _packageEvents is { Active: null, Done: false, Error: null } && _packageIdleSource is null &&
        _findFurniture is null && _seat is null && _sitting == 0 && !_furnitureApproaching && !_travelActive &&
        _escortPackage is null && _editorTravel is null && _dialoguePackage is null && _guardPackage is null && _patrol is null &&
        _animation is null && !_responseIdleActive && _packageIdleError is null && AnimationError is null &&
        _idleReplays.Remaining.Count == 0 && _conversationTarget is null && CanCaptureStoppedAiPose() &&
        !_baseLocomotionMoving && _baseClock.Resource.Length != 0;

    private FalloutActorSelectionFailure CaptureSelectionFailure()
    {
        if (!CanCaptureSelectionFailure()) throw new NotSupportedException("Failed selection has an active procedure or pose continuation.");
        var condition = _failedSelectionCondition!;
        var sourceConditions = FalloutCondition.Read(condition.Owner);
        var ordinal = sourceConditions.Select((value, index) => (value, index)).Where(value => value.value == condition)
            .Select(value => value.index).FirstOrDefault(-1);
        var failure = new FalloutActorSelectionFailure(condition.Owner.FormKey,
            FalloutActorFurnitureContinuation.RecordHash(condition.Owner), ordinal, _aiError!, WriteFurniturePose(Transform),
            _aiRandom.State, Math.Max(0, _aiPollRemaining), _aiScheduleTime, _sourceSelectionKnown,
            FalloutPackageRetirement.Capture(_aiStack!, _packageEvents!), _blink?.Capture());
        failure.Validate(_aiStack!, _aiReferenceState!);
        return failure;
    }

    private void BindSelectionFailureCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCaptureSelectionFailure = _selectionFailureReady = CanCaptureSelectionFailure;
        state.CaptureSelectionFailure = _selectionFailureCapture = CaptureSelectionFailure;
        _selectionCaptureNativeOwner = GetInstanceId();
        state.ObserveSelectionCapture = _selectionCaptureDiagnostic = () => ReadSelectionCaptureDiagnostic();
        state.RetiredSelectionCaptureDiagnostic = null;
    }

    private void RestoreSelectionFailure(FalloutActorSelectionFailure failure)
    {
        failure.Validate(_aiStack!, _aiReferenceState!);
        _failedSelectionCondition = FalloutCondition.Read(_aiStack!.GetEffective(failure.Candidate))[failure.Condition];
        _packageEvents!.RestoreRetirement(failure.Retirement);
        _aiRandom.Restore(failure.RandomState);
        if (failure.Blink is { } blink)
            (_blink ?? throw new InvalidDataException("Failed selection has no source face owner.")).Restore(blink);
        else if (_blink is not null) throw new InvalidDataException("Failed selection is missing its source blink queue.");
        _aiError = failure.Error; _failedPackage = null;
        _sourceSelectionKnown = failure.SourceSelectionKnown; _selectedSourcePackage = null;
        _selectionCaptureBlocker = _aiReferenceState!.ProcedureCaptureBlocker;
        _aiPollRemaining = failure.PollRemaining; _aiScheduleTime = failure.ScheduleTime;
        _aiQuestRevision = _questState!.Revision; _aiActivityRevision = Activity.Revision;
        Transform = ReadFurniturePose(failure.Pose);
    }

    private void RetainSelectionFailure()
    {
        if (_aiReferenceState is not { } state) return;
        // The source reference can have a new destination presentation already.
        // A retiring old node must not replace that instance's current observer.
        if (ReferenceEquals(state.ObserveSelectionCapture, _selectionCaptureDiagnostic))
        {
            if (_selectionCaptureBlocker is not null)
                state.RetiredSelectionCaptureDiagnostic = ReadSelectionCaptureDiagnostic(true);
            state.ObserveSelectionCapture = null;
        }
        if (CanCaptureSelectionFailure())
        {
            state.SelectionFailure = CaptureSelectionFailure();
            state.ProcedureCaptureBlocker = state.SelectionFailure.Error;
        }
        if (ReferenceEquals(state.CanCaptureSelectionFailure, _selectionFailureReady)) state.CanCaptureSelectionFailure = null;
        if (ReferenceEquals(state.CaptureSelectionFailure, _selectionFailureCapture)) state.CaptureSelectionFailure = null;
    }
}
