using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutSandboxAdmissionException? _sandboxAdmissionFailure;
    private FalloutSandboxAdmissionStaging? _sandboxAdmissionStaging;
    private FalloutReferenceInstance? _sandboxAdmissionSource;
    private FalloutActorSandboxAdmissionFailure? _sandboxAdmissionStagedPose;
    private bool _sandboxAdmissionConfigurationComplete;
    private Func<bool>? _sandboxAdmissionReady;
    private Func<FalloutActorSandboxAdmissionFailure>? _sandboxAdmissionCapture;
    private string? SandboxAdmissionError => _sandboxAdmissionFailure?.Message;
    private bool HasDetachedSandboxAdmission => _sandboxAdmissionStaging is { IsLive: false };

    // Create calls this before binding the base KF. A rejected detached skin
    // must never change the original reference's clock or retained receipt.
    private void PrepareSandboxAdmissionSource(FalloutReferenceInstance state)
    {
        if (state.SandboxAdmissionFailure is null) return;
        _sandboxAdmissionSource = state;
        _sandboxAdmissionStaging = new(state);
        _clock = _sandboxAdmissionStaging.Clock;
    }

    internal void RequireSourcePublication()
    {
        PublishPendingSandboxStartSource();
        if (_sandboxAdmissionStaging is not { } staging) return;
        try
        {
            staging.EnterLive(() =>
            {
                if (!IsInsideTree() || !_sandboxAdmissionConfigurationComplete ||
                    _sandboxAdmissionStagedPose is not { } saved || !HasSandboxAdmissionCaptureSource())
                    throw new InvalidDataException("Stopped creature Sandbox has no configured source presentation.");
                var intended = saved.ResolveTransform(GetParentNode3D()?.GlobalTransform ?? Transform3D.Identity,
                    Skeleton.UnitsToMetres, Scale.X);
                GlobalTransform = intended;
                if (!GlobalTransform.IsEqualApprox(intended))
                    throw new InvalidDataException("Stopped creature Sandbox pose did not publish.");
            }, () => GlobalTransform);
            staging.RequirePublished();
            _clock = _aiState!.Animation;
            _sandboxAdmissionStagedPose = null;
        }
        catch
        {
            RetireSourceBindings();
            throw;
        }
    }

    private bool CanCaptureSandboxAdmissionFailure() => IsInsideTree() && !HasDetachedSandboxAdmission &&
        HasSandboxAdmissionCaptureSource();

    private bool HasSandboxAdmissionCaptureSource() => !_sourceBindingsRetired &&
        _aiState is { } state && _sandboxAdmissionFailure is { } failure &&
        _aiError == failure.Message && _failedPackage == failure.Package &&
        _aiPackage?.FormKey == failure.Package && _sandboxPackage?.Form == failure.Package &&
        state.ProcedureCaptureBlocker == FalloutSandboxPackage.CaptureBlockerPrefix + failure.Message &&
        _initialPackageSelected && _packageSelectionAttempted && _packageSelectionAttempts is > 0 and < long.MaxValue &&
        _packageEvents is { Active: { Procedure: 12, Idles.Count: 0 } active, Done: false, Error: null, LastEvent: "POBA" } events &&
        active.Form == failure.Package && events.LastPackage == failure.Package && events.Revision is > 0 and < long.MaxValue &&
        Error is null && state.ScriptError is null && state.ScriptStoppedFrame is null &&
        !ScriptIdleActive && !_scriptIdleSourceCancelled && _eventIdleAnimation is null && _eventIdleClock is null &&
        _conversationTarget is null && !PackageEndIdlePending &&
        _sandboxWander is null && _followPackage is null && _travelPackage is null && _travelProgress is null &&
        _guardPackage is null && _patrol is null && _dialoguePackage is null && !_dialogueRequested &&
        !_accompanyPortalRequested && _accompanyPortalWait is null &&
        state.PackageMotion is null && state.PackageIdle is null && state.CreatureScriptIdle is null &&
        state.SandboxMotionFailure is null && state.PackageBindingFailure is null &&
        state.SelectionFailure is null && state.PendingPackageSelection is null && state.StoppedRetirement is null &&
        state.FurnitureContinuation is null && state.DialogueContinuation is null &&
        state.DeferredPackageContinuation is null && state.PendingSandboxStart is null &&
        state.Engagement is null && state.Ragdoll is null && state.ObjectPhysics is null && state.HitReaction is null &&
        !state.Unconscious && !state.Restrained && !state.KnockedDown && !Activity.InCombat && Velocity == Vector3.Zero &&
        Combat is { SandboxAdmissionCaptureReady: true, AnimationWeapon: null } && _clock.Capture() is not null &&
        OpenNV.Runtime.SceneGraph.NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this)
            .All(sound => sound.CanCaptureSilent);

    private FalloutActorSandboxAdmissionFailure CaptureSandboxAdmissionFailure()
    {
        if (!CanCaptureSandboxAdmissionFailure())
            throw new NotSupportedException("Stopped creature Sandbox admission still has an independent continuation owner.");
        var state = _aiState!;
        var package = _aiPackage!;
        var hash = FalloutActorFurnitureContinuation.RecordHash(package);
        var position = GlobalPosition; var basis = GlobalBasis;
        var events = _packageEvents!;
        var activity = Activity.Capture();
        var saved = new FalloutActorSandboxAdmissionFailure(package.FormKey, hash, _aiError!,
            [position.X, position.Y, position.Z], [basis.X.X, basis.X.Y, basis.X.Z,
                basis.Y.X, basis.Y.Y, basis.Y.Z, basis.Z.X, basis.Z.Y, basis.Z.Z],
            state.SoundRandom.State, Math.Max(0, _packageClock), _aiScheduleTime, _aiQuestRevision,
            activity.Revision, activity, events.Revision, events.LastEvent!, events.LastPackage!.Value, hash,
            SourcePlacement: state.SandboxAdmissionFailure?.SourcePlacement?.Copy(), SelectionAttempts: _packageSelectionAttempts);
        saved.Validate(_aiRecords!, state);
        return saved;
    }

    private void RestoreSandboxAdmissionFailure(FalloutActorSandboxAdmissionFailure saved)
    {
        var state = _aiState!;
        var records = _aiRecords!;
        saved.Validate(records, state);
        if (!ReferenceEquals(_sandboxAdmissionSource, state) || _sandboxAdmissionStaging is null ||
            _clock.Capture() != state.Animation.Capture() || state.SoundRandom.State != saved.AiRandomState ||
            state.ScriptError is not null || state.ScriptStoppedFrame is not null || state.CreatureScriptIdle is not null ||
            state.SandboxMotionFailure is not null || state.PendingSandboxStart is not null || saved.Blink is not null)
            throw new InvalidDataException("Stopped creature Sandbox lost its detached source, clock or independent owner.");
        saved.RestoreActivity(records, state, Activity);
        saved.RestoreEvents(records, state, _packageEvents!);
        _aiPackage = records.GetEffective(saved.Package);
        _sandboxPackage = FalloutSandboxPackage.Read(_aiPackage);
        _sandboxWander = null;
        _sandboxAdmissionFailure = new(saved.Package, saved.Error);
        _aiError = saved.Error; _failedPackage = saved.Package;
        _initialPackageSelected = true; _packageSelectionAttempted = true;
        _packageSelectionAttempts = saved.SelectionAttempts ??
            throw new InvalidDataException("Stopped creature Sandbox lost its selection-attempt count.");
        _evaluateRequested = false; _packageClock = saved.PollRemaining;
        _aiScheduleTime = saved.ScheduleTime; _aiQuestRevision = saved.QuestRevision;
        state.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + saved.Error;
        _sandboxAdmissionStagedPose = saved.Copy();
    }

    private void BindSandboxAdmissionCapture()
    {
        _aiState!.CanCaptureSandboxAdmissionFailure = _sandboxAdmissionReady = CanCaptureSandboxAdmissionFailure;
        _aiState.CaptureSandboxAdmissionFailure = _sandboxAdmissionCapture = CaptureSandboxAdmissionFailure;
    }

    private void RebindSandboxAdmissionCapture()
    {
        _aiState!.CanCaptureSandboxAdmissionFailure ??= _sandboxAdmissionReady;
        _aiState.CaptureSandboxAdmissionFailure ??= _sandboxAdmissionCapture;
    }

    private void RetireSandboxAdmissionCapture()
    {
        if (_aiState is not { } state || _sandboxAdmissionCapture is null ||
            !ReferenceEquals(state.CaptureSandboxAdmissionFailure, _sandboxAdmissionCapture)) return;
        try
        {
            if (SandboxAdmissionError is not null)
            {
                var placement = state.SandboxAdmissionFailure?.SourcePlacement?.Copy();
                state.SandboxAdmissionFailure = null;
                var saved = CaptureSandboxAdmissionFailure();
                if (placement is not null) saved = saved.Relocate(placement);
                saved.Validate(_aiRecords!, state);
                state.SandboxAdmissionFailure = saved.Copy();
            }
        }
        finally { ReleaseSandboxAdmissionCapture(); }
    }

    private void ReleaseSandboxAdmissionCapture()
    {
        if (_aiState is not { } state) return;
        if (ReferenceEquals(state.CaptureSandboxAdmissionFailure, _sandboxAdmissionCapture)) state.CaptureSandboxAdmissionFailure = null;
        if (ReferenceEquals(state.CanCaptureSandboxAdmissionFailure, _sandboxAdmissionReady)) state.CanCaptureSandboxAdmissionFailure = null;
    }

    private void ReleaseDetachedSandboxAdmissionBindings()
    {
        if (_aiState is not { } state) return;
        ReleaseSandboxAdmissionCapture();
        if (_scriptIdleCapture is not null && ReferenceEquals(state.CaptureCreatureScriptIdle, _scriptIdleCapture))
        {
            state.CaptureCreatureScriptIdle = null;
            if (ReferenceEquals(state.CanCaptureCreatureScriptIdle, _scriptIdleReady)) state.CanCaptureCreatureScriptIdle = null;
        }
        if (_sandboxMotionCapture is not null && ReferenceEquals(state.CaptureSandboxMotionFailure, _sandboxMotionCapture))
        {
            state.CaptureSandboxMotionFailure = null;
            if (ReferenceEquals(state.CanCaptureSandboxMotionFailure, _sandboxMotionReady)) state.CanCaptureSandboxMotionFailure = null;
        }
    }
}
