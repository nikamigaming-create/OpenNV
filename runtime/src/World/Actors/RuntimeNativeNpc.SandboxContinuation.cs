using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// Only the candidate-admission stage may issue this receipt. A later failure
// can have consumed a selected activity even before publishing package motion.
internal sealed class FalloutSandboxAdmissionException(FalloutFormKey package, string message)
    : NotSupportedException(message)
{
    internal FalloutFormKey Package { get; } = package;
}

internal partial class RuntimeNativeNpc
{
    private FalloutSandboxAdmissionException? _sandboxAdmissionFailure;
    private Func<bool>? _sandboxAdmissionReady;
    private Func<FalloutActorSandboxAdmissionFailure>? _sandboxAdmissionCapture;
    private FalloutSandboxAdmissionStaging? _sandboxAdmissionStaging;
    private FalloutActorSandboxAdmissionFailure? _sandboxAdmissionStagedPose;
    private float _sandboxAdmissionReferenceScale;
    private bool _sandboxAdmissionConfigurationComplete;

    // Called by the publication owner after AddChild returns. Godot logs and
    // consumes exceptions from lifecycle callbacks, so those cannot admit a
    // prepared actor or report publication failure to its constructing caller.
    internal void RequireSourcePublication()
    {
        if (_sandboxAdmissionStaging is not { } staging) return;
        try
        {
            staging.EnterLive(() =>
            {
                if (!IsInsideTree() || !_sandboxAdmissionConfigurationComplete || _sandboxAdmissionStagedPose is not { } saved)
                    throw new InvalidDataException("Stopped Sandbox has no configured live presentation to publish.");
                if (_headRestore is not null)
                    throw new InvalidDataException("Stopped Sandbox native head restoration did not complete at tree readiness.");
                var restoredTransform = saved.ResolveTransform(GetParentNode3D()?.GlobalTransform ?? Transform3D.Identity,
                    Skeleton.UnitsToMetres, _sandboxAdmissionReferenceScale);
                GlobalTransform = restoredTransform;
                if (!GlobalTransform.IsEqualApprox(restoredTransform))
                    throw new InvalidDataException("Stopped Sandbox native pose publication did not apply.");
            }, () => GlobalTransform);
            staging.RequirePublished();
            _baseClock = _aiReferenceState!.Animation;
            _sandboxAdmissionStagedPose = null;
        }
        catch
        {
            RetireSourceBindings();
            throw;
        }
    }

    private void CompleteSandboxAdmissionStaging()
    {
        _sandboxAdmissionConfigurationComplete = true;
    }

    internal void AcknowledgeSandboxPlacement(FalloutActorSandboxAdmissionFailure expected)
    {
        if (_aiReferenceState?.SandboxAdmissionFailure is { SourcePlacement: null }) return;
        if (!CanCaptureSandboxAdmissionFailure() || _aiReferenceState is not { } state)
            throw new NotSupportedException("Stopped Sandbox placement has no live capture owner to acknowledge it.");
        expected.Validate(_aiStack!, state);
        var intended = expected.ResolveTransform(GetParentNode3D()?.GlobalTransform ?? Transform3D.Identity,
            Skeleton.UnitsToMetres, Scale.X);
        FalloutActorSandboxAdmissionFailure.AcknowledgePlacement(state, expected, intended, GlobalTransform);
    }

    private void ReleaseDetachedSandboxAdmission()
    {
        if (_sandboxAdmissionStaging is not { } staging || _headReferenceState is not { } state || _headCapture is null) return;
        if (ReferenceEquals(state.CaptureHeadTracking, _headCapture))
        {
            state.CaptureHeadTracking = null;
            state.HeadTrackingRequired = staging.HeadTrackingRequired;
            state.HeadTrackingCaptureBlocker = staging.HeadTrackingCaptureBlocker;
        }
        Skeleton.Node.TreeExiting -= RetainHeadTracking;
        _headCapture = null;
    }

    private bool CanCaptureSandboxAdmissionFailure() => IsInsideTree() && !_bindingInitialBase &&
        (_sandboxAdmissionStaging is null || _sandboxAdmissionStaging.IsLive) &&
        _aiReferenceState is { } state && _sandboxAdmissionFailure is { } failure &&
        _aiError == failure.Message && _failedPackage == failure.Package &&
        _selectedSourcePackage == failure.Package && _sourceSelectionKnown &&
        _aiPackage?.FormKey == failure.Package && _sandboxPackage?.Form == failure.Package &&
        state.ProcedureCaptureBlocker == FalloutSandboxPackage.CaptureBlockerPrefix + failure.Message &&
        _packageEvents is { Active: { } active, Done: false, Error: null, LastEvent: "POBA" } lifecycle &&
        active.Form == failure.Package && lifecycle.LastPackage == failure.Package &&
        lifecycle.Revision is > 0 and < long.MaxValue &&
        _packageIdleSource is { Procedure: 12, Idles.Count: 0 } && _packageIdles is null &&
        _packageIdleError is null && _idleReplays.Remaining.Count == 0 &&
        _sandboxWander is null && state.PackageMotion is null &&
        state.PackageBindingFailure is null && state.DeferredPackageContinuation is null &&
        state.SelectionFailure is null && state.PendingPackageSelection is null && state.StoppedRetirement is null &&
        state.FurnitureContinuation is null && state.DialogueContinuation is null && state.PackageIdle is null &&
        _requestedSelection is null && _pendingPackage is null && _findFurniture is null && _seat is null &&
        _sitting == 0 && !_furnitureApproaching && !_travelActive && _travelProgress is null &&
        _escortPackage is null && _editorTravel is null && _nativeMarkerTravel is null &&
        _dialoguePackage is null && _guardPackage is null && _patrol is null &&
        _animation is null && _animationObjects.Count == 0 && !_responseIdleActive && AnimationError is null &&
        _animationSounds?.CanCaptureSilent != false && _conversationTarget is null &&
        !HasIndependentStoppedPose && !Activity.InCombat &&
        Combat is { SandboxAdmissionCaptureReady: true, AnimationWeapon: null } &&
        !_baseLocomotionMoving && _baseClock.Resource.Length != 0;

    private FalloutActorSandboxAdmissionFailure CaptureSandboxAdmissionFailure()
    {
        if (!CanCaptureSandboxAdmissionFailure())
            throw new NotSupportedException("Sandbox admission failure still has an independent continuation owner.");
        var package = _aiPackage!;
        var hash = FalloutActorFurnitureContinuation.RecordHash(package);
        var position = GlobalPosition;
        var basis = GlobalBasis;
        var lifecycle = _packageEvents!;
        var saved = new FalloutActorSandboxAdmissionFailure(package.FormKey, hash, _aiError!,
            [position.X, position.Y, position.Z], [basis.X.X, basis.X.Y, basis.X.Z,
                basis.Y.X, basis.Y.Y, basis.Y.Z, basis.Z.X, basis.Z.Y, basis.Z.Z],
            _aiRandom.State, Math.Max(0, _aiPollRemaining), _aiScheduleTime, _aiQuestRevision,
            _aiActivityRevision, Activity.Capture(), lifecycle.Revision, lifecycle.LastEvent!,
            lifecycle.LastPackage!.Value, hash, _blink?.Capture(), _aiReferenceState!.SandboxAdmissionFailure?.SourcePlacement?.Copy());
        saved.Validate(_aiStack!, _aiReferenceState!);
        return saved;
    }

    private void BindSandboxAdmissionCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCaptureSandboxAdmissionFailure = _sandboxAdmissionReady = CanCaptureSandboxAdmissionFailure;
        state.CaptureSandboxAdmissionFailure = _sandboxAdmissionCapture = CaptureSandboxAdmissionFailure;
    }

    private void RestoreSandboxAdmissionFailure(FalloutActorSandboxAdmissionFailure saved)
    {
        saved.Validate(_aiStack!, _aiReferenceState!);
        _sandboxAdmissionReferenceScale = Scale.X;
        // Detached construction has no final parent/global coordinate owner.
        // Resolve only after successful configuration and actual tree entry.
        _sandboxAdmissionStagedPose = saved.Copy();
        saved.RestoreActivity(_aiStack!, _aiReferenceState!, Activity);
        if (saved.Blink is { } blink)
            (_blink ?? throw new InvalidDataException("Saved Sandbox has no source blink owner.")).Restore(blink);
        else if (_blink is not null)
            throw new InvalidDataException("Saved Sandbox is missing its source blink continuation.");
        saved.RestoreEvents(_aiStack!, _aiReferenceState!, _packageEvents!);
        _aiRandom.Restore(saved.AiRandomState);
        _aiPackage = _aiStack!.GetEffective(saved.Package);
        _packageIdleSource = _packageEvents!.Active;
        _packageIdles = null;
        _sandboxPackage = FalloutSandboxPackage.Read(_aiPackage);
        _sandboxWander = null;
        _selectedSourcePackage = saved.Package; _sourceSelectionKnown = true;
        _failedPackage = saved.Package; _aiError = saved.Error;
        _sandboxAdmissionFailure = new(saved.Package, saved.Error);
        _aiPollRemaining = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime;
        _aiQuestRevision = saved.QuestRevision; _aiActivityRevision = saved.ActivityRevision;
        _aiReferenceState!.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + saved.Error;
    }

    private void RetainSandboxAdmissionFailure()
    {
        if (_aiReferenceState is not { } state) return;
        if (_sandboxAdmissionCapture is null || !ReferenceEquals(state.CaptureSandboxAdmissionFailure, _sandboxAdmissionCapture) ||
            !ReferenceEquals(state.CanCaptureSandboxAdmissionFailure, _sandboxAdmissionReady)) return;
        // A previous cold receipt cannot stand in for a refused current pose.
        // Invalidate before capture too: a capture exception must not leave a
        // stale fallback available after these callbacks are released.
        var relocation = state.SandboxAdmissionFailure?.SourcePlacement?.Copy();
        state.SandboxAdmissionFailure = null;
        if (CanCaptureSandboxAdmissionFailure())
        {
            var saved = CaptureSandboxAdmissionFailure();
            if (relocation is not null) saved = saved.Relocate(relocation);
            saved.Validate(_aiStack!, state);
            state.SandboxAdmissionFailure = saved.Copy();
        }
    }

    private void ClearSandboxAdmissionFailure()
    {
        _sandboxAdmissionFailure = null;
        if (_aiReferenceState is { } state) state.SandboxAdmissionFailure = null;
    }
}
