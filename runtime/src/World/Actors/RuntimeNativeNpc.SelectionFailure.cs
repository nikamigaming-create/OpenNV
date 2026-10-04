using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutCondition? _failedSelectionCondition;
    private Func<bool>? _selectionFailureReady;
    private Func<FalloutActorSelectionFailure>? _selectionFailureCapture;

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
        _idleReplays.Remaining.Count == 0 && _conversationTarget is null && Combat?.OwnsPose != true && Combat?.PackageOwnsPose != true &&
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
        if (CanCaptureSelectionFailure()) state.SelectionFailure = CaptureSelectionFailure();
        if (ReferenceEquals(state.CanCaptureSelectionFailure, _selectionFailureReady)) state.CanCaptureSelectionFailure = null;
        if (ReferenceEquals(state.CaptureSelectionFailure, _selectionFailureCapture)) state.CaptureSelectionFailure = null;
    }
}
