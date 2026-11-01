using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private const string FollowCaptureBlocker = "Follow lifecycle awaits its first native motion observation.";
    private FalloutFollowPackage? _followPackage;
    private NativeActorPackagePortal? _followPortal;
    private string? _followStatus;
    private string? _followOwnedBlocker;
    internal Func<FalloutFormKey, Node3D?>? ResolvePackageTarget { get; set; }
    private object? FollowState => _followPackage is not { } source ? null : new
    {
        source.Form,
        source.Target,
        source.Distance,
        source.Accompany,
        status = _followStatus,
        combatError = Combat?.FollowCombatError,
        door = _followPortal?.Door,
        portalRequested = _followPortal?.Requested,
        portalTransferred = _followPortal?.Transferred,
        portalWait = _followPortal?.Wait,
        owner = "resident-actor-source-package-native-root-motion",
        unbound = new[] { "cached-and-unloaded-target-process", "native-run-hysteresis-and-procedure-code" }
    };

    private void BeginFollow(FalloutPluginRecord package, bool initializing)
    {
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        var source = FalloutFollowContinuation.ReadResident(package).ResolveTarget(() => world.GetLinkedRef(actor));
        if (source.Target == actor) throw new NotSupportedException("Self-target follower needs its native termination policy.");
        NativeActorFollowTarget.ValidateReference(_aiStack!, source.Target);
        if (source.Accompany && source.DefensiveCombat && source.Target == _aiStack!.RuntimeFormKey(0x14))
            throw new NotSupportedException("Defensive Accompany of the player requires its player-hit assistance owner.");
        var state = world.Get(actor);
        var retained = initializing && state.PackageAssignment is { Done: false } assignment && assignment.Package == source.Form &&
            state.PackageMotion?.Package == source.Form ? state.PackageMotion : null;
        if (retained is not null) FalloutFollowContinuation.ValidateMotion(package, retained);
        _followPackage = source; _followPortal = null; _followStatus = retained is null ? "pending-native-observation" : "restored";
        _aiPackage = package; _packageIdles = null; _travelProgress?.Cancel();
        _followOwnedBlocker = retained is null ? FollowCaptureBlocker : null;
        state.ProcedureCaptureBlocker = _followOwnedBlocker;
        if (retained is null) _packageEvents!.Change(_packageIdleSource);
        else if (_packageEvents!.Active is null) _packageEvents.Restore(_packageIdleSource!, false);
        else if (_packageEvents.Active.Form != source.Form || _packageEvents.Done)
            throw new InvalidDataException("Restored follower lifecycle differs from its accepted unfinished package.");
        // POBA may request another package or fail. The physics owner checks
        // this accepted lifecycle again before issuing any movement.
        if (_packageEvents!.Error is { } error) throw new NotSupportedException(error);
    }

    private NativeActorPackagePortal FollowPortal => _followPortal ??= new(this, Combat!, _aiStack!, _aiWorld!,
        _aiReferenceState!, Skeleton.UnitsToMetres, () => _aiStack!.GetEffective(_followPackage!.Form),
        () => NativeActorFollowTarget.Cell(_aiStack!, _aiWorld!, Combat!, _followPackage!.Target),
        () => _aiError is null && _followPackage is { } source && source.Form == CurrentPackage &&
            _packageEvents?.Active?.Form == source.Form && _requestedSelection is null && _aiWorld!.IsEnabled(source.Target),
        PortalPlaybackReady, error =>
        {
            _aiError = error; _failedPackage = _aiPackage?.FormKey;
            _followOwnedBlocker = error;
            _aiReferenceState!.ProcedureCaptureBlocker = _followOwnedBlocker;
        });

    private void AdvanceFollow(double delta)
    {
        if (_followPackage is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null || _requestedSelection is not null ||
            source.Form != CurrentPackage || _packageEvents?.Active?.Form != source.Form) return;
        if (Combat.FollowCombatError is not null) { _followStatus = "unbound-defensive-combat"; return; }
        try
        {
            var world = _aiWorld ?? throw new NotSupportedException("Follower has no authoritative world owner.");
            var records = _aiStack ?? throw new NotSupportedException("Follower has no winning source owner.");
            if (!world.IsEnabled(source.Target)) { RefuseFollowWait("waiting-for-enabled-target"); return; }
            if (source.Target == records.RuntimeFormKey(0x14) && Combat.PackagePlayerCell is null)
            { RefuseFollowWait("waiting-for-player-cell-owner"); return; }
            var targetCell = NativeActorFollowTarget.Cell(records, world, Combat, source.Target);
            if (FollowPortal.Advance(targetCell, source.Running, false, delta))
            { _followStatus = "source-door-leg"; return; }
            var target = NativeActorFollowTarget.Node(records, world, Combat, source.Target, ResolvePackageTarget);
            var plan = target is null ? NativeActorFollowTarget.RetainedPlan(world, source, Skeleton.UnitsToMetres) :
                NativeActorFollowTarget.Plan(source, GlobalPosition, target, Skeleton.UnitsToMetres);
            if (target is null && plan is null) { RefuseFollowWait("waiting-for-qualified-target-process"); return; }
            if (plan is not { } movement) { RefuseFollowWait("waiting-for-leader-destination"); return; }
            Combat.AdvancePackageMotion(_aiPackage!, movement.Destination, movement.Distance, movement.Running,
                delta, requireArrivalHeight: true);
            _aiReferenceState!.ProcedureCaptureBlocker = null;
            _followOwnedBlocker = null;
            _followStatus = Combat.PackageMoving ? "following" : "waiting-within-source-distance";
            // Being within range is waiting, never package completion/POEA.
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = source.Form;
            _followOwnedBlocker = _aiError;
            _aiReferenceState!.ProcedureCaptureBlocker = _followOwnedBlocker;
            GD.PushError($"OPENNV_NATIVE_FOLLOW_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }

    private void RefuseFollowWait(string reason)
    {
        _followStatus = reason;
        _followOwnedBlocker = $"Follow has no qualified current waiting-motion continuation: {reason}.";
        _aiReferenceState!.ProcedureCaptureBlocker = _followOwnedBlocker;
    }

    private void ClearFollow()
    {
        if (_followPackage is null) return;
        Combat?.RetireFollowCombatPolicy(_followPackage.Form);
        _followPortal?.Retire();
        if (_aiReferenceState is { } state && _followOwnedBlocker is { } owned && state.ProcedureCaptureBlocker == owned)
            state.ProcedureCaptureBlocker = null;
        _followPackage = null; _followPortal = null; _followStatus = null; _followOwnedBlocker = null;
    }
}
