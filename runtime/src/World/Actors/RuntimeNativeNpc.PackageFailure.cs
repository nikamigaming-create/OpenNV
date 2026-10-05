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
        else _requestedSelection = new(package, declaration);
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
    }
}
