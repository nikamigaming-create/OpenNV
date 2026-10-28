using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutActorPendingSandboxStart? _pendingSandboxStagedReceipt;
    private FalloutActorAnimationSnapshot? _pendingSandboxInitialClock;
    private bool _pendingSandboxPublished;
    private Func<bool>? _pendingSandboxReady;
    private Func<FalloutActorPendingSandboxStart>? _pendingSandboxCapture;
    private bool HasDetachedPendingSandboxStart => _pendingSandboxStagedReceipt is not null && !_pendingSandboxPublished;

    private void PreparePendingSandboxStartSource(FalloutPluginStack records, FalloutReferenceInstance state)
    {
        if (state.PendingSandboxStart is not { } saved) return;
        saved.Validate(records, state);
        _pendingSandboxStagedReceipt = saved;
        _pendingSandboxInitialClock = state.Animation.Capture() ??
            throw new InvalidDataException("Pending creature Sandbox has no retained base clock.");
        _clock = new FalloutActorAnimationState();
        _clock.Restore(_pendingSandboxInitialClock);
    }

    private static Transform3D PendingSandboxPose(float[] pose) => new(
        new Basis(new(pose[0], pose[1], pose[2]), new(pose[3], pose[4], pose[5]), new(pose[6], pose[7], pose[8])),
        new(pose[9], pose[10], pose[11]));

    private void PublishPendingSandboxStartSource()
    {
        if (_pendingSandboxStagedReceipt is not { } saved || _pendingSandboxPublished) return;
        try
        {
            var state = _aiState ?? throw new InvalidDataException("Pending creature Sandbox has no configured source.");
            if (!IsInsideTree() || !ReferenceEquals(state.PendingSandboxStart, saved) ||
                state.Animation.Capture() != _pendingSandboxInitialClock || state.SoundRandom.State != saved.RandomState)
                throw new InvalidDataException("Pending creature Sandbox authority changed during detached construction.");
            saved.Validate(_aiRecords!, state);
            var pose = PendingSandboxPose(saved.Pose);
            Transform = pose;
            if (!Transform.IsEqualApprox(pose) || !ReferenceEquals(state.PendingSandboxStart, saved) ||
                state.Animation.Capture() != _pendingSandboxInitialClock)
                throw new InvalidDataException("Pending creature Sandbox authority changed during publication.");
            state.Animation.Restore(_clock.Capture() ?? throw new InvalidDataException("Pending creature Sandbox staged clock is absent."));
            _clock = state.Animation;
            _pendingSandboxPublished = true;
        }
        catch { RetireSourceBindings(); throw; }
    }

    private bool CanCapturePendingSandboxStart() => IsInsideTree() && !HasDetachedPendingSandboxStart && !_sourceBindingsRetired &&
        _aiState is { } state && state.ProcedureCaptureBlocker == FalloutActorPendingSandboxStart.CaptureBlocker &&
        _aiError is null && _failedPackage is null && _initialPackageSelected && _packageSelectionAttempted &&
        _packageSelectionAttempts is > 0 and < long.MaxValue && _aiPackage is { } package && _sandboxPackage?.Form == package.FormKey &&
        _packageEvents is { Active: { Procedure: 12, Idles.Count: 0 } active, Done: false, Error: null, LastEvent: "POBA" } events &&
        active.Form == package.FormKey && events.LastPackage == package.FormKey && events.Revision is > 0 and < long.MaxValue &&
        Error is null && state.ScriptError is null && state.ScriptStoppedFrame is null &&
        !ScriptIdleActive && !_scriptIdleSourceCancelled && _eventIdleAnimation is null && _eventIdleClock is null &&
        _conversationTarget is null && !PackageEndIdlePending && _sandboxWander is null && _followPackage is null &&
        _travelPackage is null && _travelProgress is null && _guardPackage is null && _patrol is null &&
        _dialoguePackage is null && !_dialogueRequested && !_accompanyPortalRequested && _accompanyPortalWait is null &&
        state.PackageMotion is null && state.PackageIdle is null && state.CreatureScriptIdle is null &&
        state.SandboxMotionFailure is null && state.SandboxAdmissionFailure is null && state.InterruptedSandboxAdmission is null &&
        state.PackageBindingFailure is null && state.SelectionFailure is null && state.PendingPackageSelection is null &&
        state.StoppedRetirement is null && state.FurnitureContinuation is null && state.DialogueContinuation is null &&
        state.DeferredPackageContinuation is null && state.Engagement is null && state.Ragdoll is null &&
        state.ObjectPhysics is null && state.HitReaction is null && !state.Unconscious && !state.Restrained &&
        !state.KnockedDown && !Activity.InCombat && Velocity == Vector3.Zero &&
        Combat is { SandboxAdmissionCaptureReady: true, AnimationWeapon: null } && _clock.Capture() is not null &&
        OpenNV.Runtime.SceneGraph.NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this)
            .All(sound => sound.CanCaptureSilent);

    private FalloutActorPendingSandboxStart CapturePendingSandboxStart()
    {
        if (!CanCapturePendingSandboxStart()) throw new NotSupportedException("Pending creature Sandbox has an independent continuation owner.");
        var state = _aiState!; var records = _aiRecords!; var package = _aiPackage!;
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(state.Base), 32, state.Templates);
        var basis = Transform.Basis; var position = Transform.Origin; var events = _packageEvents!; var activity = Activity.Capture();
        var saved = new FalloutActorPendingSandboxStart(state.Reference, owner.FormKey, FalloutActorFurnitureContinuation.RecordHash(owner),
            package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package),
            [basis.X.X, basis.X.Y, basis.X.Z, basis.Y.X, basis.Y.Y, basis.Y.Z, basis.Z.X, basis.Z.Y, basis.Z.Z, position.X, position.Y, position.Z],
            state.SoundRandom.State, Math.Max(0, _packageClock), _aiScheduleTime, _aiQuestRevision, activity.Revision, activity,
            events.Revision, events.LastEvent!, events.LastPackage!.Value, [], SourcePlacement: state.Placement?.Copy(),
            SelectionAttempts: _packageSelectionAttempts);
        saved.Validate(records, state);
        return saved;
    }

    private void RestorePendingSandboxStart(FalloutActorPendingSandboxStart saved)
    {
        var state = _aiState!; var records = _aiRecords!;
        saved.Validate(records, state);
        if (!ReferenceEquals(saved, _pendingSandboxStagedReceipt) || _clock.Capture() != state.Animation.Capture())
            throw new InvalidDataException("Pending creature Sandbox lost its detached source or clock.");
        saved.RestoreEvents(records, state, _packageEvents!);
        Activity.Restore(saved.Activity);
        _aiPackage = records.GetEffective(saved.Package); _sandboxPackage = FalloutSandboxPackage.Read(_aiPackage); _sandboxWander = null;
        _initialPackageSelected = true; _packageSelectionAttempted = true;
        _packageSelectionAttempts = saved.SelectionAttempts!.Value; _evaluateRequested = false;
        _packageClock = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime; _aiQuestRevision = saved.QuestRevision;
        _aiError = null; _failedPackage = null; state.ProcedureCaptureBlocker = FalloutActorPendingSandboxStart.CaptureBlocker;
    }

    private void BindPendingSandboxStartCapture()
    {
        var state = _aiState!;
        if (state.ProcedureCaptureBlocker == FalloutActorPendingSandboxStart.CaptureBlocker &&
            state.PendingSandboxStart is null && state.CapturePendingSandboxStart is null)
            throw new NotSupportedException("Pending creature Sandbox lost its native start receipt.");
        state.CanCapturePendingSandboxStart = _pendingSandboxReady = CanCapturePendingSandboxStart;
        state.CapturePendingSandboxStart = _pendingSandboxCapture = CapturePendingSandboxStart;
    }

    private void RetirePendingSandboxStartCapture()
    {
        if (_aiState is not { } state || !ReferenceEquals(state.CapturePendingSandboxStart, _pendingSandboxCapture)) return;
        if (!HasDetachedPendingSandboxStart)
        {
            state.PendingSandboxStart = null;
            if (CanCapturePendingSandboxStart()) state.PendingSandboxStart = CapturePendingSandboxStart().Copy();
        }
        ReleasePendingSandboxStartCapture();
    }

    private void ReleasePendingSandboxStartCapture()
    {
        if (_aiState is not { } state) return;
        if (ReferenceEquals(state.CanCapturePendingSandboxStart, _pendingSandboxReady)) state.CanCapturePendingSandboxStart = null;
        if (ReferenceEquals(state.CapturePendingSandboxStart, _pendingSandboxCapture)) state.CapturePendingSandboxStart = null;
    }
}
