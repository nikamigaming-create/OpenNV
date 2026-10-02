using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutEscortPackage? _escortPackage;
    private FalloutEscortProgress? _escortProgress;
    private (Vector3 Authored, Vector3 Floor)? _escortDestination;
    private string? _escortStatus;
    private float? _escortTargetDistance, _escortRemainingDistance;
    private object? EscortState => _escortPackage is not { } source ? null : new
    {
        package = source.Form.ToString(),
        target = source.Target.ToString(),
        destination = source.Destination.ToString(),
        source.Distance,
        source.DestinationRadius,
        progress = _escortProgress,
        status = _escortStatus,
        targetDistanceGameUnits = _escortTargetDistance,
        remainingDistanceGameUnits = _escortRemainingDistance,
        projectedEndpoint = _escortDestination is { } destination ? new[] { destination.Floor.X, destination.Floor.Y, destination.Floor.Z } : null,
        owner = "source-escort-native-capsule-and-kf",
        unbound = new[] { "non-player-target-follow-assignment", "object-search-pickup-drop", "combat-assistance", "retail-timing" }
    };

    private void BeginEscort(FalloutPluginRecord package, bool initializing)
    {
        var source = FalloutEscortPackage.Read(package);
        if (source.Target != _aiStack!.RuntimeFormKey(0x14))
        {
            var target = _aiStack.GetEffective(source.Target);
            throw new NotSupportedException(target.Signature is "ACHR" or "ACRE"
                ? "Escort of a non-player actor requires its temporary target Follow assignment owner."
                : "Escort of an inventory object requires its search, pickup and drop owner.");
        }
        var destination = _aiCell!.References.SingleOrDefault(value => value.FormKey == source.Destination) ??
            throw new NotSupportedException("Escort destination requires its other-cell route owner.");
        if (!FalloutNewVegasBuiltinForms.IsInternalStatic(_aiCell.BaseObjects[destination.Base].Signature,
            _aiStack.RuntimeFormId(destination.Base)))
            throw new NotSupportedException("Escort destination requires its non-marker interaction owner.");
        var retained = initializing ? _aiWorld?.Get(Appearance.Reference!.Value).PackageMotion : null;
        var restored = retained?.Package == package.FormKey ? retained.Escort : null;
        restored?.Validate();
        _escortPackage = source; _escortProgress = restored ?? new(false, false); _escortDestination = null;
        _escortStatus = restored is null ? "pending-target-observation" : "restored";
        _escortTargetDistance = _escortRemainingDistance = null;
        _aiPackage = package;
        _travelProgress?.Cancel();
        if (_aiWorld is { } world)
            world.Get(Appearance.Reference!.Value).ProcedureCaptureBlocker = restored is null
                ? "Escort lifecycle has started but its native motion snapshot is not initialized." : null;
        if (restored is null) _packageEvents!.Change(_packageIdleSource);
        else _packageEvents!.Restore(_packageIdleSource!, restored.Complete);
    }

    private void AdvanceEscort(double delta)
    {
        if (_escortPackage is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            var player = Combat.PackagePlayer;
            if (player is null || !player.IsInsideTree() || !player.CollisionResident ||
                Combat.PackagePlayerCell is { } playerCell && playerCell != _aiCell!.Cell.FormKey)
            { _escortStatus = "waiting-for-target-residency"; return; }
            var reference = _aiCell!.References.SingleOrDefault(value => value.FormKey == source.Destination);
            if (reference is null) { _escortStatus = "waiting-for-destination-residency"; return; }
            var local = _referenceTransform!(reference);
            var authored = GetParent<Node3D>().ToGlobal(local.Origin);
            if (_escortDestination is not { } previous || !previous.Authored.IsEqualApprox(authored))
                _escortDestination = (authored, Combat.ProjectPackageDestination(authored));
            var destination = _escortDestination.Value.Floor;
            var units = Skeleton.UnitsToMetres;
            var radius = Math.Max(source.DestinationRadius * units, .25f);
            _escortTargetDistance = GlobalPosition.DistanceTo(player.GlobalPosition) / units;
            _escortRemainingDistance = GlobalPosition.DistanceTo(destination) / units;
            var reached = IsOnFloor() && GlobalPosition.DistanceTo(destination) <= radius + .1f;
            var (progress, action) = source.Advance(_escortProgress!, _escortTargetDistance.Value,
                _escortRemainingDistance.Value, player.GlobalPosition.DistanceTo(destination) / units, reached);
            var target = action switch
            {
                FalloutEscortAction.ApproachTarget => player.GlobalPosition,
                FalloutEscortAction.Lead => destination,
                _ => GlobalPosition
            };
            var stop = action == FalloutEscortAction.ApproachTarget ? source.Distance * units : action == FalloutEscortAction.Lead ? radius : 0;
            Combat.AdvancePackageMotion(_aiPackage!, target, stop, source.Running, delta, source.WeaponDrawn, requireArrivalHeight: true);
            var complete = action == FalloutEscortAction.Complete && !_escortProgress!.Complete;
            _escortProgress = progress;
            Combat.SetEscortProgress(progress);
            if (_aiWorld is { } world) world.Get(Appearance.Reference!.Value).ProcedureCaptureBlocker = null;
            _escortStatus = action.ToString();
            // Results may synchronously select another package. Publish this
            // arrival first and never overwrite that newer owner's progress.
            if (complete) _packageEvents!.Complete();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message;
            _failedPackage = _aiPackage?.FormKey;
            GD.PushError($"OPENNV_NATIVE_ESCORT_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
