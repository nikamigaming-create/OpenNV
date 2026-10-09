using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc : IFalloutSandboxNativeActionConsumer
{
    private FalloutSandboxCandidate? _sandboxNativeAction;
    private FalloutIdleCollectionPlayback? _sandboxMarkerCollection;
    private FalloutFormKey? _sandboxMarkerReference;
    private bool _sandboxMarkerAtLocation, _sandboxMarkerReserved, _sandboxNativeRetiring, _sandboxNativeRetired;
    private bool _sandboxFurnitureAction;

    void IFalloutSandboxNativeActionConsumer.Enter(FalloutSandboxCandidate selected)
    {
        if (_sandboxNativeAction is not null && !_sandboxNativeRetired)
            throw new InvalidOperationException("Sandbox native action overlaps an unretired child.");
        selected.Validate();
        _sandboxNativeAction = selected; _sandboxNativeRetired = false; _sandboxNativeRetiring = false;
        if (_responseIdleActive || _conversationTarget is not null)
            throw new NotSupportedException("Sandbox native action overlaps an actual dialogue pose.");
        switch ((FalloutSandboxAction)selected.Action)
        {
            case FalloutSandboxAction.IdleMarker:
                var marker = selected.Reference!.Value;
                _ = _aiWorld!.RequireSandboxPublication(marker);
                var declaration = FalloutIdleCollection.Read(_aiStack!.GetEffective(_aiWorld.Get(marker).Base));
                _sandboxMarkerReference = marker;
                if (!(_sandboxMarkerReserved = _aiWorld.ReserveSandboxMarker(marker, Appearance.Reference!.Value)))
                    throw new NotSupportedException("Sandbox IDLM is occupied by another actual source child.");
                CancelIdle(); // Retire actual overlay/ANIO; preserve replay delays and RNG.
                _sandboxMarkerCollection = new(declaration, _idleReplays,
                    idle => _idleConditions!.AllPass(idle, EvaluateAiCondition), _aiRandom.NextBounded);
                _sandboxMarkerAtLocation = false;
                break;
            case FalloutSandboxAction.Furniture:
                EnterSandboxFurniture(selected.Reference!.Value);
                break;
            case FalloutSandboxAction.Sleeping:
                throw new NotSupportedException("Sandbox sleeping furniture requires its actual NPC sleep transition/rest child.");
            case FalloutSandboxAction.Eating:
                throw new NotSupportedException("Sandbox eating requires its original item transfer/consumption and source eating animation child.");
            case FalloutSandboxAction.Wandering:
                throw new NotSupportedException("Sandbox wandering requires its selected source destination/route child.");
            case FalloutSandboxAction.Dialogue:
                throw new NotSupportedException("Sandbox conversation requires its directed source participant/actor-process child.");
            default:
                throw new InvalidDataException("Sandbox native action class is invalid.");
        }
    }

    void IFalloutSandboxNativeActionConsumer.Advance(double seconds)
    {
        if (_sandboxNativeAction is null || _sandboxNativeRetired) return;
        if (AnimationError is not null || _packageIdleError is not null)
            throw new InvalidOperationException("Sandbox native action retains its actual KF failure: " + (AnimationError ?? _packageIdleError));
        if (_sandboxFurnitureAction)
        {
            _ = _aiWorld!.RequireSandboxPublication(_sandboxNativeAction.Reference!.Value);
            if (_furnitureApproaching) AdvanceFurnitureApproach(seconds);
            AdvanceSandboxFurnitureRetirement();
            return;
        }
        if (_sandboxMarkerReference is { } marker && !_sandboxMarkerAtLocation && !_sandboxNativeRetiring)
        {
            if (!Combat!.AdvanceSandboxMarkerApproach(_aiPackage!, marker, seconds)) return;
            _sandboxMarkerAtLocation = true;
            PlayLocomotion(false);
        }
        else if (_sandboxMarkerReference is { } current && !_sandboxNativeRetiring)
            _ = _aiWorld!.RequireSandboxPublication(current);
    }

    void IFalloutSandboxNativeActionConsumer.RequestRetirement(FalloutSandboxCandidate selected)
    {
        RequireSandboxNativeSelection(selected);
        if (_sandboxNativeRetiring) throw new InvalidOperationException("Sandbox native child retirement was already requested.");
        _sandboxNativeRetiring = true;
        if (_sandboxFurnitureAction) { AdvanceSandboxFurnitureRetirement(); return; }
        RetireSandboxMarkerPose();
        _sandboxNativeRetired = true;
    }

    bool IFalloutSandboxNativeActionConsumer.ObserveRetired(FalloutSandboxCandidate selected)
    {
        RequireSandboxNativeSelection(selected);
        if (AnimationError is not null || _aiError is not null)
            throw new InvalidOperationException("Sandbox native child has an actual failed suffix: " + (AnimationError ?? _aiError));
        return _sandboxNativeRetired;
    }

    private void RequireSandboxNativeSelection(FalloutSandboxCandidate selected)
    {
        if (_sandboxNativeAction != selected) throw new InvalidDataException("Sandbox native child changed its retained selected source identity.");
    }

    private double PrepareSandboxMarkerIdle(double seconds)
    {
        if (_sandboxMarkerCollection is not { } marker || _sandboxNativeRetiring || !_sandboxMarkerAtLocation ||
            _animation is not null || AnimationError is not null || _packageIdleError is not null) return seconds;
        var remaining = marker.AdvanceWait(seconds);
        try
        {
            if (marker.Select() is { } idle) PlayIdle(_aiStack!, idle, "sandbox-idle-marker");
        }
        catch (Exception failure)
        {
            _sandbox?.RetainFailure(failure); _packageIdleError ??= failure.Message;
            throw;
        }
        return remaining;
    }

    private void RetireSandboxMarkerPose()
    {
        if (_animation is not null && _idleOwner != "sandbox-idle-marker")
            throw new NotSupportedException("Sandbox marker retirement overlaps an independent actual pose.");
        CancelIdle();
        _sandboxMarkerCollection = null; _sandboxMarkerAtLocation = false;
        if (_sandboxMarkerReserved && _sandboxMarkerReference is { } marker)
            _aiWorld!.ReleaseSandboxMarker(marker, Appearance.Reference!.Value);
        _sandboxMarkerReserved = false; _sandboxMarkerReference = null;
    }

    private void RetireSandboxNativeActionOnExit()
    {
        if (_sandboxNativeAction is null || _sandboxNativeRetired) return;
        var retained = _aiReferenceState!.PackageMotion?.Sandbox?.NativeIdle;
        if (retained is null || retained.Selection != _sandboxNativeAction)
            _aiReferenceState.ProcedureCaptureBlocker ??= "Sandbox native action eviction has no cold child continuation.";
        try
        {
            if (_sandboxMarkerReference is not null) RetireSandboxMarkerPose();
            // Furniture's actual shared ExitTree owner releases its reservation.
            // Eviction is not package Done or an authored action-return receipt.
        }
        catch (Exception failure)
        {
            _sandbox?.RetainFailure(failure);
            GD.PushError("OPENNV_SANDBOX_ACTION_EVICTION_REFUSED: " + failure.Message);
        }
    }
}
