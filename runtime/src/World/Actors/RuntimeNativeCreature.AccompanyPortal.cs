using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature : INativeActorPortal
{
    private const string AccompanyPortalPending = "Creature package portal activation is pending its source event result.";
    private FalloutFormKey? _accompanyDoor;
    private FalloutFormKey? _accompanyTargetRegion;
    private Vector3? _accompanyDoorDestination;
    private bool _accompanyPortalRequested, _accompanyPortalTransferred;
    private string? _accompanyPortalWait;
    CharacterBody3D INativeActorPortal.PortalBody => this;
    RuntimeNativeActorCombat INativeActorPortal.PortalCombat => Combat!;
    bool INativeActorPortal.PortalTransferred => _accompanyPortalTransferred;
    void INativeActorPortal.RetirePortalBindings() => RetireSourceBindings();

    bool INativeActorPortal.WantsPortal(FalloutFormKey door) => WantsAccompanyPortal(door);
    private bool WantsAccompanyPortal(FalloutFormKey door) => !_accompanyPortalTransferred && _aiError is null &&
        (_followPackage is { Accompany: true } package && package.Form == _aiPackage?.FormKey ||
            _travelPackage is { } travel && travel.Form == _aiPackage?.FormKey && _travelProgress is { Complete: false }) &&
        _accompanyDoor == door && Combat?.OwnsPose == false;

    private bool AdvanceAccompanyPortal(FalloutFollowPackage package, double delta)
    {
        var world = _aiWorld!;
        if (!world.IsEnabled(package.Target)) return false;
        var targetCell = _aiRecords!.RuntimeFormId(package.Target) == 0x14
            ? Combat!.PackagePlayerCell ?? throw new NotSupportedException("Accompany player has no current CELL owner.")
            : world.Placement(package.Target).Cell;
        return AdvancePackagePortal(targetCell, package.Running, false, delta);
    }

    private bool AdvancePackagePortal(FalloutFormKey targetCell, bool running, bool sneaking, double delta)
    {
        if (_accompanyPortalTransferred || _accompanyPortalRequested) return true;
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        var targetRegion = world.TravelRegion(targetCell);
        if (world.TravelRegion(world.Placement(actor).Cell) == targetRegion)
        {
            _accompanyDoor = null; _accompanyDoorDestination = null; _accompanyTargetRegion = null; _accompanyPortalWait = null;
            return false;
        }
        if (_accompanyDoor is null || _accompanyTargetRegion != targetRegion || !world.CanUseTravelPortal(actor, _accompanyDoor.Value))
        {
            var selected = world.TravelPortal(actor, targetCell) ??
                throw new NotSupportedException("Creature package target has no admitted source portal route.");
            _accompanyTargetRegion = targetRegion;
            if (_accompanyDoor != selected) _accompanyDoorDestination = null;
            _accompanyDoor = selected;
        }
        var door = _accompanyDoor.Value;
        var location = world.Placement(door).Position;
        var authored = GetParent<Node3D>().ToGlobal(new Vector3(location[0], location[2], -location[1]) * Skeleton.UnitsToMetres);
        var destination = _accompanyDoorDestination ??= Combat!.ProjectPackageDestination(authored);
        var distance = Combat!.PackageActivationRange * .75f;
        Combat.AdvancePackageMotion(_aiPackage!, destination, distance, running, delta, requireArrivalHeight: true, sneaking: sneaking);
        if (_travelProgress is { } travel) Combat.SetTravelProgress(travel);
        _aiState!.ProcedureCaptureBlocker = null;
        if (!IsOnFloor() || GlobalPosition.DistanceTo(destination) > distance + .1f &&
            !Combat.PackagePortalContactInReach(door)) return true;
        if (!AccompanyPortalPlaybackReady()) return true;
        _accompanyPortalRequested = Combat.RequestPackagePortal(door);
        if (_accompanyPortalRequested) _aiState!.ProcedureCaptureBlocker = AccompanyPortalPending;
        return true;
    }

    private bool AccompanyPortalPlaybackReady()
    {
        _accompanyPortalWait = null;
        if (Error is not null || _aiState!.ScriptError is not null)
            throw new NotSupportedException("Creature package portal retains a failed source animation or script.");
        if (_eventIdleClock is { } idle)
        {
            if (idle.Endless) throw new NotSupportedException("Creature package portal cannot retire an endless package event idle.");
            _accompanyPortalWait = "finite-package-idle-awaiting-ordinary-retirement";
            return false;
        }
        if (_eventIdleAnimation is not null || _aiState.PackageIdle is not null || _conversationTarget is not null)
            throw new NotSupportedException("Creature package portal retains an independent idle or dialogue continuation.");
        if (NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this).All(sound => sound.CanRetireTransientEffect)) return true;
        _accompanyPortalWait = "source-audio-awaiting-retirement";
        return false;
    }

    void INativeActorPortal.CommitPortal(FalloutFormKey door)
    {
        if (!WantsAccompanyPortal(door) || !_accompanyPortalRequested || !IsOnFloor() || !AccompanyPortalPlaybackReady())
            throw new NotSupportedException("Creature package portal has no ready native approach.");
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        var state = _aiState!;
        if (state.ProcedureCaptureBlocker != AccompanyPortalPending)
            throw new NotSupportedException("Creature package no longer owns its pending door continuation.");
        var package = _aiRecords!.GetEffective(_aiPackage!.FormKey);
        var travel = _travelPackage is null ? null : FalloutTravelPackage.Read(package);
        var source = travel is null ? FalloutFollowPackage.ReadAccompany(package) : null;
        var targetCell = travel is not null ? travel.Start(_aiRecords, world, actor).Cell :
            _aiRecords.RuntimeFormId(source!.Target) == 0x14
            ? Combat!.PackagePlayerCell ?? throw new NotSupportedException("Accompany player has no current CELL owner.")
            : world.Placement(source!.Target).Cell;
        if (!world.CanUseTravelPortal(actor, door) || world.TravelPortal(actor, targetCell) != door)
            throw new NotSupportedException("Creature package door no longer matches the winning target route.");
        var placement = world.TravelPortalArrival(door, out var destination);
        state.ProcedureCaptureBlocker = null;
        FalloutReferenceSnapshot captured;
        try { captured = state.Capture(); }
        finally { state.ProcedureCaptureBlocker = AccompanyPortalPending; }
        if (captured.PackageAssignment is not { Done: false } assignment || assignment.Package != package.FormKey ||
            captured.PackageMotion is not { } motion || motion.Package != package.FormKey || captured.Engagement is not null ||
            captured.HitReaction is not null || captured.PackageIdle is not null || captured.FurnitureContinuation is not null ||
            captured.DialogueContinuation is not null || captured.ObjectAnimations is not null)
            throw new NotSupportedException("Creature package portal cannot retire an independent actor continuation.");
        FalloutTravelProgress? nextTravel = null;
        FalloutAccompanyPortalArrival? receipt = null;
        if (travel is not null)
        {
            if (motion.Travel is not { Complete: false } progress || motion.AccompanyPortal is not null)
                throw new NotSupportedException("Creature Travel portal lacks its unfinished destination continuation.");
            travel.Validate(_aiRecords, world, actor, progress);
            nextTravel = progress with { PortalArrival = new(door, destination) };
            nextTravel.Validate();
        }
        else
        {
            if (motion.Travel is not null) throw new InvalidDataException("Accompany portal retained another Travel procedure.");
            receipt = new(door, destination); receipt.Validate();
        }
        var position = GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * Skeleton.UnitsToMetres;
        var rotation = GamebryoCoordinate.ConvertReferenceEuler(new(placement.RotationRadians[0], placement.RotationRadians[1],
            placement.RotationRadians[2]), 1).GetRotationQuaternion();
        world.SetPlacement(actor, placement);
        state.PackageMotion = motion with
        {
            Position = [position.X, position.Y, position.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            AccompanyPortal = receipt,
            Travel = nextTravel
        };
        state.PackageAssignment = assignment;
        state.ProcedureCaptureBlocker = null;
        _accompanyPortalTransferred = true;
        Combat!.StopPackageMotion();
        GD.Print($"OPENNV_NATIVE_{(travel is null ? "ACCOMPANY" : "CREATURE_TRAVEL")}_PORTAL_COMMIT actor={actor} package={package.FormKey} door={door} destination={destination} cell={placement.Cell} finalArrival=false");
    }

    void INativeActorPortal.FinishPortalAttempt(FalloutFormKey door, string? error)
    {
        if (_accompanyPortalTransferred || !_accompanyPortalRequested || _accompanyDoor != door) return;
        _aiError = error ?? "Source door did not commit the pending creature package transition.";
        _failedPackage = _aiPackage?.FormKey;
        _aiState!.ProcedureCaptureBlocker = _aiError;
        GD.PushError($"OPENNV_NATIVE_CREATURE_PORTAL_DIVERGENCE actor={Appearance.Reference} door={door}: {_aiError}");
    }
}
