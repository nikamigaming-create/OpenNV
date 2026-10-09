using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature : IFalloutSandboxNativeActionConsumer
{
    private FalloutSandboxCandidate? _sandboxNativeAction;
    private FalloutIdleCollectionPlayback? _sandboxMarkerCollection;
    private FalloutFormKey? _sandboxMarkerReference;
    private bool _sandboxMarkerAtLocation, _sandboxMarkerReserved, _sandboxNativeRetiring, _sandboxNativeRetired;

    void IFalloutSandboxNativeActionConsumer.Enter(FalloutSandboxCandidate selected)
    {
        if (_sandboxNativeAction is not null && !_sandboxNativeRetired)
            throw new InvalidOperationException("Sandbox creature action overlaps an unretired native child.");
        selected.Validate();
        _sandboxNativeAction = selected; _sandboxNativeRetired = false; _sandboxNativeRetiring = false;
        if (selected.Action != (int)FalloutSandboxAction.IdleMarker)
            throw new NotSupportedException("Sandbox creature selected furniture/sleep/eat/wander/dialogue requires its actual source action child.");
        if (_eventIdleClock is not null || _conversationTarget is not null)
            throw new NotSupportedException("Sandbox creature marker overlaps an actual event/dialogue pose.");
        var marker = selected.Reference!.Value;
        _ = _aiWorld!.RequireSandboxPublication(marker);
        var source = FalloutIdleCollection.Read(_aiRecords!.GetEffective(_aiWorld.Get(marker).Base));
        _sandboxMarkerReference = marker;
        if (!(_sandboxMarkerReserved = _aiWorld.ReserveSandboxMarker(marker, Appearance.Reference!.Value)))
            throw new NotSupportedException("Sandbox creature IDLM is occupied by another actual source child.");
        RetireCurrentCollectionPose();
        _sandboxMarkerCollection = new(source, _collectionReplays,
            idle => _collectionConditions!.AllPass(idle, PackageCondition), _aiState!.SoundRandom.NextBounded);
        _sandboxMarkerAtLocation = false;
    }

    void IFalloutSandboxNativeActionConsumer.Advance(double seconds)
    {
        if (_sandboxNativeAction is null || _sandboxNativeRetired) return;
        if (Error is not null || _collectionFailure is not null)
            throw new InvalidOperationException("Sandbox creature child retains its actual KF failure: " + (Error ?? _collectionFailure));
        if (_sandboxMarkerReference is { } marker && !_sandboxMarkerAtLocation && !_sandboxNativeRetiring)
            _sandboxMarkerAtLocation = Combat!.AdvanceSandboxMarkerApproach(_aiPackage!, marker, seconds);
        else if (_sandboxMarkerReference is { } current && !_sandboxNativeRetiring)
            _ = _aiWorld!.RequireSandboxPublication(current);
    }

    void IFalloutSandboxNativeActionConsumer.RequestRetirement(FalloutSandboxCandidate selected)
    {
        RequireSandboxNativeSelection(selected);
        if (_sandboxNativeRetiring) throw new InvalidOperationException("Sandbox creature native retirement was already requested.");
        _sandboxNativeRetiring = true;
        RetireCurrentCollectionPose();
        _sandboxMarkerCollection = null; _sandboxMarkerAtLocation = false;
        if (_sandboxMarkerReserved && _sandboxMarkerReference is { } marker)
            _aiWorld!.ReleaseSandboxMarker(marker, Appearance.Reference!.Value);
        _sandboxMarkerReserved = false; _sandboxMarkerReference = null;
        _sandboxNativeRetired = true;
    }

    bool IFalloutSandboxNativeActionConsumer.ObserveRetired(FalloutSandboxCandidate selected)
    {
        RequireSandboxNativeSelection(selected);
        if (Error is not null || _aiError is not null || _collectionFailure is not null)
            throw new InvalidOperationException("Sandbox creature native child retains its actual failed suffix: " + (Error ?? _aiError ?? _collectionFailure));
        return _sandboxNativeRetired;
    }

    private void RequireSandboxNativeSelection(FalloutSandboxCandidate selected)
    {
        if (_sandboxNativeAction != selected) throw new InvalidDataException("Sandbox creature native selection changed source identity.");
    }

    private void RetireCurrentCollectionPose()
    {
        _collectionAnimation = null; _collectionClock = null; _collectionIdle = null; _collectionMediaSha256 = null;
        var basis = CollectionBaseLayer();
        basis.Animation.ApplySourceTime(basis.Seconds);
        // Pose retirement does not finish IDLC or erase actor-wide replay/RNG.
    }

    private void RetireSandboxNativeActionOnExit()
    {
        if (_sandboxNativeAction is null || _sandboxNativeRetired) return;
        _aiState!.ProcedureCaptureBlocker ??= "Sandbox creature action eviction has no cold child continuation.";
        try
        {
            if (_sandboxMarkerReserved && _sandboxMarkerReference is { } marker)
                _aiWorld!.ReleaseSandboxMarker(marker, Appearance.Reference!.Value);
            _sandboxMarkerReserved = false;
            // The actual node and its animation channels are retiring. Do not
            // mint a source return/Done receipt or clear the retained failure.
        }
        catch (Exception failure)
        {
            _sandbox?.RetainFailure(failure);
            GD.PushError("OPENNV_SANDBOX_CREATURE_EVICTION_REFUSED: " + failure.Message);
        }
    }
}
