using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutSandboxNativeIdleContinuation? _sandboxNativeCold;
    private bool _sandboxNativeColdEntered;

    float IFalloutSandboxNativeActionConsumer.GameHour => (_aiClock ??
        throw new NotSupportedException("Sandbox creature has no living shared source GameHour owner.")).Hour;

    FalloutSandboxNativeIdleContinuation IFalloutSandboxNativeActionConsumer.Capture(FalloutSandboxCandidate selected)
        => CaptureSandboxNativeIdle(selected);

    private FalloutSandboxNativeIdleContinuation CaptureSandboxNativeIdle(FalloutSandboxCandidate selected)
    {
        RequireSandboxNativeSelection(selected);
        if (_sandboxNativeCold is not null || _sandboxNativeColdEntered || !_sandboxMarkerAtLocation || !_sandboxMarkerReserved ||
            _sandboxNativeRetiring || _sandboxNativeRetired || _sandboxMarkerReference != selected.Reference ||
            _sandboxMarkerCollection is not { } collection || Error is not null || _collectionFailure is not null || _aiError is not null ||
            _eventIdleClock is not null || _conversationTarget is not null || _sounds.CanCaptureSilent == false)
            throw new NotSupportedException("Sandbox creature child has an unreturned approach, response, audio or failed KF owner.");
        var marker = _aiWorld!.RequireSandboxPublication(selected.Reference!.Value);
        Combat!.RequireSandboxNativePublication();
        FalloutActorPackageIdleAnimation? active = null;
        if (_collectionClock is { } clock)
        {
            if (clock.Complete || _collectionAnimation is null || _collectionIdle is null || _collectionMediaSha256 is null)
                throw new NotSupportedException("Sandbox creature child has no complete selected KF owner.");
            active = new(_collectionIdle.Form, Hash(_aiRecords!.GetEffective(_collectionIdle.Form).ReadData()),
                _collectionIdle.AnimationPath, _collectionMediaSha256, clock.Capture(), _collectionRevision,
                CaptureCollectionResidualPose(_collectionAnimation));
        }
        var actor = _aiState!;
        var saved = new FalloutSandboxNativeIdleContinuation(actor.Reference, actor.Base,
            Hash(_aiRecords!.GetEffective(actor.Base).ReadData()), selected, marker.Cell, marker.Position.ToArray(),
            collection.Capture(), active, _collectionRevision, active is not null && Combat.PackageOwnsPose, actor.SoundRandom.State);
        saved.ValidateSource(_aiRecords!, _aiWorld!, actor.Reference); return saved;
    }

    void IFalloutSandboxNativeActionConsumer.Restore(FalloutSandboxNativeIdleContinuation saved)
    {
        if (_sandboxNativeAction is not null || _collectionClock is not null || _sandboxMarkerCollection is not null ||
            _eventIdleClock is not null || _conversationTarget is not null)
            throw new NotSupportedException("Sandbox creature cold child overlaps an actual native action or event pose.");
        saved.ValidateSource(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
        if (_aiState!.PackageCollection is not { } parent || parent.AnimationRevision != saved.AnimationRevision ||
            parent.IdleState.ActiveAnimation is not null || _aiState.SoundRandom.State != saved.RandomState)
            throw new InvalidDataException("Sandbox creature child differs from its actual restored replay/random/revision owner.");
        Combat!.RequireSandboxNativeRestoredPose();
        var marker = _aiWorld!.RequireSandboxPublication(saved.Selection.Reference!.Value);
        if (marker.Cell != saved.MarkerCell || !marker.Position.SequenceEqual(saved.MarkerPosition))
            throw new InvalidDataException("Sandbox creature cold marker publication differs from the retained source placement.");
        _sandboxNativeAction = saved.Selection; _sandboxMarkerReference = saved.Selection.Reference;
        if (!(_sandboxMarkerReserved = _aiWorld!.ReserveSandboxMarker(_sandboxMarkerReference!.Value, Appearance.Reference!.Value)))
            throw new NotSupportedException("Sandbox creature cold marker is owned by another actual source child.");
        var source = FalloutIdleCollection.Read(_aiRecords!.GetEffective(saved.Selection.Base!.Value));
        _sandboxMarkerCollection = new(source, _collectionReplays,
            idle => _collectionConditions!.AllPass(idle, PackageCondition), _aiState.SoundRandom.NextBounded);
        _sandboxMarkerCollection.Restore(saved.Collection);
        _sandboxMarkerAtLocation = true; _sandboxNativeRetired = _sandboxNativeRetiring = false;
        _collectionRevision = saved.AnimationRevision;
        if (saved.Animation is { } active) StartCollectionIdle(active.Idle, active, saved);
        if (_aiState.SoundRandom.State != saved.RandomState)
            throw new InvalidOperationException("Sandbox cold creature reconstruction consumed a new random draw.");
    }

    private void EnsureRestoredSandboxNativeIdle()
    {
        if (_sandboxNativeCold is not { } saved) return;
        if (_sandboxNativeColdEntered) throw new NotSupportedException("Sandbox creature retains an entered failed cold child publication.");
        _sandboxNativeColdEntered = true;
        try
        {
            (Combat?.SandboxActions ?? throw new NotSupportedException("Sandbox creature cold child lost its actual native consumer.")).Restore(saved);
            _sandboxNativeCold = null; _sandboxNativeColdEntered = false;
        }
        catch (Exception error)
        {
            _sandbox?.RetainFailure(error); _aiError ??= error.Message;
            _aiState!.ProcedureCaptureBlocker ??= "Sandbox creature cold publication failed: " + error.Message;
            throw;
        }
    }
}
