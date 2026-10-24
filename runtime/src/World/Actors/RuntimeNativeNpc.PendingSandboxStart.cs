using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private bool _pendingSandboxAwaitingFirstMotion;
    private Func<bool>? _pendingSandboxReady;
    private Func<FalloutActorPendingSandboxStart>? _pendingSandboxCapture;
    private FalloutActorPendingSandboxStart? _pendingSandboxStagedReceipt;
    private FalloutActorAnimationSnapshot? _pendingSandboxInitialClock;
    private bool _pendingSandboxPublicationComplete;
    private bool _pendingSandboxHeadRequired;
    private string? _pendingSandboxHeadBlocker;

    private bool HasDetachedPendingSandboxStart => _pendingSandboxStagedReceipt is not null && !_pendingSandboxPublicationComplete;

    private FalloutActorAnimationState? PreparePendingSandboxStartClock()
    {
        if (_aiReferenceState is not { PendingSandboxStart: { } saved } state) return null;
        saved.Validate(_aiStack!, state);
        _pendingSandboxStagedReceipt = saved;
        _pendingSandboxInitialClock = state.Animation.Capture() ??
            throw new InvalidDataException("Pending Sandbox has no retained base clock.");
        _pendingSandboxHeadRequired = state.HeadTrackingRequired;
        _pendingSandboxHeadBlocker = state.HeadTrackingCaptureBlocker;
        var clock = new FalloutActorAnimationState();
        clock.Restore(_pendingSandboxInitialClock);
        return clock;
    }

    // The publication owner calls this after AddChild returns, while the saved
    // authority still belongs to the original reference, not its candidate.
    private void PublishPendingSandboxStartSource()
    {
        if (_pendingSandboxStagedReceipt is not { } saved || _pendingSandboxPublicationComplete) return;
        try
        {
            if (!IsInsideTree() || _bindingInitialBase || _headRestore is not null || _aiReferenceState is not { } state)
                throw new InvalidDataException("Pending Sandbox has no configured live presentation.");
            if (!ReferenceEquals(state.PendingSandboxStart, saved) || state.Animation.Capture() != _pendingSandboxInitialClock)
                throw new InvalidDataException("Pending Sandbox authority changed during detached construction.");
            if (!HasIndependentStoppedPose)
            {
                var pose = ReadFurniturePose(saved.Pose);
                Transform = pose;
                if (!Transform.IsEqualApprox(pose))
                    throw new InvalidDataException("Pending Sandbox native pose did not publish.");
            }
            if (!ReferenceEquals(state.PendingSandboxStart, saved) || state.Animation.Capture() != _pendingSandboxInitialClock)
                throw new InvalidDataException("Pending Sandbox authority changed during native publication.");
            var clock = _baseClock.Capture() ?? throw new InvalidDataException("Pending Sandbox staged base clock is absent.");
            state.Animation.Restore(clock);
            _baseClock = state.Animation;
            _pendingSandboxPublicationComplete = true;
        }
        catch
        {
            RetireSourceBindings();
            throw;
        }
    }

    private bool CanCapturePendingSandboxStart() => IsInsideTree() && !_bindingInitialBase &&
        (_pendingSandboxStagedReceipt is null || _pendingSandboxPublicationComplete) && _pendingSandboxAwaitingFirstMotion &&
        _aiReferenceState is { } state && state.ProcedureCaptureBlocker == FalloutActorPendingSandboxStart.CaptureBlocker &&
        state.ScriptError is null && state.ScriptStoppedFrame is null && state.PackageMotion is null && state.PackageIdle is null &&
        state.DeferredPackageContinuation is null && state.PackageBindingFailure is null && state.SelectionFailure is null &&
        state.PendingPackageSelection is null && state.StoppedRetirement is null && state.FurnitureContinuation is null &&
        state.DialogueContinuation is null && state.SandboxAdmissionFailure is null && state.SandboxMotionFailure is null &&
        state.CreatureScriptIdle is null && _aiError is null && _failedPackage is null &&
        _sourceSelectionKnown && _aiPackage is { } package && _selectedSourcePackage == package.FormKey &&
        _sandboxPackage?.Form == package.FormKey && _sandboxWander is null &&
        _packageIdleSource is { Procedure: 12, Idles.Count: 0 } idleSource && idleSource.Form == package.FormKey &&
        _packageIdles is null && _packageIdleError is null &&
        _packageEvents is { Active: { } active, Done: false, Error: null, LastEvent: "POBA" } events &&
        active.Form == package.FormKey && events.LastPackage == package.FormKey && events.Revision is > 0 and < long.MaxValue &&
        _requestedSelection is null && _pendingPackage is null && _findFurniture is null && _seat is null &&
        _sitting == 0 && !_furnitureApproaching && !_travelActive && _travelProgress?.ArrivalPending != true &&
        _escortPackage is null && _editorTravel is null && _nativeMarkerTravel is null && _pendingCurrentLocationTravel is null &&
        _dialoguePackage is null && _guardPackage is null && _patrol is null &&
        _animation is null && _animationObjects.Count == 0 && !_responseIdleActive && AnimationError is null &&
        _animationSounds?.CanCaptureSilent != false && _conversationTarget is null &&
        CanCaptureStoppedAiPose() && !_baseLocomotionMoving && _baseClock.Resource.Length != 0;

    private FalloutActorPendingSandboxStart CapturePendingSandboxStart()
    {
        if (!CanCapturePendingSandboxStart())
            throw new NotSupportedException("Pending Sandbox still requires an independent procedure, source or pose owner.");
        var records = _aiStack!;
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(Appearance.Npc), 32, _templates);
        string Hash(FalloutFormKey key) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(key));
        var events = _packageEvents!;
        var saved = new FalloutActorPendingSandboxStart(Appearance.Reference!.Value, owner.FormKey, Hash(owner.FormKey),
            _aiPackage!.FormKey, Hash(_aiPackage.FormKey), WriteFurniturePose(Transform), _aiRandom.State,
            Math.Max(0, _aiPollRemaining), _aiScheduleTime, _aiQuestRevision, _aiActivityRevision, Activity.Capture(),
            events.Revision, events.LastEvent!, events.LastPackage!.Value,
            _idleReplays.Remaining.Select(value => new FalloutIdleReplayCooldown(value.Key, Hash(value.Key), value.Value)).ToArray(),
            _blink?.Capture(), _aiReferenceState!.Placement?.Copy());
        saved.Validate(records, _aiReferenceState);
        return saved;
    }

    private void BindPendingSandboxStartCapture()
    {
        if (_aiReferenceState is not { } state) return;
        if (state.ProcedureCaptureBlocker == FalloutActorPendingSandboxStart.CaptureBlocker &&
            state.PendingSandboxStart is null && state.CapturePendingSandboxStart is null)
            throw new NotSupportedException("Pending Sandbox lost its native start receipt.");
        state.CanCapturePendingSandboxStart = _pendingSandboxReady = CanCapturePendingSandboxStart;
        state.CapturePendingSandboxStart = _pendingSandboxCapture = CapturePendingSandboxStart;
    }

    private void RestorePendingSandboxStart(FalloutActorPendingSandboxStart saved)
    {
        saved.Validate(_aiStack!, _aiReferenceState!);
        saved.RestoreEvents(_aiStack!, _aiReferenceState!, _packageEvents!);
        Activity.Restore(saved.Activity);
        _aiRandom.Restore(saved.RandomState);
        _idleReplays.Restore(saved.ReplayCooldowns.ToDictionary(value => value.Idle, value => value.Remaining));
        if (saved.Blink is { } blink)
            (_blink ?? throw new InvalidDataException("Pending Sandbox has no source face owner.")).Restore(blink);
        else if (_blink is not null) throw new InvalidDataException("Pending Sandbox is missing its source blink history.");
        _aiPackage = _aiStack!.GetEffective(saved.Package);
        _packageIdleSource = _packageEvents!.Active;
        _packageIdles = null; _packageIdleError = null;
        _sandboxPackage = FalloutSandboxPackage.Read(_aiPackage);
        _sandboxWander = null;
        _selectedSourcePackage = saved.Package; _sourceSelectionKnown = true;
        _aiError = null; _failedPackage = null;
        _aiPollRemaining = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime;
        _aiQuestRevision = saved.QuestRevision; _aiActivityRevision = saved.ActivityRevision;
        _pendingSandboxAwaitingFirstMotion = true;
        _aiReferenceState!.ProcedureCaptureBlocker = FalloutActorPendingSandboxStart.CaptureBlocker;
        Transform = ReadFurniturePose(saved.Pose);
    }

    private void RetainPendingSandboxStart()
    {
        if (_aiReferenceState is not { } state || _pendingSandboxCapture is null ||
            !ReferenceEquals(state.CapturePendingSandboxStart, _pendingSandboxCapture) ||
            !ReferenceEquals(state.CanCapturePendingSandboxStart, _pendingSandboxReady)) return;
        // A failed detached candidate must leave the original receipt and clock
        // untouched. A live retirement must never leave a stale capture fallback.
        if (_pendingSandboxStagedReceipt is not null && !_pendingSandboxPublicationComplete) return;
        state.PendingSandboxStart = null;
        if (CanCapturePendingSandboxStart()) state.PendingSandboxStart = CapturePendingSandboxStart().Copy();
    }

    private void ReleasePendingSandboxStartBindings()
    {
        if (_aiReferenceState is not { } state) return;
        if (ReferenceEquals(state.CanCapturePendingSandboxStart, _pendingSandboxReady)) state.CanCapturePendingSandboxStart = null;
        if (ReferenceEquals(state.CapturePendingSandboxStart, _pendingSandboxCapture)) state.CapturePendingSandboxStart = null;
        if (_pendingSandboxStagedReceipt is not null && !_pendingSandboxPublicationComplete &&
            ReferenceEquals(state.CaptureHeadTracking, _headCapture))
        {
            state.CaptureHeadTracking = null;
            state.HeadTrackingRequired = _pendingSandboxHeadRequired;
            state.HeadTrackingCaptureBlocker = _pendingSandboxHeadBlocker;
            Skeleton.Node.TreeExiting -= RetainHeadTracking;
            _headCapture = null;
        }
    }

    private void RebindPendingSandboxStartBindings()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCapturePendingSandboxStart ??= _pendingSandboxReady;
        state.CapturePendingSandboxStart ??= _pendingSandboxCapture;
    }

    private void ClearPendingSandboxStart(bool clearBlocker)
    {
        _pendingSandboxAwaitingFirstMotion = false;
        if (_aiReferenceState is not { } state) return;
        state.PendingSandboxStart = null;
        if (clearBlocker && state.ProcedureCaptureBlocker == FalloutActorPendingSandboxStart.CaptureBlocker)
            state.ProcedureCaptureBlocker = null;
    }
}
