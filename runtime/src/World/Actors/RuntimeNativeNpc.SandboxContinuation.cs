using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutSandboxNativeIdleContinuation? _sandboxNativeCold;
    private bool _sandboxNativeColdEntered;

    float IFalloutSandboxNativeActionConsumer.GameHour => (_aiClock ??
        throw new NotSupportedException("Sandbox NPC has no living shared source GameHour owner.")).Hour;

    FalloutSandboxNativeIdleContinuation IFalloutSandboxNativeActionConsumer.Capture(FalloutSandboxCandidate selected)
        => CaptureSandboxNativeIdle(selected);

    private FalloutSandboxNativeIdleContinuation CaptureSandboxNativeIdle(FalloutSandboxCandidate selected)
    {
        RequireSandboxNativeSelection(selected);
        if (_sandboxNativeCold is not null || _sandboxNativeColdEntered || _sandboxFurnitureAction ||
            !_sandboxMarkerAtLocation || !_sandboxMarkerReserved || _sandboxNativeRetiring || _sandboxNativeRetired ||
            _sandboxMarkerReference != selected.Reference || _sandboxMarkerCollection is not { } collection ||
            AnimationError is not null || _packageIdleError is not null || _aiError is not null ||
            _responseIdleActive || _conversationTarget is not null || _animationObjects.Count != 0 ||
            Combat?.AnimationWeapon is not null || _animationSounds?.CanCaptureSilent == false)
            throw new NotSupportedException("Sandbox NPC child has an unreturned approach, furniture, dialogue, object, audio or failed KF owner.");
        var marker = _aiWorld!.RequireSandboxPublication(selected.Reference!.Value);
        Combat!.RequireSandboxNativePublication();
        FalloutActorPackageIdleAnimation? animation = null;
        if (_animation is not null)
        {
            if (_idleOwner != "sandbox-idle-marker" || _idleForm is null || _idlePlayback is not { Complete: false } ||
                _idleAnimationResource is null || _idleAnimationSha256 is null || _baseAnimation is null)
                throw new NotSupportedException("Sandbox NPC child has no complete source KF/residual owner.");
            var basis = Combat.PackageOwnsPose ? Combat.PackageBaseLayer ??
                throw new NotSupportedException("Sandbox NPC child lost its actual package base layer.") :
                ((RuntimeNativeNifAnimation Animation, float Seconds)?)null;
            animation = new(_idleForm.Value, FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(_idleForm.Value)),
                _idleAnimationResource, _idleAnimationSha256, _idlePlayback.Capture(), _idleRevision,
                CaptureFurnitureResidualPose(basis?.Animation));
        }
        var actor = _aiReferenceState!;
        var saved = new FalloutSandboxNativeIdleContinuation(actor.Reference, actor.Base,
            FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(actor.Base)), selected, marker.Cell,
            marker.Position.ToArray(), collection.Capture(), animation, _idleRevision,
            animation is not null && Combat.PackageOwnsPose, _aiRandom.State);
        saved.ValidateSource(_aiStack!, _aiWorld!, actor.Reference); return saved;
    }

    void IFalloutSandboxNativeActionConsumer.Restore(FalloutSandboxNativeIdleContinuation saved)
    {
        if (_sandboxNativeAction is not null || _animation is not null || _sandboxMarkerCollection is not null ||
            _sandboxFurnitureAction || _responseIdleActive || _conversationTarget is not null)
            throw new NotSupportedException("Sandbox NPC cold child overlaps a live native action or independent pose.");
        saved.ValidateSource(_aiStack!, _aiWorld!, Appearance.Reference!.Value);
        if (_aiReferenceState!.PackageCollection is not { } parent || parent.RandomState != saved.RandomState ||
            parent.AnimationRevision != saved.AnimationRevision || parent.IdleState.ActiveAnimation is not null ||
            _aiRandom.State != saved.RandomState)
            throw new InvalidDataException("Sandbox NPC IDLM cold child differs from its already restored parent replay/random owner.");
        Combat!.RequireSandboxNativeRestoredPose();
        var marker = _aiWorld!.RequireSandboxPublication(saved.Selection.Reference!.Value);
        if (marker.Cell != saved.MarkerCell || !marker.Position.SequenceEqual(saved.MarkerPosition))
            throw new InvalidDataException("Sandbox NPC cold native marker publication differs from its retained source placement.");
        _sandboxNativeAction = saved.Selection;
        _sandboxMarkerReference = saved.Selection.Reference;
        if (!(_sandboxMarkerReserved = _aiWorld!.ReserveSandboxMarker(_sandboxMarkerReference!.Value, Appearance.Reference!.Value)))
            throw new NotSupportedException("Sandbox NPC cold marker is owned by another actual source child.");
        var source = FalloutIdleCollection.Read(_aiStack!.GetEffective(saved.Selection.Base!.Value));
        _sandboxMarkerCollection = new(source, _idleReplays,
            idle => _idleConditions!.AllPass(idle, EvaluateAiCondition), _aiRandom.NextBounded);
        _sandboxMarkerCollection.Restore(saved.Collection);
        _sandboxMarkerAtLocation = true;
        _sandboxNativeRetired = _sandboxNativeRetiring = false;
        _idleRevision = saved.AnimationRevision;
        if (saved.Animation is { } active)
        {
            (RuntimeNativeNifAnimation Animation, float Seconds)? basis = saved.UsesPackageBase
                ? Combat.RestoredCollectionBase(_sandboxSource!.Form) : null;
            PlayIdle(_aiStack!, active.Idle, "sandbox-idle-marker", active, basis);
        }
        if (_aiRandom.State != saved.RandomState)
            throw new InvalidOperationException("Sandbox cold NPC reconstruction consumed a new random draw.");
    }

    private void EnsureRestoredSandboxNativeIdle()
    {
        if (_sandboxNativeCold is not { } saved) return;
        if (_sandboxNativeColdEntered) throw new NotSupportedException("Sandbox NPC retains an entered failed cold child publication.");
        _sandboxNativeColdEntered = true;
        try
        {
            (Combat?.SandboxActions ?? throw new NotSupportedException("Sandbox NPC cold child lost its actual native consumer.")).Restore(saved);
            _sandboxNativeCold = null; _sandboxNativeColdEntered = false;
        }
        catch (Exception error)
        {
            _sandbox?.RetainFailure(error); _aiError ??= error.Message;
            _aiReferenceState!.ProcedureCaptureBlocker ??= "Sandbox NPC cold publication failed: " + error.Message;
            throw;
        }
    }
}
