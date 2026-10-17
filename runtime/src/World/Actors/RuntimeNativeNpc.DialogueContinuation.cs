using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Func<bool>? _dialogueCaptureReady;
    private Func<FalloutActorDialogueContinuation?>? _dialogueCapture;
    private bool CanCaptureDialogueContinuation() => _dialoguePackage is not null && !_dialoguePackageRequested &&
        _dialogueWaitReached && !_dialogueWaitPositionPending && !_bindingInitialBase && !_travelActive &&
        _travelProgress?.ArrivalPending != true && _requestedSelection is null && _pendingPackage is null &&
        _aiError is null && AnimationError is null && _packageEvents is { Active: not null, Done: false, Error: null } &&
        _aiPackage?.FormKey == _selectedSourcePackage && _aiPackage?.FormKey == _dialoguePackage.Form &&
        _packageIdleSource is not null && _packageIdles is not null && _seat is null && _sitting == 0 &&
        _findFurniture is null && !_furnitureApproaching && _escortPackage is null && _editorTravel is null &&
        _guardPackage is null && _patrol is null && _nativeMarkerTravel is null &&
        _animation is null && !_responseIdleActive && _conversationTarget is null && NpcDialogueActive?.Invoke() != true &&
        Combat?.OwnsPose != true && Combat?.PackageOwnsPose != true && Combat?.PackageMoving != true &&
        _baseClock.Resource.Length != 0 && _aiReferenceState?.ProcedureCaptureBlocker == FalloutActorDialogueContinuation.CaptureBlocker;

    private void BindDialogueCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCaptureDialogue = _dialogueCaptureReady = CanCaptureDialogueContinuation;
        state.CaptureDialogue = _dialogueCapture = CaptureDialogueContinuation;
    }

    private FalloutActorDialogueContinuation? CaptureDialogueContinuation()
    {
        if (_aiReferenceState?.ProcedureCaptureBlocker != FalloutActorDialogueContinuation.CaptureBlocker) return null;
        if (!CanCaptureDialogueContinuation()) throw new NotSupportedException("Dialogue still has an outstanding voice, route or pose continuation.");
        var lifecycle = _packageEvents!;
        var saved = new FalloutActorDialogueContinuation(FalloutActorPackageAssignment.Capture(_aiStack!, lifecycle)!,
            lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage,
            lifecycle.LastPackage is { } previous ? FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(previous)) : null,
            WriteFurniturePose(Transform), [_dialogueWaitPosition.X, _dialogueWaitPosition.Y, _dialogueWaitPosition.Z],
            _dialogueWaitReached, _dialogueNativeMovement, _aiRandom.State, Math.Max(0, _aiPollRemaining), _aiScheduleTime,
            _blink?.Capture(), CapturePackageIdleState(),
            _dialogueTargetDestination is { } target ? [target.Authored.X, target.Authored.Y, target.Authored.Z] : null,
            _dialogueTargetDestination is { } floor ? [floor.Floor.X, floor.Floor.Y, floor.Floor.Z] : null);
        saved.Validate(); return saved;
    }

    private void RestoreDialogueContinuation(FalloutActorDialogueContinuation saved)
    {
        saved.Validate(_aiStack!, _aiReferenceState!);
        _aiPackage = _aiStack!.GetEffective(saved.Assignment.Package);
        _packageIdleSource = FalloutScriptPackage.Read(_aiPackage);
        _packageIdles = new(_packageIdleSource, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
        RestorePackageIdleState(saved.IdleState);
        _dialoguePackage = FalloutDialoguePackage.Read(_aiPackage); _dialoguePackageRequested = false;
        _dialogueWaitReached = saved.WaitReached; _dialogueNativeMovement = saved.NativeMovement; _dialogueWaitPositionPending = false;
        _dialogueWaitPosition = new(saved.WaitPosition[0], saved.WaitPosition[1], saved.WaitPosition[2]);
        _dialogueTargetDestination = saved.TargetPosition is { } target && saved.TargetFloor is { } floor ?
            (new Vector3(target[0], target[1], target[2]), new Vector3(floor[0], floor[1], floor[2])) : null;
        _selectedSourcePackage = saved.Assignment.Package; _sourceSelectionKnown = true;
        if (_packageEvents!.Active is null) saved.Assignment.Bind(_aiStack, _packageEvents);
        else if (_packageEvents.Active.Form != saved.Assignment.Package || _packageEvents.Done)
            throw new InvalidDataException("Saved dialogue wait disagrees with its restored lifecycle.");
        _packageEvents.RestoreHistory(saved.EventRevision, saved.LastEvent, saved.LastPackage);
        _aiRandom.Restore(saved.RandomState);
        if (saved.Blink is { } blink) (_blink ?? throw new InvalidDataException("Saved dialogue has no source face owner.")).Restore(blink);
        else if (_blink is not null) throw new InvalidDataException("Saved dialogue is missing its source blink queue.");
        _aiQuestRevision = _questState!.Revision; _aiActivityRevision = Activity.Revision;
        _aiPollRemaining = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime;
        Transform = ReadFurniturePose(saved.Pose);
    }

    private void RetainDialogueContinuation()
    {
        if (_aiReferenceState is not { } state) return;
        if (CanCaptureDialogueContinuation()) state.DialogueContinuation = CaptureDialogueContinuation();
        if (ReferenceEquals(state.CanCaptureDialogue, _dialogueCaptureReady)) state.CanCaptureDialogue = null;
        if (ReferenceEquals(state.CaptureDialogue, _dialogueCapture)) state.CaptureDialogue = null;
    }
}
