using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutSandboxPackage? _sandboxSource;
    private FalloutSandboxState? _sandbox;
    private PackageSelection? _restoredSandboxSelection;
    private Func<FalloutActorPackageCollectionContinuation?>? _sandboxCollectionCapture;
    private bool _sandboxReplayObserved;

    private void BindSandboxCollectionCapture()
    {
        if (_aiReferenceState is { } state)
            state.CapturePackageCollection = _sandboxCollectionCapture = CaptureSandboxCollection;
    }

    private void BeginSandbox(FalloutPluginRecord record, bool restoring)
    {
        var source = FalloutSandboxPackage.Read(record);
        var saved = restoring ? _aiReferenceState?.PackageMotion?.Sandbox : null;
        _sandboxReplayObserved = true;
        _sandboxSource = source; _sandbox = null; _aiPackage = record;
        if (saved is null)
        {
            _packageEvents!.Change(_packageIdleSource);
            Combat!.BeginSandboxObservation();
            _aiReferenceState!.PackageCollection = null;
        }
        else
        {
            _sandboxNativeCold = saved.NativeIdle?.Copy();
            if (_packageEvents!.Active?.Form != source.Form || _packageEvents.Done)
                throw new InvalidDataException("Cold Sandbox lost its actual continuous lifecycle.");
            var area = source.LocationType == 2 ? saved.Area : source.Resolve(_aiStack!, _aiWorld!, Appearance.Reference!.Value);
            _sandbox = new(source, FalloutActorFurnitureContinuation.RecordHash(record), area, Combat!.PackageRandom, saved);
            var election = saved.Election ?? throw new InvalidDataException("Cold Sandbox lost its election owner.");
            Activity.Restore(election.Activity);
            _aiPollRemaining = election.PollRemaining; _aiScheduleTime = election.ScheduleTime;
            _aiQuestRevision = election.QuestRevision; _aiActivityRevision = election.ActivityRevision ??
                throw new InvalidDataException("Cold NPC Sandbox lost its activity revision.");
            if (_aiReferenceState!.PackageCollection is { } collection)
            {
                collection.Validate(_aiStack!, source.Form);
                RestorePackageIdleState(collection.IdleState);
                _idleRevision = collection.AnimationRevision;
                _aiRandom.Restore(collection.RandomState ?? throw new InvalidDataException("Cold NPC collection lost its actual repeat/sound random owner."));
                if (collection.IdleState.ActiveAnimation is { } active)
                {
                    (RuntimeNativeNifAnimation Animation, float Seconds)? basis = collection.UsesPackageBase
                        ? Combat!.RestoredCollectionBase(source.Form) : null;
                    PlayIdle(_aiStack!, active.Idle, "package-idle", active, basis);
                }
            }
        }
    }

    private void AdvanceSandbox(double seconds)
    {
        if (Combat is null || Combat.OwnsPose || !Combat.PackageMovementReady || _conversationTarget is not null || _aiError is not null) return;
        try
        {
            EnsureRestoredSandboxNativeIdle();
            var source = _sandboxSource!;
            if (_sandbox is null)
            {
                var area = source.Resolve(_aiStack!, _aiWorld!, Appearance.Reference!.Value);
                if (source.LocationType == 2)
                {
                    var local = GetParent<Node3D>().ToLocal(GlobalPosition) / Skeleton.UnitsToMetres;
                    area = area with { Center = [local.X, -local.Z, local.Y] };
                }
                _sandbox = new(source, FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(source.Form)),
                    area, Combat.PackageRandom);
            }
            Combat.AdvanceSandbox(_aiPackage!, source, _sandbox, seconds);
        }
        catch (Exception error)
        {
            _sandbox?.RetainFailure(error); _aiError ??= error.Message; _failedPackage = _aiPackage?.FormKey;
            _aiReferenceState!.ProcedureCaptureBlocker ??= "Sandbox native suffix failed: " + error.Message;
            GD.PushError($"OPENNV_NATIVE_SANDBOX_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private FalloutActorPackageMotion CaptureSandboxMotion()
    {
        if (_sandbox is null || _requestedSelection is not null || _aiReferenceState!.PendingPackageChoice is not null ||
            _aiReferenceState.ScriptPackage?.Pending == true || _packageEvents is not { Error: null, Done: false } lifecycle)
            throw new NotSupportedException("Sandbox has no returned native area or has an entered package election/event.");
        return Combat!.CaptureSandboxMotion(_sandbox, new(_aiPollRemaining, _aiScheduleTime, _aiQuestRevision,
            _aiActivityRevision, false, Activity.Capture(), lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage));
    }

    private void RestoreSandboxLifecycleBeforeSelection()
    {
        if (_aiReferenceState is not { PackageMotion: { Sandbox: { } saved } motion, PackageAssignment: { } assignment } state ||
            motion.Package != assignment.Package) return;
        if (state.ScriptPackage?.Pending == true || state.PendingPackageChoice is not null || state.PendingPackageSelection is not null || assignment.Done)
            throw new NotSupportedException("Cold continuous Sandbox has another election or a completed lifecycle.");
        var record = _aiStack!.GetEffective(assignment.Package);
        _ = FalloutSandboxPackage.Read(record); saved.Validate();
        if (_packageEvents!.Active is null) assignment.Bind(_aiStack, _packageEvents);
        if (_packageEvents.Active?.Form != assignment.Package || _packageEvents.Done)
            throw new InvalidDataException("Cold Sandbox differs from its original lifecycle.");
        (saved.Election ?? throw new InvalidDataException("Cold Sandbox lost its actual election clock.")).RestoreHistory(_packageEvents);
        _restoredSandboxSelection = new(record, FalloutScriptPackage.Read(record), assignment.ScriptPackageRevision,
            state.ScriptPackage?.Package == record.FormKey);
    }

    private FalloutActorPackageCollectionContinuation? CaptureSandboxCollection()
    {
        var nativeChild = _sandboxNativeAction is not null && !_sandboxNativeRetired;
        if (nativeChild) _ = CaptureSandboxNativeIdle(_sandboxNativeAction!);
        if (_sandboxSource is null)
        {
            if (_sandboxReplayObserved && _idleReplays.Remaining.Count != 0)
                throw new NotSupportedException("Retired Sandbox replay delays require their independent actor-wide cold owner.");
            return _aiReferenceState?.PackageCollection?.Copy();
        }
        if (_packageIdleSource is null || _packageIdles is null || _packageIdleSource.Form != _sandboxSource.Form ||
            _packageIdleError is not null || AnimationError is not null || _responseIdleActive)
            throw new NotSupportedException("Sandbox collection has an unowned selection, response or failed animation suffix.");
        FalloutActorPackageIdleAnimation? active = null;
        if (_animation is not null && !nativeChild)
        {
            if (_idleOwner != "package-idle" || _idleForm is null || _idlePlayback is not { Complete: false } ||
                _idleAnimationResource is null || _idleAnimationSha256 is null || _baseAnimation is null ||
                _animationObjects.Count != 0 || Combat?.AnimationWeapon is not null || _animationSounds?.CanCaptureSilent == false)
                throw new NotSupportedException("Sandbox active collection has an independent overlay/object/sound continuation.");
            var basis = Combat?.PackageOwnsPose == true ? Combat!.PackageBaseLayer ??
                throw new NotSupportedException("Sandbox collection lost its actually published package base layer.") :
                ((RuntimeNativeNifAnimation Animation, float Seconds)?)null;
            active = new(_idleForm.Value, FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(_idleForm.Value)),
                _idleAnimationResource, _idleAnimationSha256, _idlePlayback.Capture(), _idleRevision,
                CaptureFurnitureResidualPose(basis?.Animation));
        }
        var captured = CapturePackageIdleState() with { ActiveAnimation = active };
        var result = new FalloutActorPackageCollectionContinuation(captured, _idleRevision, RandomState: _aiRandom.State,
            UsesPackageBase: active is not null && Combat?.PackageOwnsPose == true);
        result.Validate(_aiStack!, _sandboxSource.Form);
        return result;
    }

    private void RetainSandboxCollection()
    {
        if (_aiReferenceState is not { } state || !ReferenceEquals(state.CapturePackageCollection, _sandboxCollectionCapture)) return;
        try { state.PackageCollection = CaptureSandboxCollection(); }
        catch (Exception error)
        {
            state.ProcedureCaptureBlocker ??= "Sandbox collection eviction lacks continuation: " + error.Message;
            GD.PushError($"OPENNV_NATIVE_SANDBOX_COLLECTION_RETIREMENT_REFUSED reference={state.Reference}: {error.Message}");
        }
        finally { state.CapturePackageCollection = null; }
    }

    private bool ClearSandbox()
    {
        if (_sandboxSource is null) return true;
        if (!Combat!.RetireSandbox(_sandbox)) return false;
        _sandbox = null; _sandboxSource = null;
        _sandboxNativeCold = null; _sandboxNativeColdEntered = false;
        _aiReferenceState!.PackageCollection = null;
        return true;
    }
}
