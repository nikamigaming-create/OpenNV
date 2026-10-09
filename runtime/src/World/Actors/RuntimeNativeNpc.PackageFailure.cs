using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Func<bool>? _bindingFailureReady;
    private Func<FalloutActorPackageBindingFailure>? _bindingFailureCapture;
    private Func<IReadOnlyList<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>?>? _bindingFailureWait;
    private Func<FalloutActorPackageBindingCaptureDiagnostic>? _bindingFailureDiagnostic;
    private bool _baseLocomotionMoving;

    private bool CanCaptureBindingFailure() => CanCaptureBindingFailure(allowFiniteSoundWait: false);
    private bool CanCaptureBindingFailure(bool allowFiniteSoundWait) => _aiReferenceState is { } state &&
        state.ProcedureCaptureBlocker == _selectionCaptureBlocker && _selectionCaptureBlocker is not null &&
        _aiError is not null && _failedPackage is { } failed && failed == _selectedSourcePackage &&
        _requestedSelection is null && _pendingPackage is null && _aiPackage is null &&
        _packageEvents is { Active: null, Done: false, Error: null } &&
        _packageIdleSource is not null && _packageIdles is not null &&
        _sitting == 0 && !_furnitureApproaching && !_travelActive && _travelProgress?.ArrivalPending != true &&
        _escortPackage is null && _editorTravel is null && _dialoguePackage is null && _patrol is null &&
        (_animation is null || CanCaptureStoppedIndependentIdle(allowFiniteSoundWait)) && !_responseIdleActive && AnimationError is null &&
        _conversationTarget is null && CanCaptureStoppedAiPose(allowFiniteSoundWait) &&
        _baseClock.Resource.Length != 0;

    private FalloutActorPackageBindingCaptureDiagnostic ReadBindingCaptureDiagnostic(bool retired = false)
    {
        var blockers = new List<FalloutActorCaptureBlocker>();
        void Refuse(bool condition, string predicate, string? detail = null)
        {
            if (condition) blockers.Add(new("npc-binding", predicate, detail));
        }
        Refuse(_aiReferenceState is null, "reference-instance-missing");
        Refuse(_selectionCaptureBlocker is null, "binding-blocker-missing");
        Refuse(_aiReferenceState?.ProcedureCaptureBlocker != _selectionCaptureBlocker, "procedure-blocker-binding");
        Refuse(_aiError is null, "binding-error-missing");
        Refuse(_failedPackage is null || _failedPackage != _selectedSourcePackage, "failed-source-package-binding");
        Refuse(_requestedSelection is not null, "requested-selection");
        Refuse(_pendingPackage is not null, "pending-package");
        Refuse(_aiPackage is not null, "active-package");
        Refuse(_packageEvents is null, "package-events-missing");
        if (_packageEvents is { } events)
        {
            Refuse(events.Active is not null, "active-package-event");
            Refuse(events.Done, "completed-package-event");
            Refuse(events.Error is not null, "package-event-error", events.Error);
        }
        Refuse(_packageIdleSource is null || _packageIdles is null, "idle-collection-missing");
        Refuse(_sitting != 0, "sitting");
        Refuse(_furnitureApproaching, "furniture-approach");
        Refuse(_travelActive || _travelProgress?.ArrivalPending == true, "travel-continuation");
        Refuse(_escortPackage is not null || _editorTravel is not null || _dialoguePackage is not null || _patrol is not null,
            "independent-package-continuation");
        Refuse(_animation is not null && !CanCaptureStoppedIndependentIdle(), "independent-idle",
            $"owner={_idleOwner ?? "none"} complete={_idlePlayback?.Complete} objects={_animationObjects.Count} weapon={Combat?.AnimationWeapon is not null} finiteReady={CanCaptureStoppedIndependentIdle(allowFiniteSoundWait: true)} " +
            $"baseMoving={_baseLocomotionMoving} independentPose={HasIndependentStoppedPose} combatPose={Combat?.OwnsPose == true} baseAnimation={_baseAnimation is not null} idle={_idleForm} data={_idleData is not null} revision={_idleRevision} " +
            $"resource={_idleAnimationResource} hash={_idleAnimationSha256 is not null} silent={_animationSounds?.CanCaptureSilent} finite={_animationSounds?.CanAwaitFiniteCompletion}");
        Refuse(_responseIdleActive, "response-idle");
        Refuse(AnimationError is not null, "animation-error", AnimationError);
        Refuse(_conversationTarget is not null, "conversation-target");
        Refuse(!CanCaptureStoppedAiPose(), "independent-pose");
        Refuse(_baseClock.Resource.Length == 0, "base-clock-missing");
        return new(_aiReferenceState?.Reference ?? Appearance.Reference!.Value, GetInstanceId(),
            CanCaptureBindingFailure(), CanCaptureBindingFailure(allowFiniteSoundWait: true), retired,
            _failedPackage, blockers.AsReadOnly(), ReadStoppedPoseCaptureDiagnostic());
    }

    private FalloutActorPackageBindingFailure CaptureBindingFailure() => CaptureBindingFailure(allowFiniteSoundWait: false);
    private FalloutActorPackageBindingFailure CaptureBindingFailure(bool allowFiniteSoundWait)
    {
        if (!CanCaptureBindingFailure(allowFiniteSoundWait))
            throw new NotSupportedException("Actor package failure still has an active continuation owner.");
        var package = _aiStack!.GetEffective(_failedPackage!.Value);
        var position = GlobalPosition;
        var basis = GlobalBasis;
        var failure = new FalloutActorPackageBindingFailure(package.FormKey,
            Convert.ToHexString(SHA256.HashData(package.ReadData())), _aiError!,
            [position.X, position.Y, position.Z], [basis.X.X, basis.X.Y, basis.X.Z,
                basis.Y.X, basis.Y.Y, basis.Y.Z, basis.Z.X, basis.Z.Y, basis.Z.Z],
            _baseLocomotionMoving, _aiRandom.State, Math.Max(0, _aiPollRemaining), _aiScheduleTime,
            FalloutPackageRetirement.Capture(_aiStack, _packageEvents!), _blink?.Capture(), CapturePackageIdleState(),
            _animation is null ? null : CaptureStoppedIndependentIdle(allowFiniteSoundWait));
        failure.Validate(_aiStack, _aiReferenceState!);
        return failure;
    }

    private IReadOnlyList<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>? BindingFailureFiniteSoundWait() =>
        CanCaptureBindingFailure(allowFiniteSoundWait: true) && _aiReferenceState is { } state
            ? FalloutActorRetirementCandidate.ReadLiveFiniteReceipts(_aiStack!, state, GetInstanceId()) : null;

    private void BindFailureCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.StoppedRetirement = null;
        state.CanCapturePackageBindingFailure = _bindingFailureReady = CanCaptureBindingFailure;
        state.CapturePackageBindingFailure = _bindingFailureCapture = CaptureBindingFailure;
        state.PendingPackageBindingFiniteVoices = _bindingFailureWait = BindingFailureFiniteSoundWait;
        state.RetiredPackageBindingCaptureDiagnostic = null;
        state.ObservePackageBindingCapture = _bindingFailureDiagnostic = () => ReadBindingCaptureDiagnostic();
    }

    private void RestoreBindingFailure(FalloutActorPackageBindingFailure failure)
    {
        failure.Validate(_aiStack!, _aiReferenceState!);
        _packageEvents!.RestoreRetirement(failure.Retirement);
        _aiRandom.Restore(failure.AiRandomState);
        if (failure.Blink is { } blink)
            (_blink ?? throw new NotSupportedException("Saved blink queue has no source face owner.")).Restore(blink);
        else if (_blink is not null)
            throw new NotSupportedException("Saved stopped actor has no blink continuation for its source face.");
        var package = _aiStack!.GetEffective(failure.Package);
        if (failure.IdleState is { } idles)
        {
            _idleReplays.Restore(idles.Cooldowns.ToDictionary(value => value.Idle, value => value.Remaining));
            _packageIdleError = idles.Error;
        }
        // Retry only the retained pre-begin binding. Source selection and its
        // random predicates were already consumed before this save.
        var declaration = FalloutScriptPackage.Read(package);
        if (HasIndependentStoppedPose)
        {
            _packageIdleSource = declaration;
            _packageIdles = new(declaration, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
            if (failure.IdleState is { } state) _packageIdles.Restore(state.Collection);
            _aiError = failure.Error; _failedPackage = failure.Package;
            _aiPollRemaining = failure.PollRemaining; _aiScheduleTime = failure.ScheduleTime;
            _aiQuestRevision = _questState!.Revision; _aiActivityRevision = Activity.Revision;
        }
        else _requestedSelection = new(package, declaration, ScriptPackageRevision,
            _aiReferenceState?.ScriptPackage?.Package == package.FormKey);
        _selectedSourcePackage = package.FormKey; _sourceSelectionKnown = true;
        _selectionCaptureBlocker = _aiReferenceState!.ProcedureCaptureBlocker;
        RestoreBindingFailurePose(failure);
    }

    private void RestoreBindingFailurePose(FalloutActorPackageBindingFailure failure) =>
        GlobalTransform = new(new Basis(new Vector3(failure.Basis[0], failure.Basis[1], failure.Basis[2]),
            new Vector3(failure.Basis[3], failure.Basis[4], failure.Basis[5]),
            new Vector3(failure.Basis[6], failure.Basis[7], failure.Basis[8])),
            new(failure.Position[0], failure.Position[1], failure.Position[2]));

    private void ClearBindingFailure() => _aiReferenceState!.PackageBindingFailure = null;

    private void RetainBindingFailure()
    {
        if (_aiReferenceState is not { } state) return;
        var authoritative = ReferenceEquals(state.CanCapturePackageBindingFailure, _bindingFailureReady) &&
            ReferenceEquals(state.CapturePackageBindingFailure, _bindingFailureCapture);
        if (authoritative) state.RetiredPackageBindingCaptureDiagnostic = ReadBindingCaptureDiagnostic(retired: true);
        if (authoritative && CanCaptureBindingFailure())
        {
            state.PackageBindingFailure = CaptureBindingFailure();
            state.ProcedureCaptureBlocker = state.PackageBindingFailure.Error;
        }
        else if (authoritative && CanCaptureBindingFailure(allowFiniteSoundWait: true))
        {
            FalloutActorRetirementCandidate.Prepare(_aiStack!, state, GetInstanceId(), null,
                CaptureBindingFailure(allowFiniteSoundWait: true))?.Bind();
        }
        if (ReferenceEquals(state.CanCapturePackageBindingFailure, _bindingFailureReady))
            state.CanCapturePackageBindingFailure = null;
        if (ReferenceEquals(state.PendingPackageBindingFiniteVoices, _bindingFailureWait))
            state.PendingPackageBindingFiniteVoices = null;
        if (ReferenceEquals(state.CapturePackageBindingFailure, _bindingFailureCapture))
            state.CapturePackageBindingFailure = null;
        if (ReferenceEquals(state.ObservePackageBindingCapture, _bindingFailureDiagnostic))
            state.ObservePackageBindingCapture = null;
    }
}
